// ReSharper disable RedundantUsingDirective
using System;
using System.IO;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using Adk.Image;
using Adk.Image.Png;

namespace Adk.Image.Dds
{
    /// <summary>
    /// DDS output formats needed by Space Engineers
    /// </summary>
    public enum DdsOutputFormat
    {
        Bc7Typeless = 98,
        Bc7Unorm = 99,
        Bc7UnormSrgb = 100,
        LegacyBgra8 = 1000
    }

    public enum DdsMipFilter
    {
        Box = 0,
        NormalGloss = 1,
        Point = 2
    }

    public sealed class DdsEncodeOptions
    {
        public DdsOutputFormat Format { get; set; }
        public bool GenerateMipmaps { get; set; }
        public int MaximumMipLevels { get; set; }
        public DdsMipFilter MipFilter { get; set; }
        public Bc7Quality Bc7Quality { get; set; }

        public DdsEncodeOptions()
        {
            Format = DdsOutputFormat.Bc7Unorm;
            GenerateMipmaps = true;
            MaximumMipLevels = 0;
            MipFilter = DdsMipFilter.Box;
            Bc7Quality = Bc7Quality.Fast;
        }
    }

    /// <summary>
    /// Pure managed DDS writer for runtime-generated RGBA8 data.
    /// Produces DX10 BC7 DDS files (typeless/UNORM/sRGB) or
    /// 32-bit BGRA layout
    /// </summary>
    public static class DdsEncoder
    {
        const uint DdsMagic = 0x20534444u;
        const uint HeaderSize = 124u;
        const uint PixelFormatSize = 32u;

        const uint DdsdCaps = 0x00000001u;
        const uint DdsdHeight = 0x00000002u;
        const uint DdsdWidth = 0x00000004u;
        const uint DdsdPitch = 0x00000008u;
        const uint DdsdPixelFormat = 0x00001000u;
        const uint DdsdMipMapCount = 0x00020000u;
        const uint DdsdLinearSize = 0x00080000u;

        const uint DdpfAlphaPixels = 0x00000001u;
        const uint DdpfFourCc = 0x00000004u;
        const uint DdpfRgb = 0x00000040u;

        const uint DdsCapsComplex = 0x00000008u;
        const uint DdsCapsTexture = 0x00001000u;
        const uint DdsCapsMipMap = 0x00400000u;

        const uint FourCcDx10 = 0x30315844u;
        const uint D3d10ResourceDimensionTexture2D = 3u;

        sealed class StreamOutput : IDdsByteOutput
        {
            readonly Stream _stream;

            public StreamOutput(Stream stream)
            {
                _stream = stream;
            }

            public void Write(byte[] data, int offset, int count)
            {
                _stream.Write(data, offset, count);
            }
        }

        sealed class BinaryWriterOutput : IDdsByteOutput
        {
            readonly BinaryWriter _writer;

            public BinaryWriterOutput(BinaryWriter writer)
            {
                _writer = writer;
            }

            public void Write(byte[] data, int offset, int count)
            {
                _writer.Write(data, offset, count);
            }
        }

        sealed class ArrayOutput : IDdsByteOutput
        {
            readonly byte[] _data;
            int _offset;

            public ArrayOutput(byte[] data)
            {
                _data = data;
            }

            public void Write(byte[] data, int offset, int count)
            {
                Buffer.BlockCopy(data, offset, _data, _offset, count);
                _offset += count;
            }
        }

        public static byte[] Encode(RawRgbaBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");

            return Encode(
                bitmap.Pixels,
                bitmap.Width,
                bitmap.Height,
                bitmap.Stride,
                options);
        }

        public static byte[] Encode(RawPngBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");

            return Encode(
                bitmap.Pixels,
                bitmap.Width,
                bitmap.Height,
                bitmap.Stride,
                options);
        }

        public static byte[] Encode(
            byte[] rgba,
            int width,
            int height,
            int stride,
            DdsEncodeOptions options)
        {
            byte[] output =
                new byte[GetEncodedSize(
                    width,
                    height,
                    options)];

            WriteCore(
                new ArrayOutput(output),
                rgba,
                width,
                height,
                stride,
                options);

            return output;
        }

