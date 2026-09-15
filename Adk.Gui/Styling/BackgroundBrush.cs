using System;
using VRageMath;

namespace Adk.Gui.Styling
{
    /// <summary>
    /// Base class for renderer-neutral background brushes.
    /// </summary>
    public abstract class BackgroundBrush : IEquatable<BackgroundBrush>
    {
        /// <summary>
        /// Color used when the renderer cannot draw the specialized brush.
        /// </summary>
        public abstract Color FallbackColor { get; }

        public static implicit operator BackgroundBrush(Color color)
        {
            return new SolidColorBrush(color);
        }

        public bool Equals(BackgroundBrush other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (ReferenceEquals(other, null) || GetType() != other.GetType())
                return false;
            return EqualsCore(other);
        }

        protected abstract bool EqualsCore(BackgroundBrush other);

        public override bool Equals(object obj)
        {
            return Equals(obj as BackgroundBrush);
        }

        public abstract override int GetHashCode();
    }

    /// <summary>
    /// Background brush that fills its bounds with one color.
    /// </summary>
    public sealed class SolidColorBrush : BackgroundBrush
    {
        public SolidColorBrush(Color color)
        {
            Color = color;
        }

        public Color Color { get; }
        public override Color FallbackColor => Color;

        protected override bool EqualsCore(BackgroundBrush other)
        {
            return Color == ((SolidColorBrush)other).Color;
        }

        public override int GetHashCode()
        {
            return Color.GetHashCode();
        }
    }

    /// <summary>
    /// Background brush backed by a renderer texture/material key.
    /// </summary>
    public sealed class TextureBrush : BackgroundBrush
    {
        public TextureBrush(string source)
            : this(source, Color.Transparent, Color.White)
        {
        }

        public TextureBrush(string source, Color fallback)
            : this(source, fallback, Color.White)
        {
        }

        public TextureBrush(string source, Color fallback, Color tint)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A texture brush requires a source.", nameof(source));

            Source = source.Trim();
            Fallback = fallback;
            Tint = tint;
        }

        public string Source { get; }
        public Color Fallback { get; }
        public Color Tint { get; }
        public override Color FallbackColor => Fallback;

