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
    public enum MessageBoxResult
    {
        None,
        Ok,
        Cancel,
        Close
    }

    public struct MessageBoxButton
    {
        public MessageBoxButton(string text, MessageBoxResult result)
        {
            Text = text;
            Result = result;
        }

        public string Text { get; private set; }
        public MessageBoxResult Result { get; private set; }

        public static MessageBoxButton Ok
        {
            get { return new MessageBoxButton("OK", MessageBoxResult.Ok); }
        }

        public static MessageBoxButton Cancel
        {
            get { return new MessageBoxButton("Cancel", MessageBoxResult.Cancel); }
        }

        public static MessageBoxButton Close
        {
            get { return new MessageBoxButton("Close", MessageBoxResult.Close); }
        }
    }

    /// <summary>
    /// Retained modal dialog rendered through VisualTree's overlay pass. Add one
    /// instance to a screen root and reuse it for every message shown by that screen.
    /// </summary>
    public sealed class MessageBox : VisualElement, IOverlayProvider, IModalOverlayProvider
    {
        static readonly IReadOnlyList<VisualElement> NoVisualChildren =
            new VisualElement[0];

        public static readonly StyledProperty<Color> OverlayColorProperty =
            StyledProperty.Register<MessageBox, Color>(
                nameof(OverlayColor), new Color(0, 0, 0, 150));
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<MessageBox, BackgroundBrush>(
                nameof(Background),
                new SolidColorBrush(new Color(40, 47, 52, 252)));
        public static readonly StyledProperty<Color> BorderColorProperty =
            StyledProperty.Register<MessageBox, Color>(
                nameof(BorderColor), new Color(82, 98, 107));
        public static readonly StyledProperty<Color> TitleColorProperty =
            StyledProperty.Register<MessageBox, Color>(
                nameof(TitleColor), Color.White);
        public static readonly StyledProperty<Color> DescriptionColorProperty =
            StyledProperty.Register<MessageBox, Color>(
                nameof(DescriptionColor), new Color(225, 230, 233));
        public static readonly StyledProperty<float> DialogWidthProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(DialogWidth), 520f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> DialogHeightProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(DialogHeight), float.NaN, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MaximumDescriptionHeightProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(MaximumDescriptionHeight), 300f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MinimumDescriptionHeightProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(MinimumDescriptionHeight), 36f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> CornerRadiusProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(CornerRadius), 7f, UiInvalidation.Render);
        public static readonly StyledProperty<int> BorderThicknessProperty =
            StyledProperty.Register<MessageBox, int>(
                nameof(BorderThickness), 1, UiInvalidation.Measure);
        public static readonly StyledProperty<Vector4> PaddingProperty =
            StyledProperty.Register<MessageBox, Vector4>(
                nameof(Padding), new Vector4(18f), UiInvalidation.Measure);
        public static readonly StyledProperty<float> ContentSpacingProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(ContentSpacing), 12f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> ButtonSpacingProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(ButtonSpacing), 12f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> TitleScaleProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(TitleScale), 0.78f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> DescriptionScaleProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(DescriptionScale), 0.62f, UiInvalidation.Measure);
        public static readonly StyledProperty<TextAlignment> TitleTextAlignmentProperty =
            StyledProperty.Register<MessageBox, TextAlignment>(
                nameof(TitleTextAlignment), TextAlignment.Left, UiInvalidation.Render);
        public static readonly StyledProperty<TextAlignment> DescriptionTextAlignmentProperty =
            StyledProperty.Register<MessageBox, TextAlignment>(
                nameof(DescriptionTextAlignment), TextAlignment.Left, UiInvalidation.Render);
        public static readonly StyledProperty<bool> CenterDescriptionVerticallyProperty =
            StyledProperty.Register<MessageBox, bool>(
                nameof(CenterDescriptionVertically), false, UiInvalidation.Render);
        public static readonly StyledProperty<Color> SeparatorColorProperty =
            StyledProperty.Register<MessageBox, Color>(
                nameof(SeparatorColor), new Color(82, 98, 107), UiInvalidation.Render);
        public static readonly StyledProperty<float> SeparatorWidthFractionProperty =
            StyledProperty.Register<MessageBox, float>(
                nameof(SeparatorWidthFraction), 1f, UiInvalidation.Measure);

        readonly Border _overlay;
        readonly Border _dialog;
        readonly StackPanel _content;
        readonly TextBlock _title;
        readonly Border _separator;
        readonly ScrollViewer _descriptionScroller;
        readonly WrappingTextBlock _description;
        readonly StackPanel _buttonsPanel;
        readonly Button[] _buttons = new Button[3];
        readonly MessageBoxButton[] _buttonDefinitions = new MessageBoxButton[3];

        Action<MessageBoxResult> _showClosed;
        MessageBoxResult _escapeResult = MessageBoxResult.Close;

        public MessageBox()
        {
            _title = new TextBlock
            {
                StyleClass = "message-box-title",
                Alignment = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 30f
            };
            _separator = new Border
            {
                StyleClass = "message-box-separator",
                Height = 1f,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _description = new WrappingTextBlock
            {
                StyleClass = "message-box-description"
            };
            _descriptionScroller = new ScrollViewer
            {
                StyleClass = "message-box-scroll-viewer",
                Child = _description,
                VerticalScrollEnabled = true,
                HorizontalScrollEnabled = false
            };

            _buttonsPanel = new StackPanel
            {
                StyleClass = "message-box-buttons",
                Orientation = StackOrientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            for (int i = 0; i < _buttons.Length; i++)
            {
                int buttonIndex = i;
                _buttons[i] = _buttonsPanel.Add(new Button
                {
                    StyleClass = "message-box-button",
                    Visible = false
                });
                _buttons[i].Clicked = () => ActivateButton(buttonIndex);
            }

            _content = new StackPanel
            {
                StyleClass = "message-box-content"
            };
            _content.Add(_title);
            _content.Add(_separator);
            _content.Add(_descriptionScroller);
            _content.Add(_buttonsPanel);

            _dialog = new Border
            {
                StyleClass = "message-box-dialog",
                Child = _content,
                Interactive = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _overlay = new Border
            {
                StyleClass = "message-box-overlay",
                Child = _dialog,
                Interactive = true,
                BorderThickness = 0,
                Visible = false
            };

            // The overlay is a logical child so it inherits styles and text metrics,
            // but it is drawn only by VisualTree's overlay pass.
            AddChild(_overlay);
        }

        public override IReadOnlyList<VisualElement> VisualChildren => NoVisualChildren;

        public bool IsOpen { get; private set; }
        public event Action<MessageBoxResult> Closed;

        public Color OverlayColor
        {
            get { return GetValue(OverlayColorProperty); }
            set { SetValue(OverlayColorProperty, value); }
        }

        public BackgroundBrush Background
        {
            get { return GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }

        public Color BorderColor
        {
            get { return GetValue(BorderColorProperty); }
            set { SetValue(BorderColorProperty, value); }
        }

        public Color TitleColor
        {
            get { return GetValue(TitleColorProperty); }
            set { SetValue(TitleColorProperty, value); }
        }

        public Color DescriptionColor
        {
            get { return GetValue(DescriptionColorProperty); }
            set { SetValue(DescriptionColorProperty, value); }
        }

        public float DialogWidth
        {
            get { return GetValue(DialogWidthProperty); }
            set { SetValue(DialogWidthProperty, Math.Max(1f, value)); }
        }

        public float DialogHeight
        {
            get { return GetValue(DialogHeightProperty); }
            set { SetValue(DialogHeightProperty, value); }
        }

        public float MaximumDescriptionHeight
        {
            get { return GetValue(MaximumDescriptionHeightProperty); }
            set { SetValue(MaximumDescriptionHeightProperty, Math.Max(1f, value)); }
        }

        public float MinimumDescriptionHeight
        {
            get { return GetValue(MinimumDescriptionHeightProperty); }
            set { SetValue(MinimumDescriptionHeightProperty, Math.Max(0f, value)); }
        }

        public float CornerRadius
        {
            get { return GetValue(CornerRadiusProperty); }
            set { SetValue(CornerRadiusProperty, Math.Max(0f, value)); }
        }

        public int BorderThickness
        {
            get { return GetValue(BorderThicknessProperty); }
            set { SetValue(BorderThicknessProperty, Math.Max(0, value)); }
        }

        public Vector4 Padding
        {
            get { return GetValue(PaddingProperty); }
            set { SetValue(PaddingProperty, value); }
        }

        public float ContentSpacing
        {
            get { return GetValue(ContentSpacingProperty); }
            set { SetValue(ContentSpacingProperty, Math.Max(0f, value)); }
        }

        public float ButtonSpacing
        {
            get { return GetValue(ButtonSpacingProperty); }
            set { SetValue(ButtonSpacingProperty, Math.Max(0f, value)); }
        }

        public float TitleScale
        {
            get { return GetValue(TitleScaleProperty); }
            set { SetValue(TitleScaleProperty, Math.Max(0f, value)); }
        }

        public float DescriptionScale
        {
            get { return GetValue(DescriptionScaleProperty); }
            set { SetValue(DescriptionScaleProperty, Math.Max(0f, value)); }
        }

        public TextAlignment TitleTextAlignment
        {
            get { return GetValue(TitleTextAlignmentProperty); }
            set { SetValue(TitleTextAlignmentProperty, value); }
        }

        public TextAlignment DescriptionTextAlignment
        {
            get { return GetValue(DescriptionTextAlignmentProperty); }
            set { SetValue(DescriptionTextAlignmentProperty, value); }
        }

        public bool CenterDescriptionVertically
        {
            get { return GetValue(CenterDescriptionVerticallyProperty); }
            set { SetValue(CenterDescriptionVerticallyProperty, value); }
        }

        public Color SeparatorColor
        {
            get { return GetValue(SeparatorColorProperty); }
            set { SetValue(SeparatorColorProperty, value); }
        }

        public float SeparatorWidthFraction
        {
            get { return GetValue(SeparatorWidthFractionProperty); }
            set { SetValue(SeparatorWidthFractionProperty, Math.Max(0f, Math.Min(1f, value))); }
        }

        public void Show(
            string title,
            string description,
            Action<MessageBoxResult> closed = null)
        {
            Show(title, description, MessageBoxButton.Ok, null, null, closed);
        }

        public void Show(
            string title,
            string description,
            MessageBoxButton button1,
            MessageBoxButton? button2 = null,
            MessageBoxButton? button3 = null,
            Action<MessageBoxResult> closed = null)
        {
            if (IsOpen)
                Close(MessageBoxResult.Close);

            _title.Text = title ?? string.Empty;
            _description.Text = description ?? string.Empty;
            ConfigureButton(0, button1);
            ConfigureButton(1, button2);
            ConfigureButton(2, button3);
            _descriptionScroller.ScrollTo(0f, 0f);
            _showClosed = closed;
            _escapeResult = FindEscapeResult();
            IsOpen = true;
            _overlay.Visible = true;
            Invalidate(UiInvalidation.Measure);
            UpdateOverlayLayout();
        }

        public void Close()
        {
            Close(MessageBoxResult.Close);
        }

        public void Close(MessageBoxResult result)
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            _overlay.Visible = false;
            Action<MessageBoxResult> showClosed = _showClosed;
            _showClosed = null;
            Invalidate(UiInvalidation.Render);
            if (showClosed != null)
                showClosed(result);
            Action<MessageBoxResult> closed = Closed;
            if (closed != null)
                closed(result);
        }

        void ConfigureButton(int index, MessageBoxButton? definition)
        {
            Button button = _buttons[index];
            if (!definition.HasValue)
            {
                button.Visible = false;
                _buttonDefinitions[index] = default(MessageBoxButton);
                return;
            }

            MessageBoxButton value = definition.Value;
            _buttonDefinitions[index] = value;
            button.Text = string.IsNullOrWhiteSpace(value.Text)
                ? value.Result.ToString()
                : value.Text;
            button.Visible = true;
        }

        void ActivateButton(int index)
        {
            if (index < 0 || index >= _buttons.Length || !_buttons[index].Visible)
                return;
            Close(_buttonDefinitions[index].Result);
        }

        MessageBoxResult FindEscapeResult()
        {
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i].Visible &&
                    _buttonDefinitions[i].Result == MessageBoxResult.Cancel)
                {
                    return MessageBoxResult.Cancel;
                }
            }
            return MessageBoxResult.Close;
        }

        bool IOverlayProvider.IsOverlayOpen => IsOpen;
        VisualElement IOverlayProvider.OverlayElement => _overlay;
        void IOverlayProvider.UpdateOverlayLayout() { UpdateOverlayLayout(); }
        void IOverlayProvider.CloseOverlay() { Close(_escapeResult); }

        void IModalOverlayProvider.HandleOverlayInput(IInputSource input)
        {
            if (input != null && input.IsNewKeyPressed(MyKeys.Enter))
                ActivateButton(0);
        }

        void UpdateOverlayLayout()
        {
            if (!IsOpen)
                return;

            _overlay.BackgroundColor = OverlayColor;
            _dialog.Background = Background;
            _dialog.BorderColor = BorderColor;
            _dialog.BorderThickness = BorderThickness;
            _dialog.CornerRadius = CornerRadius;
            _dialog.UseGeneratedTexture = CornerRadius > 0f;
            _dialog.Padding = Padding;
            _dialog.Width = DialogWidth;
            _dialog.Height = float.IsNaN(DialogHeight) ? (float?)null : DialogHeight;
            _dialog.MaxWidth = Math.Max(1f, Bounds.Width - 32f);
            _dialog.MaxHeight = Math.Max(1f, Bounds.Height - 32f);
            _content.Spacing = ContentSpacing;
            _buttonsPanel.Spacing = ButtonSpacing;
            _descriptionScroller.MinHeight = MinimumDescriptionHeight;
            _descriptionScroller.MaxHeight = Math.Min(
                MaximumDescriptionHeight,
                Math.Max(36f, Bounds.Height - 180f));
            _title.Color = TitleColor;
            _title.Scale = TitleScale;
            _title.TextAlignment = TitleTextAlignment;
            _description.Color = DescriptionColor;
            _description.Scale = DescriptionScale;
            _description.TextAlignment = DescriptionTextAlignment;
            _description.CenterVertically = CenterDescriptionVertically;
            _separator.BackgroundColor = SeparatorColor;
            _separator.Width = Math.Max(
                1f,
                (Math.Min(DialogWidth, _dialog.MaxWidth) - Padding.X - Padding.Z) *
                SeparatorWidthFraction);
            _overlay.Arrange(Bounds);
        }

        sealed class WrappingTextBlock : VisualElement
        {
            string _text;
            string _wrappedText = string.Empty;
            float _scale = 0.62f;
            float _wrappedWidth = -1f;

            public string Text
            {
                get { return _text; }
                set
                {
                    string next = value ?? string.Empty;
                    if (string.Equals(_text, next, StringComparison.Ordinal))
                        return;
                    _text = next;
                    _wrappedWidth = -1f;
                    Invalidate(UiInvalidation.Measure);
                }
            }

            public float Scale
            {
                get { return _scale; }
                set
                {
                    float next = Math.Max(0f, value);
                    if (Math.Abs(_scale - next) < 0.0001f)
                        return;
                    _scale = next;
                    _wrappedWidth = -1f;
                    Invalidate(UiInvalidation.Measure);
                }
            }

            public Color Color { get; set; } = Color.White;
            public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
            public bool CenterVertically { get; set; }

            protected override Vector2 MeasureOverride(Vector2 availableSize)
            {
                float width = float.IsPositiveInfinity(availableSize.X)
                    ? float.MaxValue
                    : Math.Max(1f, availableSize.X);
                EnsureWrapped(width);
                if (TextMetrics == null || string.IsNullOrEmpty(_wrappedText))
                    return Vector2.Zero;
                Vector2 measured = TextMetrics.Measure("White", _wrappedText, Scale);
                return new Vector2(
                    Math.Min(availableSize.X, measured.X),
                    Math.Min(availableSize.Y, measured.Y));
            }

            protected override void DrawSelf(RenderContext context)
            {
                EnsureWrapped(Math.Max(1f, Bounds.Width));
                Vector2 measured = TextMetrics == null || string.IsNullOrEmpty(_wrappedText)
                    ? Vector2.Zero
                    : TextMetrics.Measure("White", _wrappedText, Scale);
                float x = Bounds.X;
                MyGuiDrawAlignEnum alignment =
                    MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
                if (TextAlignment == TextAlignment.Center)
                {
                    x = Bounds.X + Bounds.Width * 0.5f;
                    alignment = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP;
                }
                else if (TextAlignment == TextAlignment.Right)
                {
                    x = Bounds.Right;
                    alignment = MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_TOP;
                }
                float y = CenterVertically
                    ? Bounds.Y + Math.Max(0f, (Bounds.Height - measured.Y) * 0.5f)
                    : Bounds.Y;
                context.Text(
                    _wrappedText,
                    new Vector2(x, y),
                    Scale,
                    Color,
                    alignment,
                    TextOptions.GetDropShadow(this));
            }

            void EnsureWrapped(float maximumWidth)
            {
                if (Math.Abs(_wrappedWidth - maximumWidth) < 0.5f)
                    return;
                _wrappedWidth = maximumWidth;
                _wrappedText = Wrap(Text ?? string.Empty, maximumWidth);
            }

            string Wrap(string value, float maximumWidth)
            {
                if (TextMetrics == null || maximumWidth >= float.MaxValue * 0.5f)
                    return value;

                string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
                string[] paragraphs = normalized.Split('\n');
                var lines = new List<string>();
                for (int paragraphIndex = 0;
                     paragraphIndex < paragraphs.Length;
                     paragraphIndex++)
                {
                    string remaining = paragraphs[paragraphIndex];
                    if (remaining.Length == 0)
                    {
                        lines.Add(string.Empty);
                        continue;
                    }

                    while (remaining.Length > 0)
                    {
                        if (MeasureWidth(remaining) <= maximumWidth)
                        {
                            lines.Add(remaining);
                            break;
                        }

                        int fit = FindFittingLength(remaining, maximumWidth);
                        int split = FindWordBoundary(remaining, fit);
                        string line = remaining.Substring(0, split).TrimEnd();
                        if (line.Length == 0)
                        {
                            split = Math.Max(1, fit);
                            line = remaining.Substring(0, split);
                        }
                        lines.Add(line);
                        remaining = remaining.Substring(split).TrimStart();
                    }
                }
                return string.Join("\n", lines.ToArray());
            }

            int FindFittingLength(string value, float maximumWidth)
            {
                int low = 1;
                int high = value.Length;
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    if (MeasureWidth(value.Substring(0, middle)) <= maximumWidth)
                        low = middle;
                    else
                        high = middle - 1;
                }
                return Math.Max(1, low);
            }

            static int FindWordBoundary(string value, int fit)
            {
                int maximum = Math.Min(value.Length, Math.Max(1, fit));
                for (int i = maximum; i > 0; i--)
                {
                    if (char.IsWhiteSpace(value[i - 1]))
                        return i;
                }
                return maximum;
            }

            float MeasureWidth(string value)
            {
                return TextMetrics.Measure("White", value, Scale).X;
            }
        }
    }
}
