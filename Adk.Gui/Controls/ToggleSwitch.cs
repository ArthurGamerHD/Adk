using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRage.Input;
using VRageMath;

namespace Adk.Gui.Controls
{
    public sealed class ToggleSwitch : TemplatedControl
    {
        static ToggleSwitch() { }

        Border _track;
        Border _thumb;
        Border _marker;
        bool _animationInitialized;
        bool _animationTarget;
        float _animationStartPosition;
        int _animationStartFrame;
        int _renderFrame;
        bool _backgroundAnimationInitialized;
        Color _backgroundAnimationStart;
        Color _backgroundAnimationTarget;
        int _backgroundAnimationStartFrame;

        public static readonly StyledProperty<bool> IsOnProperty =
            StyledProperty.Register<ToggleSwitch, bool>(nameof(IsOn), false);
        public static readonly StyledProperty<float> PaddingProperty =
            StyledProperty.Register<ToggleSwitch, float>(
                nameof(Padding), 3f, UiInvalidation.Arrange);
        public static readonly StyledProperty<int> TransitionFramesProperty =
            StyledProperty.Register<ToggleSwitch, int>(nameof(TransitionFrames), 9);
        public static readonly StyledProperty<float> DarkenProperty =
            StyledProperty.Register<ToggleSwitch, float>(nameof(Darken), 0f);
        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<ToggleSwitch, BackgroundBrush>(
                "Background", new SolidColorBrush(new Color(82, 98, 107)));
        public static readonly StyledProperty<BackgroundBrush> ThumbBackgroundProperty =
            StyledProperty.Register<ToggleSwitch, BackgroundBrush>(
                nameof(ThumbBackground), new SolidColorBrush(Color.White));
        public static readonly StyledProperty<Color> ThumbShadowProperty =
            StyledProperty.Register<ToggleSwitch, Color>(
                nameof(ThumbShadow), new Color(0, 0, 0, 95));
        public static readonly StyledProperty<Vector2> ThumbShadowOffsetProperty =
            StyledProperty.Register<ToggleSwitch, Vector2>(
                nameof(ThumbShadowOffset), new Vector2(1f, 1f));

        public ToggleSwitch()
        {
        }

        public ToggleSwitch(Func<bool> getState, Action<bool> valueChanged) : this()
        {
            IsOnProvider = getState;
            ValueChanged = valueChanged;
        }

        public bool IsOn { get { return GetValue(IsOnProperty); } set { SetValue(IsOnProperty, value); } }
        public float Padding { get { return GetValue(PaddingProperty); } set { SetValue(PaddingProperty, value); } }
        public int TransitionFrames { get { return GetValue(TransitionFramesProperty); } set { SetValue(TransitionFramesProperty, value); } }
        public float Darken { get { return GetValue(DarkenProperty); } set { SetValue(DarkenProperty, value); } }
        public BackgroundBrush Background { get { return GetValue(BackgroundProperty); } set { SetValue(BackgroundProperty, value); } }
        public BackgroundBrush ThumbBackground { get { return GetValue(ThumbBackgroundProperty); } set { SetValue(ThumbBackgroundProperty, value); } }
        public Color ThumbShadow { get { return GetValue(ThumbShadowProperty); } set { SetValue(ThumbShadowProperty, value); } }
        public Vector2 ThumbShadowOffset { get { return GetValue(ThumbShadowOffsetProperty); } set { SetValue(ThumbShadowOffsetProperty, value); } }

        public Func<bool> IsOnProvider { get; set; }
        public Func<bool> GetState { get { return IsOnProvider; } set { IsOnProvider = value; } }
        public Action<bool> ValueChanged { get; set; }
        public Action<bool> Changed { get { return ValueChanged; } set { ValueChanged = value; } }
        public override bool AcceptsKeyboardFocus => true;
        public bool IsChecked => CurrentValue;

        protected override void OnApplyTemplate()
        {
            _track = GetTemplateChild<Border>("PART_Track");
            _thumb = GetTemplateChild<Border>("PART_Thumb");
            _marker = GetTemplateChild<Border>("PART_Marker");
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, IsPressed);
            bool current = CurrentValue;
            PseudoClasses.Set(PseudoClassNames.Checked, current);
            PseudoClasses.Set(PseudoClassNames.Unchecked, !current);
        }

        public void SetValue(bool value, bool notify = false)
        {
            IsOn = value;
            if (notify && ValueChanged != null)
                ValueChanged(value);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            return new Vector2(
                Math.Min(availableSize.X, 52f),
                Math.Min(availableSize.Y, 30f));
        }

