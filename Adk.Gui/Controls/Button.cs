using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Controls
{
    public enum ButtonShape
    {
        Rectangle,
        RoundedRectangle,
        Pill,
        Circle
    }

    public enum ButtonContentAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// The framework's single clickable content control. Visual variants are
    /// selected by generic Style selectors and Setter elements, not subclasses.
    /// </summary>
    public class Button : TemplatedControl
    {
        static Button() { }

        Border _chrome;
        Border _outline;
        ButtonPresenter _presenter;
        VisualElement _content;
        RectangleF _drawBounds;
        string _text;
        string _icon;
        bool _selected;

        public static readonly StyledProperty<ButtonShape> ShapeProperty =
            StyledProperty.Register<Button, ButtonShape>(
                nameof(Shape), ButtonShape.Rectangle, UiInvalidation.Arrange);
        public static readonly StyledProperty<ButtonContentAlignment> ContentAlignmentProperty =
            StyledProperty.Register<Button, ButtonContentAlignment>(
                nameof(ContentAlignment), ButtonContentAlignment.Center, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> CornerRadiusProperty =
            StyledProperty.Register<Button, float>(
                nameof(CornerRadius), 0f);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<Button, int>(
                nameof(BorderThickness), 1);
        public static readonly StyledProperty<Vector4> PaddingProperty =
            StyledProperty.Register<Button, Vector4>(
                nameof(Padding), Vector4.Zero, UiInvalidation.Measure);
        public static readonly StyledProperty<Vector2> ShadowOffsetProperty =
            StyledProperty.Register<Button, Vector2>(
                nameof(ShadowOffset), Vector2.Zero);
        public static readonly StyledProperty<float> ScaleProperty =
            StyledProperty.Register<Button, float>(nameof(Scale), 1f);
        public static readonly StyledProperty<float> IconScaleProperty =
            StyledProperty.Register<Button, float>(
                nameof(IconScale), 0.75f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> TextScaleProperty =
            StyledProperty.Register<Button, float>(
                nameof(TextScale), 0.72f, UiInvalidation.Measure);
        public static readonly StyledProperty<bool> UseMissingIconFallbackProperty =
            StyledProperty.Register<Button, bool>(nameof(UseMissingIconFallback), true);
        public static readonly StyledProperty<float> SeparatorInsetProperty =
            StyledProperty.Register<Button, float>(nameof(SeparatorInset), 8f);
        public static readonly StyledProperty<float> SeparatorThicknessProperty =
            StyledProperty.Register<Button, float>(nameof(SeparatorThickness), 1f);
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<Button, BackgroundBrush>(
                "Background", new SolidColorBrush(Color.Transparent));
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<Button, Color>(nameof(Foreground), Color.White);
        public static readonly StyledProperty<Color> BorderProperty =
            StyledProperty.Register<Button, Color>(
                "Border", new Color(82, 98, 107));
        public static readonly StyledProperty<Color> ShadowProperty =
            StyledProperty.Register<Button, Color>("Shadow", Color.Transparent);
        public static readonly StyledProperty<Color> SeparatorProperty =
            StyledProperty.Register<Button, Color>(nameof(Separator), Color.Transparent);

        public ButtonShape Shape { get { return GetValue(ShapeProperty); } set { SetValue(ShapeProperty, value); } }
        public ButtonContentAlignment ContentAlignment { get { return GetValue(ContentAlignmentProperty); } set { SetValue(ContentAlignmentProperty, value); } }
        public float CornerRadius { get { return GetValue(CornerRadiusProperty); } set { SetValue(CornerRadiusProperty, value); } }
        public int BorderThickness { get { return GetValue(BorderThicknessProperty); } set { SetValue(BorderThicknessProperty, value); } }
        public Vector4 Padding { get { return GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public Vector2 ShadowOffset { get { return GetValue(ShadowOffsetProperty); } set { SetValue(ShadowOffsetProperty, value); } }
        public float Scale { get { return GetValue(ScaleProperty); } set { SetValue(ScaleProperty, value); } }
        public float IconScale { get { return GetValue(IconScaleProperty); } set { SetValue(IconScaleProperty, value); } }
        public float TextScale { get { return GetValue(TextScaleProperty); } set { SetValue(TextScaleProperty, value); } }
        public bool UseMissingIconFallback { get { return GetValue(UseMissingIconFallbackProperty); } set { SetValue(UseMissingIconFallbackProperty, value); } }
        public float SeparatorInset { get { return GetValue(SeparatorInsetProperty); } set { SetValue(SeparatorInsetProperty, value); } }
        public float SeparatorThickness { get { return GetValue(SeparatorThicknessProperty); } set { SetValue(SeparatorThicknessProperty, value); } }
        public BackgroundBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public Color BorderColor { get { return GetValue(BorderProperty); } set { SetValue(BorderProperty, value); } }
        public Color ShadowColor { get { return GetValue(ShadowProperty); } set { SetValue(ShadowProperty, value); } }
        public Color Separator { get { return GetValue(SeparatorProperty); } set { SetValue(SeparatorProperty, value); } }

        public Button()
        {
        }

        public string Text
        {
            get { return _text; }
            set
            {
                if (string.Equals(_text, value, StringComparison.Ordinal))
                    return;
                _text = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public string Icon
        {
            get { return _icon; }
            set
            {
                if (string.Equals(_icon, value, StringComparison.Ordinal))
                    return;
                _icon = value;
                Invalidate(UiInvalidation.Measure);
            }
        }
        public float IconRotation { get; set; }
        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                PseudoClasses.Set(PseudoClassNames.Selected, value);
            }
        }
        public Func<bool> SelectedProvider { get; set; }
        public Action Clicked { get; set; }
        public Action SecondaryClicked { get; set; }
        public object Tag { get; set; }
        public bool ShowTopSeparator { get; set; }

        public VisualElement Content
        {
            get { return _content; }
            set
            {
                if (ReferenceEquals(_content, value))
                    return;
                if (_content != null && _content.Parent != null)
                    _content.Parent.RemoveChild(_content);
                _content = value;
                if (_content != null)
                {
                    if (_presenter != null)
                        _presenter.Child = _content;
                    else
                        AddChild(_content);
                }
                Invalidate(UiInvalidation.Measure);
            }
        }

        internal bool IsSelected => SelectedProvider?.Invoke() ?? Selected;

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, IsPressed);
            PseudoClasses.Set(PseudoClassNames.Selected, IsSelected);
        }
        internal RectangleF DrawBounds => _drawBounds;

        protected override void OnApplyTemplate()
        {
            _chrome = GetTemplateChild<Border>("PART_Chrome");
            _outline = GetTemplateChild<Border>("PART_Outline");
            _presenter = GetTemplateChild<ButtonPresenter>("PART_ContentPresenter");
            if (_presenter != null && _content != null)
            {
                VisualElement content = _content;
                _presenter.Child = content;
                _content = content;
            }
        }

        protected override void OnTemplateChanging()
        {
            if (_presenter != null && ReferenceEquals(_presenter.Child, _content))
                _presenter.Child = null;
            _chrome = null;
            _outline = null;
            _presenter = null;
        }

        protected override void ValidateChildForAdd(VisualElement child)
        {
            if (IsApplyingTemplate)
            {
                base.ValidateChildForAdd(child);
                return;
            }
            if (ReferenceEquals(child, _chrome) || ReferenceEquals(child, _outline) ||
                child is ButtonPresenter)
                return;
            if (_content == null)
            {
                _content = child;
                return;
            }
            if (!ReferenceEquals(child, _content))
                throw new InvalidOperationException("Button accepts a single content visual.");
        }

        public override bool RemoveChild(VisualElement child)
        {
            bool removed = base.RemoveChild(child);
            if (removed && ReferenceEquals(child, _content))
                _content = null;
            return removed;
        }

        public override void ClearChildren()
        {
            Content = null;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            float horizontalPadding = Math.Max(0f, Padding.X) + Math.Max(0f, Padding.Z);
            float verticalPadding = Math.Max(0f, Padding.Y) + Math.Max(0f, Padding.W);
            Vector2 contentAvailable = new Vector2(
                float.IsPositiveInfinity(availableSize.X)
                    ? availableSize.X
                    : Math.Max(0f, availableSize.X - horizontalPadding),
                float.IsPositiveInfinity(availableSize.Y)
                    ? availableSize.Y
                    : Math.Max(0f, availableSize.Y - verticalPadding));

            Vector2 desired = Vector2.Zero;
            if (_content != null && _content.Visible)
                desired = _content.Measure(contentAvailable);

            if (!string.IsNullOrEmpty(Text))
            {
                Vector2 textSize = TextMetrics == null
                    ? Vector2.Zero
                    : TextMetrics.Measure("White", Text, TextScale);
                desired.X = Math.Max(desired.X, textSize.X);
                desired.Y = Math.Max(desired.Y, textSize.Y);
            }

            if (!string.IsNullOrEmpty(Icon) && _content == null)
            {
                float iconSize = Math.Max(1f, 24f * Math.Max(0f, IconScale));
                desired.X = Math.Max(desired.X, iconSize);
                desired.Y = Math.Max(desired.Y, iconSize);
            }

            return new Vector2(
                desired.X + horizontalPadding,
                desired.Y + verticalPadding);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            if (Shape != ButtonShape.Circle)
                return true;
            Vector2 delta = point - Bounds.Center;
            float radius = Math.Min(Bounds.Width, Bounds.Height) * 0.5f;
            return delta.LengthSquared() <= radius * radius;
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ApplyStyleAndLayout(bounds);
        }

        protected override void DrawSelf(RenderContext context)
        {
            ApplyStyleAndLayout(Bounds);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && Clicked != null)
                Clicked();
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && SecondaryClicked != null)
                SecondaryClicked();
        }

        void ApplyStyleAndLayout(RectangleF bounds)
        {
            _drawBounds = ScaleBounds(bounds, Math.Max(0f, Scale));

            float radius = ResolveCornerRadius(_drawBounds);
            if (_chrome != null)
            {
                _chrome.CornerRadius = radius;
                _chrome.UseGeneratedTexture = radius > 0f;
            }
            if (_outline != null)
            {
                _outline.CornerRadius = radius;
                _outline.UseGeneratedTexture = radius > 0f;
            }
            if (_presenter != null)
                _presenter.Padding = Padding;
            ArrangeTemplate(_drawBounds);
        }

        float ResolveCornerRadius(RectangleF bounds)
        {
            if (Shape == ButtonShape.Rectangle)
                return 0f;
            if (Shape == ButtonShape.Circle || Shape == ButtonShape.Pill)
                return Math.Min(bounds.Width, bounds.Height) * 0.5f;
            return Math.Max(0f, CornerRadius);
        }

        Color ResolveForeground(bool selected)
        {
            return Foreground;
        }

        static RectangleF ScaleBounds(RectangleF bounds, float scale)
        {
            Vector2 size = bounds.Size * scale;
            return new RectangleF(bounds.Center - size * 0.5f, size);
        }

        public sealed class ButtonPresenter : Decorator
        {
            Button Owner => TemplatedParent as Button;

            protected override void DrawSelf(RenderContext context)
            {
                Button owner = Owner;
                if (owner == null)
                    return;
                bool selected = owner.IsSelected;
                Color foreground = owner.ResolveForeground(selected);

                if (owner.ShowTopSeparator)
                {
                    float inset = Math.Max(0f, owner.SeparatorInset);
                    context.Fill(new RectangleF(
                        Bounds.X + inset,
                        Bounds.Y,
                        Math.Max(0f, Bounds.Width - inset * 2f),
                        Math.Max(0f, owner.SeparatorThickness)),
                        owner.Separator);
                }

                if (!string.IsNullOrEmpty(owner.Icon))
                {
                    float size = Math.Min(Bounds.Width, Bounds.Height) *
                                 Math.Max(0f, owner.IconScale);
                    context.Texture(
                        owner.Icon,
                        new RectangleF(
                            Bounds.Center - new Vector2(size * 0.5f),
                            new Vector2(size)),
                        foreground,
                        owner.UseMissingIconFallback,
                        owner.IconRotation);
                }

                if (string.IsNullOrEmpty(owner.Text))
                    return;

                Vector2 position;
                MyGuiDrawAlignEnum alignment;
                if (owner.ContentAlignment == ButtonContentAlignment.Left)
                {
                    position = new Vector2(
                        Bounds.X + Math.Max(0f, owner.Padding.X),
                        Bounds.Center.Y);
                    alignment = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER;
                }
                else if (owner.ContentAlignment == ButtonContentAlignment.Right)
                {
                    position = new Vector2(
                        Bounds.Right - Math.Max(0f, owner.Padding.Z),
                        Bounds.Center.Y);
                    alignment = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER;
                }
                else
                {
                    position = Bounds.Center;
                    alignment = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;
                }
                context.Text(
                    owner.Text,
                    position,
                    owner.TextScale,
                    foreground,
                    alignment,
                    TextOptions.GetDropShadow(owner));
            }
        }
    }
}
