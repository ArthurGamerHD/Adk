using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRage.Input;
using VRageMath;

namespace Adk.Gui.Controls
{
    /// <summary>
    /// Renderer-neutral single-line text editor. Input arrives through IInputSource,
    /// so the control has no dependency on a particular screen or render backend.
    /// </summary>
    public sealed class TextBox : TemplatedControl
    {
        static TextBox() { }

        const float CaretRevealMargin = 4f;
        const int KeyRepeatDelayFrames = 18;
        const int KeyRepeatIntervalFrames = 2;

        Border _border;
        ScrollViewer _scroller;
        TextPresenter _presenter;
        int _caretIndex;
        int _leftHeldFrames;
        int _rightHeldFrames;
        int _backspaceHeldFrames;
        int _deleteHeldFrames;

        public static readonly StyledProperty<string> TextProperty =
            StyledProperty.Register<TextBox, string>(nameof(Text), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> PlaceholderProperty =
            StyledProperty.Register<TextBox, string>(nameof(Placeholder), string.Empty);
        public static readonly StyledProperty<int> MaxLengthProperty =
            StyledProperty.Register<TextBox, int>(nameof(MaxLength), 128);
        public static readonly StyledProperty<Vector4> PaddingProperty =
            StyledProperty.Register<TextBox, Vector4>(
                nameof(Padding), new Vector4(10f, 0f, 10f, 0f), UiInvalidation.Measure);
        public static readonly StyledProperty<float> CornerRadiusProperty =
            StyledProperty.Register<TextBox, float>(nameof(CornerRadius), 6f);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<TextBox, int>(nameof(BorderThickness), 1);
        public static readonly StyledProperty<float> TextScaleProperty =
            StyledProperty.Register<TextBox, float>(nameof(TextScale), 0.72f, UiInvalidation.Measure);
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<TextBox, BackgroundBrush>(
                "Background", new SolidColorBrush(new Color(32, 38, 42, 245)));
        public static readonly StyledProperty<Color> BorderProperty =
            StyledProperty.Register<TextBox, Color>("Border", new Color(82, 98, 107));
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<TextBox, Color>(nameof(Foreground), Color.White);
        public static readonly StyledProperty<Color> PlaceholderForegroundProperty =
            StyledProperty.Register<TextBox, Color>(
                nameof(PlaceholderForeground), new Color(105, 113, 118));

        public TextBox()
        {
            _caretIndex = Text.Length;

        }

        public string Text
        {
            get { return GetValue(TextProperty) ?? string.Empty; }
            set
            {
                string next = Limit(value ?? string.Empty);
                SetValue(TextProperty, next);
                _caretIndex = Math.Min(_caretIndex, next.Length);
            }
        }
        public string Value { get { return Text; } set { Text = value; } }
        public string Placeholder
        {
            get { return GetValue(PlaceholderProperty) ?? string.Empty; }
            set
            {
                string next = value ?? string.Empty;
                SetValue(PlaceholderProperty, next);
            }
        }
        public int MaxLength { get { return GetValue(MaxLengthProperty); } set { SetValue(MaxLengthProperty, Math.Max(0, value)); Text = Text; } }
        public Vector4 Padding { get { return GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public float CornerRadius { get { return GetValue(CornerRadiusProperty); } set { SetValue(CornerRadiusProperty, value); } }
        public int BorderThickness { get { return GetValue(BorderThicknessProperty); } set { SetValue(BorderThicknessProperty, value); } }
        public float TextScale { get { return GetValue(TextScaleProperty); } set { SetValue(TextScaleProperty, value); } }
        public BackgroundBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public Color BorderColor { get { return GetValue(BorderProperty); } set { SetValue(BorderProperty, value); } }
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public Color PlaceholderForeground { get { return GetValue(PlaceholderForegroundProperty); } set { SetValue(PlaceholderForegroundProperty, value); } }

        public Action<string> TextChanged { get; set; }
        public Action<string> ValueChanged { get { return TextChanged; } set { TextChanged = value; } }
        public float HorizontalOffset => _scroller == null ? 0f : _scroller.HorizontalOffset;
        public override bool AcceptsKeyboardFocus => true;

        protected override void OnApplyTemplate()
        {
            _border = GetTemplateChild<Border>("PART_Border");
            _scroller = GetTemplateChild<ScrollViewer>("PART_Scroller");
            _presenter = GetTemplateChild<TextPresenter>("PART_Presenter");
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            return new Vector2(
                Math.Min(availableSize.X, 260f),
                Math.Min(availableSize.Y, 34f));
        }

        protected override bool HitTestSelf(Vector2 point) { return true; }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ArrangeTemplate(bounds);
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (_presenter == null || _scroller == null)
                return;
            bool showPlaceholder = Text.Length == 0 && !IsFocused;
            _presenter.Text = showPlaceholder ? Placeholder : Text;
            _presenter.Scale = TextScale;
            _presenter.Color = showPlaceholder ? PlaceholderForeground : Foreground;
            _presenter.ShowCaret = IsFocused;
            _presenter.CaretIndex = showPlaceholder ? 0 : Math.Max(0, Math.Min(_caretIndex, Text.Length));

            _scroller.RefreshContentLayout();
            if (IsFocused)
                RevealCaret();
            else if (showPlaceholder)
                _scroller.HorizontalOffset = 0f;
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (!clicked || _presenter == null || _scroller == null)
                return;
            _presenter.Text = Text;
            _presenter.CaretIndex = _caretIndex;
            _scroller.RefreshContentLayout();
            float contentX = point.X - _scroller.ViewportBounds.X + _scroller.HorizontalOffset;
            _caretIndex = _presenter.GetCaretIndexAt(contentX);
            _presenter.CaretIndex = _caretIndex;
            RevealCaret();
        }

        internal override void KeyboardInput(IInputSource input)
        {
            if (input == null)
                return;
            string value = Text;
            bool changed = false;
            foreach (char character in input.TextInput)
            {
                if (character == '\b' || char.IsControl(character))
                    continue;
                if (MaxLength > 0 && value.Length >= MaxLength)
                    continue;
                value = value.Insert(_caretIndex, character.ToString());
                _caretIndex++;
                changed = true;
            }

            if (ShouldRepeatKey(input, MyKeys.Back, ref _backspaceHeldFrames) && _caretIndex > 0)
            {
                value = value.Remove(_caretIndex - 1, 1);
                _caretIndex--;
                changed = true;
            }
            if (ShouldRepeatKey(input, MyKeys.Delete, ref _deleteHeldFrames) && _caretIndex < value.Length)
            {
                value = value.Remove(_caretIndex, 1);
                changed = true;
            }
            if (ShouldRepeatKey(input, MyKeys.Left, ref _leftHeldFrames) && _caretIndex > 0)
                _caretIndex--;
            if (ShouldRepeatKey(input, MyKeys.Right, ref _rightHeldFrames) && _caretIndex < value.Length)
                _caretIndex++;
            if (input.IsNewKeyPressed(MyKeys.Home))
                _caretIndex = 0;
            if (input.IsNewKeyPressed(MyKeys.End))
                _caretIndex = value.Length;

            if (changed)
            {
                Text = value;
                if (TextChanged != null)
                    TextChanged(Text);
            }
        }

        string Limit(string value)
        {
            return MaxLength > 0 && value.Length > MaxLength
                ? value.Substring(0, MaxLength)
                : value;
        }

        static bool ShouldRepeatKey(IInputSource input, MyKeys key, ref int heldFrames)
        {
            if (!input.IsKeyPressed(key))
            {
                heldFrames = 0;
                return false;
            }
            if (input.IsNewKeyPressed(key))
            {
                heldFrames = 1;
                return true;
            }
            heldFrames++;
            return heldFrames >= KeyRepeatDelayFrames &&
                   (heldFrames - KeyRepeatDelayFrames) % KeyRepeatIntervalFrames == 0;
        }

        void RevealCaret()
        {
            if (_presenter == null || _scroller == null)
                return;
            _presenter.Text = Text;
            _presenter.CaretIndex = Math.Max(0, Math.Min(_caretIndex, Text.Length));
            _presenter.ShowCaret = true;
            _scroller.RefreshContentLayout();
            float caretX = _presenter.GetCaretOffset();
            _scroller.BringIntoView(
                new RectangleF(caretX, 0f, Math.Max(1f, _presenter.CaretWidth),
                    Math.Max(1f, _scroller.ViewportHeight)),
                CaretRevealMargin);
        }
    }
}