        protected override bool HitTestSelf(Vector2 point) { return true; }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            bool current = CurrentValue;
            if (_thumb != null)
                _thumb.Visible = true;
            if (_marker != null)
                _marker.Visible = current;
            ArrangeTemplate(bounds);
            if (_track != null)
                _track.Arrange(bounds);
            if (_thumb != null)
                ArrangeThumb(GetAnimatedPosition(_renderFrame));
        }

        protected override void DrawSelf(RenderContext context)
        {
            _renderFrame++;
            float darken = MathHelper.Clamp(Darken, 0f, 1f);
            BackgroundBrush background = Background;
            if (_track != null && background is TextureBrush)
            {
                _track.Background = background;
            }
            else if (_track != null)
            {
                Color animatedBackground = GetAnimatedBackground(
                    background == null ? Color.Transparent : background.FallbackColor, _renderFrame);
                _track.BackgroundColor = LerpColor(
                    animatedBackground, Color.Black, darken);
            }
            if (_marker != null)
                _marker.Visible = CurrentValue;
            if (_thumb != null)
            {
                float position = GetAnimatedPosition(_renderFrame);
                _thumb.Visible = true;
                _thumb.Background = ThumbBackground;
                _thumb.ShadowColor = ThumbShadow;
                _thumb.ShadowOffset = ThumbShadowOffset;
                ArrangeThumb(position);
            }
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (clicked)
                Toggle();
        }

        internal override void KeyboardInput(IInputSource input)
        {
            if (input != null && (input.IsNewKeyPressed(MyKeys.Space) ||
                                  input.IsNewKeyPressed(MyKeys.Enter)))
                Toggle();
        }

        void Toggle()
        {
            if (!Enabled)
                return;
            bool value = !CurrentValue;
            IsOn = value;
            if (ValueChanged != null)
                ValueChanged(value);
        }

        bool CurrentValue => IsOnProvider?.Invoke() ?? IsOn;

        void ArrangeThumb(float position)
        {
            float padding = Math.Max(0f, Padding);
            float diameter = Math.Max(1f, Bounds.Height - padding * 2f);
            diameter = Math.Min(diameter, Math.Max(1f, Bounds.Width - padding * 2f));
            float left = Bounds.X + padding;
            float right = Bounds.Right - padding - diameter;
            float x = MathHelper.Lerp(left, Math.Max(left, right),
                MathHelper.Clamp(position, 0f, 1f));
            if (_thumb != null)
                _thumb.Arrange(new RectangleF(
                    x, Bounds.Center.Y - diameter * 0.5f, diameter, diameter));
        }

        float GetAnimatedPosition(int frame)
        {
            bool target = CurrentValue;
            if (!_animationInitialized)
            {
                _animationInitialized = true;
                _animationTarget = target;
                _animationStartPosition = target ? 1f : 0f;
                _animationStartFrame = frame;
                return _animationStartPosition;
            }

            float current = EvaluatePosition(frame);
            if (target != _animationTarget)
            {
                _animationStartPosition = current;
                _animationTarget = target;
                _animationStartFrame = frame;
            }
            return EvaluatePosition(frame);
        }

        float EvaluatePosition(int frame)
        {
            int duration = Math.Max(1, TransitionFrames);
            int elapsed = Math.Max(0, unchecked(frame - _animationStartFrame));
            float amount = MathHelper.Clamp(elapsed / (float)duration, 0f, 1f);
            float eased = amount * amount * (3f - 2f * amount);
            return MathHelper.Lerp(
                _animationStartPosition, _animationTarget ? 1f : 0f, eased);
        }

        Color GetAnimatedBackground(Color target, int frame)
        {
            if (!_backgroundAnimationInitialized)
            {
                _backgroundAnimationInitialized = true;
                _backgroundAnimationStart = target;
                _backgroundAnimationTarget = target;
                _backgroundAnimationStartFrame = frame;
                return target;
            }

            Color current = EvaluateBackground(frame);
            if (_backgroundAnimationTarget != target)
            {
                _backgroundAnimationStart = current;
                _backgroundAnimationTarget = target;
                _backgroundAnimationStartFrame = frame;
            }
            return EvaluateBackground(frame);
        }

        Color EvaluateBackground(int frame)
        {
            int duration = Math.Max(1, TransitionFrames);
            int elapsed = Math.Max(0, unchecked(frame - _backgroundAnimationStartFrame));
            float amount = MathHelper.Clamp(elapsed / (float)duration, 0f, 1f);
            float eased = amount * amount * (3f - 2f * amount);
            return LerpColor(_backgroundAnimationStart, _backgroundAnimationTarget, eased);
        }

        static Color LerpColor(Color from, Color to, float amount)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            return new Color(
                (byte)Math.Round(MathHelper.Lerp(from.R, to.R, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.G, to.G, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.B, to.B, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.A, to.A, amount)));
        }
    }
}
