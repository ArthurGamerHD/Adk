using System;
using System.Collections.Generic;
using VRageMath;

namespace Adk.Gui.Rendering
{
    /// <summary>
    /// Shares standard rounded Border textures by the pixels that define them.
    /// Entries live only while at least one Border owns a lease.
    /// </summary>
    public sealed class BorderTextureCache
    {
        const float RADIUS_PRECISION = 1000f;
        readonly IDynamicTextureFactory _textures;
        readonly Dictionary<Key, Entry> _entries = new Dictionary<Key, Entry>();

        public BorderTextureCache(IDynamicTextureFactory textures)
        {
            if (textures == null)
                throw new ArgumentNullException(nameof(textures));
            _textures = textures;
        }

        public struct Key : IEquatable<Key>
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int RadiusMilli;
            public readonly int BorderThickness;
            public readonly uint BorderColor;
            public readonly uint BackgroundColor;
            public readonly bool Styled;

            public Key(
                Vector2I size,
                float radius,
                int borderThickness,
                Color borderColor,
                Color backgroundColor,
                bool styled)
            {
                Width = size.X;
                Height = size.Y;
                RadiusMilli = Math.Max(0, (int)Math.Round(radius * RADIUS_PRECISION));
                Styled = styled;
                BorderThickness = styled ? Math.Max(0, borderThickness) : 0;
                BorderColor = styled ? Pack(borderColor) : 0u;
                BackgroundColor = styled ? Pack(backgroundColor) : 0u;
            }

            public float Radius => RadiusMilli / RADIUS_PRECISION;

            public Color UnpackedBorderColor => Unpack(BorderColor);
            public Color UnpackedBackgroundColor => Unpack(BackgroundColor);

            public bool Equals(Key other)
            {
                return Width == other.Width &&
                       Height == other.Height &&
                       RadiusMilli == other.RadiusMilli &&
                       BorderThickness == other.BorderThickness &&
                       BorderColor == other.BorderColor &&
                       BackgroundColor == other.BackgroundColor &&
                       Styled == other.Styled;
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Width;
                    hash = (hash * 397) ^ Height;
                    hash = (hash * 397) ^ RadiusMilli;
                    hash = (hash * 397) ^ BorderThickness;
                    hash = (hash * 397) ^ (int)BorderColor;
                    hash = (hash * 397) ^ (int)BackgroundColor;
                    hash = (hash * 397) ^ (Styled ? 1 : 0);
                    return hash;
                }
            }

            static uint Pack(Color color)
            {
                return (uint)(color.R |
                    (color.G << 8) |
                    (color.B << 16) |
                    (color.A << 24));
            }

