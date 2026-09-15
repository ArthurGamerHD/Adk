using VRageMath;

namespace Adk.Gui.Styling
{
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
}
