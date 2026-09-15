using VRageMath;

namespace Adk.Gui.Rendering
{
    public interface ITextMetrics
    {
        Vector2 Measure(string font, string text, float scale);
    }
}