        public static void Write(Stream output, RawRgbaBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");
            Write(output, bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, options);
        }

        public static void Write(Stream output, RawPngBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");
            Write(output, bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, options);
        }

        public static void Write(BinaryWriter output, RawRgbaBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");
            Write(output, bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, options);
        }

        public static void Write(BinaryWriter output, RawPngBitmap bitmap, DdsEncodeOptions options)
        {
            if (bitmap == null)
                throw new ArgumentNullException("bitmap");
            Write(output, bitmap.Pixels, bitmap.Width, bitmap.Height, bitmap.Stride, options);
        }

        public static void Write(
            Stream output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            DdsEncodeOptions options)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            if (!output.CanWrite)
                throw new ArgumentException("Output stream is not writable.", "output");

            WriteCore(new StreamOutput(output), rgba, width, height, stride, options);
        }

        public static void Write(
            BinaryWriter output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            DdsEncodeOptions options)
        {
            if (output == null)
                throw new ArgumentNullException("output");
            WriteCore(new BinaryWriterOutput(output), rgba, width, height, stride, options);
        }

        static void WriteCore(
            IDdsByteOutput output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            DdsEncodeOptions options)
        {
            if (rgba == null)
                throw new ArgumentNullException("rgba");
            if (options == null)
                throw new ArgumentNullException("options");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            if (stride < checked(width * 4))
                throw new ArgumentOutOfRangeException("stride");

            long required = (long)(height - 1) * stride + (long)width * 4L;
            if (required > rgba.Length)
                throw new ArgumentException("RGBA buffer is shorter than width/height/stride require.", "rgba");

            int mipCount = options.GenerateMipmaps
                ? GetMipCount(width, height, options.MaximumMipLevels)
                : 1;

            WriteHeader(output, width, height, mipCount, options.Format);

            byte[] current = rgba;
            int currentWidth = width;
            int currentHeight = height;
            int currentStride = stride;

            for (int mip = 0; mip < mipCount; mip++)
            {
                if (options.Format == DdsOutputFormat.LegacyBgra8)
                {
                    WriteLegacyBgraLevel(
                        output,
                        current,
                        currentWidth,
                        currentHeight,
                        currentStride);
                }
                else
                {
                    WriteBc7Level(
                        output,
                        current,
                        currentWidth,
                        currentHeight,
                        currentStride,
                        options.Bc7Quality);
                }

                if (mip + 1 >= mipCount)
                    break;

                int nextWidth;
                int nextHeight;
                byte[] next = BuildNextMip(
                    current,
                    currentWidth,
                    currentHeight,
                    currentStride,
                    options.MipFilter,
                    out nextWidth,
                    out nextHeight);
                current = next;
                currentWidth = nextWidth;
                currentHeight = nextHeight;
                currentStride = checked(nextWidth * 4);
            }
        }

        static int GetMipCount(int width, int height, int maximumMipLevels)
        {
            int count = 1;
            while (width > 1 || height > 1)
            {
                width = width > 1 ? width / 2 : 1;
                height = height > 1 ? height / 2 : 1;
                count++;
                if (maximumMipLevels > 0 && count >= maximumMipLevels)
                    break;
            }

            return count;
        }

        static int GetEncodedSize(
            int width,
            int height,
            DdsEncodeOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");

            bool legacy =
                options.Format == DdsOutputFormat.LegacyBgra8;

            int mipCount =
                options.GenerateMipmaps
                    ? GetMipCount(
                        width,
                        height,
                        options.MaximumMipLevels)
                    : 1;

            long size =
                legacy
                    ? 128L
                    : 148L;

            for (int mip = 0;
                mip < mipCount;
                mip++)
            {
                if (legacy)
                {
                    size +=
                        (long)width *
                        height *
                        4L;
                }
                else
                {
                    size +=
                        ((width + 3L) / 4L) *
                        ((height + 3L) / 4L) *
                        16L;
                }

                if (size > int.MaxValue)
                    throw new ArgumentOutOfRangeException("width/height");

                width =
                    width > 1
                        ? width / 2
                        : 1;
                height =
                    height > 1
                        ? height / 2
                        : 1;
            }

            return (int)size;
        }

