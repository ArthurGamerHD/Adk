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
    /// Both modes emit legal BC7 mode-6 blocks, which preserve RGBA.
    /// </summary>
    public enum Bc7Quality
    {
        Fast = 0,
        Balanced = 1
    }

    /// <summary>
    /// Small, allocation-conscious BC7 mode-6 encoder intended for the
    /// Space Engineers ModAPI runtime.  It does not use unsafe code, SIMD,
    /// reflection, native DLLs, tasks, or APIs outside the normal ModAPI
    /// whitelist surface used by Adk.
    /// </summary>
    public static class Bc7Encoder
    {
        static readonly int[] Weights4 =
        {
            0, 4, 9, 13, 17, 21, 26, 30,
            34, 38, 43, 47, 51, 55, 60, 64
        };

        sealed class Scratch
        {
            public readonly int[] Pixels = new int[16 * 4];
            public readonly int[] Endpoint0 = new int[4];
            public readonly int[] Endpoint1 = new int[4];
            public readonly int[] Quantized0 = new int[4];
            public readonly int[] Quantized1 = new int[4];
            public readonly int[] Decoded0 = new int[4];
            public readonly int[] Decoded1 = new int[4];
            public readonly int[] Palette = new int[16 * 4];
            public readonly int[] Indices = new int[16];
            public readonly byte[] Block = new byte[16];
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

            int firstExtreme = FindFarthestPixel(scratch.Pixels, 0);
            int secondExtreme = FindFarthestPixel(scratch.Pixels, firstExtreme);
            CopyPixel(scratch.Pixels, firstExtreme, scratch.Endpoint0);
            CopyPixel(scratch.Pixels, secondExtreme, scratch.Endpoint1);

            int p0;
            int p1;
            QuantizeEndpoint(
                scratch.Endpoint0,
                scratch.Quantized0,
                scratch.Decoded0,
                out p0);
            QuantizeEndpoint(
                scratch.Endpoint1,
                scratch.Quantized1,
                scratch.Decoded1,
                out p1);

            BuildPalette(scratch.Decoded0, scratch.Decoded1, scratch.Palette);
            AssignIndices(
                scratch.Pixels,
                scratch.Decoded0,
                scratch.Decoded1,
                scratch.Palette,
                scratch.Indices,
                quality == Bc7Quality.Balanced ? 2 : 1);

            if (quality == Bc7Quality.Balanced)
            {
                FitEndpoints(scratch.Pixels, scratch.Indices, scratch.Endpoint0, scratch.Endpoint1);
                QuantizeEndpoint(
                    scratch.Endpoint0,
                    scratch.Quantized0,
                    scratch.Decoded0,
                    out p0);
                QuantizeEndpoint(
                    scratch.Endpoint1,
                    scratch.Quantized1,
                    scratch.Decoded1,
                    out p1);
                BuildPalette(scratch.Decoded0, scratch.Decoded1, scratch.Palette);
                AssignIndices(
                    scratch.Pixels,
                    scratch.Decoded0,
                    scratch.Decoded1,
                    scratch.Palette,
                    scratch.Indices,
                    2);
            }

            // Mode 6 stores only three bits for the subset-0 anchor selector
            // (texel 0).  If its optimal selector has the high bit set, reverse
            // the endpoints and mirror all selectors.  The decoded palette is
            // identical because BC7 mode-6 weights are symmetric.
            if (scratch.Indices[0] >= 8)
            {
                SwapEndpointArrays(scratch.Quantized0, scratch.Quantized1);
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
                scratch.Block);
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

        static void QuantizeEndpoint(
            int[] endpoint,
            int[] quantized,
            int[] decoded,
            out int pbit)
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

        static void BuildPalette(int[] endpoint0, int[] endpoint1, int[] palette)
        {
            for (int index = 0; index < 16; index++)
            {
                int weight = Weights4[index];
                int inverseWeight = 64 - weight;
                int offset = index * 4;
                palette[offset] =
                    (inverseWeight * endpoint0[0] + weight * endpoint1[0] + 32) >> 6;
                palette[offset + 1] =
                    (inverseWeight * endpoint0[1] + weight * endpoint1[1] + 32) >> 6;
                palette[offset + 2] =
                    (inverseWeight * endpoint0[2] + weight * endpoint1[2] + 32) >> 6;
                palette[offset + 3] =
                    (inverseWeight * endpoint0[3] + weight * endpoint1[3] + 32) >> 6;
            }
        }

        static void AssignIndices(
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
                    {
                        estimate = 0;
                    }
                    else if (numerator >= denominator)
                    {
                        estimate = 15;
                    }
                    else
                    {
                        estimate = (int)((numerator * 15L + denominator / 2L) / denominator);
                    }
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

        static void FitEndpoints(int[] pixels, int[] indices, int[] endpoint0, int[] endpoint1)
        {
            long saa = 0L;
            long sab = 0L;
            long sbb = 0L;
            long sayR = 0L;
            long sayG = 0L;
            long sayB = 0L;
            long sayA = 0L;
            long sbyR = 0L;
            long sbyG = 0L;
            long sbyB = 0L;
            long sbyA = 0L;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int weightB = Weights4[indices[pixel]];
                int weightA = 64 - weightB;
                saa += (long)weightA * weightA;
                sab += (long)weightA * weightB;
                sbb += (long)weightB * weightB;

                int offset = pixel * 4;
                long r = (long)pixels[offset] * 64L;
                long g = (long)pixels[offset + 1] * 64L;
                long b = (long)pixels[offset + 2] * 64L;
                long a = (long)pixels[offset + 3] * 64L;
                sayR += weightA * r;
                sayG += weightA * g;
                sayB += weightA * b;
                sayA += weightA * a;
                sbyR += weightB * r;
                sbyG += weightB * g;
                sbyB += weightB * b;
                sbyA += weightB * a;
            }

            long determinant = saa * sbb - sab * sab;
            if (determinant <= 0L)
                return;

            endpoint0[0] = SolveEndpoint(sayR, sbyR, sbb, sab, determinant);
            endpoint0[1] = SolveEndpoint(sayG, sbyG, sbb, sab, determinant);
            endpoint0[2] = SolveEndpoint(sayB, sbyB, sbb, sab, determinant);
            endpoint0[3] = SolveEndpoint(sayA, sbyA, sbb, sab, determinant);

            endpoint1[0] = SolveEndpoint(sbyR, sayR, saa, sab, determinant);
            endpoint1[1] = SolveEndpoint(sbyG, sayG, saa, sab, determinant);
            endpoint1[2] = SolveEndpoint(sbyB, sayB, saa, sab, determinant);
            endpoint1[3] = SolveEndpoint(sbyA, sayA, saa, sab, determinant);
        }

        static int SolveEndpoint(long sy, long otherSy, long diagonal, long cross, long determinant)
        {
            long numerator = sy * diagonal - otherSy * cross;
            if (numerator >= 0L)
                return ClampByte((int)((numerator + determinant / 2L) / determinant));
            return ClampByte((int)((numerator - determinant / 2L) / determinant));
        }

        static void SwapEndpointArrays(int[] left, int[] right)
        {
            for (int channel = 0; channel < 4; channel++)
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

            // BC7 mode marker: six zero bits followed by one bit.
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

            PutBits(ref low, ref high, ref bitPosition, (uint)indices[0], 3);
            for (int pixel = 1; pixel < 16; pixel++)
                PutBits(ref low, ref high, ref bitPosition, (uint)indices[pixel], 4);

            WriteUInt64LittleEndian(output, 0, low);
            WriteUInt64LittleEndian(output, 8, high);
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
