using System;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using VRageMath;

namespace Adk.Gui.Controls
{
    public enum TriangleDirection
    {
        Up,
        Down
    }

    public sealed class TriangleIcon : VisualElement
    {
        public TriangleDirection Direction { get; set; } = TriangleDirection.Down;
        public Color Color { get; set; } = Color.White;
        public Func<Color> ColorProvider { get; set; }
        public float MaxSize { get; set; } = 10f;
        public int Steps { get; set; } = 6;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            float size = Math.Max(0f, MaxSize);
            return new Vector2(
                Math.Min(availableSize.X, size),
                Math.Min(availableSize.Y, size));
        }

        protected override void DrawSelf(RenderContext context)
        {
            int steps = Math.Max(2, Steps);
            float size = Math.Max(1f, Math.Min(MaxSize, Math.Min(Bounds.Width, Bounds.Height)));
            float rowHeight = Math.Max(1f, size / steps);
            float top = Bounds.Center.Y - size * 0.5f;
            Color color = ColorProvider?.Invoke() ?? Color;

            for (int row = 0; row < steps; row++)
            {
                int widthStep = Direction == TriangleDirection.Down ? steps - row : row + 1;
                float width = size * widthStep / steps;
                float y = top + row * rowHeight;
                context.Fill(new RectangleF(
                    Bounds.Center.X - width * 0.5f,
                    y,
                    width,
                    Math.Min(rowHeight + 0.5f, top + size - y)), color);
            }
        }
    }
}
