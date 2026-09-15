using System;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.ScreenSpace
{
    public sealed class ScreenSpaceRenderer : IUiRenderer
    {
        static readonly MyStringId Square = MyStringId.GetOrCompute("SquareSimple");
        readonly ScreenSpaceTextureResolver _textures = new ScreenSpaceTextureResolver();
        readonly ScreenSpaceTextMetrics _textMetrics = new ScreenSpaceTextMetrics();
        readonly ScreenSpaceDynamicTextureFactory _dynamicTextures =
            new ScreenSpaceDynamicTextureFactory();
        readonly BorderTextureCache _borderTextures;
        readonly MyScreenSpaceLayer _layer;

        public ScreenSpaceRenderer(MyScreenSpaceLayer layer = MyScreenSpaceLayer.BelowHud)
        {
            _layer = layer;
            _borderTextures = new BorderTextureCache(_dynamicTextures);
        }

        public ITextMetrics TextMetrics => _textMetrics;
        public ITextureResolver Textures => _textures;
        public IDynamicTextureFactory DynamicTextures => _dynamicTextures;
        public BorderTextureCache BorderTextures => _borderTextures;

        public IDisposable PushClip(RectangleF bounds)
        {
            return MyScreenSpace.UsingScissorRectanglePixels(ToRectangle(bounds), _layer);
        }

        public void FillRectangle(RectangleF bounds, Color color)
        {
            MyScreenSpaceSprite.DrawPixels(
                Square,
                ToRectangle(bounds),
                null,
                color,
                0f,
                _layer);
        }

        public void DrawTexture(
            TextureSource texture,
            RectangleF bounds,
            Color color,
            float rotation)
        {
            object resolved;
            if (!_textures.TryResolve(texture, out resolved))
                return;

            MyScreenSpaceSprite.DrawPixels(
                (MyStringId)resolved,
                ToRectangle(bounds),
                null,
                color,
                rotation,
                _layer);
        }

        public void DrawText(
            string font,
            string text,
            Vector2 position,
            float scale,
            Color color,
            MyGuiDrawAlignEnum alignment)
        {
            if (string.IsNullOrEmpty(text))
                return;

            MyScreenSpaceText.DrawPixels(
                string.IsNullOrWhiteSpace(font) ? "White" : font,
                text,
                position,
                scale,
                color,
                alignment,
                float.PositiveInfinity,
                _layer);
        }

        static Rectangle ToRectangle(RectangleF value)
        {
            int left = (int)Math.Round(value.X);
            int top = (int)Math.Round(value.Y);
            int right = (int)Math.Round(value.Right);
            int bottom = (int)Math.Round(value.Bottom);
            return new Rectangle(
                left,
                top,
                Math.Max(0, right - left),
                Math.Max(0, bottom - top));
        }

    }
}
