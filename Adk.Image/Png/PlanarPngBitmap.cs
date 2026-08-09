using System;
using System.IO;
using System.Text;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;
using ZipCrc32 = Adk.Compression.Zip.Crc32;
using ZipZlib = Adk.Compression.Zip.Zlib;

namespace Adk.Image.Png
{
    /// <summary>
    /// A mutable PNG bitmap stored as four byte planes.
    /// Grayscale16 uses planes 0 and 1 for the high and low sample bytes.
    /// Grayscale8, RGB8, and RGBA8 use the conventional RGBA planes.
    /// </summary>
    public sealed class PlanarPngBitmap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int BitDepth { get; private set; }
        public int ColorType { get; private set; }
        public int SourceInterlaceMethod { get; private set; }
        public byte[][] Planes { get; private set; }

        public PlanarPngBitmap(
            int width,
            int height,
            int bitDepth,
            int colorType,
            byte[][] planes)
            : this(width, height, bitDepth, colorType, 0, planes)
        {
        }

        PlanarPngBitmap(
            int width,
            int height,
            int bitDepth,
            int colorType,
            int sourceInterlaceMethod,
            byte[][] planes)
        {
            ValidateFormat(bitDepth, colorType);

            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            int pixelCount = checked(width * height);

            if (planes == null || planes.Length != 4)
                throw new ArgumentException("Exactly four image planes are required.", nameof(planes));

            for (int i = 0; i < planes.Length; i++)
            {
                if (planes[i] == null || planes[i].Length != pixelCount)
                    throw new ArgumentException("Unexpected image plane length.", nameof(planes));
            }

            Width = width;
            Height = height;
            BitDepth = bitDepth;
            ColorType = colorType;
            SourceInterlaceMethod = sourceInterlaceMethod;
            Planes = planes;
        }

        public static PlanarPngBitmap Load(Stream input)
        {
            return FromRawBitmap(
                RawPngBitmap.Load(
                    input));
        }

        public static PlanarPngBitmap Load(byte[] input)
        {
            return FromRawBitmap(
                RawPngBitmap.Load(
                    input));
        }

        static PlanarPngBitmap FromRawBitmap(RawPngBitmap bitmap)
        {
            ValidateFormat(bitmap.SourceBitDepth, bitmap.SourceColorType);

            int pixelCount = checked(bitmap.Width * bitmap.Height);
            byte[][] planes =
            {
                new byte[pixelCount],
                new byte[pixelCount],
                new byte[pixelCount],
                new byte[pixelCount]
            };

            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int offset = pixel * 4;

                if (bitmap.SourceColorType == 0 && bitmap.SourceBitDepth == 16)
                {
                    ushort sample = bitmap.RedSamples16[pixel];
                    planes[0][pixel] = (byte)(sample >> 8);
                    planes[1][pixel] = (byte)sample;
                    planes[3][pixel] = bitmap.Pixels[offset + 3];
                }
                else
                {
                    planes[0][pixel] = bitmap.Pixels[offset];
                    planes[1][pixel] = bitmap.Pixels[offset + 1];
                    planes[2][pixel] = bitmap.Pixels[offset + 2];
                    planes[3][pixel] = bitmap.Pixels[offset + 3];
                }
            }

