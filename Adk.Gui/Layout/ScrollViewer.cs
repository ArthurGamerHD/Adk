using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Layout
{
    public sealed class ScrollViewer : Decorator
    {
        const string ThumbPointerOverPseudoClass = ":thumb-pointerover";
        const string ThumbPressedPseudoClass = ":thumb-pressed";
        const float DEFAULT_SCROLLER_WIDTH_PIXELS = 6f;
        const float SCROLLBAR_SIDE_MARGIN_RATIO = 0.25f;
        const float MINIMUM_THUMB_PIXELS = 8f;

        static ScrollViewer() { }

        float _horizontalOffset;
        float _verticalOffset;
        float _extentWidth;
        float _extentHeight;
        RectangleF _panelBounds;
        RectangleF _viewportBounds;
        bool _showHorizontalScrollBar;
        bool _showVerticalScrollBar;
        ScrollAxis _dragAxis;
        bool _draggingThumb;
        float _dragGrabOffset;
        ScrollAxis _hoverAxis;

        enum ScrollAxis
        {
            None,
            Horizontal,
            Vertical
        }

        struct ScrollBarMetrics
        {
            public RectangleF TrackHitBounds;
            public RectangleF TrackBounds;
            public RectangleF ThumbBounds;
            public float ThumbTravel;
            public float MaxOffset;
        }

        public static readonly StyledProperty<float> ScrollerWidthPixelsProperty =
            StyledProperty.Register<ScrollViewer, float>(
                nameof(ScrollerWidthPixels),
                DEFAULT_SCROLLER_WIDTH_PIXELS,
                UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ScrollBarVisualWidthPixelsProperty =
            StyledProperty.Register<ScrollViewer, float>(
                nameof(ScrollBarVisualWidthPixels),
                -1f,
                UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ScrollBarSideMarginPixelsProperty =
            StyledProperty.Register<ScrollViewer, float>(
                nameof(ScrollBarSideMarginPixels),
                -1f,
                UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ScrollBarEndMarginPixelsProperty =
            StyledProperty.Register<ScrollViewer, float>(
                nameof(ScrollBarEndMarginPixels),
                -1f,
                UiInvalidation.Arrange);
        public static readonly StyledProperty<float> ScrollBarMinimumThumbPixelsProperty =
            StyledProperty.Register<ScrollViewer, float>(
                nameof(ScrollBarMinimumThumbPixels),
                MINIMUM_THUMB_PIXELS,
                UiInvalidation.Arrange);

        public static readonly StyledProperty<BackgroundBrush> ScrollBarTrackBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(ScrollBarTrackBackground),
                new SolidColorBrush(new Color(82, 98, 107, 127)),
                UiInvalidation.Render);
        public static readonly StyledProperty<BackgroundBrush> ScrollBarThumbBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(ScrollBarThumbBackground),
                new SolidColorBrush(new Color(56, 64, 70, 248)),
                UiInvalidation.Render);
        public static readonly StyledProperty<BackgroundBrush> VerticalScrollBarTrackBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(VerticalScrollBarTrackBackground),
                null,
                UiInvalidation.Render);
        public static readonly StyledProperty<BackgroundBrush> VerticalScrollBarThumbBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(VerticalScrollBarThumbBackground),
                null,
                UiInvalidation.Render);
        public static readonly StyledProperty<BackgroundBrush> HorizontalScrollBarTrackBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(HorizontalScrollBarTrackBackground),
                null,
                UiInvalidation.Render);
        public static readonly StyledProperty<BackgroundBrush> HorizontalScrollBarThumbBackgroundProperty =
            StyledProperty.Register<ScrollViewer, BackgroundBrush>(
                nameof(HorizontalScrollBarThumbBackground),
                null,
                UiInvalidation.Render);

        public bool HorizontalScrollEnabled { get; set; }
        public bool VerticalScrollEnabled { get; set; }
        public bool InputEnabled { get; set; } = true;
        public float WheelStep { get; set; } = 36f;
        public float ScrollerWidthPixels
        {
            get { return GetValue(ScrollerWidthPixelsProperty); }
            set { SetValue(ScrollerWidthPixelsProperty, value); }
        }
        public float ScrollBarVisualWidthPixels
        {
            get { return GetValue(ScrollBarVisualWidthPixelsProperty); }
            set { SetValue(ScrollBarVisualWidthPixelsProperty, value); }
        }
        public float ScrollBarSideMarginPixels
        {
            get { return GetValue(ScrollBarSideMarginPixelsProperty); }
            set { SetValue(ScrollBarSideMarginPixelsProperty, value); }
        }
        public float ScrollBarEndMarginPixels
        {
            get { return GetValue(ScrollBarEndMarginPixelsProperty); }
            set { SetValue(ScrollBarEndMarginPixelsProperty, value); }
        }
        public float ScrollBarMinimumThumbPixels
        {
            get { return GetValue(ScrollBarMinimumThumbPixelsProperty); }
            set { SetValue(ScrollBarMinimumThumbPixelsProperty, value); }
        }
        public BackgroundBrush ScrollBarTrackBackground
        {
            get { return GetValue(ScrollBarTrackBackgroundProperty); }
            set { SetValue(ScrollBarTrackBackgroundProperty, value); }
        }
        public BackgroundBrush ScrollBarThumbBackground
        {
            get { return GetValue(ScrollBarThumbBackgroundProperty); }
            set { SetValue(ScrollBarThumbBackgroundProperty, value); }
        }
        public BackgroundBrush VerticalScrollBarTrackBackground
        {
            get { return GetValue(VerticalScrollBarTrackBackgroundProperty); }
            set { SetValue(VerticalScrollBarTrackBackgroundProperty, value); }
        }
        public BackgroundBrush VerticalScrollBarThumbBackground
        {
            get { return GetValue(VerticalScrollBarThumbBackgroundProperty); }
            set { SetValue(VerticalScrollBarThumbBackgroundProperty, value); }
        }
        public BackgroundBrush HorizontalScrollBarTrackBackground
        {
            get { return GetValue(HorizontalScrollBarTrackBackgroundProperty); }
            set { SetValue(HorizontalScrollBarTrackBackgroundProperty, value); }
        }
        public BackgroundBrush HorizontalScrollBarThumbBackground
        {
            get { return GetValue(HorizontalScrollBarThumbBackgroundProperty); }
            set { SetValue(HorizontalScrollBarThumbBackgroundProperty, value); }
        }

        public float HorizontalOffset
        {
            get { return _horizontalOffset; }
            set { SetOffsets(value, _verticalOffset, true); }
        }

        public float VerticalOffset
        {
            get { return _verticalOffset; }
            set { SetOffsets(_horizontalOffset, value, true); }
        }

        public float ExtentWidth => _extentWidth;
        public float ExtentHeight => _extentHeight;
        public float ViewportWidth => _viewportBounds.Width;
        public float ViewportHeight => _viewportBounds.Height;
        public RectangleF ViewportBounds => _viewportBounds;
        public float MaxHorizontalOffset => Math.Max(0f, _extentWidth - _viewportBounds.Width);
        public float MaxVerticalOffset => Math.Max(0f, _extentHeight - _viewportBounds.Height);
        public bool IsHorizontallyScrollable => _showHorizontalScrollBar;
        public bool IsVerticallyScrollable => _showVerticalScrollBar;

        public void SetScrollBarColors(Color trackColor, Color thumbColor)
        {
            ScrollBarTrackBackground = trackColor;
            ScrollBarThumbBackground = thumbColor;
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(ThumbPointerOverPseudoClass,
                IsPointerOver && !_draggingThumb && _hoverAxis != ScrollAxis.None);
            PseudoClasses.Set(ThumbPressedPseudoClass,
                _draggingThumb && _dragAxis != ScrollAxis.None);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            VisualElement child = Child;
            if (child == null)
                return Vector2.Zero;

            float horizontalPadding = Math.Max(0f, Padding.X) + Math.Max(0f, Padding.Z);
            float verticalPadding = Math.Max(0f, Padding.Y) + Math.Max(0f, Padding.W);
            Vector2 childAvailable = new Vector2(
                HorizontalScrollEnabled ? float.MaxValue : Math.Max(0f, availableSize.X - horizontalPadding),
                VerticalScrollEnabled ? float.MaxValue : Math.Max(0f, availableSize.Y - verticalPadding));
            Vector2 desired = child.Measure(childAvailable);

            return new Vector2(
                Math.Min(availableSize.X, desired.X + horizontalPadding),
                Math.Min(availableSize.Y, desired.Y + verticalPadding));
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            RefreshContentLayout();
            ArrangeTemplate(bounds);
        }

        public void RefreshContentLayout()
        {
            _panelBounds = GetPaddedBounds(Bounds);
            VisualElement child = Child;
            if (child == null)
            {
                _viewportBounds = _panelBounds;
                _extentWidth = _viewportBounds.Width;
                _extentHeight = _viewportBounds.Height;
                _horizontalOffset = 0f;
                _verticalOffset = 0f;
                _showHorizontalScrollBar = false;
                _showVerticalScrollBar = false;
                return;
            }

            Vector2 desired = child.Measure(new Vector2(
                HorizontalScrollEnabled ? float.MaxValue : _panelBounds.Width,
                VerticalScrollEnabled ? float.MaxValue : _panelBounds.Height));

            // Resolve scrollbar visibility twice because adding one gutter can make the
            // other axis overflow.
            bool showH = HorizontalScrollEnabled && desired.X > _panelBounds.Width + 0.5f;
            bool showV = VerticalScrollEnabled && desired.Y > _panelBounds.Height + 0.5f;
            RectangleF viewport = CalculateViewport(showH, showV);
            showH = HorizontalScrollEnabled && desired.X > viewport.Width + 0.5f;
            showV = VerticalScrollEnabled && desired.Y > viewport.Height + 0.5f;
            viewport = CalculateViewport(showH, showV);

            _showHorizontalScrollBar = showH;
            _showVerticalScrollBar = showV;
            _viewportBounds = viewport;
            _extentWidth = HorizontalScrollEnabled
                ? Math.Max(_viewportBounds.Width, Math.Max(0f, desired.X))
                : _viewportBounds.Width;
            _extentHeight = VerticalScrollEnabled
                ? Math.Max(_viewportBounds.Height, Math.Max(0f, desired.Y))
                : _viewportBounds.Height;

            _horizontalOffset = Clamp(_horizontalOffset, 0f, MaxHorizontalOffset);
            _verticalOffset = Clamp(_verticalOffset, 0f, MaxVerticalOffset);
            ArrangeContent(child);
        }

        public void ScrollTo(float horizontalOffset, float verticalOffset)
        {
            SetOffsets(horizontalOffset, verticalOffset, true);
        }

        public void ScrollBy(float horizontalDelta, float verticalDelta)
        {
            SetOffsets(_horizontalOffset + horizontalDelta, _verticalOffset + verticalDelta, true);
        }

        public void BringIntoView(RectangleF contentRectangle, float margin = 0f)
        {
            float nextX = _horizontalOffset;
            float nextY = _verticalOffset;
            margin = Math.Max(0f, margin);

            if (HorizontalScrollEnabled && _viewportBounds.Width > 0f)
            {
                float visibleLeft = _horizontalOffset + margin;
                float visibleRight = _horizontalOffset + _viewportBounds.Width - margin;
                if (contentRectangle.X < visibleLeft)
                    nextX = contentRectangle.X - margin;
                else if (contentRectangle.Right > visibleRight)
                    nextX = contentRectangle.Right - _viewportBounds.Width + margin;
            }

            if (VerticalScrollEnabled && _viewportBounds.Height > 0f)
            {
                float visibleTop = _verticalOffset + margin;
                float visibleBottom = _verticalOffset + _viewportBounds.Height - margin;
                if (contentRectangle.Y < visibleTop)
                    nextY = contentRectangle.Y - margin;
                else if (contentRectangle.Bottom > visibleBottom)
                    nextY = contentRectangle.Bottom - _viewportBounds.Height + margin;
            }

            SetOffsets(nextX, nextY, true);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return InputEnabled;
        }

        protected override IDisposable BeginChildDraw(
            RenderContext context,
            VisualElement child)
        {
            return ReferenceEquals(child, Child)
                ? context.Clip(_viewportBounds)
                : null;
        }

        protected override void DrawSelf(RenderContext context)
        {
        }

        internal void DrawTemplateScrollBars(RenderContext context)
        {
            DrawScrollBar(context, ScrollAxis.Vertical);
            DrawScrollBar(context, ScrollAxis.Horizontal);
        }

        internal override void PointerHovered(Vector2 point)
        {
            if (_draggingThumb)
                return;
            _hoverAxis = ThumbContains(ScrollAxis.Vertical, point) ? ScrollAxis.Vertical
                : ThumbContains(ScrollAxis.Horizontal, point) ? ScrollAxis.Horizontal
                : ScrollAxis.None;
        }

        internal override void PointerPressed(Vector2 point)
        {
            if (!InputEnabled)
                return;

            ScrollAxis axis = HitScrollBarAxis(point);
            if (axis == ScrollAxis.None)
                return;

            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(axis, out metrics))
                return;

            if (metrics.ThumbBounds.Contains(point))
            {
                _draggingThumb = true;
                _dragAxis = axis;
                _dragGrabOffset = axis == ScrollAxis.Vertical
                    ? point.Y - metrics.ThumbBounds.Y
                    : point.X - metrics.ThumbBounds.X;
                return;
            }

            float page = axis == ScrollAxis.Vertical ? _viewportBounds.Height : _viewportBounds.Width;
            float coordinate = axis == ScrollAxis.Vertical ? point.Y : point.X;
            float thumbStart = axis == ScrollAxis.Vertical ? metrics.ThumbBounds.Y : metrics.ThumbBounds.X;
            float thumbEnd = axis == ScrollAxis.Vertical ? metrics.ThumbBounds.Bottom : metrics.ThumbBounds.Right;
            float delta = coordinate < thumbStart ? -page : coordinate > thumbEnd ? page : 0f;
            if (axis == ScrollAxis.Vertical)
                ScrollBy(0f, delta);
            else
                ScrollBy(delta, 0f);
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (!_draggingThumb || _dragAxis == ScrollAxis.None)
                return;

            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(_dragAxis, out metrics) || metrics.ThumbTravel <= 0f)
                return;

            float pointer = _dragAxis == ScrollAxis.Vertical ? point.Y : point.X;
            float trackStart = _dragAxis == ScrollAxis.Vertical ? metrics.TrackBounds.Y : metrics.TrackBounds.X;
            float thumbStart = pointer - _dragGrabOffset;
            float fraction = Clamp((thumbStart - trackStart) / metrics.ThumbTravel, 0f, 1f);
            float offset = fraction * metrics.MaxOffset;
            if (_dragAxis == ScrollAxis.Vertical)
                VerticalOffset = offset;
            else
                HorizontalOffset = offset;
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            _draggingThumb = false;
            _dragAxis = ScrollAxis.None;
        }

        internal override bool PointerScrolled(Vector2 point, int delta)
        {
            if (!InputEnabled || delta == 0)
                return false;

            float direction = delta > 0 ? -1f : 1f;
            float step = Math.Max(1f, WheelStep);
            if (VerticalScrollEnabled && MaxVerticalOffset > 0f)
            {
                ScrollBy(0f, direction * step);
                return true;
            }
            if (HorizontalScrollEnabled && MaxHorizontalOffset > 0f)
            {
                ScrollBy(direction * step, 0f);
                return true;
            }
            return false;
        }

        void DrawScrollBar(RenderContext context, ScrollAxis axis)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(axis, out metrics))
                return;

            context.Fill(metrics.TrackBounds, ResolveTrackBackground(axis));
            context.Fill(metrics.ThumbBounds, ResolveThumbBackground(axis));
        }

        BackgroundBrush ResolveTrackBackground(ScrollAxis axis)
        {
            BackgroundBrush brush = axis == ScrollAxis.Vertical
                ? VerticalScrollBarTrackBackground
                : HorizontalScrollBarTrackBackground;
            return brush ?? ScrollBarTrackBackground;
        }

        BackgroundBrush ResolveThumbBackground(ScrollAxis axis)
        {
            BackgroundBrush brush = axis == ScrollAxis.Vertical
                ? VerticalScrollBarThumbBackground
                : HorizontalScrollBarThumbBackground;
            return brush ?? ScrollBarThumbBackground;
        }

        bool ThumbContains(ScrollAxis axis, Vector2 point)
        {
            ScrollBarMetrics metrics;
            return TryGetScrollBarMetrics(axis, out metrics) && metrics.ThumbBounds.Contains(point);
        }

        ScrollAxis HitScrollBarAxis(Vector2 point)
        {
            ScrollBarMetrics metrics;
            if (TryGetScrollBarMetrics(ScrollAxis.Vertical, out metrics) && metrics.TrackHitBounds.Contains(point))
                return ScrollAxis.Vertical;
            if (TryGetScrollBarMetrics(ScrollAxis.Horizontal, out metrics) && metrics.TrackHitBounds.Contains(point))
                return ScrollAxis.Horizontal;
            return ScrollAxis.None;
        }

        bool TryGetScrollBarMetrics(ScrollAxis axis, out ScrollBarMetrics metrics)
        {
            metrics = default(ScrollBarMetrics);
            bool visible = axis == ScrollAxis.Vertical ? _showVerticalScrollBar : _showHorizontalScrollBar;
            if (!visible || ScrollerWidthPixels <= 0f)
                return false;

            float gutter = Math.Max(1f, ScrollerWidthPixels);
            float visual = ScrollBarVisualWidthPixels >= 0f
                ? Math.Max(1f, ScrollBarVisualWidthPixels)
                : Math.Max(1f, gutter * 0.5f);
            float sideMargin = ScrollBarSideMarginPixels >= 0f
                ? Math.Max(0f, ScrollBarSideMarginPixels)
                : Math.Max(0f, gutter * SCROLLBAR_SIDE_MARGIN_RATIO);
            float endMargin = ScrollBarEndMarginPixels >= 0f
                ? Math.Max(0f, ScrollBarEndMarginPixels)
                : gutter;
            float slotWidth = Math.Max(gutter, visual);
            float visualInset = Math.Max(0f, (slotWidth - visual) * 0.5f);
            float reservedCross = slotWidth + sideMargin * 2f;
            float minimumThumb = Math.Max(1f, ScrollBarMinimumThumbPixels);
            float maxOffset = axis == ScrollAxis.Vertical ? MaxVerticalOffset : MaxHorizontalOffset;
            float total = axis == ScrollAxis.Vertical ? _extentHeight : _extentWidth;
            float viewportLength = axis == ScrollAxis.Vertical ? _viewportBounds.Height : _viewportBounds.Width;
            if (total <= 0f || viewportLength <= 0f)
                return false;

            if (axis == ScrollAxis.Vertical)
            {
                float trackLength = Math.Max(1f, _viewportBounds.Height - endMargin * 2f);
                float thumbLength = Math.Max(minimumThumb,
                    Math.Min(trackLength, viewportLength / Math.Max(1f, total) * trackLength));
                thumbLength = Math.Min(trackLength, thumbLength);
                float travel = Math.Max(0f, trackLength - thumbLength);
                float fraction = maxOffset > 0f ? Clamp(_verticalOffset / maxOffset, 0f, 1f) : 0f;
                float start = _viewportBounds.Y + endMargin;
                float reservedX = _panelBounds.Right - reservedCross;
                float slotX = reservedX + sideMargin;
                float trackX = slotX + visualInset;
                metrics.TrackHitBounds = new RectangleF(reservedX, start, reservedCross, trackLength);
                metrics.TrackBounds = new RectangleF(trackX, start, visual, trackLength);
                metrics.ThumbBounds = new RectangleF(trackX, start + travel * fraction, visual, thumbLength);
                metrics.ThumbTravel = travel;
                metrics.MaxOffset = maxOffset;
                return true;
            }

            float horizontalTrackLength = Math.Max(1f, _viewportBounds.Width - endMargin * 2f);
            float horizontalThumbLength = Math.Max(minimumThumb,
                Math.Min(horizontalTrackLength, viewportLength / Math.Max(1f, total) * horizontalTrackLength));
            horizontalThumbLength = Math.Min(horizontalTrackLength, horizontalThumbLength);
            float horizontalTravel = Math.Max(0f, horizontalTrackLength - horizontalThumbLength);
            float horizontalFraction = maxOffset > 0f ? Clamp(_horizontalOffset / maxOffset, 0f, 1f) : 0f;
            float horizontalStart = _viewportBounds.X + endMargin;
            float reservedY = _panelBounds.Bottom - reservedCross;
            float slotY = reservedY + sideMargin;
            float trackY = slotY + visualInset;
            metrics.TrackHitBounds = new RectangleF(horizontalStart, reservedY, horizontalTrackLength, reservedCross);
            metrics.TrackBounds = new RectangleF(horizontalStart, trackY, horizontalTrackLength, visual);
            metrics.ThumbBounds = new RectangleF(horizontalStart + horizontalTravel * horizontalFraction, trackY, horizontalThumbLength, visual);
            metrics.ThumbTravel = horizontalTravel;
            metrics.MaxOffset = maxOffset;
            return true;
        }

        RectangleF CalculateViewport(bool showHorizontal, bool showVertical)
        {
            float scroller = Math.Max(0f, ScrollerWidthPixels);
            float visual = ScrollBarVisualWidthPixels >= 0f
                ? Math.Max(0f, ScrollBarVisualWidthPixels)
                : Math.Max(0f, scroller * 0.5f);
            float margin = ScrollBarSideMarginPixels >= 0f
                ? Math.Max(0f, ScrollBarSideMarginPixels)
                : scroller * SCROLLBAR_SIDE_MARGIN_RATIO;
            float slotWidth = Math.Max(scroller, visual);
            float verticalGutter = showVertical ? slotWidth + margin * 2f : 0f;
            float horizontalGutter = showHorizontal ? slotWidth + margin * 2f : 0f;
            return new RectangleF(
                _panelBounds.X,
                _panelBounds.Y,
                Math.Max(0f, _panelBounds.Width - verticalGutter),
                Math.Max(0f, _panelBounds.Height - horizontalGutter));
        }

        RectangleF GetPaddedBounds(RectangleF bounds)
        {
            float left = bounds.X + Math.Max(0f, Padding.X);
            float top = bounds.Y + Math.Max(0f, Padding.Y);
            float right = bounds.Right - Math.Max(0f, Padding.Z);
            float bottom = bounds.Bottom - Math.Max(0f, Padding.W);
            return new RectangleF(left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
        }

        void SetOffsets(float horizontal, float vertical, bool arrange)
        {
            float nextX = HorizontalScrollEnabled ? Clamp(horizontal, 0f, MaxHorizontalOffset) : 0f;
            float nextY = VerticalScrollEnabled ? Clamp(vertical, 0f, MaxVerticalOffset) : 0f;
            if (Math.Abs(nextX - _horizontalOffset) < 0.001f && Math.Abs(nextY - _verticalOffset) < 0.001f)
                return;

            _horizontalOffset = nextX;
            _verticalOffset = nextY;
            if (arrange && Child != null && _viewportBounds.Width >= 0f && _viewportBounds.Height >= 0f)
                ArrangeContent(Child);
        }

        void ArrangeContent(VisualElement child)
        {
            child.Arrange(new RectangleF(
                _viewportBounds.X - _horizontalOffset,
                _viewportBounds.Y - _verticalOffset,
                _extentWidth,
                _extentHeight));
        }

        static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public sealed class ScrollViewerPresenter : VisualElement
        {
            protected override void DrawSelf(RenderContext context)
            {
                ScrollViewer owner = TemplatedParent as ScrollViewer;
                if (owner != null)
                    owner.DrawTemplateScrollBars(context);
            }
        }
    }
}
