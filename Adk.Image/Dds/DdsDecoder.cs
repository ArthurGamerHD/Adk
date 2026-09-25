// ReSharper disable RedundantUsingDirective
using System;
using System.IO;
using Adk.Image;

namespace Adk.Image.Dds
{
    /// <summary>
    /// Small managed DDS decoder aimed at the formats used by Space Engineers
    /// content. Decodes the first mip into top-to-bottom RGBA8 pixels.
    ///
    /// Supported inputs:
    /// - legacy DXT1 / DX10 BC1 UNORM(/sRGB)
    /// - legacy DXT5 / DX10 BC3 UNORM(/sRGB)
    /// - DX10 BC4 TYPELESS/UNORM (decoded as grayscale RGB, A=255)
    /// - DX10 BC7 TYPELESS/UNORM(/sRGB), all BC7 block modes 0-7
    /// - legacy uncompressed 24-bit and 32-bit RGB(A) DDS
    ///
    /// BC7 modes 0-7 are decoded in managed code. The implementation uses
    /// compact partition/anchor tables and reuses per-image scratch arrays so
    /// it does not allocate temporary objects for each 4x4 block.
    /// </summary>
    public static class DdsDecoder
    {
        const uint DdsMagic = 0x20534444u;
        const uint DdsHeaderSize = 124u;

        const uint DdpfAlphaPixels = 0x00000001u;
        const uint DdpfFourCc = 0x00000004u;
        const uint DdpfRgb = 0x00000040u;

        const uint FourCcDxt1 = 0x31545844u;
        const uint FourCcDxt5 = 0x35545844u;
        const uint FourCcAti1 = 0x31495441u; // "ATI1"
        const uint FourCcBc4U = 0x55344342u; // "BC4U"
        const uint FourCcDx10 = 0x30315844u;

        const uint DxgiFormatBc1Typeless = 70u;
        const uint DxgiFormatBc1Unorm = 71u;
        const uint DxgiFormatBc1UnormSrgb = 72u;
        const uint DxgiFormatBc3Typeless = 76u;
        const uint DxgiFormatBc3Unorm = 77u;
        const uint DxgiFormatBc3UnormSrgb = 78u;
        const uint DxgiFormatBc4Typeless = 79u;
        const uint DxgiFormatBc4Unorm = 80u;
        const uint DxgiFormatBc7Typeless = 98u;
        const uint DxgiFormatBc7Unorm = 99u;
        const uint DxgiFormatBc7UnormSrgb = 100u;

        const int DdsHeaderBytes = 128;
        const int DdsDx10HeaderBytes = 20;

        static readonly int[] Bc7Weights2 =
        {
            0, 21, 43, 64
        };

        static readonly int[] Bc7Weights3 =
        {
            0, 9, 18, 27, 37, 46, 55, 64
        };

        static readonly int[] Bc7Weights4 =
        {
            0, 4, 9, 13, 17, 21, 26, 30,
            34, 38, 43, 47, 51, 55, 60, 64
        };

        // BC7 partition maps packed in texel order. For two-subset modes each
        // ushort stores one subset bit per texel. For three-subset modes each
        // uint stores two subset bits per texel.
        static readonly ushort[] Bc7Partition2Masks =
        {
            0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80,
            0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
            0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE,
            0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
            0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A,
            0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
            0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C,
            0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22
        };

        static readonly uint[] Bc7Partition3Packed =
        {
            0xAA685050u, 0x6A5A5040u, 0x5A5A4200u, 0x5450A0A8u,
            0xA5A50000u, 0xA0A05050u, 0x5555A0A0u, 0x5A5A5050u,
            0xAA550000u, 0xAA555500u, 0xAAAA5500u, 0x90909090u,
            0x94949494u, 0xA4A4A4A4u, 0xA9A59450u, 0x2A0A4250u,
            0xA5945040u, 0x0A425054u, 0xA5A5A500u, 0x55A0A0A0u,
            0xA8A85454u, 0x6A6A4040u, 0xA4A45000u, 0x1A1A0500u,
            0x0050A4A4u, 0xAAA59090u, 0x14696914u, 0x69691400u,
            0xA08585A0u, 0xAA821414u, 0x50A4A450u, 0x6A5A0200u,
            0xA9A58000u, 0x5090A0A8u, 0xA8A09050u, 0x24242424u,
            0x00AA5500u, 0x24924924u, 0x24499224u, 0x50A50A50u,
            0x500AA550u, 0xAAAA4444u, 0x66660000u, 0xA5A0A5A0u,
            0x50A050A0u, 0x69286928u, 0x44AAAA44u, 0x66666600u,
            0xAA444444u, 0x54A854A8u, 0x95809580u, 0x96969600u,
            0xA85454A8u, 0x80959580u, 0xAA141414u, 0x96960000u,
            0xAAAA1414u, 0xA05050A0u, 0xA0A5A5A0u, 0x96000000u,
            0x40804080u, 0xA9A8A9A8u, 0xAAAAAA44u, 0x2A4A5254u
        };

        static readonly byte[] Bc7Anchor2 =
        {
            15,15,15,15,15,15,15,15, 15,15,15,15,15,15,15,15,
            15,2,8,2,2,8,8,15, 2,8,2,2,8,8,2,2,
            15,15,6,8,2,8,15,15, 2,8,2,2,2,15,15,6,
            6,2,6,8,15,15,2,2, 15,15,15,15,15,2,2,15
        };

        static readonly byte[] Bc7Anchor3Subset1 =
        {
            3,3,15,15,8,3,15,15, 8,8,6,6,6,5,3,3,
            3,3,8,15,3,3,6,10, 5,8,8,6,8,5,15,15,
            8,15,3,5,6,10,8,15, 15,3,15,5,15,15,15,15,
            3,15,5,5,5,8,5,10, 5,10,8,13,15,12,3,3
        };

