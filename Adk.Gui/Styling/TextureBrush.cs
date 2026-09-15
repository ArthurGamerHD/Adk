using System;
using VRageMath;

namespace Adk.Gui.Styling
{
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
}
