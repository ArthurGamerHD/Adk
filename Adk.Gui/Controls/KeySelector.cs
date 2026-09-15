using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Controls
{
    /// <summary>
    /// Focusable key-binding editor. Clicking begins capture; the next newly
    /// pressed key becomes SelectedKey and ends capture.
    /// </summary>
    public sealed class KeySelector : TemplatedControl
    {
        static KeySelector() { }

        Border _border;
        TextBlock _text;
        readonly List<MyKeys> _pressedKeys = new List<MyKeys>(16);
        readonly Action<MyKeys> _selectionChanged;
        readonly Action _stateChanged;

        public static readonly StyledProperty<MyKeys> SelectedKeyProperty =
            StyledProperty.Register<KeySelector, MyKeys>(
                nameof(SelectedKey), MyKeys.None, UiInvalidation.Measure);
        public static readonly StyledProperty<string> CaptureTextProperty =
            StyledProperty.Register<KeySelector, string>(
                nameof(CaptureText), "Press a key…", UiInvalidation.Measure);
        public static readonly StyledProperty<Vector4> PaddingProperty =
            StyledProperty.Register<KeySelector, Vector4>(
                nameof(Padding), new Vector4(12f, 0f, 12f, 0f), UiInvalidation.Measure);
        public static readonly StyledProperty<float> CornerRadiusProperty =
            StyledProperty.Register<KeySelector, float>(nameof(CornerRadius), 6f);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<KeySelector, int>(nameof(BorderThickness), 1);
        public static readonly StyledProperty<float> TextScaleProperty =
            StyledProperty.Register<KeySelector, float>(nameof(TextScale), 0.68f, UiInvalidation.Measure);
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<KeySelector, BackgroundBrush>(
                "Background", new SolidColorBrush(new Color(32, 38, 42, 245)));
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<KeySelector, Color>(nameof(Foreground), Color.White);
        public static readonly StyledProperty<Color> BorderProperty =
            StyledProperty.Register<KeySelector, Color>(
                "Border", new Color(82, 98, 107));

        public KeySelector(MyKeys selectedKey = MyKeys.None,
            Action<MyKeys> selectionChanged = null, Action stateChanged = null)
        {
            SelectedKey = selectedKey;
            _selectionChanged = selectionChanged;
            _stateChanged = stateChanged;
        }

        public MyKeys SelectedKey
        {
            get { return GetValue(SelectedKeyProperty); }
            set { SetValue(SelectedKeyProperty, value); }
        }
        public MyKeys SelectedValue => KeyProvider?.Invoke() ?? SelectedKey;
        public string CaptureText { get { return GetValue(CaptureTextProperty); } set { SetValue(CaptureTextProperty, value ?? string.Empty); } }
        public Vector4 Padding { get { return GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public float CornerRadius { get { return GetValue(CornerRadiusProperty); } set { SetValue(CornerRadiusProperty, value); } }
        public int BorderThickness { get { return GetValue(BorderThicknessProperty); } set { SetValue(BorderThicknessProperty, value); } }
        public float TextScale { get { return GetValue(TextScaleProperty); } set { SetValue(TextScaleProperty, value); } }
        public BackgroundBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public Color BorderColor { get { return GetValue(BorderProperty); } set { SetValue(BorderProperty, value); } }

        public bool IsOpen { get; private set; }
        public Func<MyKeys> KeyProvider { get; set; }
        public Action<MyKeys> SelectionChanged { get; set; }
        public Action<MyKeys> Changed { get { return SelectionChanged; } set { SelectionChanged = value; } }
        public Action StateChanged { get; set; }
        public override bool AcceptsKeyboardFocus => true;

        protected override void OnApplyTemplate()
        {
            _border = GetTemplateChild<Border>("PART_Border");
            _text = GetTemplateChild<TextBlock>("PART_Text");
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, IsPressed);
        }

        public void Configure(RectangleF bounds, float scale)
        {
            Arrange(bounds);
            Visible = true;
        }

        public void SetSelectedValue(MyKeys value, bool notify = false)
        {
            if (SelectedKey == value)
                return;
            SelectedKey = value;
            if (notify)
                RaiseSelectionChanged(value);
        }

        public void Open()
        {
            if (IsOpen || !Enabled)
                return;
            IsOpen = true;
            PseudoClasses.Set(PseudoClassNames.Open, true);
            Invalidate(UiInvalidation.Measure);
            RaiseStateChanged();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            PseudoClasses.Set(PseudoClassNames.Open, false);
            Invalidate(UiInvalidation.Measure);
            RaiseStateChanged();
        }

        public void CloseSelector() { Close(); }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            string text = IsOpen ? CaptureText : FormatKey(SelectedValue);
            Vector2 measured = TextMetrics == null
                ? Vector2.Zero
                : TextMetrics.Measure("White", text, TextScale);
            float horizontalPadding = Math.Max(0f, Padding.X) + Math.Max(0f, Padding.Z);
            float verticalPadding = Math.Max(0f, Padding.Y) + Math.Max(0f, Padding.W);
            return new Vector2(
                Math.Min(availableSize.X, Math.Max(160f, measured.X + horizontalPadding)),
                Math.Min(availableSize.Y, Math.Max(34f, measured.Y + verticalPadding)));
        }

        protected override bool HitTestSelf(Vector2 point) { return true; }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ArrangeTemplate(bounds);
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (_text != null)
                _text.Text = IsOpen ? CaptureText : FormatKey(SelectedValue);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (!clicked)
                return;
            if (IsOpen) Close(); else Open();
        }

        internal override void KeyboardInput(IInputSource input)
        {
            if (!IsOpen || input == null)
                return;
            if (input.IsNewKeyPressed(MyKeys.Escape))
            {
                Close();
                return;
            }

            _pressedKeys.Clear();
            input.GetPressedKeys(_pressedKeys);
            for (int i = 0; i < _pressedKeys.Count; i++)
            {
                MyKeys key = _pressedKeys[i];
                if (!input.IsNewKeyPressed(key))
                    continue;
                SelectedKey = key;
                RaiseSelectionChanged(key);
                Close();
                return;
            }
        }

        public static string FormatKey(MyKeys key)
        {
            switch (key)
            {
                case MyKeys.OemPlus: return "+";
                case MyKeys.OemMinus: return "-";
                case MyKeys.OemQuotes: return "'";
                case MyKeys.Space: return "Space";
                case MyKeys.None: return "None";
                default: return key.ToString();
            }
        }

        void RaiseSelectionChanged(MyKeys key)
        {
            if (_selectionChanged != null) _selectionChanged(key);
            if (SelectionChanged != null) SelectionChanged(key);
        }

        void RaiseStateChanged()
        {
            if (_stateChanged != null) _stateChanged();
            if (StateChanged != null) StateChanged();
        }
    }
}
