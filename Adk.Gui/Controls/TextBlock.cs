using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Controls
{
    public enum TextAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// Attached text-rendering options.  These are intentionally independent of
    /// individual text controls so a theme can opt controls into text effects with
    /// owner-qualified setters such as TextOptions.DropShadow.
    /// </summary>
    public static class TextOptions
    {
        public static readonly StyledProperty<bool> DropShadowProperty =
            StyledProperty.RegisterAttached<Control, bool>(
                typeof(TextOptions),
                "DropShadow",
                false,
                UiInvalidation.Render);

        public static bool GetDropShadow(Control control)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));
            return control.GetValue(DropShadowProperty);
        }

        public static void SetDropShadow(Control control, bool value)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));
            control.SetValue(DropShadowProperty, value);
        }

        /// <summary>Forces attached-property registration before a stylesheet is parsed.</summary>
        public static void EnsureRegistered()
        {
            // Accessing the static field is sufficient; the CLR initializes this type first.
            StyledProperty<bool> ignored = DropShadowProperty;
        }
    }

    public class TextBlock : VisualElement
    {
        static TextBlock() { }

        public static readonly StyledProperty<string> TextProperty =
            StyledProperty.Register<TextBlock, string>(
                "Text", null, UiInvalidation.Measure);
        public static readonly StyledProperty<float> ScaleProperty =
            StyledProperty.Register<TextBlock, float>(
                "Scale", 0.8f, UiInvalidation.Measure);
        public static readonly StyledProperty<Color> ColorProperty =
            StyledProperty.Register<TextBlock, Color>(
                "Color", Color.White, UiInvalidation.Render);
        public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
            StyledProperty.Register<TextBlock, TextAlignment>(
                "TextAlignment", TextAlignment.Left, UiInvalidation.Render);

        public string Text
        {
            get { return GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public Func<string> TextProvider { get; set; }
        public Func<Color> ColorProvider { get; set; }

        public float Scale
        {
            get { return GetValue(ScaleProperty); }
            set { SetValue(ScaleProperty, value); }
        }

        public Color Color
        {
            get { return GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }

        /// <summary>
        /// Horizontal alignment of text inside the arranged TextBlock bounds.
        /// This is separate from the legacy renderer draw-anchor Alignment property.
        /// </summary>
        public TextAlignment TextAlignment
        {
            get { return GetValue(TextAlignmentProperty); }
            set { SetValue(TextAlignmentProperty, value); }
        }

        public MyGuiDrawAlignEnum Alignment { get; set; } =
            MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            string text = TextProvider != null ? TextProvider() : Text;
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;
            Vector2 measured = TextMetrics?.Measure("White", text, Scale) ?? Vector2.Zero;
            return new Vector2(
                Math.Min(availableSize.X, Math.Max(0f, measured.X)),
                Math.Min(availableSize.Y, Math.Max(0f, measured.Y)));
        }

        protected override void DrawSelf(RenderContext context)
        {
            string text = TextProvider != null ? TextProvider() : Text;
            MyGuiDrawAlignEnum drawAlignment = ResolveTextAlignment(Alignment, TextAlignment);
            Vector2 position = ResolveTextPosition(drawAlignment);
            Color color = ColorProvider?.Invoke() ?? Color;
            context.Text(
                text,
                position,
                Scale,
                color,
                drawAlignment,
                TextOptions.GetDropShadow(this));
        }

        Vector2 ResolveTextPosition(MyGuiDrawAlignEnum alignment)
        {
            float x = Bounds.X;
            float y = Bounds.Y;

            switch (alignment)
            {
                case MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP:
                case MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER:
                case MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM:
                    x = Bounds.Center.X;
                    break;
                case MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP:
                case MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER:
                case MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM:
                    x = Bounds.Right;
                    break;
            }

            switch (alignment)
            {
                case MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER:
                case MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER:
                case MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER:
                    y = Bounds.Center.Y;
                    break;
                case MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM:
                case MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM:
                case MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM:
                    y = Bounds.Bottom;
                    break;
            }

            return new Vector2(x, y);
        }

        static MyGuiDrawAlignEnum ResolveTextAlignment(
            MyGuiDrawAlignEnum alignment,
            TextAlignment textAlignment)
        {
            bool top = alignment == MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP ||
                       alignment == MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP ||
                       alignment == MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP;
            bool bottom = alignment == MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM ||
                          alignment == MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM ||
                          alignment == MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM;

            if (top)
            {
                if (textAlignment == TextAlignment.Center)
                    return MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP;
                if (textAlignment == TextAlignment.Right)
                    return MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP;
                return MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
            }

            if (bottom)
            {
                if (textAlignment == TextAlignment.Center)
                    return MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM;
                if (textAlignment == TextAlignment.Right)
                    return MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_BOTTOM;
                return MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM;
            }

            if (textAlignment == TextAlignment.Center)
                return MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
            if (textAlignment == TextAlignment.Right)
                return MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER;
            return MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER;
        }
    }
}