        protected override bool EqualsCore(BackgroundBrush other)
        {
            TextureBrush brush = (TextureBrush)other;
            return string.Equals(Source, brush.Source, StringComparison.Ordinal) &&
                   Fallback == brush.Fallback &&
                   Tint == brush.Tint;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Source.GetHashCode();
                hash = hash * 397 ^ Fallback.GetHashCode();
                return hash * 397 ^ Tint.GetHashCode();
            }
        }
    }
    /// <summary>
    /// Axis used by <see cref="CompositeTextureBrush"/> when stretching its center segment.
    /// </summary>
    public enum CompositeBrushOrientation
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Three-segment texture brush that preserves the native-sized start/end caps
    /// and stretches only the center segment. This mirrors Space Engineers'
    /// MyGuiCompositeTexture rails/thumbs without coupling Adk.Gui to the game GUI renderer.
    /// </summary>
    public sealed class CompositeTextureBrush : BackgroundBrush
    {
        public CompositeTextureBrush(
            string startSource,
            string centerSource,
            string endSource,
            CompositeBrushOrientation orientation,
            float startLength,
            float endLength,
            float referenceCrossLength)
            : this(
                startSource, centerSource, endSource, orientation,
                startLength, endLength, referenceCrossLength,
                Color.Transparent, Color.White)
        {
        }

        public CompositeTextureBrush(
            string startSource,
            string centerSource,
            string endSource,
            CompositeBrushOrientation orientation,
            float startLength,
            float endLength,
            float referenceCrossLength,
            Color fallback,
            Color tint)
        {
            if (string.IsNullOrWhiteSpace(startSource))
                throw new ArgumentException("A composite texture brush requires a start source.", nameof(startSource));
            if (string.IsNullOrWhiteSpace(centerSource))
                throw new ArgumentException("A composite texture brush requires a center source.", nameof(centerSource));
            if (string.IsNullOrWhiteSpace(endSource))
                throw new ArgumentException("A composite texture brush requires an end source.", nameof(endSource));
            if (startLength < 0f)
                throw new ArgumentException(nameof(startLength));
            if (endLength < 0f)
                throw new ArgumentException(nameof(endLength));
            if (referenceCrossLength <= 0f)
                throw new ArgumentException(nameof(referenceCrossLength));

            StartSource = startSource.Trim();
            CenterSource = centerSource.Trim();
            EndSource = endSource.Trim();
            Orientation = orientation;
            StartLength = startLength;
            EndLength = endLength;
            ReferenceCrossLength = referenceCrossLength;
            Fallback = fallback;
            Tint = tint;
        }

        public string StartSource { get; }
        public string CenterSource { get; }
        public string EndSource { get; }
        public CompositeBrushOrientation Orientation { get; }
        public float StartLength { get; }
        public float EndLength { get; }
        public float ReferenceCrossLength { get; }
        public Color Fallback { get; }
        public Color Tint { get; }
        public override Color FallbackColor => Fallback;

        protected override bool EqualsCore(BackgroundBrush other)
        {
            CompositeTextureBrush brush = (CompositeTextureBrush)other;
            return string.Equals(StartSource, brush.StartSource, StringComparison.Ordinal) &&
                   string.Equals(CenterSource, brush.CenterSource, StringComparison.Ordinal) &&
                   string.Equals(EndSource, brush.EndSource, StringComparison.Ordinal) &&
                   Orientation == brush.Orientation &&
                   StartLength.Equals(brush.StartLength) &&
                   EndLength.Equals(brush.EndLength) &&
                   ReferenceCrossLength.Equals(brush.ReferenceCrossLength) &&
                   Fallback == brush.Fallback &&
                   Tint == brush.Tint;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StartSource.GetHashCode();
                hash = hash * 397 ^ CenterSource.GetHashCode();
                hash = hash * 397 ^ EndSource.GetHashCode();
                hash = hash * 397 ^ (int)Orientation;
                hash = hash * 397 ^ StartLength.GetHashCode();
                hash = hash * 397 ^ EndLength.GetHashCode();
                hash = hash * 397 ^ ReferenceCrossLength.GetHashCode();
                hash = hash * 397 ^ Fallback.GetHashCode();
                return hash * 397 ^ Tint.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Nine-segment texture brush that preserves native border pieces and stretches
    /// only the center rows/columns. The slice metrics are authored against a
    /// reference width so GUI-scale changes keep Keen-style borders proportional.
    /// </summary>
    public sealed class NineSliceTextureBrush : BackgroundBrush
    {
        public NineSliceTextureBrush(
            string leftTopSource,
            string centerTopSource,
            string rightTopSource,
            string leftCenterSource,
            string centerSource,
            string rightCenterSource,
            string leftBottomSource,
            string centerBottomSource,
            string rightBottomSource,
            float leftWidth,
            float rightWidth,
            float topHeight,
            float bottomHeight,
            float referenceWidth,
            Color fallback,
            Color tint)
        {
            LeftTopSource = Require(leftTopSource, nameof(leftTopSource));
            CenterTopSource = Require(centerTopSource, nameof(centerTopSource));
            RightTopSource = Require(rightTopSource, nameof(rightTopSource));
            LeftCenterSource = Require(leftCenterSource, nameof(leftCenterSource));
            CenterSource = Require(centerSource, nameof(centerSource));
            RightCenterSource = Require(rightCenterSource, nameof(rightCenterSource));
            LeftBottomSource = Require(leftBottomSource, nameof(leftBottomSource));
            CenterBottomSource = Require(centerBottomSource, nameof(centerBottomSource));
            RightBottomSource = Require(rightBottomSource, nameof(rightBottomSource));
            if (leftWidth < 0f) throw new ArgumentException(nameof(leftWidth));
            if (rightWidth < 0f) throw new ArgumentException(nameof(rightWidth));
            if (topHeight < 0f) throw new ArgumentException(nameof(topHeight));
            if (bottomHeight < 0f) throw new ArgumentException(nameof(bottomHeight));
            if (referenceWidth <= 0f) throw new ArgumentException(nameof(referenceWidth));
            LeftWidth = leftWidth;
            RightWidth = rightWidth;
            TopHeight = topHeight;
            BottomHeight = bottomHeight;
            ReferenceWidth = referenceWidth;
            Fallback = fallback;
            Tint = tint;
        }

        static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A nine-slice texture brush requires every source.", name);
            return value.Trim();
        }

        public string LeftTopSource { get; }
        public string CenterTopSource { get; }
        public string RightTopSource { get; }
        public string LeftCenterSource { get; }
        public string CenterSource { get; }
        public string RightCenterSource { get; }
        public string LeftBottomSource { get; }
        public string CenterBottomSource { get; }
        public string RightBottomSource { get; }
        public float LeftWidth { get; }
        public float RightWidth { get; }
        public float TopHeight { get; }
        public float BottomHeight { get; }
        public float ReferenceWidth { get; }
        public Color Fallback { get; }
        public Color Tint { get; }
        public override Color FallbackColor => Fallback;

        protected override bool EqualsCore(BackgroundBrush other)
        {
            NineSliceTextureBrush brush = (NineSliceTextureBrush)other;
            return string.Equals(LeftTopSource, brush.LeftTopSource, StringComparison.Ordinal) &&
                   string.Equals(CenterTopSource, brush.CenterTopSource, StringComparison.Ordinal) &&
                   string.Equals(RightTopSource, brush.RightTopSource, StringComparison.Ordinal) &&
                   string.Equals(LeftCenterSource, brush.LeftCenterSource, StringComparison.Ordinal) &&
                   string.Equals(CenterSource, brush.CenterSource, StringComparison.Ordinal) &&
                   string.Equals(RightCenterSource, brush.RightCenterSource, StringComparison.Ordinal) &&
                   string.Equals(LeftBottomSource, brush.LeftBottomSource, StringComparison.Ordinal) &&
                   string.Equals(CenterBottomSource, brush.CenterBottomSource, StringComparison.Ordinal) &&
                   string.Equals(RightBottomSource, brush.RightBottomSource, StringComparison.Ordinal) &&
                   LeftWidth.Equals(brush.LeftWidth) && RightWidth.Equals(brush.RightWidth) &&
                   TopHeight.Equals(brush.TopHeight) && BottomHeight.Equals(brush.BottomHeight) &&
                   ReferenceWidth.Equals(brush.ReferenceWidth) && Fallback == brush.Fallback && Tint == brush.Tint;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = LeftTopSource.GetHashCode();
                hash = hash * 397 ^ CenterTopSource.GetHashCode();
                hash = hash * 397 ^ RightTopSource.GetHashCode();
                hash = hash * 397 ^ LeftCenterSource.GetHashCode();
                hash = hash * 397 ^ CenterSource.GetHashCode();
                hash = hash * 397 ^ RightCenterSource.GetHashCode();
                hash = hash * 397 ^ LeftBottomSource.GetHashCode();
                hash = hash * 397 ^ CenterBottomSource.GetHashCode();
                hash = hash * 397 ^ RightBottomSource.GetHashCode();
                hash = hash * 397 ^ LeftWidth.GetHashCode();
                hash = hash * 397 ^ RightWidth.GetHashCode();
                hash = hash * 397 ^ TopHeight.GetHashCode();
                hash = hash * 397 ^ BottomHeight.GetHashCode();
                hash = hash * 397 ^ ReferenceWidth.GetHashCode();
                hash = hash * 397 ^ Fallback.GetHashCode();
                return hash * 397 ^ Tint.GetHashCode();
            }
        }
    }

}
