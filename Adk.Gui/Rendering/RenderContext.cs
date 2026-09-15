using System;
using Adk.Gui.Styling;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Rendering
{
    /// <summary>
    /// Per-pass rendering state shared by controls. It applies inherited opacity
    /// before forwarding renderer-neutral primitives to the active backend.
    /// </summary>
    public sealed class RenderContext
    {
        readonly IUiRenderer _renderer;

        public RenderContext(IUiRenderer renderer, float opacity)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));
            _renderer = renderer;
            Opacity = MathHelper.Clamp(opacity, 0f, 1f);
        }

        public float Opacity { get; }
        public ITextMetrics TextMetrics => _renderer.TextMetrics;
        public ITextureResolver Textures => _renderer.Textures;
        public IDynamicTextureFactory DynamicTextures => _renderer.DynamicTextures;
        public BorderTextureCache BorderTextures => _renderer.BorderTextures;

        public IDisposable PushClip(RectangleF bounds)
        {
            return _renderer.PushClip(bounds);
        }

        public IDisposable Clip(RectangleF bounds)
        {
            return PushClip(bounds);
        }

        public void FillRectangle(RectangleF bounds, Color color)
        {
            _renderer.FillRectangle(bounds, ApplyOpacity(color));
        }

        public void Fill(RectangleF bounds, Color color)
        {
            FillRectangle(bounds, color);
        }

        public void Fill(RectangleF bounds, BackgroundBrush brush)
        {
            if (brush == null)
                return;

            TextureBrush textureBrush = brush as TextureBrush;
            if (textureBrush != null)
            {
                TextureSource texture = TextureSource.FromKey(textureBrush.Source);
                if (Textures.IsAvailable(texture))
                {
                    DrawTexture(texture, bounds, textureBrush.Tint);
                    return;
                }
            }

            CompositeTextureBrush compositeBrush = brush as CompositeTextureBrush;
            if (compositeBrush != null && DrawCompositeTexture(bounds, compositeBrush))
                return;

            NineSliceTextureBrush nineSliceBrush = brush as NineSliceTextureBrush;
            if (nineSliceBrush != null && DrawNineSliceTexture(bounds, nineSliceBrush))
                return;

            FillRectangle(bounds, brush.FallbackColor);
        }

        bool DrawCompositeTexture(RectangleF bounds, CompositeTextureBrush brush)
        {
            TextureSource start = TextureSource.FromKey(brush.StartSource);
            TextureSource center = TextureSource.FromKey(brush.CenterSource);
            TextureSource end = TextureSource.FromKey(brush.EndSource);
            if (!Textures.IsAvailable(start) ||
                !Textures.IsAvailable(center) ||
                !Textures.IsAvailable(end))
                return false;

            float crossLength = brush.Orientation == CompositeBrushOrientation.Horizontal
                ? bounds.Height
                : bounds.Width;
            if (crossLength <= 0f)
                return true;

            float scale = crossLength / brush.ReferenceCrossLength;
            float startLength = Math.Max(0f, brush.StartLength * scale);
            float endLength = Math.Max(0f, brush.EndLength * scale);
            float availableLength = brush.Orientation == CompositeBrushOrientation.Horizontal
                ? Math.Max(0f, bounds.Width)
                : Math.Max(0f, bounds.Height);
            float capsLength = startLength + endLength;
            if (capsLength > availableLength && capsLength > 0f)
            {
                float capScale = availableLength / capsLength;
                startLength *= capScale;
                endLength *= capScale;
            }
            float centerLength = Math.Max(0f, availableLength - startLength - endLength);
            if (brush.Orientation == CompositeBrushOrientation.Horizontal)
            {
                if (startLength > 0f)
                    DrawTexture(start, new RectangleF(
                        bounds.X, bounds.Y, startLength, bounds.Height), brush.Tint);
                if (centerLength > 0f)
                    DrawTexture(center, new RectangleF(
                        bounds.X + startLength, bounds.Y, centerLength, bounds.Height), brush.Tint);
                if (endLength > 0f)
                    DrawTexture(end, new RectangleF(
                        bounds.Right - endLength, bounds.Y, endLength, bounds.Height), brush.Tint);
            }
            else
            {
                if (startLength > 0f)
                    DrawTexture(start, new RectangleF(
                        bounds.X, bounds.Y, bounds.Width, startLength), brush.Tint);
                if (centerLength > 0f)
                    DrawTexture(center, new RectangleF(
                        bounds.X, bounds.Y + startLength, bounds.Width, centerLength), brush.Tint);
                if (endLength > 0f)
                    DrawTexture(end, new RectangleF(
                        bounds.X, bounds.Bottom - endLength, bounds.Width, endLength), brush.Tint);
            }
            return true;
        }

        bool DrawNineSliceTexture(RectangleF bounds, NineSliceTextureBrush brush)
        {
            TextureSource leftTop = TextureSource.FromKey(brush.LeftTopSource);
            TextureSource centerTop = TextureSource.FromKey(brush.CenterTopSource);
            TextureSource rightTop = TextureSource.FromKey(brush.RightTopSource);
            TextureSource leftCenter = TextureSource.FromKey(brush.LeftCenterSource);
            TextureSource center = TextureSource.FromKey(brush.CenterSource);
            TextureSource rightCenter = TextureSource.FromKey(brush.RightCenterSource);
            TextureSource leftBottom = TextureSource.FromKey(brush.LeftBottomSource);
            TextureSource centerBottom = TextureSource.FromKey(brush.CenterBottomSource);
            TextureSource rightBottom = TextureSource.FromKey(brush.RightBottomSource);

            if (!Textures.IsAvailable(leftTop) || !Textures.IsAvailable(centerTop) ||
                !Textures.IsAvailable(rightTop) || !Textures.IsAvailable(leftCenter) ||
                !Textures.IsAvailable(center) || !Textures.IsAvailable(rightCenter) ||
                !Textures.IsAvailable(leftBottom) || !Textures.IsAvailable(centerBottom) ||
                !Textures.IsAvailable(rightBottom))
                return false;

            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return true;

            float scale = bounds.Width / Math.Max(1f, brush.ReferenceWidth);
            float left = Math.Max(0f, brush.LeftWidth * scale);
            float right = Math.Max(0f, brush.RightWidth * scale);
            float top = Math.Max(0f, brush.TopHeight * scale);
            float bottom = Math.Max(0f, brush.BottomHeight * scale);

            float horizontalCaps = left + right;
            if (horizontalCaps > bounds.Width && horizontalCaps > 0f)
            {
                float capScale = bounds.Width / horizontalCaps;
                left *= capScale;
                right *= capScale;
            }
            float verticalCaps = top + bottom;
            if (verticalCaps > bounds.Height && verticalCaps > 0f)
            {
                float capScale = bounds.Height / verticalCaps;
                top *= capScale;
                bottom *= capScale;
            }

            float middleWidth = Math.Max(0f, bounds.Width - left - right);
            float middleHeight = Math.Max(0f, bounds.Height - top - bottom);
            float x0 = bounds.X;
            float x1 = x0 + left;
            float x2 = bounds.Right - right;
            float y0 = bounds.Y;
            float y1 = y0 + top;
            float y2 = bounds.Bottom - bottom;

            if (left > 0f && top > 0f)
                DrawTexture(leftTop, new RectangleF(x0, y0, left, top), brush.Tint);
            if (middleWidth > 0f && top > 0f)
                DrawTexture(centerTop, new RectangleF(x1, y0, middleWidth, top), brush.Tint);
            if (right > 0f && top > 0f)
                DrawTexture(rightTop, new RectangleF(x2, y0, right, top), brush.Tint);

            if (left > 0f && middleHeight > 0f)
                DrawTexture(leftCenter, new RectangleF(x0, y1, left, middleHeight), brush.Tint);
            if (middleWidth > 0f && middleHeight > 0f)
                DrawTexture(center, new RectangleF(x1, y1, middleWidth, middleHeight), brush.Tint);
            if (right > 0f && middleHeight > 0f)
                DrawTexture(rightCenter, new RectangleF(x2, y1, right, middleHeight), brush.Tint);

            if (left > 0f && bottom > 0f)
                DrawTexture(leftBottom, new RectangleF(x0, y2, left, bottom), brush.Tint);
            if (middleWidth > 0f && bottom > 0f)
                DrawTexture(centerBottom, new RectangleF(x1, y2, middleWidth, bottom), brush.Tint);
            if (right > 0f && bottom > 0f)
                DrawTexture(rightBottom, new RectangleF(x2, y2, right, bottom), brush.Tint);
            return true;
        }

        public void DrawBorder(RectangleF bounds, Color color, int thickness)
        {
            if (thickness <= 0)
                return;
            FillRectangle(new RectangleF(bounds.X, bounds.Y, bounds.Width, thickness), color);
            FillRectangle(new RectangleF(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
            FillRectangle(new RectangleF(bounds.X, bounds.Y, thickness, bounds.Height), color);
            FillRectangle(new RectangleF(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
        }

        public void Border(RectangleF bounds, Color color, int thickness)
        {
            DrawBorder(bounds, color, thickness);
        }

        public void DrawTexture(
            TextureSource texture,
            RectangleF bounds,
            Color color,
            float rotation = 0f)
        {
            _renderer.DrawTexture(texture, bounds, ApplyOpacity(color), rotation);
        }

        public void Texture(
            MyStringId material,
            RectangleF bounds,
            Color color,
            float rotation = 0f)
        {
            if (material == MyStringId.NullOrEmpty)
                return;
            DrawTexture(TextureSource.FromNative(material), bounds, color, rotation);
        }

        public void Texture(
            string texture,
            RectangleF bounds,
            Color color,
            bool useMissingIconFallback = false,
            float rotation = 0f)
        {
            if (string.IsNullOrWhiteSpace(texture))
                return;
            TextureSource source = TextureSource.FromKey(texture);
            if (useMissingIconFallback && !Textures.IsAvailable(source))
                source = Textures.MissingTexture;
            DrawTexture(source, bounds, color, rotation);
        }

        public void DrawText(
            string font,
            string text,
            Vector2 position,
            float scale,
            Color color,
            MyGuiDrawAlignEnum alignment)
        {
            _renderer.DrawText(font, text, position, scale, ApplyOpacity(color), alignment);
        }

        public void Text(
            string text,
            Vector2 position,
            float scale,
            Color color,
            MyGuiDrawAlignEnum alignment =
                MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            bool dropShadow = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (!dropShadow)
            {
                DrawText("White", text, position, scale, color, alignment);
                return;
            }

            // The backend remains theme-neutral.  A text-owning control decides whether
            // to request this pass through the TextOptions.DropShadow attached property.
            // Dark face colors keep the archived hue-preserving highlight treatment;
            // already-bright faces receive a dark offset shadow.
            float shadeOffset = scale * .3f;
            Vector2 offset = new Vector2(shadeOffset);
            Color shade = DeriveTextShade(color);
            if (UsesLightTextShade(color))
            {
                DrawText("White", text, position + offset, scale, color, alignment);
                DrawText("White", text, position, scale, shade, alignment);
            }
            else
            {
                DrawText("White", text, position + offset, scale, shade, alignment);
                DrawText("White", text, position, scale, color, alignment);
            }
        }

        static bool UsesLightTextShade(Color source)
        {
            return RelativeLuminance(source) < 0.42f;
        }

        static Color DeriveTextShade(Color source)
        {
            Vector3 hsv = source.ColorToHSV();
            if (UsesLightTextShade(source))
            {
                hsv.Y = MathHelper.Clamp(hsv.Y * 0.82f, 0f, 1f);
                hsv.Z = Math.Max(hsv.Z, 0.92f);
            }
            else
            {
                hsv.Z = Math.Min(hsv.Z, 0.12f);
            }

            Color shade = hsv.HSVtoColor();
            shade.A = (byte)Math.Min((int)source.A, 220);
            return shade;
        }

        static float RelativeLuminance(Color color)
        {
            return (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
        }

        Color ApplyOpacity(Color color)
        {
            Vector4 value = color.ToVector4();
            value *= Opacity;
            return new Color(value);
        }
    }
}
