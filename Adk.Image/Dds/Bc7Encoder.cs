// ReSharper disable RedundantUsingDirective
using System;
using System.IO;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace Adk.Image.Dds
{
    internal interface IDdsByteOutput
    {
        void Write(byte[] data, int offset, int count);
    }

    /// <summary>
    /// Quality/speed tradeoff for the pure managed BC7 encoder.
    /// The encoder automatically selects between BC7 modes 4, 5, and 6 per block.
    /// </summary>
    public enum Bc7Quality
    {
        Fast = 0,
        Balanced = 1
    }

    /// <summary>
    /// Small, allocation-conscious BC7 encoder intended for the Space Engineers
    /// ModAPI runtime. It does not use unsafe code, SIMD, reflection, native DLLs,
    /// tasks, or APIs outside the normal ModAPI whitelist surface used by Adk.
    ///
    /// Modes 4 and 5 encode the vector (normally RGB) and scalar (normally alpha)
    /// channels with independent selector streams. Mode 6 is retained for blocks
    /// where one shared RGBA interpolation line is a better fit. The best legal
    /// candidate is selected independently for every 4x4 block.
    /// </summary>
    public static class Bc7Encoder
    {
        static readonly int[] Weights2 = { 0, 21, 43, 64 };
        static readonly int[] Weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
        static readonly int[] Weights4 =
        {
            0, 4, 9, 13, 17, 21, 26, 30,
            34, 38, 43, 47, 51, 55, 60, 64
        };

        sealed class Scratch
        {
            public readonly int[] Pixels = new int[16 * 4];
            public readonly int[] RotatedPixels = new int[16 * 4];

            public readonly int[] Endpoint0 = new int[4];
            public readonly int[] Endpoint1 = new int[4];
            public readonly int[] Quantized0 = new int[4];
            public readonly int[] Quantized1 = new int[4];
            public readonly int[] Decoded0 = new int[4];
            public readonly int[] Decoded1 = new int[4];

            public readonly int[] VectorPalette = new int[8 * 3];
            public readonly int[] ScalarPalette = new int[8];
            public readonly int[] Palette = new int[16 * 4];

            public readonly int[] Indices = new int[16];
            public readonly int[] VectorIndices = new int[16];
            public readonly int[] ScalarIndices = new int[16];

            public readonly byte[] CandidateBlock = new byte[16];
            public readonly byte[] BestBlock = new byte[16];
            public readonly byte[] Block = new byte[16];
            public long BestError;
        }

        sealed class StreamByteOutput : IDdsByteOutput
        {
            readonly Stream _stream;

            public StreamByteOutput(Stream stream)
            {
                _stream = stream;
            }

            public void Write(byte[] data, int offset, int count)
            {
                _stream.Write(data, offset, count);
            }
        }

        sealed class BinaryWriterByteOutput : IDdsByteOutput
        {
            readonly BinaryWriter _writer;

            public BinaryWriterByteOutput(BinaryWriter writer)
            {
                _writer = writer;
            }

            public void Write(byte[] data, int offset, int count)
            {
                _writer.Write(data, offset, count);
            }
        }

        public static int GetEncodedSize(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");

            long blocksX = (width + 3L) / 4L;
            long blocksY = (height + 3L) / 4L;
            long bytes = blocksX * blocksY * 16L;
            if (bytes > int.MaxValue)
                throw new ArgumentOutOfRangeException("width/height");

            return (int)bytes;
        }

        public static byte[] Encode(byte[] rgba, int width, int height, int stride, Bc7Quality quality)
        {
            ValidateInput(rgba, width, height, stride);
            byte[] output = new byte[GetEncodedSize(width, height)];
            EncodeToArray(rgba, width, height, stride, quality, output, 0);
            return output;
        }

        public static void Encode(
            Stream output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            Bc7Quality quality)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            if (!output.CanWrite)
                throw new ArgumentException("Output stream is not writable.", "output");

            ValidateInput(rgba, width, height, stride);
            EncodeToOutput(new StreamByteOutput(output), rgba, width, height, stride, quality);
        }

        public static void Encode(
            BinaryWriter output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            Bc7Quality quality)
        {
            if (output == null)
                throw new ArgumentNullException("output");

            ValidateInput(rgba, width, height, stride);
            EncodeToOutput(new BinaryWriterByteOutput(output), rgba, width, height, stride, quality);
        }

        internal static void EncodeToArray(
            byte[] rgba,
            int width,
            int height,
            int stride,
            Bc7Quality quality,
            byte[] output,
            int outputOffset)
        {
            ValidateInput(rgba, width, height, stride);
            if (output == null)
                throw new ArgumentNullException("output");

            int required = GetEncodedSize(width, height);
            if (outputOffset < 0 || outputOffset > output.Length - required)
                throw new ArgumentOutOfRangeException("outputOffset");

            Scratch scratch = new Scratch();
            int destination = outputOffset;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                int baseY = blockY * 4;
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    int baseX = blockX * 4;
                    EncodeBlock(rgba, width, height, stride, baseX, baseY, quality, scratch);
                    Buffer.BlockCopy(scratch.Block, 0, output, destination, 16);
                    destination += 16;
                }
            }
        }

        internal static void EncodeToOutput(
            IDdsByteOutput output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            Bc7Quality quality)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            ValidateInput(rgba, width, height, stride);

            Scratch scratch = new Scratch();
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            byte[] row = new byte[checked(blockCountX * 16)];
            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                int baseY = blockY * 4;
                int rowOffset = 0;
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    int baseX = blockX * 4;
                    EncodeBlock(rgba, width, height, stride, baseX, baseY, quality, scratch);
                    Buffer.BlockCopy(scratch.Block, 0, row, rowOffset, 16);
                    rowOffset += 16;
                }

                output.Write(row, 0, row.Length);
            }
        }

        static void ValidateInput(byte[] rgba, int width, int height, int stride)
        {
            if (rgba == null)
                throw new ArgumentNullException("rgba");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if (stride < checked(width * 4))
                throw new ArgumentOutOfRangeException("stride");

            long required = (long)(height - 1) * stride + (long)width * 4L;
            if (required > rgba.Length)
                throw new ArgumentException("RGBA buffer is shorter than width/height/stride require.", "rgba");
        }

        static void EncodeBlock(
            byte[] rgba,
            int width,
            int height,
            int stride,
            int baseX,
            int baseY,
            Bc7Quality quality,
            Scratch scratch)
        {
            LoadBlock(rgba, width, height, stride, baseX, baseY, scratch.Pixels);
            scratch.BestError = long.MaxValue;

            EncodeMode6Candidate(scratch, quality);

            // Modes 4 and 5 can rotate R/G/B into the independently indexed scalar
            // channel. Trying all rotations is important for masks, packed maps, and
            // recolored UI textures where alpha is not necessarily the only channel
            // whose distribution differs from the other three.
            for (int rotation = 0; rotation < 4; rotation++)
            {
                RotatePixelsForEncoding(scratch.Pixels, scratch.RotatedPixels, rotation);

                // Mode 4 has one 2-bit selector set and one 3-bit selector set.
                // The index-selection bit decides which one belongs to the vector.
                EncodeMode4Candidate(scratch, quality, rotation, 0);
                EncodeMode4Candidate(scratch, quality, rotation, 1);
                EncodeMode5Candidate(scratch, quality, rotation);
            }

            Buffer.BlockCopy(scratch.BestBlock, 0, scratch.Block, 0, 16);
        }

        static void EncodeMode4Candidate(Scratch scratch, Bc7Quality quality, int rotation, int indexSelection)
        {
            int vectorIndexBits = indexSelection == 0 ? 2 : 3;
            int scalarIndexBits = indexSelection == 0 ? 3 : 2;

            InitializeSeparatedEndpoints(scratch.RotatedPixels, scratch.Endpoint0, scratch.Endpoint1);
            QuantizeSeparatedEndpoints(
                scratch.Endpoint0,
                scratch.Endpoint1,
                5,
                6,
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1);

            AssignSeparatedIndices(
                scratch.RotatedPixels,
                scratch.Decoded0,
                scratch.Decoded1,
                vectorIndexBits,
                scalarIndexBits,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                scratch.VectorPalette,
                scratch.ScalarPalette);

            if (quality == Bc7Quality.Balanced)
            {
                FitSeparatedEndpoints(
                    scratch.RotatedPixels,
                    scratch.VectorIndices,
                    scratch.ScalarIndices,
                    vectorIndexBits,
                    scalarIndexBits,
                    scratch.Endpoint0,
                    scratch.Endpoint1);
                QuantizeSeparatedEndpoints(
                    scratch.Endpoint0,
                    scratch.Endpoint1,
                    5,
                    6,
                    scratch.Quantized0,
                    scratch.Quantized1,
                    scratch.Decoded0,
                    scratch.Decoded1);
                AssignSeparatedIndices(
                    scratch.RotatedPixels,
                    scratch.Decoded0,
                    scratch.Decoded1,
                    vectorIndexBits,
                    scalarIndexBits,
                    scratch.VectorIndices,
                    scratch.ScalarIndices,
                    scratch.VectorPalette,
                    scratch.ScalarPalette);
            }

            NormalizeSeparatedAnchor(
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.VectorIndices,
                vectorIndexBits,
                0,
                3);
            NormalizeSeparatedAnchor(
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.ScalarIndices,
                scalarIndexBits,
                3,
                1);

            PackMode4(
                scratch.Quantized0,
                scratch.Quantized1,
                rotation,
                indexSelection,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                scratch.CandidateBlock);

            long error = ComputeSeparatedError(
                scratch.Pixels,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                vectorIndexBits,
                scalarIndexBits,
                rotation);
            KeepBestCandidate(scratch, error);
        }

        static void EncodeMode5Candidate(Scratch scratch, Bc7Quality quality, int rotation)
        {
            const int vectorIndexBits = 2;
            const int scalarIndexBits = 2;

            InitializeSeparatedEndpoints(scratch.RotatedPixels, scratch.Endpoint0, scratch.Endpoint1);
            QuantizeSeparatedEndpoints(
                scratch.Endpoint0,
                scratch.Endpoint1,
                7,
                8,
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1);

            AssignSeparatedIndices(
                scratch.RotatedPixels,
                scratch.Decoded0,
                scratch.Decoded1,
                vectorIndexBits,
                scalarIndexBits,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                scratch.VectorPalette,
                scratch.ScalarPalette);

            if (quality == Bc7Quality.Balanced)
            {
                FitSeparatedEndpoints(
                    scratch.RotatedPixels,
                    scratch.VectorIndices,
                    scratch.ScalarIndices,
                    vectorIndexBits,
                    scalarIndexBits,
                    scratch.Endpoint0,
                    scratch.Endpoint1);
                QuantizeSeparatedEndpoints(
                    scratch.Endpoint0,
                    scratch.Endpoint1,
                    7,
                    8,
                    scratch.Quantized0,
                    scratch.Quantized1,
                    scratch.Decoded0,
                    scratch.Decoded1);
                AssignSeparatedIndices(
                    scratch.RotatedPixels,
                    scratch.Decoded0,
                    scratch.Decoded1,
                    vectorIndexBits,
                    scalarIndexBits,
                    scratch.VectorIndices,
                    scratch.ScalarIndices,
                    scratch.VectorPalette,
                    scratch.ScalarPalette);
            }

            NormalizeSeparatedAnchor(
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.VectorIndices,
                vectorIndexBits,
                0,
                3);
            NormalizeSeparatedAnchor(
                scratch.Quantized0,
                scratch.Quantized1,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.ScalarIndices,
                scalarIndexBits,
                3,
                1);

            PackMode5(
                scratch.Quantized0,
                scratch.Quantized1,
                rotation,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                scratch.CandidateBlock);

            long error = ComputeSeparatedError(
                scratch.Pixels,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.VectorIndices,
                scratch.ScalarIndices,
                vectorIndexBits,
                scalarIndexBits,
                rotation);
            KeepBestCandidate(scratch, error);
        }

        static void EncodeMode6Candidate(Scratch scratch, Bc7Quality quality)
        {
            int firstExtreme = FindFarthestPixel(scratch.Pixels, 0);
            int secondExtreme = FindFarthestPixel(scratch.Pixels, firstExtreme);
            CopyPixel(scratch.Pixels, firstExtreme, scratch.Endpoint0);
            CopyPixel(scratch.Pixels, secondExtreme, scratch.Endpoint1);

            int p0;
            int p1;
            QuantizeMode6Endpoint(scratch.Endpoint0, scratch.Quantized0, scratch.Decoded0, out p0);
            QuantizeMode6Endpoint(scratch.Endpoint1, scratch.Quantized1, scratch.Decoded1, out p1);

            BuildMode6Palette(scratch.Decoded0, scratch.Decoded1, scratch.Palette);
            AssignMode6Indices(
                scratch.Pixels,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.Palette,
                scratch.Indices,
                quality == Bc7Quality.Balanced ? 2 : 1);

            if (quality == Bc7Quality.Balanced)
            {
                FitMode6Endpoints(scratch.Pixels, scratch.Indices, scratch.Endpoint0, scratch.Endpoint1);
                QuantizeMode6Endpoint(scratch.Endpoint0, scratch.Quantized0, scratch.Decoded0, out p0);
                QuantizeMode6Endpoint(scratch.Endpoint1, scratch.Quantized1, scratch.Decoded1, out p1);
                BuildMode6Palette(scratch.Decoded0, scratch.Decoded1, scratch.Palette);
                AssignMode6Indices(
                    scratch.Pixels,
                    scratch.Decoded0,
                    scratch.Decoded1,
                    scratch.Palette,
                    scratch.Indices,
                    2);
            }

            if (scratch.Indices[0] >= 8)
            {
                SwapEndpointArrays(scratch.Quantized0, scratch.Quantized1, 0, 4);
                SwapEndpointArrays(scratch.Decoded0, scratch.Decoded1, 0, 4);
                int temporary = p0;
                p0 = p1;
                p1 = temporary;
                for (int i = 0; i < 16; i++)
                    scratch.Indices[i] = 15 - scratch.Indices[i];
            }

            PackMode6(
                scratch.Quantized0,
                scratch.Quantized1,
                p0,
                p1,
                scratch.Indices,
                scratch.CandidateBlock);

            long error = ComputeMode6Error(scratch.Pixels, scratch.Decoded0, scratch.Decoded1, scratch.Indices);
            KeepBestCandidate(scratch, error);
        }

        static void KeepBestCandidate(Scratch scratch, long error)
        {
            if (error >= scratch.BestError)
                return;

            scratch.BestError = error;
            Buffer.BlockCopy(scratch.CandidateBlock, 0, scratch.BestBlock, 0, 16);
        }

        static void LoadBlock(
            byte[] rgba,
            int width,
            int height,
            int stride,
            int baseX,
            int baseY,
            int[] pixels)
        {
            int destination = 0;
            for (int y = 0; y < 4; y++)
            {
                int sourceY = baseY + y;
                if (sourceY >= height)
                    sourceY = height - 1;
                int row = sourceY * stride;
                for (int x = 0; x < 4; x++)
                {
                    int sourceX = baseX + x;
                    if (sourceX >= width)
                        sourceX = width - 1;
                    int source = row + sourceX * 4;
                    pixels[destination++] = rgba[source];
                    pixels[destination++] = rgba[source + 1];
                    pixels[destination++] = rgba[source + 2];
                    pixels[destination++] = rgba[source + 3];
                }
            }
        }

        static void RotatePixelsForEncoding(int[] source, int[] destination, int rotation)
        {
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int offset = pixel * 4;
                destination[offset] = source[offset];
                destination[offset + 1] = source[offset + 1];
                destination[offset + 2] = source[offset + 2];
                destination[offset + 3] = source[offset + 3];

                if (rotation != 0)
                {
                    int channel = rotation - 1;
                    int temporary = destination[offset + channel];
                    destination[offset + channel] = destination[offset + 3];
                    destination[offset + 3] = temporary;
                }
            }
        }

        static void InitializeSeparatedEndpoints(int[] pixels, int[] endpoint0, int[] endpoint1)
        {
            int firstExtreme = FindFarthestVectorPixel(pixels, 0);
            int secondExtreme = FindFarthestVectorPixel(pixels, firstExtreme);
            int firstOffset = firstExtreme * 4;
            int secondOffset = secondExtreme * 4;

            endpoint0[0] = pixels[firstOffset];
            endpoint0[1] = pixels[firstOffset + 1];
            endpoint0[2] = pixels[firstOffset + 2];
            endpoint1[0] = pixels[secondOffset];
            endpoint1[1] = pixels[secondOffset + 1];
            endpoint1[2] = pixels[secondOffset + 2];

            int minimum = 255;
            int maximum = 0;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int scalar = pixels[pixel * 4 + 3];
                if (scalar < minimum)
                    minimum = scalar;
                if (scalar > maximum)
                    maximum = scalar;
            }

            endpoint0[3] = minimum;
            endpoint1[3] = maximum;
        }

        static int FindFarthestVectorPixel(int[] pixels, int fromPixel)
        {
            int from = fromPixel * 4;
            int bestPixel = 0;
            long bestDistance = -1L;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int offset = pixel * 4;
                long d0 = pixels[offset] - pixels[from];
                long d1 = pixels[offset + 1] - pixels[from + 1];
                long d2 = pixels[offset + 2] - pixels[from + 2];
                long distance = d0 * d0 + d1 * d1 + d2 * d2;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestPixel = pixel;
                }
            }

            return bestPixel;
        }

        static void QuantizeSeparatedEndpoints(
            int[] endpoint0,
            int[] endpoint1,
            int vectorBits,
            int scalarBits,
            int[] quantized0,
            int[] quantized1,
            int[] decoded0,
            int[] decoded1)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                QuantizeComponent(endpoint0[channel], vectorBits, out quantized0[channel], out decoded0[channel]);
                QuantizeComponent(endpoint1[channel], vectorBits, out quantized1[channel], out decoded1[channel]);
            }

            QuantizeComponent(endpoint0[3], scalarBits, out quantized0[3], out decoded0[3]);
            QuantizeComponent(endpoint1[3], scalarBits, out quantized1[3], out decoded1[3]);
        }

        static void QuantizeComponent(int value, int bits, out int quantized, out int decoded)
        {
            value = ClampByte(value);
            if (bits >= 8)
            {
                quantized = value;
                decoded = value;
                return;
            }

            int maximum = (1 << bits) - 1;
            int bestQuantized = 0;
            int bestDecoded = 0;
            int bestError = int.MaxValue;
            int estimate = (value * maximum + 127) / 255;
            int first = estimate - 1;
            int last = estimate + 1;
            if (first < 0)
                first = 0;
            if (last > maximum)
                last = maximum;

            for (int candidate = first; candidate <= last; candidate++)
            {
                int reconstructed = ExpandEndpoint(candidate, bits);
                int error = value - reconstructed;
                error *= error;
                if (error < bestError)
                {
                    bestError = error;
                    bestQuantized = candidate;
                    bestDecoded = reconstructed;
                }
            }

            quantized = bestQuantized;
            decoded = bestDecoded;
        }

        static int ExpandEndpoint(int value, int bits)
        {
            if (bits >= 8)
                return value & 255;

            int expanded = value << (8 - bits);
            expanded |= value >> (2 * bits - 8);
            return expanded & 255;
        }

        static void AssignSeparatedIndices(
            int[] pixels,
            int[] endpoint0,
            int[] endpoint1,
            int vectorIndexBits,
            int scalarIndexBits,
            int[] vectorIndices,
            int[] scalarIndices,
            int[] vectorPalette,
            int[] scalarPalette)
        {
            int[] vectorWeights = GetWeights(vectorIndexBits);
            int[] scalarWeights = GetWeights(scalarIndexBits);
            int vectorCount = 1 << vectorIndexBits;
            int scalarCount = 1 << scalarIndexBits;

            for (int index = 0; index < vectorCount; index++)
            {
                int weight = vectorWeights[index];
                int inverseWeight = 64 - weight;
                int paletteOffset = index * 3;
                vectorPalette[paletteOffset] =
                    (inverseWeight * endpoint0[0] + weight * endpoint1[0] + 32) >> 6;
                vectorPalette[paletteOffset + 1] =
                    (inverseWeight * endpoint0[1] + weight * endpoint1[1] + 32) >> 6;
                vectorPalette[paletteOffset + 2] =
                    (inverseWeight * endpoint0[2] + weight * endpoint1[2] + 32) >> 6;
            }

            for (int index = 0; index < scalarCount; index++)
            {
                int weight = scalarWeights[index];
                int inverseWeight = 64 - weight;
                scalarPalette[index] =
                    (inverseWeight * endpoint0[3] + weight * endpoint1[3] + 32) >> 6;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int pixelOffset = pixel * 4;
                long bestVectorError = long.MaxValue;
                int bestVectorIndex = 0;
                for (int index = 0; index < vectorCount; index++)
                {
                    int paletteOffset = index * 3;
                    long d0 = pixels[pixelOffset] - vectorPalette[paletteOffset];
                    long d1 = pixels[pixelOffset + 1] - vectorPalette[paletteOffset + 1];
                    long d2 = pixels[pixelOffset + 2] - vectorPalette[paletteOffset + 2];
                    long error = d0 * d0 + d1 * d1 + d2 * d2;
                    if (error < bestVectorError)
                    {
                        bestVectorError = error;
                        bestVectorIndex = index;
                    }
                }

                long bestScalarError = long.MaxValue;
                int bestScalarIndex = 0;
                for (int index = 0; index < scalarCount; index++)
                {
                    long difference = pixels[pixelOffset + 3] - scalarPalette[index];
                    long error = difference * difference;
                    if (error < bestScalarError)
                    {
                        bestScalarError = error;
                        bestScalarIndex = index;
                    }
                }

                vectorIndices[pixel] = bestVectorIndex;
                scalarIndices[pixel] = bestScalarIndex;
            }
        }

        static void FitSeparatedEndpoints(
            int[] pixels,
            int[] vectorIndices,
            int[] scalarIndices,
            int vectorIndexBits,
            int scalarIndexBits,
            int[] endpoint0,
            int[] endpoint1)
        {
            int[] vectorWeights = GetWeights(vectorIndexBits);
            int[] scalarWeights = GetWeights(scalarIndexBits);

            for (int channel = 0; channel < 3; channel++)
                FitOneChannel(pixels, channel, vectorIndices, vectorWeights, endpoint0, endpoint1);
            FitOneChannel(pixels, 3, scalarIndices, scalarWeights, endpoint0, endpoint1);
        }

        static void FitOneChannel(
            int[] pixels,
            int channel,
            int[] indices,
            int[] weights,
            int[] endpoint0,
            int[] endpoint1)
        {
            long saa = 0L;
            long sab = 0L;
            long sbb = 0L;
            long say = 0L;
            long sby = 0L;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int weightB = weights[indices[pixel]];
                int weightA = 64 - weightB;
                long value = (long)pixels[pixel * 4 + channel] * 64L;
                saa += (long)weightA * weightA;
                sab += (long)weightA * weightB;
                sbb += (long)weightB * weightB;
                say += weightA * value;
                sby += weightB * value;
            }

            long determinant = saa * sbb - sab * sab;
            if (determinant <= 0L)
                return;

            endpoint0[channel] = SolveEndpoint(say, sby, sbb, sab, determinant);
            endpoint1[channel] = SolveEndpoint(sby, say, saa, sab, determinant);
        }

        static void NormalizeSeparatedAnchor(
            int[] quantized0,
            int[] quantized1,
            int[] decoded0,
            int[] decoded1,
            int[] indices,
            int indexBits,
            int firstChannel,
            int channelCount)
        {
            int half = 1 << (indexBits - 1);
            if (indices[0] < half)
                return;

            SwapEndpointArrays(quantized0, quantized1, firstChannel, channelCount);
            SwapEndpointArrays(decoded0, decoded1, firstChannel, channelCount);
            int maximum = (1 << indexBits) - 1;
            for (int pixel = 0; pixel < 16; pixel++)
                indices[pixel] = maximum - indices[pixel];
        }

        static long ComputeSeparatedError(
            int[] originalPixels,
            int[] endpoint0,
            int[] endpoint1,
            int[] vectorIndices,
            int[] scalarIndices,
            int vectorIndexBits,
            int scalarIndexBits,
            int rotation)
        {
            int[] vectorWeights = GetWeights(vectorIndexBits);
            int[] scalarWeights = GetWeights(scalarIndexBits);
            long error = 0L;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int vectorWeight = vectorWeights[vectorIndices[pixel]];
                int vectorInverse = 64 - vectorWeight;
                int scalarWeight = scalarWeights[scalarIndices[pixel]];
                int scalarInverse = 64 - scalarWeight;

                int r = (vectorInverse * endpoint0[0] + vectorWeight * endpoint1[0] + 32) >> 6;
                int g = (vectorInverse * endpoint0[1] + vectorWeight * endpoint1[1] + 32) >> 6;
                int b = (vectorInverse * endpoint0[2] + vectorWeight * endpoint1[2] + 32) >> 6;
                int a = (scalarInverse * endpoint0[3] + scalarWeight * endpoint1[3] + 32) >> 6;

                if (rotation == 1)
                {
                    int temporary = a;
                    a = r;
                    r = temporary;
                }
                else if (rotation == 2)
                {
                    int temporary = a;
                    a = g;
                    g = temporary;
                }
                else if (rotation == 3)
                {
                    int temporary = a;
                    a = b;
                    b = temporary;
                }

                int offset = pixel * 4;
                long dr = originalPixels[offset] - r;
                long dg = originalPixels[offset + 1] - g;
                long db = originalPixels[offset + 2] - b;
                long da = originalPixels[offset + 3] - a;
                error += dr * dr + dg * dg + db * db + da * da;
            }

            return error;
        }

        static int[] GetWeights(int indexBits)
        {
            if (indexBits == 2)
                return Weights2;
            if (indexBits == 3)
                return Weights3;
            return Weights4;
        }

        static int FindFarthestPixel(int[] pixels, int fromPixel)
        {
            int from = fromPixel * 4;
            int bestPixel = 0;
            long bestDistance = -1L;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int offset = pixel * 4;
                long dr = pixels[offset] - pixels[from];
                long dg = pixels[offset + 1] - pixels[from + 1];
                long db = pixels[offset + 2] - pixels[from + 2];
                long da = pixels[offset + 3] - pixels[from + 3];
                long distance = dr * dr + dg * dg + db * db + da * da;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestPixel = pixel;
                }
            }

            return bestPixel;
        }

        static void CopyPixel(int[] pixels, int pixel, int[] endpoint)
        {
            int offset = pixel * 4;
            endpoint[0] = pixels[offset];
            endpoint[1] = pixels[offset + 1];
            endpoint[2] = pixels[offset + 2];
            endpoint[3] = pixels[offset + 3];
        }

        static void QuantizeMode6Endpoint(int[] endpoint, int[] quantized, int[] decoded, out int pbit)
        {
            long bestError = long.MaxValue;
            int bestPbit = 0;
            for (int candidatePbit = 0; candidatePbit <= 1; candidatePbit++)
            {
                long error = 0L;
                for (int channel = 0; channel < 4; channel++)
                {
                    int value = ClampByte(endpoint[channel]);
                    int q = (value - candidatePbit + 1) / 2;
                    if (q < 0)
                        q = 0;
                    else if (q > 127)
                        q = 127;
                    int reconstructed = (q << 1) | candidatePbit;
                    long difference = value - reconstructed;
                    error += difference * difference;
                }

                if (error < bestError)
                {
                    bestError = error;
                    bestPbit = candidatePbit;
                }
            }

            pbit = bestPbit;
            for (int channel = 0; channel < 4; channel++)
            {
                int value = ClampByte(endpoint[channel]);
                int q = (value - pbit + 1) / 2;
                if (q < 0)
                    q = 0;
                else if (q > 127)
                    q = 127;
                quantized[channel] = q;
                decoded[channel] = (q << 1) | pbit;
            }
        }

        static void BuildMode6Palette(int[] endpoint0, int[] endpoint1, int[] palette)
        {
            for (int index = 0; index < 16; index++)
            {
                int weight = Weights4[index];
                int inverseWeight = 64 - weight;
                int offset = index * 4;
                palette[offset] = (inverseWeight * endpoint0[0] + weight * endpoint1[0] + 32) >> 6;
                palette[offset + 1] = (inverseWeight * endpoint0[1] + weight * endpoint1[1] + 32) >> 6;
                palette[offset + 2] = (inverseWeight * endpoint0[2] + weight * endpoint1[2] + 32) >> 6;
                palette[offset + 3] = (inverseWeight * endpoint0[3] + weight * endpoint1[3] + 32) >> 6;
            }
        }

        static void AssignMode6Indices(
            int[] pixels,
            int[] endpoint0,
            int[] endpoint1,
            int[] palette,
            int[] indices,
            int searchRadius)
        {
            long d0 = endpoint1[0] - endpoint0[0];
            long d1 = endpoint1[1] - endpoint0[1];
            long d2 = endpoint1[2] - endpoint0[2];
            long d3 = endpoint1[3] - endpoint0[3];
            long denominator = d0 * d0 + d1 * d1 + d2 * d2 + d3 * d3;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int pixelOffset = pixel * 4;
                int estimate;
                if (denominator <= 0L)
                {
                    estimate = 0;
                }
                else
                {
                    long v0 = pixels[pixelOffset] - endpoint0[0];
                    long v1 = pixels[pixelOffset + 1] - endpoint0[1];
                    long v2 = pixels[pixelOffset + 2] - endpoint0[2];
                    long v3 = pixels[pixelOffset + 3] - endpoint0[3];
                    long numerator = v0 * d0 + v1 * d1 + v2 * d2 + v3 * d3;
                    if (numerator <= 0L)
                        estimate = 0;
                    else if (numerator >= denominator)
                        estimate = 15;
                    else
                        estimate = (int)((numerator * 15L + denominator / 2L) / denominator);
                }

                int first = estimate - searchRadius;
                int last = estimate + searchRadius;
                if (first < 0)
                    first = 0;
                if (last > 15)
                    last = 15;

                long bestError = long.MaxValue;
                int bestIndex = estimate;
                for (int index = first; index <= last; index++)
                {
                    int paletteOffset = index * 4;
                    long dr = pixels[pixelOffset] - palette[paletteOffset];
                    long dg = pixels[pixelOffset + 1] - palette[paletteOffset + 1];
                    long db = pixels[pixelOffset + 2] - palette[paletteOffset + 2];
                    long da = pixels[pixelOffset + 3] - palette[paletteOffset + 3];
                    long error = dr * dr + dg * dg + db * db + da * da;
                    if (error < bestError)
                    {
                        bestError = error;
                        bestIndex = index;
                    }
                }

                indices[pixel] = bestIndex;
            }
        }

        static void FitMode6Endpoints(int[] pixels, int[] indices, int[] endpoint0, int[] endpoint1)
        {
            for (int channel = 0; channel < 4; channel++)
                FitOneChannel(pixels, channel, indices, Weights4, endpoint0, endpoint1);
        }

        static long ComputeMode6Error(int[] pixels, int[] endpoint0, int[] endpoint1, int[] indices)
        {
            long error = 0L;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int weight = Weights4[indices[pixel]];
                int inverseWeight = 64 - weight;
                int offset = pixel * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int reconstructed =
                        (inverseWeight * endpoint0[channel] + weight * endpoint1[channel] + 32) >> 6;
                    long difference = pixels[offset + channel] - reconstructed;
                    error += difference * difference;
                }
            }

            return error;
        }

        static int SolveEndpoint(long sy, long otherSy, long diagonal, long cross, long determinant)
        {
            long numerator = sy * diagonal - otherSy * cross;
            if (numerator >= 0L)
                return ClampByte((int)((numerator + determinant / 2L) / determinant));
            return ClampByte((int)((numerator - determinant / 2L) / determinant));
        }

        static void SwapEndpointArrays(int[] left, int[] right, int firstChannel, int channelCount)
        {
            int last = firstChannel + channelCount;
            for (int channel = firstChannel; channel < last; channel++)
            {
                int temporary = left[channel];
                left[channel] = right[channel];
                right[channel] = temporary;
            }
        }

        static int ClampByte(int value)
        {
            if (value < 0)
                return 0;
            if (value > 255)
                return 255;
            return value;
        }

        static void PackMode4(
            int[] endpoint0,
            int[] endpoint1,
            int rotation,
            int indexSelection,
            int[] vectorIndices,
            int[] scalarIndices,
            byte[] output)
        {
            ulong low = 0UL;
            ulong high = 0UL;
            int bitPosition = 0;

            // Mode 4 marker: 00001 (LSB first).
            PutBits(ref low, ref high, ref bitPosition, 1u << 4, 5);
            PutBits(ref low, ref high, ref bitPosition, (uint)rotation, 2);
            PutBits(ref low, ref high, ref bitPosition, (uint)indexSelection, 1);

            for (int channel = 0; channel < 3; channel++)
            {
                PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[channel], 5);
                PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[channel], 5);
            }
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[3], 6);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[3], 6);

            int[] primary = indexSelection == 0 ? vectorIndices : scalarIndices;
            int[] secondary = indexSelection == 0 ? scalarIndices : vectorIndices;
            PutIndexSet(ref low, ref high, ref bitPosition, primary, 2);
            PutIndexSet(ref low, ref high, ref bitPosition, secondary, 3);

            WriteUInt64LittleEndian(output, 0, low);
            WriteUInt64LittleEndian(output, 8, high);
        }

        static void PackMode5(
            int[] endpoint0,
            int[] endpoint1,
            int rotation,
            int[] vectorIndices,
            int[] scalarIndices,
            byte[] output)
        {
            ulong low = 0UL;
            ulong high = 0UL;
            int bitPosition = 0;

            // Mode 5 marker: 000001 (LSB first).
            PutBits(ref low, ref high, ref bitPosition, 1u << 5, 6);
            PutBits(ref low, ref high, ref bitPosition, (uint)rotation, 2);

            for (int channel = 0; channel < 3; channel++)
            {
                PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[channel], 7);
                PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[channel], 7);
            }
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[3], 8);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[3], 8);

            PutIndexSet(ref low, ref high, ref bitPosition, vectorIndices, 2);
            PutIndexSet(ref low, ref high, ref bitPosition, scalarIndices, 2);

            WriteUInt64LittleEndian(output, 0, low);
            WriteUInt64LittleEndian(output, 8, high);
        }

        static void PackMode6(
            int[] endpoint0,
            int[] endpoint1,
            int p0,
            int p1,
            int[] indices,
            byte[] output)
        {
            ulong low = 0UL;
            ulong high = 0UL;
            int bitPosition = 0;

            PutBits(ref low, ref high, ref bitPosition, 1u << 6, 7);

            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[0], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[0], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[1], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[1], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[2], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[2], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint0[3], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)endpoint1[3], 7);
            PutBits(ref low, ref high, ref bitPosition, (uint)p0, 1);
            PutBits(ref low, ref high, ref bitPosition, (uint)p1, 1);

            PutIndexSet(ref low, ref high, ref bitPosition, indices, 4);

            WriteUInt64LittleEndian(output, 0, low);
            WriteUInt64LittleEndian(output, 8, high);
        }

        static void PutIndexSet(
            ref ulong low,
            ref ulong high,
            ref int bitPosition,
            int[] indices,
            int indexBits)
        {
            PutBits(ref low, ref high, ref bitPosition, (uint)indices[0], indexBits - 1);
            for (int pixel = 1; pixel < 16; pixel++)
                PutBits(ref low, ref high, ref bitPosition, (uint)indices[pixel], indexBits);
        }

        static void PutBits(
            ref ulong low,
            ref ulong high,
            ref int bitPosition,
            uint value,
            int count)
        {
            ulong mask = (1UL << count) - 1UL;
            ulong bits = ((ulong)value) & mask;
            if (bitPosition < 64)
            {
                low |= bits << bitPosition;
                int spill = bitPosition + count - 64;
                if (spill > 0)
                    high |= bits >> (count - spill);
            }
            else
            {
                high |= bits << (bitPosition - 64);
            }

            bitPosition += count;
        }

        static void WriteUInt64LittleEndian(byte[] output, int offset, ulong value)
        {
            output[offset] = (byte)value;
            output[offset + 1] = (byte)(value >> 8);
            output[offset + 2] = (byte)(value >> 16);
            output[offset + 3] = (byte)(value >> 24);
            output[offset + 4] = (byte)(value >> 32);
            output[offset + 5] = (byte)(value >> 40);
            output[offset + 6] = (byte)(value >> 48);
            output[offset + 7] = (byte)(value >> 56);
        }
    }
}