        static readonly byte[] Bc7Anchor3Subset2 =
        {
            15,8,8,3,15,15,3,8, 15,15,15,15,15,15,15,8,
            15,8,15,3,15,8,15,8, 3,15,6,10,15,15,10,8,
            15,3,15,10,10,8,9,10, 6,15,8,15,3,6,6,8,
            15,3,15,15,15,15,15,15, 15,15,15,15,3,15,15,8
        };

        enum DdsPixelEncoding
        {
            Unknown,
            Bc1,
            Bc3,
            Bc4,
            Bc7,
            Rgb
        }

        struct DdsInfo
        {
            public int Width;
            public int Height;
            public int DataOffset;
            public DdsPixelEncoding Encoding;
            public uint RgbBitCount;
            public uint RMask;
            public uint GMask;
            public uint BMask;
            public uint AMask;
            public bool HasAlpha;
        }

        /// <summary>
        /// Decodes the first DDS mip into a mutable RGBA8 bitmap.
        /// </summary>
        public static RawRgbaBitmap Decode(byte[] data)
        {
            RawRgbaBitmap bitmap;
            string failureReason;
            if (!TryDecode(data, out bitmap, out failureReason))
                throw new NotSupportedException(failureReason ?? "DDS data could not be decoded.");

            return bitmap;
        }

        /// <summary>
        /// Reads and decodes only the first DDS mip from a stream.
        /// The stream path is forward-only and buffers only mip 0.
        /// </summary>
        public static RawRgbaBitmap Decode(Stream input)
        {
            RawRgbaBitmap bitmap;
            string failureReason;
            if (!TryDecode(input, out bitmap, out failureReason))
                throw new NotSupportedException(failureReason ?? "DDS stream could not be decoded.");

            return bitmap;
        }

        public static bool TryDecode(byte[] data, out RawRgbaBitmap bitmap)
        {
            string failureReason;
            return TryDecode(data, out bitmap, out failureReason);
        }

        public static bool TryDecode(
            byte[] data,
            out RawRgbaBitmap bitmap,
            out string failureReason)
        {
            bitmap = null;
            failureReason = null;

            DdsInfo info;
            if (!TryReadInfo(data, out info, out failureReason))
                return false;

            int requiredBytes;
            if (!TryGetFirstMipByteCount(info, out requiredBytes))
            {
                failureReason = "Could not calculate the first DDS mip byte count.";
                return false;
            }

            if (info.DataOffset < 0 ||
                info.DataOffset > data.Length ||
                requiredBytes > data.Length - info.DataOffset)
            {
                failureReason = "DDS first mip is truncated: expected " + requiredBytes +
                                " bytes at offset " + info.DataOffset + ".";
                return false;
            }

            return TryDecodeFirstMip(
                data,
                info.DataOffset,
                info,
                out bitmap,
                out failureReason);
        }

        public static bool TryDecode(Stream input, out RawRgbaBitmap bitmap)
        {
            string failureReason;
            return TryDecode(input, out bitmap, out failureReason);
        }

        public static bool TryDecode(
            Stream input,
            out RawRgbaBitmap bitmap,
            out string failureReason)
        {
            bitmap = null;
            failureReason = null;

            if (input == null)
            {
                failureReason = "DDS stream is null.";
                return false;
            }

            if (!input.CanRead)
            {
                failureReason = "DDS stream is not readable.";
                return false;
            }

            bool restorePosition = false;
            long start = 0;

            try
            {
                if (input.CanSeek)
                {
                    start = input.Position;
                    restorePosition = true;
                }

                // Read only the fixed DDS header first. A DX10 texture appends
                // a 20-byte extension, detected from the base header. This is
                // intentionally compatible with forward-only game streams.
                byte[] header = new byte[DdsHeaderBytes + DdsDx10HeaderBytes];
                if (!TryReadExactly(input, header, 0, DdsHeaderBytes))
                {
                    failureReason = "DDS header is truncated before 128 bytes.";
                    return false;
                }

                uint pixelFormatFlags = ReadUInt32(header, 80);
                uint fourCc = ReadUInt32(header, 84);
                if ((pixelFormatFlags & DdpfFourCc) != 0 && fourCc == FourCcDx10)
                {
                    if (!TryReadExactly(
                            input,
                            header,
                            DdsHeaderBytes,
                            DdsDx10HeaderBytes))
                    {
                        failureReason = "DDS declares a DX10 header, but it is truncated.";
                        return false;
                    }
                }

                DdsInfo info;
                if (!TryReadInfo(header, out info, out failureReason))
                    return false;

                int requiredBytes;
                if (!TryGetFirstMipByteCount(info, out requiredBytes))
                {
                    failureReason = "Could not calculate the first DDS mip byte count.";
                    return false;
                }

                // At this point the stream is exactly at info.DataOffset. Read
                // only mip 0; do not buffer the remainder of the DDS file.
                byte[] mipData = new byte[requiredBytes];
                if (!TryReadExactly(input, mipData, 0, mipData.Length))
                {
                    failureReason = "DDS first mip is truncated: expected " +
                                    requiredBytes + " bytes.";
                    return false;
                }

                return TryDecodeFirstMip(
                    mipData,
                    0,
                    info,
                    out bitmap,
                    out failureReason);
            }
            catch (NotSupportedException error)
            {
                failureReason = "Stream operation is not supported while reading DDS: " +
                                error.Message;
                return false;
            }
            catch (Exception error)
            {
                failureReason = "Could not read DDS stream: " + error.Message;
                return false;
            }
            finally
            {
                if (restorePosition)
                {
                    try
                    {
                        input.Position = start;
                    }
                    catch
                    {
                        // Position restoration is best effort only.
                    }
                }
            }
        }

