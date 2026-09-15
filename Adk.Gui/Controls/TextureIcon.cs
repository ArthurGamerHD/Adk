using System;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using VRageMath;

namespace Adk.Gui.Controls
{
    /// <summary>
    /// Retained texture/icon visual for non-interactive imagery inside layout panels.
    /// </summary>
    public sealed class TextureIcon : VisualElement
    {
        public string Texture { get; set; }
        public Func<string> TextureProvider { get; set; }
        public Color Color { get; set; } = Color.White;
        public Func<Color> ColorProvider { get; set; }
        public bool UseMissingIconFallback { get; set; } = true;
        public float Rotation { get; set; }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            const float DefaultIconSize = 20f;
            return new Vector2(
                Math.Min(availableSize.X, DefaultIconSize),
                Math.Min(availableSize.Y, DefaultIconSize));
        }

        protected override void DrawSelf(RenderContext context)
        {
            string texture = TextureProvider != null ? TextureProvider() : Texture;
            if (string.IsNullOrWhiteSpace(texture))
                return;
            float size = Math.Max(0f, Math.Min(Bounds.Width, Bounds.Height));
            if (size <= 0f)
                return;
            context.Texture(
                texture,
                new RectangleF(Bounds.Center - new Vector2(size * 0.5f), new Vector2(size)),
                ColorProvider?.Invoke() ?? Color,
                UseMissingIconFallback,
                Rotation);
        }
    }
}
