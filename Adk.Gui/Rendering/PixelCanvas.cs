using System;
using VRageMath;

namespace Adk.Gui.Rendering
{
    /// <summary>
    /// Allocation-free RGBA painter for generated-texture upload buffers.
    /// Colours are premultiplied as required by the screen-space sprite pass.
    /// </summary>
    public sealed class PixelCanvas
    {
        readonly byte[] _pixels;

        public PixelCanvas(byte[] pixels, int width, int height)
        {
            _pixels = pixels;
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        public void Clear(Color color)
        {
            byte r;
            byte g;
            byte b;
            byte a;
            Premultiply(color, out r, out g, out b, out a);
            for (int i = 0; i < _pixels.Length; i += 4)
            {
                _pixels[i] = r;
                _pixels[i + 1] = g;
                _pixels[i + 2] = b;
                _pixels[i + 3] = a;
            }
        }

        public void FillRectangle(int x, int y, int width, int height, Color color)
        {
            int left = Math.Max(0, x);
            int top = Math.Max(0, y);
            int right = Math.Min(Width, x + width);
            int bottom = Math.Min(Height, y + height);
            for (int py = top; py < bottom; py++)
            for (int px = left; px < right; px++)
                SetPixel(px, py, color);
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color color, int thickness = 1)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            int radius = Math.Max(0, thickness / 2);

            while (true)
            {
                FillCircle(x0, y0, radius, color);
                if (x0 == x1 && y0 == y1)
                    break;
                int twiceError = error * 2;
                if (twiceError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (twiceError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        public void FillCircle(int centerX, int centerY, int radius, Color color)
        {
            if (radius <= 0)
            {
                SetPixel(centerX, centerY, color);
                return;
            }

            int radiusSquared = radius * radius;
            int top = Math.Max(0, centerY - radius);
            int bottom = Math.Min(Height - 1, centerY + radius);
            int left = Math.Max(0, centerX - radius);
            int right = Math.Min(Width - 1, centerX + radius);
            for (int y = top; y <= bottom; y++)
            {
                int dy = y - centerY;
                for (int x = left; x <= right; x++)
                {
                    int dx = x - centerX;
                    if (dx * dx + dy * dy <= radiusSquared)
                        SetPixel(x, y, color);
                }
            }
        }

        public void DrawCircle(int centerX, int centerY, int radius, Color color, int thickness = 1)
        {
            int outer = radius * radius;
            int innerRadius = Math.Max(0, radius - thickness);
            int inner = innerRadius * innerRadius;
            int top = Math.Max(0, centerY - radius);
            int bottom = Math.Min(Height - 1, centerY + radius);
            int left = Math.Max(0, centerX - radius);
            int right = Math.Min(Width - 1, centerX + radius);
            for (int y = top; y <= bottom; y++)
            {
                int dy = y - centerY;
                for (int x = left; x <= right; x++)
                {
                    int dx = x - centerX;
                    int distance = dx * dx + dy * dy;
                    if (distance <= outer && distance >= inner)
                        SetPixel(x, y, color);
                }
            }
        }

        public void FillTriangle(Vector2I a, Vector2I b, Vector2I c, Color color)
        {
            int left = Math.Max(0, Math.Min(a.X, Math.Min(b.X, c.X)));
            int right = Math.Min(Width - 1, Math.Max(a.X, Math.Max(b.X, c.X)));
            int top = Math.Max(0, Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            int bottom = Math.Min(Height - 1, Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            int area = Edge(a, b, c);
            if (area == 0)
                return;

            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                Vector2I p = new Vector2I(x, y);
                int w0 = Edge(b, c, p);
                int w1 = Edge(c, a, p);
                int w2 = Edge(a, b, p);
                if ((w0 >= 0 && w1 >= 0 && w2 >= 0) ||
                    (w0 <= 0 && w1 <= 0 && w2 <= 0))
                    SetPixel(x, y, color);
            }
        }

        public void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;

            byte r;
            byte g;
            byte b;
            byte a;
            Premultiply(color, out r, out g, out b, out a);
            int index = (y * Width + x) * 4;
            _pixels[index] = r;
            _pixels[index + 1] = g;
            _pixels[index + 2] = b;
            _pixels[index + 3] = a;
        }
        
        public void BlendPixel(int x, int y, Color color, float coverage)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;

            coverage = Math.Max(0f, Math.Min(1f, coverage));
            if (coverage <= 0f || color.A == 0)
                return;

            float sourceAlpha = color.A / 255f * coverage;
            float inverseAlpha = 1f - sourceAlpha;
            int index = (y * Width + x) * 4;

            _pixels[index] = ToByte(color.R * sourceAlpha + _pixels[index] * inverseAlpha);
            _pixels[index + 1] = ToByte(color.G * sourceAlpha + _pixels[index + 1] * inverseAlpha);
            _pixels[index + 2] = ToByte(color.B * sourceAlpha + _pixels[index + 2] * inverseAlpha);
            _pixels[index + 3] = ToByte(255f * sourceAlpha + _pixels[index + 3] * inverseAlpha);
        }

        static byte ToByte(float value)
        {
            if (value <= 0f)
                return 0;
            if (value >= 255f)
                return 255;
            return (byte)Math.Round(value);
        }

        static int Edge(Vector2I a, Vector2I b, Vector2I p)
        {
            return (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
        }

        static void Premultiply(Color color, out byte r, out byte g, out byte b, out byte a)
        {
            a = color.A;
            r = (byte)(color.R * a / 255);
            g = (byte)(color.G * a / 255);
            b = (byte)(color.B * a / 255);
        }
    }
}
