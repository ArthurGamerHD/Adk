using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using VRageMath;

namespace Adk.Gui.Layout
{
    public sealed class Grid : VisualElement
    {
        sealed class Placement
        {
            public int Column;
            public int Row;
            public int ColumnSpan;
            public int RowSpan;
        }

        readonly Dictionary<VisualElement, Placement> _placements =
            new Dictionary<VisualElement, Placement>();

        public Grid()
            : this(1, 1)
        {
        }

        public Grid(int columns, int rows)
        {
            Columns = CreateSegments(columns);
            Rows = CreateSegments(rows);
        }

        public Grid(VisualElement parent, int columns, int rows)
            : this(columns, rows)
        {
            if (parent != null)
                parent.AddChild(this);
        }

        public float[] Columns { get; private set; }
        public float[] Rows { get; private set; }

        public void SetColumns(params float[] columns)
        {
            Columns = NormalizeSegments(columns);
            Invalidate(UiInvalidation.Measure);
        }

        public void SetRows(params float[] rows)
        {
            Rows = NormalizeSegments(rows);
            Invalidate(UiInvalidation.Measure);
        }

        public T Set<T>(T child, int column, int row, int columnSpan = 1, int rowSpan = 1)
            where T : VisualElement
        {
            ValidatePlacement(column, row, columnSpan, rowSpan);
            AddChild(child);
            _placements[child] = new Placement
            {
                Column = column,
                Row = row,
                ColumnSpan = columnSpan,
                RowSpan = rowSpan
            };
            Invalidate(UiInvalidation.Measure);
            return child;
        }

        public override bool RemoveChild(VisualElement child)
        {
            _placements.Remove(child);
            return base.RemoveChild(child);
        }

        public override void ClearChildren()
        {
            _placements.Clear();
            base.ClearChildren();
        }

        /// <summary>
        /// This Grid currently supports proportional segment definitions. Measurement
        /// asks each child for the space available to its proportional cell and derives
        /// the smallest total grid size that can satisfy every child's desired size.
        /// </summary>
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            float totalColumnWeight = SumSegments(Columns);
            float totalRowWeight = SumSegments(Rows);
            float desiredWidth = 0f;
            float desiredHeight = 0f;

            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                Placement placement;
                if (!_placements.TryGetValue(child, out placement))
                    placement = GetSequentialPlacement(i);

                float columnWeight = SumSegments(
                    Columns,
                    placement.Column,
                    placement.ColumnSpan);
                float rowWeight = SumSegments(
                    Rows,
                    placement.Row,
                    placement.RowSpan);

                float childAvailableWidth = GetProportionalConstraint(
                    availableSize.X,
                    columnWeight,
                    totalColumnWeight);
                float childAvailableHeight = GetProportionalConstraint(
                    availableSize.Y,
                    rowWeight,
                    totalRowWeight);
                Vector2 childDesired = child.Measure(new Vector2(
                    childAvailableWidth,
                    childAvailableHeight));

                if (columnWeight > 0f && totalColumnWeight > 0f)
                    desiredWidth = Math.Max(
                        desiredWidth,
                        childDesired.X * totalColumnWeight / columnWeight);
                else
                    desiredWidth = Math.Max(desiredWidth, childDesired.X);

                if (rowWeight > 0f && totalRowWeight > 0f)
                    desiredHeight = Math.Max(
                        desiredHeight,
                        childDesired.Y * totalRowWeight / rowWeight);
                else
                    desiredHeight = Math.Max(desiredHeight, childDesired.Y);
            }