        static void WriteHeader(
            IDdsByteOutput output,
            int width,
            int height,
            int mipCount,
            DdsOutputFormat format)
        {
            bool legacy = format == DdsOutputFormat.LegacyBgra8;
            byte[] header = new byte[legacy ? 128 : 148];
            int offset = 0;

            WriteUInt32(header, ref offset, DdsMagic);
            WriteUInt32(header, ref offset, HeaderSize);

            uint flags = DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat;
            flags |= legacy ? DdsdPitch : DdsdLinearSize;
            if (mipCount > 1)
                flags |= DdsdMipMapCount;
            WriteUInt32(header, ref offset, flags);
            WriteUInt32(header, ref offset, (uint)height);
            WriteUInt32(header, ref offset, (uint)width);

            uint pitchOrLinearSize;
            if (legacy)
            {
                pitchOrLinearSize = checked((uint)(width * 4));
            }
            else
            {
                long blocksX = (width + 3L) / 4L;
                long blocksY = (height + 3L) / 4L;
                pitchOrLinearSize = checked((uint)(blocksX * blocksY * 16L));
            }
            WriteUInt32(header, ref offset, pitchOrLinearSize);
            WriteUInt32(header, ref offset, 1u);
            WriteUInt32(header, ref offset, (uint)mipCount);
            for (int i = 0; i < 11; i++)
                WriteUInt32(header, ref offset, 0u);

            WriteUInt32(header, ref offset, PixelFormatSize);
            if (legacy)
            {
                WriteUInt32(header, ref offset, DdpfRgb | DdpfAlphaPixels);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 32u);
                WriteUInt32(header, ref offset, 0x00ff0000u);
                WriteUInt32(header, ref offset, 0x0000ff00u);
                WriteUInt32(header, ref offset, 0x000000ffu);
                WriteUInt32(header, ref offset, 0xff000000u);
            }
            else
            {
                WriteUInt32(header, ref offset, DdpfFourCc);
                WriteUInt32(header, ref offset, FourCcDx10);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 0u);
            }

            uint caps = DdsCapsTexture;
            if (mipCount > 1)
                caps |= DdsCapsComplex | DdsCapsMipMap;
            WriteUInt32(header, ref offset, caps);
            WriteUInt32(header, ref offset, 0u);
            WriteUInt32(header, ref offset, 0u);
            WriteUInt32(header, ref offset, 0u);
            WriteUInt32(header, ref offset, 0u);

            if (!legacy)
            {
                WriteUInt32(header, ref offset, (uint)format);
                WriteUInt32(header, ref offset, D3d10ResourceDimensionTexture2D);
                WriteUInt32(header, ref offset, 0u);
                WriteUInt32(header, ref offset, 1u);
                WriteUInt32(header, ref offset, 0u);
            }

            output.Write(header, 0, header.Length);
        }

        static void WriteBc7Level(
            IDdsByteOutput output,
            byte[] rgba,
            int width,
            int height,
            int stride,
            Bc7Quality quality)
        {
            // Stream block rows directly to the destination so an 8192x4096
            // texture does not need another ~32 MiB compressed-mip buffer.
            Bc7Encoder.EncodeToOutput(
                output,
                rgba,
                width,
                height,
                stride,
                quality);
        }

        static void WriteLegacyBgraLevel(
            IDdsByteOutput output,
            byte[] rgba,
            int width,
            int height,
            int stride)
        {
            byte[] row = new byte[checked(width * 4)];
            for (int y = 0; y < height; y++)
            {
                int source = y * stride;
                int destination = 0;
                for (int x = 0; x < width; x++)
                {
                    byte r = rgba[source];
                    byte g = rgba[source + 1];
                    byte b = rgba[source + 2];
                    byte a = rgba[source + 3];
                    row[destination] = b;
                    row[destination + 1] = g;
                    row[destination + 2] = r;
                    row[destination + 3] = a;
                    source += 4;
                    destination += 4;
                }

                output.Write(row, 0, row.Length);
            }
        }

        static byte[] BuildNextMip(
            byte[] source,
            int width,
            int height,
            int stride,
            DdsMipFilter filter,
            out int nextWidth,
            out int nextHeight)
        {
            nextWidth = width > 1 ? width / 2 : 1;
            nextHeight = height > 1 ? height / 2 : 1;
            byte[] destination = new byte[checked(nextWidth * nextHeight * 4)];

            for (int y = 0; y < nextHeight; y++)
            {
                for (int x = 0; x < nextWidth; x++)
                {
                    int destinationOffset = (y * nextWidth + x) * 4;
                    if (filter == DdsMipFilter.Point)
                    {
                        int sourceX = Math.Min(width - 1, x * 2);
                        int sourceY = Math.Min(height - 1, y * 2);
                        int sourceOffset = sourceY * stride + sourceX * 4;
                        destination[destinationOffset] = source[sourceOffset];
                        destination[destinationOffset + 1] = source[sourceOffset + 1];
                        destination[destinationOffset + 2] = source[sourceOffset + 2];
                        destination[destinationOffset + 3] = source[sourceOffset + 3];
                    }
                    else if (filter == DdsMipFilter.NormalGloss)
                    {
                        FilterNormalGloss(
                            source,
                            width,
                            height,
                            stride,
                            x,
                            y,
                            destination,
                            destinationOffset);
                    }
                    else
                    {
                        FilterBox(
                            source,
                            width,
                            height,
                            stride,
                            x,
                            y,
                            destination,
                            destinationOffset);
                    }
                }
            }

            return destination;
        }

        static void FilterBox(
            byte[] source,
            int width,
            int height,
            int stride,
            int destinationX,
            int destinationY,
            byte[] destination,
            int destinationOffset)
        {
            int startX = destinationX * 2;
            int startY = destinationY * 2;
            int endX = Math.Min(width, startX + 2);
            int endY = Math.Min(height, startY + 2);
            int r = 0;
            int g = 0;
            int b = 0;
            int a = 0;
            int count = 0;

            for (int y = startY; y < endY; y++)
            {
                int row = y * stride;
                for (int x = startX; x < endX; x++)
                {
                    int sourceOffset = row + x * 4;
                    r += source[sourceOffset];
                    g += source[sourceOffset + 1];
                    b += source[sourceOffset + 2];
                    a += source[sourceOffset + 3];
                    count++;
                }
            }

            destination[destinationOffset] = (byte)((r + count / 2) / count);
            destination[destinationOffset + 1] = (byte)((g + count / 2) / count);
            destination[destinationOffset + 2] = (byte)((b + count / 2) / count);
            destination[destinationOffset + 3] = (byte)((a + count / 2) / count);
        }

        static void FilterNormalGloss(
            byte[] source,
            int width,
            int height,
            int stride,
            int destinationX,
            int destinationY,
            byte[] destination,
            int destinationOffset)
        {
            int startX = destinationX * 2;
            int startY = destinationY * 2;
            int endX = Math.Min(width, startX + 2);
            int endY = Math.Min(height, startY + 2);
            double nx = 0.0;
            double ny = 0.0;
            double nz = 0.0;
            int alpha = 0;
            int count = 0;

            for (int y = startY; y < endY; y++)
            {
                int row = y * stride;
                for (int x = startX; x < endX; x++)
                {
                    int sourceOffset = row + x * 4;
                    nx += source[sourceOffset] / 127.5 - 1.0;
                    ny += source[sourceOffset + 1] / 127.5 - 1.0;
                    nz += source[sourceOffset + 2] / 127.5 - 1.0;
                    alpha += source[sourceOffset + 3];
                    count++;
                }
            }

            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length > 0.000001)
            {
                nx /= length;
                ny /= length;
                nz /= length;
            }
            else
            {
                nx = 0.0;
                ny = 0.0;
                nz = 1.0;
            }

            destination[destinationOffset] = ToNormalByte(nx);
            destination[destinationOffset + 1] = ToNormalByte(ny);
            destination[destinationOffset + 2] = ToNormalByte(nz);
            destination[destinationOffset + 3] = (byte)((alpha + count / 2) / count);
        }

        static byte ToNormalByte(double value)
        {
            int encoded = (int)(value * 127.5 + 127.5 + 0.5);
            if (encoded < 0)
                encoded = 0;
            else if (encoded > 255)
                encoded = 255;
            return (byte)encoded;
        }

        static void WriteUInt32(byte[] destination, ref int offset, uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
            offset += 4;
        }
    }
}