        static bool TryDecodeFirstMip(
            byte[] data,
            int dataOffset,
            DdsInfo info,
            out RawRgbaBitmap bitmap,
            out string failureReason)
        {
            bitmap = null;
            failureReason = null;

            RawRgbaBitmap output;
            try
            {
                output = new RawRgbaBitmap(info.Width, info.Height);
            }
            catch (Exception error)
            {
                failureReason = "Could not allocate DDS output bitmap: " + error.Message;
                return false;
            }

            bool decoded;
            if (info.Encoding == DdsPixelEncoding.Bc1)
            {
                decoded = TryDecodeBc1(
                    data, dataOffset, info.Width, info.Height,
                    output.Pixels, output.Stride, out failureReason);
            }
            else if (info.Encoding == DdsPixelEncoding.Bc3)
            {
                decoded = TryDecodeBc3(
                    data, dataOffset, info.Width, info.Height,
                    output.Pixels, output.Stride, out failureReason);
            }
            else if (info.Encoding == DdsPixelEncoding.Bc4)
            {
                decoded = TryDecodeBc4(
                    data, dataOffset, info.Width, info.Height,
                    output.Pixels, output.Stride, out failureReason);
            }
            else if (info.Encoding == DdsPixelEncoding.Bc7)
            {
                decoded = TryDecodeBc7(
                    data, dataOffset, info.Width, info.Height,
                    output.Pixels, output.Stride, out failureReason);
            }
            else if (info.Encoding == DdsPixelEncoding.Rgb)
            {
                decoded = TryDecodeRgb(
                    data,
                    dataOffset,
                    info.Width,
                    info.Height,
                    info.RgbBitCount,
                    info.RMask,
                    info.GMask,
                    info.BMask,
                    info.AMask,
                    info.HasAlpha,
                    output.Pixels,
                    output.Stride,
                    out failureReason);
            }
            else
            {
                failureReason = "Unsupported DDS pixel encoding.";
                decoded = false;
            }

            if (!decoded)
                return false;

            bitmap = output;
            return true;
        }

        static bool TryReadExactly(Stream input, byte[] output, int offset, int count)
        {
            while (count > 0)
            {
                int read = input.Read(output, offset, count);
                if (read <= 0)
                    return false;

                offset += read;
                count -= read;
            }

            return true;
        }

        static bool TryReadInfo(byte[] data, out DdsInfo info, out string failureReason)
        {
            info = default(DdsInfo);
            failureReason = null;

            if (data == null)
            {
                failureReason = "DDS data is null.";
                return false;
            }

            if (data.Length < DdsHeaderBytes)
            {
                failureReason = "DDS header is truncated before 128 bytes.";
                return false;
            }

            uint magic = ReadUInt32(data, 0);
            if (magic != DdsMagic)
            {
                failureReason = "Invalid DDS magic 0x" + magic.ToString("X8") + ".";
                return false;
            }

            uint headerSize = ReadUInt32(data, 4);
            if (headerSize != DdsHeaderSize)
            {
                failureReason = "Invalid DDS header size " + headerSize + "; expected 124.";
                return false;
            }

            int height = (int)ReadUInt32(data, 12);
            int width = (int)ReadUInt32(data, 16);
            if (width <= 0 || height <= 0)
            {
                failureReason = "Invalid DDS dimensions " + width + "x" + height + ".";
                return false;
            }

            info.Width = width;
            info.Height = height;
            info.DataOffset = DdsHeaderBytes;

            uint pixelFormatFlags = ReadUInt32(data, 80);
            uint fourCc = ReadUInt32(data, 84);

            if ((pixelFormatFlags & DdpfFourCc) != 0)
            {
                if (fourCc == FourCcDxt1)
                {
                    info.Encoding = DdsPixelEncoding.Bc1;
                    return true;
                }

                if (fourCc == FourCcDxt5)
                {
                    info.Encoding = DdsPixelEncoding.Bc3;
                    return true;
                }

                if (fourCc == FourCcAti1 || fourCc == FourCcBc4U)
                {
                    info.Encoding = DdsPixelEncoding.Bc4;
                    return true;
                }

                if (fourCc != FourCcDx10)
                {
                    failureReason = "Unsupported DDS FourCC " + FormatFourCc(fourCc) + ".";
                    return false;
                }

                if (data.Length < DdsHeaderBytes + DdsDx10HeaderBytes)
                {
                    failureReason = "DDS declares a DX10 header, but it is truncated.";
                    return false;
                }

                uint dxgiFormat = ReadUInt32(data, DdsHeaderBytes);
                info.DataOffset += DdsDx10HeaderBytes;

                if (dxgiFormat == DxgiFormatBc1Typeless ||
                    dxgiFormat == DxgiFormatBc1Unorm ||
                    dxgiFormat == DxgiFormatBc1UnormSrgb)
                {
                    info.Encoding = DdsPixelEncoding.Bc1;
                    return true;
                }

                if (dxgiFormat == DxgiFormatBc3Typeless ||
                    dxgiFormat == DxgiFormatBc3Unorm ||
                    dxgiFormat == DxgiFormatBc3UnormSrgb)
                {
                    info.Encoding = DdsPixelEncoding.Bc3;
                    return true;
                }

                if (dxgiFormat == DxgiFormatBc4Typeless ||
                    dxgiFormat == DxgiFormatBc4Unorm)
                {
                    info.Encoding = DdsPixelEncoding.Bc4;
                    return true;
                }

                if (dxgiFormat == DxgiFormatBc7Typeless ||
                    dxgiFormat == DxgiFormatBc7Unorm ||
                    dxgiFormat == DxgiFormatBc7UnormSrgb)
                {
                    info.Encoding = DdsPixelEncoding.Bc7;
                    return true;
                }

                failureReason = "Unsupported DDS DXGI format " + dxgiFormat +
                                "; supported compressed formats are BC1, BC3, BC4, and BC7.";
                return false;
            }

            if ((pixelFormatFlags & DdpfRgb) == 0)
            {
                failureReason = "DDS pixel format is neither supported FourCC/DX10 nor RGB.";
                return false;
            }

            info.Encoding = DdsPixelEncoding.Rgb;
            info.RgbBitCount = ReadUInt32(data, 88);
            info.RMask = ReadUInt32(data, 92);
            info.GMask = ReadUInt32(data, 96);
            info.BMask = ReadUInt32(data, 100);
            info.AMask = ReadUInt32(data, 104);
            info.HasAlpha = (pixelFormatFlags & DdpfAlphaPixels) != 0 && info.AMask != 0;

            if (info.RgbBitCount != 24u && info.RgbBitCount != 32u)
            {
                failureReason = "Unsupported uncompressed DDS RGB bit count " +
                                info.RgbBitCount + "; supported values are 24 and 32.";
                return false;
            }

            return true;
        }

