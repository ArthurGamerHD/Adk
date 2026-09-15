using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Controls
{
    public enum ComboBoxOpenDirection
    {
        Down,
        Up
    }

    /// <summary>
    /// Non-generic style owner for every ComboBox&lt;T&gt;. This keeps runtime style
    /// metadata closed and reflection-free while item values remain strongly typed.
    /// </summary>
    public abstract class ComboBox : TemplatedControl
    {
        static ComboBox() { }

        public static readonly StyledProperty<float> HeaderHeightProperty =
            StyledProperty.Register<ComboBox, float>(
                nameof(HeaderHeight), 34f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> OptionHeightProperty =
            StyledProperty.Register<ComboBox, float>(
                nameof(OptionHeight), 32f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> SpacingProperty =
            StyledProperty.Register<ComboBox, float>(
                nameof(Spacing), 0f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> CornerRadiusProperty =
            StyledProperty.Register<ComboBox, float>(nameof(CornerRadius), 6f);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<ComboBox, int>(nameof(BorderThickness), 1);
        public static readonly StyledProperty<Vector4> PaddingProperty =
            StyledProperty.Register<ComboBox, Vector4>(
                nameof(Padding), new Vector4(12f, 0f, 8f, 0f), UiInvalidation.Measure);
        public static readonly StyledProperty<Vector4> PopupPaddingProperty =
            StyledProperty.Register<ComboBox, Vector4>(
                nameof(PopupPadding), Vector4.Zero, UiInvalidation.Measure);
        public static readonly StyledProperty<float> TextScaleProperty =
            StyledProperty.Register<ComboBox, float>(
                nameof(TextScale), 0.68f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> IndicatorSizeProperty =
            StyledProperty.Register<ComboBox, float>(
                nameof(IndicatorSize), 9f, UiInvalidation.Measure);
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<ComboBox, BackgroundBrush>(
                "Background", new SolidColorBrush(new Color(32, 38, 42, 245)));
        public static readonly StyledProperty<BackgroundBrush> PopupBackgroundProperty =
            StyledProperty.Register<ComboBox, BackgroundBrush>(
                nameof(PopupBackground),
                new SolidColorBrush(new Color(32, 38, 42, 250)));
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<ComboBox, Color>(nameof(Foreground), Color.White);
        public static readonly StyledProperty<Color> ItemForegroundProperty =
            StyledProperty.Register<ComboBox, Color>(nameof(ItemForeground), Color.White);
        public static readonly StyledProperty<Color> ActiveItemForegroundProperty =
            StyledProperty.Register<ComboBox, Color>(nameof(ActiveItemForeground), Color.White);
        public static readonly StyledProperty<Color> BorderProperty =
            StyledProperty.Register<ComboBox, Color>("Border", new Color(82, 98, 107));

        public float HeaderHeight { get { return GetValue(HeaderHeightProperty); } set { SetValue(HeaderHeightProperty, value); } }
        public float OptionHeight { get { return GetValue(OptionHeightProperty); } set { SetValue(OptionHeightProperty, value); } }
        public float Spacing { get { return GetValue(SpacingProperty); } set { SetValue(SpacingProperty, value); } }
        public float CornerRadius { get { return GetValue(CornerRadiusProperty); } set { SetValue(CornerRadiusProperty, value); } }
        public int BorderThickness { get { return GetValue(BorderThicknessProperty); } set { SetValue(BorderThicknessProperty, value); } }
        public Vector4 Padding { get { return GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public Vector4 PopupPadding { get { return GetValue(PopupPaddingProperty); } set { SetValue(PopupPaddingProperty, value); } }
        public float TextScale { get { return GetValue(TextScaleProperty); } set { SetValue(TextScaleProperty, value); } }
        public float IndicatorSize { get { return GetValue(IndicatorSizeProperty); } set { SetValue(IndicatorSizeProperty, value); } }
        public BackgroundBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public BackgroundBrush PopupBackground { get { return GetValue(PopupBackgroundProperty); } set { SetValue(PopupBackgroundProperty, value); } }
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        public Color ItemForeground { get { return GetValue(ItemForegroundProperty); } set { SetValue(ItemForegroundProperty, value); } }
        public Color ActiveItemForeground { get { return GetValue(ActiveItemForegroundProperty); } set { SetValue(ActiveItemForegroundProperty, value); } }
        public Color BorderColor { get { return GetValue(BorderProperty); } set { SetValue(BorderProperty, value); } }
    }

    /// <summary>A typed drop-down selector whose popup is rendered by VisualTree's overlay pass.</summary>
    public sealed class ComboBox<T> : ComboBox, IOverlayProvider
    {
        readonly List<T> _options = new List<T>();
        readonly List<Border> _items = new List<Border>();
        readonly List<TextBlock> _itemTexts = new List<TextBlock>();
        readonly Func<T, string> _getLabel;
        readonly Action<T> _selectionChanged;
        readonly Action _stateChanged;
        Border _header;
        TextBlock _headerText;
        TriangleIcon _headerIndicator;
        readonly Border _popup;
        readonly StackPanel _popupPanel;
        T _selectedValue;
        float _layoutScale = 1f;

        public ComboBox(
            IEnumerable<T> options,
            Func<T, string> getLabel,
            Action<T> selectionChanged,
            Action stateChanged = null)
        {
            _getLabel = getLabel ??
                (value => ReferenceEquals(value, null) ? string.Empty : value.ToString());
            _selectionChanged = selectionChanged;
            _stateChanged = stateChanged;

            _popupPanel = new StackPanel();
            _popup = new Border { Child = _popupPanel, Visible = false };
            SetOptions(options);
        }

        public ComboBoxOpenDirection OpenDirection { get; set; } = ComboBoxOpenDirection.Down;
        public float SpacingPixels { get { return Spacing; } set { Spacing = value; } }
        public bool IsOpen { get; private set; }
        public T SelectedValue => _selectedValue;
        public Action<T> SelectionChanged { get; set; }
        public Action StateChanged { get; set; }
        public override bool AcceptsKeyboardFocus => true;

        protected override void OnApplyTemplate()
        {
            _header = GetTemplateChild<Border>("PART_Header");
            _headerText = GetTemplateChild<TextBlock>("PART_HeaderText");
            _headerIndicator = GetTemplateChild<TriangleIcon>("PART_Indicator");
            if (_header != null)
                _header.Clicked = Toggle;
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, IsPressed);
        }

        public void Configure(RectangleF bounds, float scale)
        {
            _layoutScale = Math.Max(0.01f, scale);
            Arrange(bounds);
            Visible = true;
        }

        public void SetOptions(IEnumerable<T> options)
        {
            _options.Clear();
            if (options != null)
                _options.AddRange(options);
            if (_options.Count > 0 && !ContainsValue(_selectedValue))
                _selectedValue = _options[0];
            RebuildItems();
        }

        public void SetSelectedValue(T value, bool notify = false)
        {
            if (_options.Count > 0 && !ContainsValue(value))
                return;
            bool changed = !EqualityComparer<T>.Default.Equals(_selectedValue, value);
            _selectedValue = value;
            if (changed)
                Invalidate(UiInvalidation.Measure);
            if (changed && notify)
                RaiseSelectionChanged(value);
        }

        public void Open()
        {
            if (IsOpen || !Enabled || _options.Count == 0)
                return;
            IsOpen = true;
            PseudoClasses.Set(PseudoClassNames.Open, true);
            _popup.Visible = true;
            Invalidate(UiInvalidation.Render);
            UpdateOverlayLayout();
            RaiseStateChanged();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            PseudoClasses.Set(PseudoClassNames.Open, false);
            _popup.Visible = false;
            Invalidate(UiInvalidation.Render);
            RaiseStateChanged();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            string label = _getLabel(_selectedValue);
            Vector2 textSize = TextMetrics == null
                ? Vector2.Zero
                : TextMetrics.Measure("White", label, TextScale);
            float horizontalPadding = Math.Max(0f, Padding.X) + Math.Max(0f, Padding.Z);
            float desiredWidth = Math.Max(260f, textSize.X + horizontalPadding + Math.Max(0f, IndicatorSize) + 8f);
            float desiredHeight = Math.Max(HeaderHeight, textSize.Y);
            return new Vector2(
                Math.Min(availableSize.X, desiredWidth),
                Math.Min(availableSize.Y, desiredHeight));
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ArrangeTemplate(bounds);
        }

        protected override void DrawSelf(RenderContext context)
        {
            ApplyPopupAppearance();
            if (_headerText != null)
                _headerText.Text = _getLabel(_selectedValue);
            if (_headerIndicator != null)
                _headerIndicator.Direction = IsOpen ? TriangleDirection.Up : TriangleDirection.Down;
            UpdateOverlayLayout();
        }

        internal override void KeyboardInput(IInputSource input)
        {
            if (input == null)
                return;
            if (input.IsNewKeyPressed(MyKeys.Escape))
            {
                Close();
                return;
            }
            if (input.IsNewKeyPressed(MyKeys.Enter) || input.IsNewKeyPressed(MyKeys.Space))
            {
                Toggle();
                return;
            }
            if (!IsOpen || _options.Count == 0)
                return;
            int index = IndexOf(_selectedValue);
            if (input.IsNewKeyPressed(MyKeys.Down))
                SelectValue(_options[Math.Min(_options.Count - 1, Math.Max(0, index + 1))], false);
            else if (input.IsNewKeyPressed(MyKeys.Up))
                SelectValue(_options[Math.Max(0, index - 1)], false);
        }

        void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        void RebuildItems()
        {
            for (int i = 0; i < _items.Count; i++)
                _popupPanel.RemoveChild(_items[i]);
            _items.Clear();
            _itemTexts.Clear();

            for (int i = 0; i < _options.Count; i++)
            {
                T option = _options[i];
                var text = new TextBlock
                {
                    Text = _getLabel(option),
                    Alignment = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
                };
                Border border = _popupPanel.Add(new Border
                {
                    Interactive = true,
                    StyleClass = "combo-box-item",
                    Child = text
                });
                border.Clicked = () => SelectValue(option, true);
                _items.Add(border);
                _itemTexts.Add(text);
            }
            ApplyPopupAppearance();
        }

        void ApplyPopupAppearance()
        {
            _popup.Background = PopupBackground;
            _popup.BorderColor = BorderColor;
            _popup.BorderThickness = BorderThickness;
            _popup.CornerRadius = CornerRadius;
            _popup.Padding = PopupPadding;
            float itemLeftPadding = Math.Max(0f, Padding.X - PopupPadding.X);
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].BorderColor = Color.Transparent;
                _items[i].BorderThickness = 0;
                _items[i].CornerRadius = 0f;
                _items[i].Padding = new Vector4(itemLeftPadding, 0f, 0f, 0f);
                _itemTexts[i].Scale = TextScale;
                _itemTexts[i].Color = _items[i].IsPressed
                    ? ActiveItemForeground
                    : ItemForeground;
            }
        }

        void SelectValue(T value, bool close)
        {
            bool changed = !EqualityComparer<T>.Default.Equals(_selectedValue, value);
            _selectedValue = value;
            if (changed)
                Invalidate(UiInvalidation.Measure);
            if (close)
            {
                IsOpen = false;
                PseudoClasses.Set(PseudoClassNames.Open, false);
                _popup.Visible = false;
            }
            if (changed)
                RaiseSelectionChanged(value);
            RaiseStateChanged();
        }

        bool ContainsValue(T value) { return IndexOf(value) >= 0; }

        int IndexOf(T value)
        {
            for (int i = 0; i < _options.Count; i++)
                if (EqualityComparer<T>.Default.Equals(_options[i], value))
                    return i;
            return -1;
        }

        bool IOverlayProvider.IsOverlayOpen => IsOpen;
        VisualElement IOverlayProvider.OverlayElement => _popup;
        void IOverlayProvider.UpdateOverlayLayout() { UpdateOverlayLayout(); }
        void IOverlayProvider.CloseOverlay() { Close(); }

        void UpdateOverlayLayout()
        {
            if (!IsOpen)
                return;
            float spacing = Spacing * _layoutScale;
            _popupPanel.Spacing = spacing;
            for (int i = 0; i < _items.Count; i++)
                _items[i].Height = OptionHeight;
            float height = BorderThickness * 2f + PopupPadding.Y + PopupPadding.W +
                _items.Count * OptionHeight + Math.Max(0, _items.Count - 1) * spacing;
            float y = OpenDirection == ComboBoxOpenDirection.Up
                ? Bounds.Y - height
                : Bounds.Bottom;
            _popup.Arrange(new RectangleF(Bounds.X, y, Bounds.Width, Math.Max(1f, height)));
        }

        public override void Dispose()
        {
            _popup.Dispose();
            base.Dispose();
        }

        void RaiseSelectionChanged(T value)
        {
            if (_selectionChanged != null) _selectionChanged(value);
            if (SelectionChanged != null) SelectionChanged(value);
        }

        void RaiseStateChanged()
        {
            if (_stateChanged != null) _stateChanged();
            if (StateChanged != null) StateChanged();
        }
    }
}
