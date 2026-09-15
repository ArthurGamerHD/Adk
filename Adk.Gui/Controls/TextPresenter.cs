using System;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Controls
{
    public sealed class TextPresenter : VisualElement
    {
        const string Font = "White";

        public string Text { get; set; } = string.Empty;
        public float Scale { get; set; } = 0.72f;
        public Color Color { get; set; } = Color.White;
        public bool ShowCaret { get; set; }
        public int CaretIndex { get; set; }
        public float CaretWidth { get; set; } = 1f;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Vector2 measured = MeasureText(Text);
            float width = Math.Max(1f, measured.X + (ShowCaret ? Math.Max(1f, CaretWidth) : 0f));
            float height = Math.Max(1f, measured.Y);
            return new Vector2(
                Math.Min(availableSize.X, width),
                Math.Min(availableSize.Y, height));
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                context.Text(
                    Text,
                    new Vector2(Bounds.X, Bounds.Center.Y),
                    Scale,
                    Color,
                    MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
                    TextOptions.GetDropShadow(this));
            }

            if (!ShowCaret)
                return;

            float x = Bounds.X + GetCaretOffset();
            float height = Math.Max(4f, MeasureText("M").Y * 0.82f);
            context.Fill(
                new RectangleF(x, Bounds.Center.Y - height * 0.5f, Math.Max(1f, CaretWidth), height),
                Color);
        }

        public float GetCaretOffset()
        {
            string text = Text ?? string.Empty;
            int index = Math.Max(0, Math.Min(CaretIndex, text.Length));
            if (index == 0)
                return 0f;
            return MeasureText(text.Substring(0, index)).X;
        }

        public int GetCaretIndexAt(float contentX)
        {
            string text = Text ?? string.Empty;
            if (text.Length == 0 || contentX <= 0f)
                return 0;

            float fullWidth = MeasureText(text).X;
            if (contentX >= fullWidth)
                return text.Length;

            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high) / 2;
                float width = mid == 0 ? 0f : MeasureText(text.Substring(0, mid)).X;
                if (width < contentX)
                    low = mid + 1;
                else
                    high = mid;
            }

            int rightIndex = Math.Max(0, Math.Min(low, text.Length));
            int leftIndex = Math.Max(0, rightIndex - 1);
            float leftWidth = leftIndex == 0 ? 0f : MeasureText(text.Substring(0, leftIndex)).X;
            float rightWidth = rightIndex == 0 ? 0f : MeasureText(text.Substring(0, rightIndex)).X;
            return Math.Abs(contentX - leftWidth) <= Math.Abs(rightWidth - contentX)
                ? leftIndex
                : rightIndex;
        }

        Vector2 MeasureText(string value)
        {
            return TextMetrics?.Measure(Font, value, Scale) ?? Vector2.Zero;
        }
    }
}