        static bool TryGetFirstMipByteCount(DdsInfo info, out int byteCount)
        {
            byteCount = 0;
            long result;

            if (info.Encoding == DdsPixelEncoding.Bc1 ||
                info.Encoding == DdsPixelEncoding.Bc4)
            {
                result = (long)((info.Width + 3) / 4) * ((info.Height + 3) / 4) * 8L;
            }
            else if (info.Encoding == DdsPixelEncoding.Bc3 ||
                     info.Encoding == DdsPixelEncoding.Bc7)
            {
                result = (long)((info.Width + 3) / 4) * ((info.Height + 3) / 4) * 16L;
            }
            else if (info.Encoding == DdsPixelEncoding.Rgb)
            {
                result = (long)((info.Width * (int)info.RgbBitCount + 7) / 8) * info.Height;
            }
            else
            {
                return false;
            }

            if (result <= 0 || result > int.MaxValue)
                return false;

            byteCount = (int)result;
            return true;
        }

        static bool TryDecodeBc1(
            byte[] data,
            int offset,
            int width,
            int height,
            byte[] output,
            int stride,
            out string failureReason)
        {
            failureReason = null;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            byte[] palette = new byte[16];

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    ushort color0 = ReadUInt16(data, offset);
                    ushort color1 = ReadUInt16(data, offset + 2);
                    uint indices = ReadUInt32(data, offset + 4);
                    offset += 8;

                    DecodeRgb565(color0, palette, 0);
                    palette[3] = 255;
                    DecodeRgb565(color1, palette, 4);
                    palette[7] = 255;

                    if (color0 > color1)
                    {
                        palette[8] = (byte)((2 * palette[0] + palette[4]) / 3);
                        palette[9] = (byte)((2 * palette[1] + palette[5]) / 3);
                        palette[10] = (byte)((2 * palette[2] + palette[6]) / 3);
                        palette[11] = 255;
                        palette[12] = (byte)((palette[0] + 2 * palette[4]) / 3);
                        palette[13] = (byte)((palette[1] + 2 * palette[5]) / 3);
                        palette[14] = (byte)((palette[2] + 2 * palette[6]) / 3);
                        palette[15] = 255;
                    }
                    else
                    {
                        palette[8] = (byte)((palette[0] + palette[4]) / 2);
                        palette[9] = (byte)((palette[1] + palette[5]) / 2);
                        palette[10] = (byte)((palette[2] + palette[6]) / 2);
                        palette[11] = 255;
                        palette[12] = 0;
                        palette[13] = 0;
                        palette[14] = 0;
                        palette[15] = 0;
                    }

                    for (int pixel = 0; pixel < 16; pixel++)
                    {
                        int x = pixel & 3;
                        int y = pixel >> 2;
                        int pixelX = blockX * 4 + x;
                        int pixelY = blockY * 4 + y;
                        if (pixelX >= width || pixelY >= height)
                            continue;

                        int selector = (int)((indices >> (pixel * 2)) & 3u);
                        int source = selector * 4;
                        int destination = pixelY * stride + pixelX * 4;
                        output[destination] = palette[source];
                        output[destination + 1] = palette[source + 1];
                        output[destination + 2] = palette[source + 2];
                        output[destination + 3] = palette[source + 3];
                    }
                }
            }

