using System;
using Adk.Gui.Core.Invalidation;
using VRageMath;

namespace Adk.Gui.Core
{
    public enum HorizontalAlignment
    {
        Stretch,
        Left,
        Center,
        Right
    }

    public enum VerticalAlignment
    {
        Stretch,
        Top,
        Center,
        Bottom
    }

    public abstract class FrameworkElement : Visual
    {
        float? _width;
        float? _height;
        float _minWidth;
        float _minHeight;
        float _maxWidth = float.PositiveInfinity;
        float _maxHeight = float.PositiveInfinity;
        Vector4 _margin;
        HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment _verticalAlignment = VerticalAlignment.Stretch;

        public RectangleF Bounds { get; private set; }
        public Vector2 DesiredSize { get; private set; }

        /// <summary>
        /// Optional explicit target width. Null leaves width to intrinsic measurement
        /// and the parent layout policy.
        /// </summary>
        public virtual float? Width
        {
            get { return _width; }
            set
            {
                if (_width == value)
                    return;
                _width = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        /// <summary>
        /// Optional explicit target height. Null leaves height to intrinsic measurement
        /// and the parent layout policy.
        /// </summary>
        public virtual float? Height
        {
            get { return _height; }
            set
            {
                if (_height == value)
                    return;
                _height = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public virtual float MinWidth
        {
            get { return _minWidth; }
            set
            {
                float next = Math.Max(0f, value);
                if (Math.Abs(_minWidth - next) < 0.001f)
                    return;
                _minWidth = next;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public virtual float MinHeight
        {
            get { return _minHeight; }
            set
            {
                float next = Math.Max(0f, value);
                if (Math.Abs(_minHeight - next) < 0.001f)
                    return;
                _minHeight = next;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public virtual float MaxWidth
        {
            get { return _maxWidth; }
            set
            {
                float next = float.IsNaN(value)
                    ? float.PositiveInfinity
                    : Math.Max(0f, value);
                if (_maxWidth == next)
                    return;
                _maxWidth = next;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public virtual float MaxHeight
        {
            get { return _maxHeight; }
            set
            {
                float next = float.IsNaN(value)
                    ? float.PositiveInfinity
                    : Math.Max(0f, value);
                if (_maxHeight == next)
                    return;
                _maxHeight = next;
                Invalidate(UiInvalidation.Measure);
            }
        }

        /// <summary>
        /// External spacing around this element: left, top, right, bottom.
        /// Margin participates in DesiredSize and is consumed by the parent slot.
        /// </summary>
        public virtual Vector4 Margin
        {
            get { return _margin; }
            set
            {
                if (_margin == value)
                    return;
                _margin = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public virtual HorizontalAlignment HorizontalAlignment
        {
            get { return _horizontalAlignment; }
            set
            {
                if (_horizontalAlignment == value)
                    return;
                _horizontalAlignment = value;
                Invalidate(UiInvalidation.Arrange);
            }
        }

        public virtual VerticalAlignment VerticalAlignment
        {
            get { return _verticalAlignment; }
            set
            {
                if (_verticalAlignment == value)
                    return;
                _verticalAlignment = value;
                Invalidate(UiInvalidation.Arrange);
            }
        }

        public void Arrange(RectangleF bounds)
        {
            Control control = this as Control;
            if (control != null)
                control.EnsureStyleApplied();

            if ((PendingInvalidation & UiInvalidation.Measure) != 0)
                Measure(bounds.Size);

            Bounds = bounds;
            ArrangeChildren(bounds);
            ClearInvalidation(UiInvalidation.Arrange);
        }

        /// <summary>
        /// Measures the element using an Avalonia-style contract: available size is a
        /// constraint, explicit Width/Height are targets, Min/Max constrain the result,
        /// and Margin is added to the reported DesiredSize.
        /// </summary>
        public Vector2 Measure(Vector2 availableSize)
        {
            Control control = this as Control;
            if (control != null)
                control.EnsureStyleApplied();

            if (!Visible)
            {
                DesiredSize = Vector2.Zero;
                ClearInvalidation(UiInvalidation.Measure);
                return DesiredSize;
            }

            float availableWidth = NormalizeConstraint(availableSize.X);
            float availableHeight = NormalizeConstraint(availableSize.Y);
            float marginWidth = Margin.X + Margin.Z;
            float marginHeight = Margin.Y + Margin.W;
            float contentAvailableWidth = DeflateConstraint(availableWidth, marginWidth);
            float contentAvailableHeight = DeflateConstraint(availableHeight, marginHeight);

            float effectiveMaxWidth = Math.Max(MinWidth, MaxWidth);
            float effectiveMaxHeight = Math.Max(MinHeight, MaxHeight);
            float measureWidth = Width.HasValue
                ? ClampDimension(Math.Max(0f, Width.Value), MinWidth, effectiveMaxWidth)
                : Math.Min(contentAvailableWidth, effectiveMaxWidth);
            float measureHeight = Height.HasValue
                ? ClampDimension(Math.Max(0f, Height.Value), MinHeight, effectiveMaxHeight)
                : Math.Min(contentAvailableHeight, effectiveMaxHeight);

            Vector2 intrinsic = MeasureOverride(new Vector2(measureWidth, measureHeight));
            float desiredWidth = Width.HasValue
                ? Math.Max(0f, Width.Value)
                : Math.Max(0f, intrinsic.X);
            float desiredHeight = Height.HasValue
                ? Math.Max(0f, Height.Value)
                : Math.Max(0f, intrinsic.Y);

            desiredWidth = ClampDimension(desiredWidth, MinWidth, effectiveMaxWidth);
            desiredHeight = ClampDimension(desiredHeight, MinHeight, effectiveMaxHeight);
            if (!float.IsPositiveInfinity(contentAvailableWidth))
                desiredWidth = Math.Min(desiredWidth, contentAvailableWidth);
            if (!float.IsPositiveInfinity(contentAvailableHeight))
                desiredHeight = Math.Min(desiredHeight, contentAvailableHeight);

            DesiredSize = new Vector2(
                Math.Max(0f, desiredWidth + marginWidth),
                Math.Max(0f, desiredHeight + marginHeight));
            ClearInvalidation(UiInvalidation.Measure);
            return DesiredSize;
        }

        /// <summary>
        /// Leaf elements have no intrinsic size unless they override measurement.
        /// Containers and content controls should measure their children/content.
        /// </summary>
        protected virtual Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        internal void ArrangeInSlot(RectangleF slot)
        {
            Vector2 desired = (PendingInvalidation & UiInvalidation.Measure) != 0
                ? Measure(slot.Size)
                : DesiredSize;
            ArrangeInSlot(slot, desired);
        }

        internal void ArrangeInSlot(RectangleF slot, Vector2 desiredSize)
        {
            RectangleF contentSlot = Deflate(slot, Margin);
            float marginWidth = Margin.X + Margin.Z;
            float marginHeight = Margin.Y + Margin.W;
            float desiredWidth = Math.Max(0f, desiredSize.X - marginWidth);
            float desiredHeight = Math.Max(0f, desiredSize.Y - marginHeight);

            float effectiveMaxWidth = Math.Max(MinWidth, MaxWidth);
            float effectiveMaxHeight = Math.Max(MinHeight, MaxHeight);
            desiredWidth = ClampDimension(
                Width.HasValue ? Math.Max(0f, Width.Value) : desiredWidth,
                MinWidth,
                effectiveMaxWidth);
            desiredHeight = ClampDimension(
                Height.HasValue ? Math.Max(0f, Height.Value) : desiredHeight,
                MinHeight,
                effectiveMaxHeight);

            float width = Width.HasValue
                ? Math.Min(contentSlot.Width, desiredWidth)
                : HorizontalAlignment == HorizontalAlignment.Stretch
                    ? contentSlot.Width
                    : Math.Min(contentSlot.Width, desiredWidth);
            float height = Height.HasValue
                ? Math.Min(contentSlot.Height, desiredHeight)
                : VerticalAlignment == VerticalAlignment.Stretch
                    ? contentSlot.Height
                    : Math.Min(contentSlot.Height, desiredHeight);

            float x = contentSlot.X;
            float y = contentSlot.Y;

            if (HorizontalAlignment == HorizontalAlignment.Center)
                x += (contentSlot.Width - width) * 0.5f;
            else if (HorizontalAlignment == HorizontalAlignment.Right)
                x += contentSlot.Width - width;

            if (VerticalAlignment == VerticalAlignment.Center)
                y += (contentSlot.Height - height) * 0.5f;
            else if (VerticalAlignment == VerticalAlignment.Bottom)
                y += contentSlot.Height - height;

            Arrange(new RectangleF(x, y, Math.Max(0f, width), Math.Max(0f, height)));
        }

        protected virtual void ArrangeChildren(RectangleF bounds)
        {
        }

        static float NormalizeConstraint(float value)
        {
            if (float.IsPositiveInfinity(value))
                return value;
            if (float.IsNaN(value))
                return 0f;
            return Math.Max(0f, value);
        }

        static float DeflateConstraint(float available, float amount)
        {
            if (float.IsPositiveInfinity(available))
                return available;
            return Math.Max(0f, available - amount);
        }

        static float ClampDimension(float value, float minimum, float maximum)
        {
            float max = Math.Max(minimum, maximum);
            if (value < minimum)
                return minimum;
            if (value > max)
                return max;
            return value;
        }

        static RectangleF Deflate(RectangleF bounds, Vector4 thickness)
        {
            float left = bounds.X + thickness.X;
            float top = bounds.Y + thickness.Y;
            float right = bounds.Right - thickness.Z;
            float bottom = bounds.Bottom - thickness.W;
            return new RectangleF(
                left,
                top,
                Math.Max(0f, right - left),
                Math.Max(0f, bottom - top));
        }
    }
}