            if (!float.IsPositiveInfinity(availableSize.X))
                desiredWidth = Math.Min(Math.Max(0f, availableSize.X), desiredWidth);
            if (!float.IsPositiveInfinity(availableSize.Y))
                desiredHeight = Math.Min(Math.Max(0f, availableSize.Y), desiredHeight);
            return new Vector2(Math.Max(0f, desiredWidth), Math.Max(0f, desiredHeight));
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                Placement placement;
                if (!_placements.TryGetValue(child, out placement))
                    placement = GetSequentialPlacement(i);
                child.ArrangeInSlot(GetCellBounds(
                    bounds,
                    placement.Column,
                    placement.Row,
                    placement.ColumnSpan,
                    placement.RowSpan));
            }
        }

        Placement GetSequentialPlacement(int index)
        {
            int columnCount = Columns.Length;
            return new Placement
            {
                Column = index % columnCount,
                Row = Math.Min(Rows.Length - 1, index / columnCount),
                ColumnSpan = 1,
                RowSpan = 1
            };
        }

        RectangleF GetCellBounds(
            RectangleF bounds,
            int column,
            int row,
            int columnSpan,
            int rowSpan)
        {
            float x = GetSegmentStart(bounds.X, bounds.Width, Columns, column);
            float y = GetSegmentStart(bounds.Y, bounds.Height, Rows, row);
            float right = GetSegmentStart(
                bounds.X,
                bounds.Width,
                Columns,
                Math.Min(Columns.Length, column + columnSpan));
            float bottom = GetSegmentStart(
                bounds.Y,
                bounds.Height,
                Rows,
                Math.Min(Rows.Length, row + rowSpan));
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        void ValidatePlacement(int column, int row, int columnSpan, int rowSpan)
        {
            if (column < 0 || column >= Columns.Length)
                throw new InvalidOperationException("Grid column is outside the configured columns.");
            if (row < 0 || row >= Rows.Length)
                throw new InvalidOperationException("Grid row is outside the configured rows.");
            if (columnSpan < 1 || column + columnSpan > Columns.Length)
                throw new InvalidOperationException("Grid column span is outside the configured columns.");
            if (rowSpan < 1 || row + rowSpan > Rows.Length)
                throw new InvalidOperationException("Grid row span is outside the configured rows.");
        }

        static float GetSegmentStart(float origin, float size, float[] segments, int index)
        {
            if (index <= 0)
                return origin;
            if (index >= segments.Length)
                return origin + size;
            float total = SumSegments(segments);
            if (total <= 0f)
                return origin + size * index / segments.Length;
            float offset = SumSegments(segments, 0, index);
            return origin + size * offset / total;
        }

        static float GetProportionalConstraint(float available, float part, float total)
        {
            if (float.IsPositiveInfinity(available))
                return available;
            if (available <= 0f || part <= 0f || total <= 0f)
                return 0f;
            return available * part / total;
        }

        static float SumSegments(float[] segments)
        {
            return SumSegments(segments, 0, segments == null ? 0 : segments.Length);
        }

        static float SumSegments(float[] segments, int start, int count)
        {
            if (segments == null || segments.Length == 0 || count <= 0)
                return 0f;
            int first = Math.Max(0, start);
            int end = Math.Min(segments.Length, first + count);
            float total = 0f;
            for (int i = first; i < end; i++)
                total += Math.Max(0f, segments[i]);
            return total;
        }

        static float[] CreateSegments(int count)
        {
            int length = Math.Max(1, count);
            float[] result = new float[length];
            for (int i = 0; i < result.Length; i++)
                result[i] = 1f;
            return result;
        }

        static float[] NormalizeSegments(float[] segments)
        {
            if (segments == null || segments.Length == 0)
                return new[] { 1f };
            float[] result = new float[segments.Length];
            for (int i = 0; i < segments.Length; i++)
                result[i] = Math.Max(0f, segments[i]);
            return result;
        }
    }

    public enum StackOrientation
    {
        Vertical,
        Horizontal
    }

    public sealed class StackPanel : VisualElement
    {
        StackOrientation _orientation = StackOrientation.Vertical;
        float _spacing;

        public StackPanel()
        {
        }

        public StackPanel(VisualElement parent)
        {
            if (parent != null)
                parent.AddChild(this);
        }

        public StackOrientation Orientation
        {
            get { return _orientation; }
            set
            {
                if (_orientation == value)
                    return;
                _orientation = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        public float Spacing
        {
            get { return _spacing; }
            set
            {
                if (Math.Abs(_spacing - value) < 0.001f)
                    return;
                _spacing = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        /// <summary>
        /// Avalonia-style stack measurement: children receive infinite space on the
        /// stacking axis and the parent's constraint on the cross axis. Desired size is
        /// the sum on the stacking axis and the maximum child on the cross axis.
        /// </summary>
        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Orientation == StackOrientation.Horizontal
                ? MeasureHorizontal(availableSize)
                : MeasureVertical(availableSize);
        }

        Vector2 MeasureVertical(Vector2 availableSize)
        {
            float width = 0f;
            float height = 0f;
            bool hasVisibleChild = false;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                Vector2 desired = child.Measure(new Vector2(
                    availableSize.X,
                    float.PositiveInfinity));
                if (hasVisibleChild)
                    height += Spacing;
                width = Math.Max(width, Math.Max(0f, desired.X));
                height += Math.Max(0f, desired.Y);
                hasVisibleChild = true;
            }
            return new Vector2(width, Math.Max(0f, height));
        }

        Vector2 MeasureHorizontal(Vector2 availableSize)
        {
            float width = 0f;
            float height = 0f;
            bool hasVisibleChild = false;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                Vector2 desired = child.Measure(new Vector2(
                    float.PositiveInfinity,
                    availableSize.Y));
                if (hasVisibleChild)
                    width += Spacing;
                width += Math.Max(0f, desired.X);
                height = Math.Max(height, Math.Max(0f, desired.Y));
                hasVisibleChild = true;
            }
            return new Vector2(Math.Max(0f, width), height);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            if (Orientation == StackOrientation.Horizontal)
                ArrangeHorizontal(bounds);
            else
                ArrangeVertical(bounds);
        }

        void ArrangeVertical(RectangleF bounds)
        {
            float y = bounds.Y;
            bool hasVisibleChild = false;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                Vector2 desired = child.DesiredSize;
                if (hasVisibleChild)
                    y += Spacing;
                float slotHeight = Math.Max(0f, desired.Y);
                child.ArrangeInSlot(
                    new RectangleF(bounds.X, y, bounds.Width, slotHeight),
                    desired);
                y += slotHeight;
                hasVisibleChild = true;
            }
        }

        void ArrangeHorizontal(RectangleF bounds)
        {
            float x = bounds.X;
            bool hasVisibleChild = false;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                Vector2 desired = child.DesiredSize;
                if (hasVisibleChild)
                    x += Spacing;
                float slotWidth = Math.Max(0f, desired.X);
                child.ArrangeInSlot(
                    new RectangleF(x, bounds.Y, slotWidth, bounds.Height),
                    desired);
                x += slotWidth;
                hasVisibleChild = true;
            }
        }
    }
}