            return true;
        }

        static bool TryDecodeBc3(
            byte[] data,
            int offset,
            int width,
            int height,
            byte[] output,
            int stride,
            out string failureReason)
        {
            failureReason = null;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            byte[] alphaPalette = new byte[8];
            byte[] colorPalette = new byte[12];

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    byte alpha0 = data[offset];
                    byte alpha1 = data[offset + 1];
                    BuildBc4Palette(alpha0, alpha1, alphaPalette);

                    ulong alphaIndices = 0;
                    for (int i = 0; i < 6; i++)
                        alphaIndices |= (ulong)data[offset + 2 + i] << (8 * i);

                    ushort color0 = ReadUInt16(data, offset + 8);
                    ushort color1 = ReadUInt16(data, offset + 10);
                    uint colorIndices = ReadUInt32(data, offset + 12);
                    offset += 16;

                    DecodeRgb565(color0, colorPalette, 0);
                    DecodeRgb565(color1, colorPalette, 3);
                    colorPalette[6] = (byte)((2 * colorPalette[0] + colorPalette[3]) / 3);
                    colorPalette[7] = (byte)((2 * colorPalette[1] + colorPalette[4]) / 3);
                    colorPalette[8] = (byte)((2 * colorPalette[2] + colorPalette[5]) / 3);
                    colorPalette[9] = (byte)((colorPalette[0] + 2 * colorPalette[3]) / 3);
                    colorPalette[10] = (byte)((colorPalette[1] + 2 * colorPalette[4]) / 3);
                    colorPalette[11] = (byte)((colorPalette[2] + 2 * colorPalette[5]) / 3);

                    for (int pixel = 0; pixel < 16; pixel++)
                    {
                        int x = pixel & 3;
                        int y = pixel >> 2;
                        int pixelX = blockX * 4 + x;
                        int pixelY = blockY * 4 + y;
                        if (pixelX >= width || pixelY >= height)
                            continue;

                        int colorSelector = (int)((colorIndices >> (pixel * 2)) & 3u);
                        int alphaSelector = (int)((alphaIndices >> (pixel * 3)) & 7uL);
                        int source = colorSelector * 3;
                        int destination = pixelY * stride + pixelX * 4;
                        output[destination] = colorPalette[source];
                        output[destination + 1] = colorPalette[source + 1];
                        output[destination + 2] = colorPalette[source + 2];
                        output[destination + 3] = alphaPalette[alphaSelector];
                    }
                }
            }

            return true;
        }

        static bool TryDecodeBc4(
            byte[] data,
            int offset,
            int width,
            int height,
            byte[] output,
            int stride,
            out string failureReason)
        {
            failureReason = null;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            byte[] palette = new byte[8];

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++)
                {
                    byte endpoint0 = data[offset];
                    byte endpoint1 = data[offset + 1];
                    BuildBc4Palette(endpoint0, endpoint1, palette);

                    ulong indices = 0;
                    for (int i = 0; i < 6; i++)
                        indices |= (ulong)data[offset + 2 + i] << (8 * i);
                    offset += 8;

                    for (int pixel = 0; pixel < 16; pixel++)
                    {
                        int x = pixel & 3;
                        int y = pixel >> 2;
                        int pixelX = blockX * 4 + x;
                        int pixelY = blockY * 4 + y;
                        if (pixelX >= width || pixelY >= height)
                            continue;

                        int selector = (int)((indices >> (pixel * 3)) & 7uL);
                        byte value = palette[selector];
                        int destination = pixelY * stride + pixelX * 4;
                        output[destination] = value;
                        output[destination + 1] = value;
                        output[destination + 2] = value;
                        output[destination + 3] = 255;
                    }
                }
            }

            return true;
        }

        static bool TryDecodeBc7(
            byte[] data,
            int offset,
            int width,
            int height,
            byte[] output,
            int stride,
            out string failureReason)
        {
            failureReason = null;
            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            int blockIndex = 0;
            int[] colorIndices = new int[16];
            int[] alphaIndices = new int[16];
            int[] endpoints = new int[24];
            int[] pbits = new int[6];

            for (int blockY = 0; blockY < blockCountY; blockY++)
            {
                for (int blockX = 0; blockX < blockCountX; blockX++, blockIndex++)
                {
                    LsbBitReader bits = new LsbBitReader(data, offset, 16);
                    offset += 16;

                    int mode;
                    if (!TryReadBc7Mode(ref bits, out mode))
                    {
                        failureReason = "BC7 block " + blockIndex + " has an invalid mode marker.";
                        return false;
                    }

                    if (mode == 0 || mode == 2)
                    {
                        if (!TryDecodeBc7Mode0Or2(
                                ref bits,
                                mode,
                                blockX,
                                blockY,
                                width,
                                height,
                                output,
                                stride,
                                colorIndices,
                                endpoints,
                                pbits))
                        {
                            failureReason = "BC7 mode " + mode + " block " + blockIndex + " is malformed.";
                            return false;
                        }
                    }
                    else if (mode == 1 || mode == 3 || mode == 7)
                    {
                        if (!TryDecodeBc7Mode1Or3Or7(
                                ref bits,
                                mode,
                                blockX,
                                blockY,
                                width,
                                height,
                                output,
                                stride,
                                colorIndices,
                                endpoints,
                                pbits))
                        {
                            failureReason = "BC7 mode " + mode + " block " + blockIndex + " is malformed.";
                            return false;
                        }
                    }
                    else if (mode == 4)
                    {
                        if (!TryDecodeBc7Mode4(
                                ref bits,
                                blockX,
                                blockY,
                                width,
                                height,
                                output,
                                stride,
                                colorIndices,
                                alphaIndices))
                        {
                            failureReason = "BC7 mode 4 block " + blockIndex + " is malformed.";
                            return false;
                        }
                    }
                    else if (mode == 5)
                    {
                        if (!TryDecodeBc7Mode5(
                                ref bits,
                                blockX,
                                blockY,
                                width,
                                height,
                                output,
                                stride,
                                colorIndices,
                                alphaIndices))
                        {
                            failureReason = "BC7 mode 5 block " + blockIndex + " is malformed.";
                            return false;
                        }
                    }
                    else if (mode == 6)
                    {
                        if (!TryDecodeBc7Mode6(
                                ref bits,
                                blockX,
                                blockY,
                                width,
                                height,
                                output,
                                stride))
                        {
                            failureReason = "BC7 mode 6 block " + blockIndex + " is malformed.";
                            return false;
                        }
                    }
                    else
                    {
                        failureReason = "BC7 block " + blockIndex + " has unsupported mode " + mode + ".";
                        return false;
                    }
                }
            }

            return true;
        }

        static bool TryReadBc7Mode(ref LsbBitReader bits, out int mode)
        {
            mode = 0;
            int marker;
            while (mode < 8)
            {
                if (!bits.TryReadBits(1, out marker))
                    return false;
                if (marker != 0)
                    return true;
                mode++;
            }

            return false;
        }

        static bool TryDecodeBc7Mode0Or2(
            ref LsbBitReader bits,
            int mode,
            int blockX,
            int blockY,
            int width,
            int height,
            byte[] output,
            int stride,
            int[] indices,
            int[] endpoints,
            int[] pbits)
        {
            int endpointBits = mode == 0 ? 4 : 5;
            int indexBits = mode == 0 ? 3 : 2;
            int partitionBits = mode == 0 ? 4 : 6;
            int partition;
            if (!bits.TryReadBits(partitionBits, out partition))
                return false;

            int endpointCount = 6;
            for (int component = 0; component < 3; component++)
            {
                for (int endpoint = 0; endpoint < endpointCount; endpoint++)
                {
                    int value;
                    if (!bits.TryReadBits(endpointBits, out value))
                        return false;
                    endpoints[endpoint * 4 + component] = value;
                }
            }

            if (mode == 0)
            {
                for (int endpoint = 0; endpoint < endpointCount; endpoint++)
                {
                    if (!bits.TryReadBits(1, out pbits[endpoint]))
                        return false;
                }
            }

            int anchor1 = Bc7Anchor3Subset1[partition];
            int anchor2 = Bc7Anchor3Subset2[partition];
            for (int pixel = 0; pixel < 16; pixel++)
            {
                bool anchor = pixel == 0 || pixel == anchor1 || pixel == anchor2;
                if (!bits.TryReadBits(anchor ? indexBits - 1 : indexBits, out indices[pixel]))
                    return false;
            }

            for (int endpoint = 0; endpoint < endpointCount; endpoint++)
            {
                for (int component = 0; component < 3; component++)
                {
                    int raw = endpoints[endpoint * 4 + component];
                    endpoints[endpoint * 4 + component] = mode == 0
                        ? DequantBc7(raw, pbits[endpoint], endpointBits)
                        : DequantBc7(raw, endpointBits);
                }
                endpoints[endpoint * 4 + 3] = 255;
            }

            uint partitionMap = Bc7Partition3Packed[partition];
            int[] weights = indexBits == 3 ? Bc7Weights3 : Bc7Weights2;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int subset = (int)((partitionMap >> (pixel * 2)) & 3u);
                int endpoint0 = subset * 2 * 4;
                int endpoint1 = endpoint0 + 4;
                int weight = weights[indices[pixel]];
                WritePixel(
                    output,
                    stride,
                    pixelX,
                    pixelY,
                    InterpolateBc7(endpoints[endpoint0], endpoints[endpoint1], weight),
                    InterpolateBc7(endpoints[endpoint0 + 1], endpoints[endpoint1 + 1], weight),
                    InterpolateBc7(endpoints[endpoint0 + 2], endpoints[endpoint1 + 2], weight),
                    255);
            }

            return true;
        }

        static bool TryDecodeBc7Mode1Or3Or7(
            ref LsbBitReader bits,
            int mode,
            int blockX,
            int blockY,
            int width,
            int height,
            byte[] output,
            int stride,
            int[] indices,
            int[] endpoints,
            int[] pbits)
        {
            int endpointBits = mode == 7 ? 5 : (mode == 1 ? 6 : 7);
            int componentCount = mode == 7 ? 4 : 3;
            int indexBits = mode == 1 ? 3 : 2;
            int partition;
            if (!bits.TryReadBits(6, out partition))
                return false;

            int endpointCount = 4;
            for (int component = 0; component < componentCount; component++)
            {
                for (int endpoint = 0; endpoint < endpointCount; endpoint++)
                {
                    int value;
                    if (!bits.TryReadBits(endpointBits, out value))
                        return false;
                    endpoints[endpoint * 4 + component] = value;
                }
            }

            int pbitCount = mode == 1 ? 2 : 4;
            for (int i = 0; i < pbitCount; i++)
            {
                if (!bits.TryReadBits(1, out pbits[i]))
                    return false;
            }

            int anchor = Bc7Anchor2[partition];
            for (int pixel = 0; pixel < 16; pixel++)
            {
                bool isAnchor = pixel == 0 || pixel == anchor;
                if (!bits.TryReadBits(isAnchor ? indexBits - 1 : indexBits, out indices[pixel]))
                    return false;
            }

            for (int endpoint = 0; endpoint < endpointCount; endpoint++)
            {
                int pbit = pbits[mode == 1 ? endpoint >> 1 : endpoint];
                for (int component = 0; component < componentCount; component++)
                {
                    int raw = endpoints[endpoint * 4 + component];
                    endpoints[endpoint * 4 + component] = DequantBc7(raw, pbit, endpointBits);
                }
                if (componentCount == 3)
                    endpoints[endpoint * 4 + 3] = 255;
            }

            ushort partitionMap = Bc7Partition2Masks[partition];
            int[] weights = indexBits == 3 ? Bc7Weights3 : Bc7Weights2;
            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int subset = (partitionMap >> pixel) & 1;
                int endpoint0 = subset * 2 * 4;
                int endpoint1 = endpoint0 + 4;
                int weight = weights[indices[pixel]];
                WritePixel(
                    output,
                    stride,
                    pixelX,
                    pixelY,
                    InterpolateBc7(endpoints[endpoint0], endpoints[endpoint1], weight),
                    InterpolateBc7(endpoints[endpoint0 + 1], endpoints[endpoint1 + 1], weight),
                    InterpolateBc7(endpoints[endpoint0 + 2], endpoints[endpoint1 + 2], weight),
                    InterpolateBc7(endpoints[endpoint0 + 3], endpoints[endpoint1 + 3], weight));
            }

            return true;
        }

        static bool TryDecodeBc7Mode4(
            ref LsbBitReader bits,
            int blockX,
            int blockY,
            int width,
            int height,
            byte[] output,
            int stride,
            int[] colorIndices,
            int[] alphaIndices)
        {
            int rotation;
            int indexSelection;
            int r0;
            int r1;
            int g0;
            int g1;
            int b0;
            int b1;
            int a0;
            int a1;

            if (!bits.TryReadBits(2, out rotation) ||
                !bits.TryReadBits(1, out indexSelection) ||
                !bits.TryReadBits(5, out r0) || !bits.TryReadBits(5, out r1) ||
                !bits.TryReadBits(5, out g0) || !bits.TryReadBits(5, out g1) ||
                !bits.TryReadBits(5, out b0) || !bits.TryReadBits(5, out b1) ||
                !bits.TryReadBits(6, out a0) || !bits.TryReadBits(6, out a1))
            {
                return false;
            }

            r0 = ExpandBc7FiveBit(r0);
            r1 = ExpandBc7FiveBit(r1);
            g0 = ExpandBc7FiveBit(g0);
            g1 = ExpandBc7FiveBit(g1);
            b0 = ExpandBc7FiveBit(b0);
            b1 = ExpandBc7FiveBit(b1);
            a0 = ExpandBc7SixBit(a0);
            a1 = ExpandBc7SixBit(a1);

            // Mode 4 stores a primary 2-bit index plane followed by a
            // secondary 3-bit plane. indexSelection chooses which plane
            // drives RGB; the other plane drives alpha. Each plane's first
            // texel is an anchor and therefore stores one fewer bit.
            int[] primaryIndices = indexSelection == 0 ? colorIndices : alphaIndices;
            int[] secondaryIndices = indexSelection == 0 ? alphaIndices : colorIndices;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 1 : 2, out primaryIndices[pixel]))
                    return false;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 2 : 3, out secondaryIndices[pixel]))
                    return false;
            }

            int[] colorWeights = indexSelection == 0 ? Bc7Weights2 : Bc7Weights3;
            int[] alphaWeights = indexSelection == 0 ? Bc7Weights3 : Bc7Weights2;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int colorWeight = colorWeights[colorIndices[pixel]];
                int alphaWeight = alphaWeights[alphaIndices[pixel]];
                int red = InterpolateBc7(r0, r1, colorWeight);
                int green = InterpolateBc7(g0, g1, colorWeight);
                int blue = InterpolateBc7(b0, b1, colorWeight);
                int alpha = InterpolateBc7(a0, a1, alphaWeight);

                int swap;
                if (rotation == 1)
                {
                    swap = red;
                    red = alpha;
                    alpha = swap;
                }
                else if (rotation == 2)
                {
                    swap = green;
                    green = alpha;
                    alpha = swap;
                }
                else if (rotation == 3)
                {
                    swap = blue;
                    blue = alpha;
                    alpha = swap;
                }

                WritePixel(output, stride, pixelX, pixelY, red, green, blue, alpha);
            }

            return true;
        }

        static bool TryDecodeBc7Mode5(
            ref LsbBitReader bits,
            int blockX,
            int blockY,
            int width,
            int height,
            byte[] output,
            int stride,
            int[] colorIndices,
            int[] alphaIndices)
        {
            int rotation;
            int r0;
            int r1;
            int g0;
            int g1;
            int b0;
            int b1;
            int a0;
            int a1;

            if (!bits.TryReadBits(2, out rotation) ||
                !bits.TryReadBits(7, out r0) || !bits.TryReadBits(7, out r1) ||
                !bits.TryReadBits(7, out g0) || !bits.TryReadBits(7, out g1) ||
                !bits.TryReadBits(7, out b0) || !bits.TryReadBits(7, out b1) ||
                !bits.TryReadBits(8, out a0) || !bits.TryReadBits(8, out a1))
            {
                return false;
            }

            r0 = ExpandBc7SevenBit(r0);
            r1 = ExpandBc7SevenBit(r1);
            g0 = ExpandBc7SevenBit(g0);
            g1 = ExpandBc7SevenBit(g1);
            b0 = ExpandBc7SevenBit(b0);
            b1 = ExpandBc7SevenBit(b1);

            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 1 : 2, out colorIndices[pixel]))
                    return false;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                if (!bits.TryReadBits(pixel == 0 ? 1 : 2, out alphaIndices[pixel]))
                    return false;
            }

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int colorWeight = Bc7Weights2[colorIndices[pixel]];
                int alphaWeight = Bc7Weights2[alphaIndices[pixel]];
                int red = InterpolateBc7(r0, r1, colorWeight);
                int green = InterpolateBc7(g0, g1, colorWeight);
                int blue = InterpolateBc7(b0, b1, colorWeight);
                int alpha = InterpolateBc7(a0, a1, alphaWeight);

                int swap;
                if (rotation == 1)
                {
                    swap = red;
                    red = alpha;
                    alpha = swap;
                }
                else if (rotation == 2)
                {
                    swap = green;
                    green = alpha;
                    alpha = swap;
                }
                else if (rotation == 3)
                {
                    swap = blue;
                    blue = alpha;
                    alpha = swap;
                }

                WritePixel(output, stride, pixelX, pixelY, red, green, blue, alpha);
            }

            return true;
        }

        static bool TryDecodeBc7Mode6(
            ref LsbBitReader bits,
            int blockX,
            int blockY,
            int width,
            int height,
            byte[] output,
            int stride)
        {
            int r0;
            int r1;
            int g0;
            int g1;
            int b0;
            int b1;
            int a0;
            int a1;
            int p0;
            int p1;

            if (!bits.TryReadBits(7, out r0) || !bits.TryReadBits(7, out r1) ||
                !bits.TryReadBits(7, out g0) || !bits.TryReadBits(7, out g1) ||
                !bits.TryReadBits(7, out b0) || !bits.TryReadBits(7, out b1) ||
                !bits.TryReadBits(7, out a0) || !bits.TryReadBits(7, out a1) ||
                !bits.TryReadBits(1, out p0) || !bits.TryReadBits(1, out p1))
            {
                return false;
            }

            r0 = (r0 << 1) | p0;
            g0 = (g0 << 1) | p0;
            b0 = (b0 << 1) | p0;
            a0 = (a0 << 1) | p0;
            r1 = (r1 << 1) | p1;
            g1 = (g1 << 1) | p1;
            b1 = (b1 << 1) | p1;
            a1 = (a1 << 1) | p1;

            for (int pixel = 0; pixel < 16; pixel++)
            {
                int index;
                if (!bits.TryReadBits(pixel == 0 ? 3 : 4, out index))
                    return false;

                int x = pixel & 3;
                int y = pixel >> 2;
                int pixelX = blockX * 4 + x;
                int pixelY = blockY * 4 + y;
                if (pixelX >= width || pixelY >= height)
                    continue;

                int weight = Bc7Weights4[index];
                WritePixel(
                    output,
                    stride,
                    pixelX,
                    pixelY,
                    InterpolateBc7(r0, r1, weight),
                    InterpolateBc7(g0, g1, weight),
                    InterpolateBc7(b0, b1, weight),
                    InterpolateBc7(a0, a1, weight));
            }

            return true;
        }

        static bool TryDecodeRgb(
            byte[] data,
            int offset,
            int width,
            int height,
            uint rgbBitCount,
            uint rMask,
            uint gMask,
            uint bMask,
            uint aMask,
            bool hasAlpha,
            byte[] output,
            int stride,
            out string failureReason)
        {
            failureReason = null;
            int bytesPerPixel = (int)rgbBitCount / 8;
            int rowPitch = (width * (int)rgbBitCount + 7) / 8;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = offset + y * rowPitch;
                for (int x = 0; x < width; x++)
                {
                    uint pixel = 0;
                    int pixelOffset = rowOffset + x * bytesPerPixel;
                    for (int i = 0; i < bytesPerPixel; i++)
                        pixel |= (uint)data[pixelOffset + i] << (i * 8);

                    int destination = y * stride + x * 4;
                    output[destination] = ExtractMaskedByte(pixel, rMask);
                    output[destination + 1] = ExtractMaskedByte(pixel, gMask);
                    output[destination + 2] = ExtractMaskedByte(pixel, bMask);
                    output[destination + 3] = hasAlpha
                        ? ExtractMaskedByte(pixel, aMask)
                        : (byte)255;
                }
            }

            return true;
        }

        static void BuildBc4Palette(byte endpoint0, byte endpoint1, byte[] palette)
        {
            palette[0] = endpoint0;
            palette[1] = endpoint1;

            if (endpoint0 > endpoint1)
            {
                palette[2] = (byte)((6 * endpoint0 + endpoint1) / 7);
                palette[3] = (byte)((5 * endpoint0 + 2 * endpoint1) / 7);
                palette[4] = (byte)((4 * endpoint0 + 3 * endpoint1) / 7);
                palette[5] = (byte)((3 * endpoint0 + 4 * endpoint1) / 7);
                palette[6] = (byte)((2 * endpoint0 + 5 * endpoint1) / 7);
                palette[7] = (byte)((endpoint0 + 6 * endpoint1) / 7);
            }
            else
            {
                palette[2] = (byte)((4 * endpoint0 + endpoint1) / 5);
                palette[3] = (byte)((3 * endpoint0 + 2 * endpoint1) / 5);
                palette[4] = (byte)((2 * endpoint0 + 3 * endpoint1) / 5);
                palette[5] = (byte)((endpoint0 + 4 * endpoint1) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }
        }

        static int DequantBc7(int value, int bits)
        {
            value <<= 8 - bits;
            return value | (value >> bits);
        }

        static int DequantBc7(int value, int pbit, int bits)
        {
            int totalBits = bits + 1;
            value = (value << 1) | pbit;
            value <<= 8 - totalBits;
            return value | (value >> totalBits);
        }

        static int ExpandBc7FiveBit(int value)
        {
            return (value << 3) | (value >> 2);
        }

        static int ExpandBc7SixBit(int value)
        {
            return (value << 2) | (value >> 4);
        }

        static int ExpandBc7SevenBit(int value)
        {
            return (value << 1) | (value >> 6);
        }

        static int InterpolateBc7(int endpoint0, int endpoint1, int weight)
        {
            return ((64 - weight) * endpoint0 + weight * endpoint1 + 32) >> 6;
        }

        static void WritePixel(
            byte[] output,
            int stride,
            int x,
            int y,
            int red,
            int green,
            int blue,
            int alpha)
        {
            int destination = y * stride + x * 4;
            output[destination] = (byte)red;
            output[destination + 1] = (byte)green;
            output[destination + 2] = (byte)blue;
            output[destination + 3] = (byte)alpha;
        }

        static void DecodeRgb565(ushort value, byte[] output, int offset)
        {
            output[offset] = (byte)(((value >> 11) & 31) * 255 / 31);
            output[offset + 1] = (byte)(((value >> 5) & 63) * 255 / 63);
            output[offset + 2] = (byte)((value & 31) * 255 / 31);
        }

        static byte ExtractMaskedByte(uint value, uint mask)
        {
            if (mask == 0)
                return 0;

            int shift = 0;
            while (shift < 32 && ((mask >> shift) & 1u) == 0u)
                shift++;

            int bitCount = 0;
            while (shift + bitCount < 32 && ((mask >> (shift + bitCount)) & 1u) != 0u)
                bitCount++;

            uint raw = (value & mask) >> shift;
            if (bitCount >= 8)
                return (byte)(raw >> (bitCount - 8));

            uint maximum = (1u << bitCount) - 1u;
            return (byte)((raw * 255u + maximum / 2u) / maximum);
        }

        static string FormatFourCc(uint value)
        {
            char a = (char)(value & 255u);
            char b = (char)((value >> 8) & 255u);
            char c = (char)((value >> 16) & 255u);
            char d = (char)((value >> 24) & 255u);
            return "'" + a + b + c + d + "' (0x" + value.ToString("X8") + ")";
        }

        static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) |
                          (data[offset + 3] << 24));
        }

        struct LsbBitReader
        {
            readonly byte[] _data;
            readonly int _endBit;
            int _bitPosition;

            public LsbBitReader(byte[] data, int offset, int byteCount)
            {
                _data = data;
                _bitPosition = offset * 8;
                _endBit = (offset + byteCount) * 8;
            }

            public bool TryReadBits(int count, out int value)
            {
                value = 0;
                if (count < 0 || _bitPosition > _endBit - count)
                    return false;

                for (int bit = 0; bit < count; bit++)
                {
                    int byteIndex = _bitPosition >> 3;
                    int bitIndex = _bitPosition & 7;
                    value |= ((_data[byteIndex] >> bitIndex) & 1) << bit;
                    _bitPosition++;
                }

                return true;
            }
        }
    }
}