            return new PlanarPngBitmap(
                bitmap.Width,
                bitmap.Height,
                bitmap.SourceBitDepth,
                bitmap.SourceColorType,
                bitmap.SourceInterlaceMethod,
                planes);
        }

        public PlanarPngBitmap Clone()
        {
            ValidatePlanes();

            byte[][] planes = new byte[4][];

            for (int i = 0; i < planes.Length; i++)
            {
                planes[i] = new byte[Planes[i].Length];
                Buffer.BlockCopy(Planes[i], 0, planes[i], 0, Planes[i].Length);
            }

            return new PlanarPngBitmap(
                Width,
                Height,
                BitDepth,
                ColorType,
                SourceInterlaceMethod,
                planes);
        }

        public byte[] Encode()
        {
            ValidatePlanes();

            int bytesPerPixel = GetBytesPerPixel(BitDepth, ColorType);
            int rowBytes = checked(Width * bytesPerPixel);
            byte[] filtered = new byte[checked(Height * (rowBytes + 1))];
            int output = 0;
            int pixel = 0;

            for (int y = 0; y < Height; y++)
            {
                filtered[output++] = 0;

                for (int x = 0; x < Width; x++)
                {
                    filtered[output++] = Planes[0][pixel];

                    if (ColorType == 0 && BitDepth == 16)
                    {
                        filtered[output++] = Planes[1][pixel];
                    }
                    else if (ColorType == 2 || ColorType == 6)
                    {
                        filtered[output++] = Planes[1][pixel];
                        filtered[output++] = Planes[2][pixel];

                        if (ColorType == 6)
                            filtered[output++] = Planes[3][pixel];
                    }

                    pixel++;
                }
            }

            return BuildPng(ZipZlib.DeflateZlibStored(filtered));
        }

        public void Save(Stream output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            byte[] png = Encode();
            output.Write(png, 0, png.Length);
        }

        byte[] BuildPng(byte[] compressed)
        {
            byte[] header = new byte[13];
            int headerOffset = 0;

            WriteUInt32BigEndian(header, ref headerOffset, (uint)Width);
            WriteUInt32BigEndian(header, ref headerOffset, (uint)Height);
            header[headerOffset++] = (byte)BitDepth;
            header[headerOffset++] = (byte)ColorType;
            header[headerOffset++] = 0;
            header[headerOffset++] = 0;
            header[headerOffset] = 0;

            byte[] output = new byte[checked(8 + 12 + header.Length + 12 + compressed.Length + 12)];
            int position = 0;
            byte[] signature =
            {
                0x89, 0x50, 0x4E, 0x47,
                0x0D, 0x0A, 0x1A, 0x0A
            };

            Buffer.BlockCopy(signature, 0, output, position, signature.Length);
            position += signature.Length;

            WriteChunk(output, ref position, "IHDR", header);
            WriteChunk(output, ref position, "IDAT", compressed);
            WriteChunk(output, ref position, "IEND", new byte[0]);

            if (position != output.Length)
                throw new InvalidDataException("Internal PNG encoder length mismatch.");

            return output;
        }

        static void WriteChunk(byte[] output, ref int position, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            WriteUInt32BigEndian(output, ref position, (uint)data.Length);
            Buffer.BlockCopy(typeBytes, 0, output, position, typeBytes.Length);
            position += typeBytes.Length;

            if (data.Length > 0)
            {
                Buffer.BlockCopy(data, 0, output, position, data.Length);
                position += data.Length;
            }

            WriteUInt32BigEndian(output, ref position, ZipCrc32.Compute(typeBytes, data));
        }

        static void WriteUInt32BigEndian(byte[] data, ref int offset, uint value)
        {
            data[offset++] = (byte)(value >> 24);
            data[offset++] = (byte)(value >> 16);
            data[offset++] = (byte)(value >> 8);
            data[offset++] = (byte)value;
        }

        static int GetBytesPerPixel(int bitDepth, int colorType)
        {
            if (colorType == 0 && bitDepth == 8)
                return 1;

            if (colorType == 0 && bitDepth == 16)
                return 2;

            if (colorType == 2 && bitDepth == 8)
                return 3;

            if (colorType == 6 && bitDepth == 8)
                return 4;

            throw new NotSupportedException(
                "Planar PNG supports Grayscale8, Grayscale16, RGB8, and RGBA8 only.");
        }

        static void ValidateFormat(int bitDepth, int colorType)
        {
            GetBytesPerPixel(bitDepth, colorType);
        }

        void ValidatePlanes()
        {
            int pixelCount = checked(Width * Height);

            if (Planes == null || Planes.Length != 4)
                throw new InvalidDataException("Exactly four image planes are required.");

            for (int i = 0; i < Planes.Length; i++)
            {
                if (Planes[i] == null || Planes[i].Length != pixelCount)
                    throw new InvalidDataException("Unexpected image plane length.");
            }
        }
    }
}