            static Color Unpack(uint color)
            {
                return new Color(
                    (byte)(color & 0xff),
                    (byte)((color >> 8) & 0xff),
                    (byte)((color >> 16) & 0xff),
                    (byte)((color >> 24) & 0xff));
            }
        }

        sealed class Entry
        {
            public IDynamicTexture Texture;
            public int References;
        }

        public sealed class Lease : IDisposable
        {
            readonly BorderTextureCache _owner;
            readonly Key _key;
            bool _disposed;

            internal Lease(BorderTextureCache owner, Key key, IDynamicTexture texture)
            {
                _owner = owner;
                _key = key;
                Texture = texture;
            }

            public IDynamicTexture Texture { get; private set; }
            public Key CacheKey => _key;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner.Release(_key);
                Texture = null;
            }
        }

        public Lease AcquireMask(Vector2I supportedSize, float cornerRadius)
        {
            return Acquire(
                new Key(
                    supportedSize,
                    ClampRadius(supportedSize, cornerRadius),
                    0,
                    Color.White,
                    Color.White,
                    false));
        }

        public Lease AcquireStyled(
            Vector2I supportedSize,
            float cornerRadius,
            int borderThickness,
            Color borderColor,
            Color backgroundColor)
        {
            return Acquire(
                new Key(
                    supportedSize,
                    ClampRadius(supportedSize, cornerRadius),
                    borderThickness,
                    borderColor,
                    backgroundColor,
                    true));
        }

        Lease Acquire(Key key)
        {
            if (key.Width <= 0 || key.Height <= 0)
                return null;

            Entry entry;
            if (!_entries.TryGetValue(key, out entry))
            {
                string name = "ADKBorderShared_" +
                              key.Width + "x" + key.Height +
                              "_r" + key.RadiusMilli +
                              (key.Styled
                                  ? "_t" + key.BorderThickness +
                                    "_b" + key.BorderColor.ToString("X8") +
                                    "_g" + key.BackgroundColor.ToString("X8")
                                  : "_mask");
                IDynamicTexture texture = _textures.Create(
                    name,
                    new Vector2I(key.Width, key.Height));
                if (!texture.IsAvailable)
                {
                    texture.Dispose();
                    return null;
                }

                if (key.Styled)
                {
                    texture.Update(canvas => PaintRoundedStyled(
                        canvas,
                        key.Radius,
                        key.BorderThickness,
                        key.UnpackedBorderColor,
                        key.UnpackedBackgroundColor));
                }
                else
                {
                    texture.Update(canvas => PaintRoundedMask(canvas, key.Radius));
                }

                entry = new Entry
                {
                    Texture = texture,
                    References = 0,
                };
                _entries.Add(key, entry);
            }

            entry.References++;
            return new Lease(this, key, entry.Texture);
        }

        void Release(Key key)
        {
            Entry entry;
            if (!_entries.TryGetValue(key, out entry))
                return;

            entry.References--;
            if (entry.References > 0)
                return;

            entry.Texture.Dispose();
            _entries.Remove(key);
        }

        static float ClampRadius(Vector2I size, float cornerRadius)
        {
            return Math.Min(
                Math.Max(0f, cornerRadius),
                Math.Min(size.X, size.Y) * 0.5f);
        }

        static void PaintRoundedMask(PixelCanvas canvas, float cornerRadius)
        {
            canvas.Clear(Color.Transparent);
            PaintRoundedRect(
                canvas,
                0f,
                0f,
                canvas.Width,
                canvas.Height,
                cornerRadius,
                Color.White);
        }

        static void PaintRoundedStyled(
            PixelCanvas canvas,
            float cornerRadius,
            int borderThickness,
            Color borderColor,
            Color backgroundColor)
        {
            canvas.Clear(Color.Transparent);

            float inset = Math.Max(0, borderThickness);
            float innerWidth = canvas.Width - inset * 2f;
            float innerHeight = canvas.Height - inset * 2f;
            float innerRadius = Math.Max(0f, cornerRadius - inset);
            
            for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                float sampleX = x + 0.5f;
                float sampleY = y + 0.5f;
                float outerCoverage = RoundedRectCoverage(
                    sampleX,
                    sampleY,
                    0f,
                    0f,
                    canvas.Width,
                    canvas.Height,
                    cornerRadius);
                if (outerCoverage <= 0f)
                    continue;

                float innerCoverage = innerWidth > 0f && innerHeight > 0f
                    ? RoundedRectCoverage(
                        sampleX,
                        sampleY,
                        inset,
                        inset,
                        innerWidth,
                        innerHeight,
                        innerRadius)
                    : 0f;

                float borderCoverage = outerCoverage * (1f - innerCoverage);
                if (borderCoverage > 0f)
                    canvas.BlendPixel(x, y, borderColor, borderCoverage);
                if (innerCoverage > 0f)
                    canvas.BlendPixel(x, y, backgroundColor, innerCoverage);
            }
        }

        static void ClearCornerIfOutside(PixelCanvas canvas, int x, int y, float cornerRadius)
        {
            float coverage = RoundedRectCoverage(
                x + 0.5f,
                y + 0.5f,
                0f,
                0f,
                canvas.Width,
                canvas.Height,
                cornerRadius);
            if (coverage <= 0f)
                canvas.SetPixel(x, y, Color.Transparent);
        }

        static float RoundedRectCoverage(
            float sampleX,
            float sampleY,
            float left,
            float top,
            float width,
            float height,
            float cornerRadius)
        {
            if (width <= 0f || height <= 0f)
                return 0f;

            float right = left + width;
            float bottom = top + height;
            float radius = Math.Min(
                Math.Max(0f, cornerRadius),
                Math.Min(width, height) * 0.5f);

            if (radius <= 0f)
            {
                float dx = Math.Min(sampleX - left, right - sampleX);
                float dy = Math.Min(sampleY - top, bottom - sampleY);
                return MathHelper.Clamp(Math.Min(dx, dy) + 0.5f, 0f, 1f);
            }

            float nearestX = MathHelper.Clamp(sampleX, left + radius, right - radius);
            float nearestY = MathHelper.Clamp(sampleY, top + radius, bottom - radius);
            float offsetX = sampleX - nearestX;
            float offsetY = sampleY - nearestY;
            float distance = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
            return MathHelper.Clamp(radius + 0.5f - distance, 0f, 1f);
        }

        static void PaintRoundedRect(
            PixelCanvas canvas,
            float left,
            float top,
            float width,
            float height,
            float cornerRadius,
            Color color)
        {
            if (width <= 0f || height <= 0f || color.A == 0)
                return;

            float right = left + width;
            float bottom = top + height;
            float radius = Math.Min(
                Math.Max(0f, cornerRadius),
                Math.Min(width, height) * 0.5f);

            int startX = Math.Max(0, (int)Math.Floor(left - 1f));
            int endX = Math.Min(canvas.Width - 1, (int)Math.Ceiling(right));
            int startY = Math.Max(0, (int)Math.Floor(top - 1f));
            int endY = Math.Min(canvas.Height - 1, (int)Math.Ceiling(bottom));

            if (radius <= 0f)
            {
                for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                {
                    float sx = x + 0.5f;
                    float sy = y + 0.5f;
                    float dx = Math.Min(sx - left, right - sx);
                    float dy = Math.Min(sy - top, bottom - sy);
                    float coverage = MathHelper.Clamp(Math.Min(dx, dy) + 0.5f, 0f, 1f);
                    canvas.BlendPixel(x, y, color, coverage);
                }
                return;
            }

            float leftCenter = left + radius;
            float rightCenter = right - radius;
            float topCenter = top + radius;
            float bottomCenter = bottom - radius;
            for (int y = startY; y <= endY; y++)
            for (int x = startX; x <= endX; x++)
            {
                float sampleX = x + 0.5f;
                float sampleY = y + 0.5f;
                float nearestX = MathHelper.Clamp(sampleX, leftCenter, rightCenter);
                float nearestY = MathHelper.Clamp(sampleY, topCenter, bottomCenter);
                float dx = sampleX - nearestX;
                float dy = sampleY - nearestY;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                float coverage = MathHelper.Clamp(radius + 0.5f - distance, 0f, 1f);
                canvas.BlendPixel(x, y, color, coverage);
            }
        }
    }
}
