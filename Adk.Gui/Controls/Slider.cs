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
    /// <summary>A horizontal range control with pointer and keyboard input.</summary>
    public sealed class Slider : TemplatedControl
    {
        static Slider() { }

        Border _track;
        Border _fill;
        Border _thumb;

        public static readonly StyledProperty<float> MinimumProperty =
            StyledProperty.Register<Slider, float>(nameof(Minimum), 0f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> MaximumProperty =
            StyledProperty.Register<Slider, float>(nameof(Maximum), 1f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ValueProperty =
            StyledProperty.Register<Slider, float>(nameof(Value), 0f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> SmallChangeProperty =
            StyledProperty.Register<Slider, float>(nameof(SmallChange), 0.1f);
        public static readonly StyledProperty<float> TrackHeightProperty =
            StyledProperty.Register<Slider, float>(
                nameof(TrackHeight), 6f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> TrackInsetProperty =
            StyledProperty.Register<Slider, float>(
                nameof(TrackInset), 12f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> TrackVisualHeightProperty =
            StyledProperty.Register<Slider, float>(
                nameof(TrackVisualHeight), -1f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> TrackVisualInsetProperty =
            StyledProperty.Register<Slider, float>(
                nameof(TrackVisualInset), -1f, UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ThumbSizeProperty =
            StyledProperty.Register<Slider, float>(
                nameof(ThumbSize), 16f, UiInvalidation.Arrange);
        public static readonly StyledProperty<BackgroundBrush> TrackBackgroundProperty =
            StyledProperty.Register<Slider, BackgroundBrush>(
                nameof(TrackBackground),
                new SolidColorBrush(new Color(82, 98, 107)));
        public static readonly StyledProperty<BackgroundBrush> FillBackgroundProperty =
            StyledProperty.Register<Slider, BackgroundBrush>(
                nameof(FillBackground),
                new SolidColorBrush(new Color(80, 160, 255)));
        public static readonly StyledProperty<BackgroundBrush> ThumbBackgroundProperty =
            StyledProperty.Register<Slider, BackgroundBrush>(
                nameof(ThumbBackground), new SolidColorBrush(Color.White));
        public static readonly StyledProperty<Color> ThumbShadowProperty =
            StyledProperty.Register<Slider, Color>(
                nameof(ThumbShadow), new Color(0, 0, 0, 90));
        public static readonly StyledProperty<Vector2> ThumbShadowOffsetProperty =
            StyledProperty.Register<Slider, Vector2>(
                nameof(ThumbShadowOffset), new Vector2(1f, 1f));

        public Slider()
        {
        }

        public float Minimum { get { return GetValue(MinimumProperty); } set { SetValue(MinimumProperty, value); } }
        public float Maximum { get { return GetValue(MaximumProperty); } set { SetValue(MaximumProperty, value); } }
        public float Value { get { return GetValue(ValueProperty); } set { SetValue(ValueProperty, Clamp(value)); } }
        public float SmallChange { get { return GetValue(SmallChangeProperty); } set { SetValue(SmallChangeProperty, value); } }
        public float TrackHeight { get { return GetValue(TrackHeightProperty); } set { SetValue(TrackHeightProperty, value); } }
        public float TrackInset { get { return GetValue(TrackInsetProperty); } set { SetValue(TrackInsetProperty, value); } }
        public float TrackVisualHeight { get { return GetValue(TrackVisualHeightProperty); } set { SetValue(TrackVisualHeightProperty, value); } }
        public float TrackVisualInset { get { return GetValue(TrackVisualInsetProperty); } set { SetValue(TrackVisualInsetProperty, value); } }
        public float ThumbSize { get { return GetValue(ThumbSizeProperty); } set { SetValue(ThumbSizeProperty, value); } }
        public BackgroundBrush TrackBackground { get { return GetValue(TrackBackgroundProperty); } set { SetValue(TrackBackgroundProperty, value); } }
        public BackgroundBrush FillBackground { get { return GetValue(FillBackgroundProperty); } set { SetValue(FillBackgroundProperty, value); } }
        public BackgroundBrush ThumbBackground { get { return GetValue(ThumbBackgroundProperty); } set { SetValue(ThumbBackgroundProperty, value); } }
        public Color ThumbShadow { get { return GetValue(ThumbShadowProperty); } set { SetValue(ThumbShadowProperty, value); } }
        public Vector2 ThumbShadowOffset { get { return GetValue(ThumbShadowOffsetProperty); } set { SetValue(ThumbShadowOffsetProperty, value); } }

        public Func<float> ValueProvider { get; set; }
        public Action<float> ValueChanged { get; set; }
        public override bool AcceptsKeyboardFocus => true;

        protected override void OnApplyTemplate()
        {
            _track = GetTemplateChild<Border>("PART_Track");
            _fill = GetTemplateChild<Border>("PART_Fill");
            _thumb = GetTemplateChild<Border>("PART_Thumb");
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Pressed, IsPressed);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            EnsureStyleApplied();
            return new Vector2(
                Math.Min(availableSize.X, 260f),
                Math.Min(availableSize.Y, 34f));
        }

        protected override bool HitTestSelf(Vector2 point) { return ValueChanged != null; }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ArrangeTemplate(bounds);
            float normalized = Normalize(CurrentValue);
            RectangleF track = GetTrackBounds(bounds);
            if (_track != null)
                _track.Arrange(GetVisualTrackBounds(bounds));
            if (_fill != null)
                _fill.Arrange(new RectangleF(
                    track.X, track.Y, Math.Max(1f, track.Width * normalized), track.Height));
            float thumbSize = Math.Max(1f, Math.Min(ThumbSize, bounds.Height));
            float thumbX = track.X + track.Width * normalized - thumbSize * 0.5f;
            if (_thumb != null)
                _thumb.Arrange(new RectangleF(
                    thumbX, bounds.Center.Y - thumbSize * 0.5f, thumbSize, thumbSize));
        }

        protected override void DrawSelf(RenderContext context)
        {
            ArrangeChildren(Bounds);
        }

        internal override void PointerPressed(Vector2 point) { CommitFromPoint(point); }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (IsPressed)
                CommitFromPoint(point);
        }

        internal override void KeyboardInput(IInputSource input)
        {
            if (input == null)
                return;
            if (input.IsNewKeyPressed(MyKeys.Left) || input.IsNewKeyPressed(MyKeys.Down))
                SetValue(CurrentValue - Math.Abs(SmallChange), true);
            else if (input.IsNewKeyPressed(MyKeys.Right) || input.IsNewKeyPressed(MyKeys.Up))
                SetValue(CurrentValue + Math.Abs(SmallChange), true);
            else if (input.IsNewKeyPressed(MyKeys.Home))
                SetValue(Minimum, true);
            else if (input.IsNewKeyPressed(MyKeys.End))
                SetValue(Maximum, true);
        }

        public void SetValue(float value, bool notify = false)
        {
            float next = Clamp(value);
            if (Math.Abs(next - Value) < 0.0001f)
                return;
            Value = next;
            if (notify && ValueChanged != null)
                ValueChanged(next);
        }

        void CommitFromPoint(Vector2 point)
        {
            if (ValueChanged == null)
                return;
            RectangleF track = GetTrackBounds(Bounds);
            if (track.Width <= 0f)
                return;
            float normalized = MathHelper.Clamp((point.X - track.X) / track.Width, 0f, 1f);
            SetValue(MathHelper.Lerp(Minimum, EffectiveMaximum, normalized), true);
        }

        float CurrentValue => Clamp(ValueProvider?.Invoke() ?? Value);
        float EffectiveMaximum => Math.Max(Minimum, Maximum);

        float Clamp(float value)
        {
            return MathHelper.Clamp(value, Minimum, EffectiveMaximum);
        }

        float Normalize(float value)
        {
            float range = EffectiveMaximum - Minimum;
            return range <= 0.0001f ? 0f : MathHelper.Clamp((value - Minimum) / range, 0f, 1f);
        }

        RectangleF GetVisualTrackBounds(RectangleF rect)
        {
            float configuredHeight = TrackVisualHeight >= 0f ? TrackVisualHeight : TrackHeight;
            float configuredInset = TrackVisualInset >= 0f ? TrackVisualInset : TrackInset;
            float height = Math.Max(1f, Math.Min(configuredHeight, rect.Height));
            float inset = Math.Max(0f, Math.Min(configuredInset, rect.Width * 0.5f));
            return new RectangleF(
                rect.X + inset,
                rect.Center.Y - height * 0.5f,
                Math.Max(1f, rect.Width - inset * 2f),
                height);
        }

        RectangleF GetTrackBounds(RectangleF rect)
        {
            float height = Math.Max(1f, Math.Min(TrackHeight, rect.Height));
            float inset = Math.Max(0f, Math.Min(TrackInset, rect.Width * 0.5f));
            return new RectangleF(
                rect.X + inset,
                rect.Center.Y - height * 0.5f,
                Math.Max(1f, rect.Width - inset * 2f),
                height);
        }
    }
}
