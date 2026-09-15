using System.Text;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRageMath;

namespace Adk.Gui.ScreenSpace
{
    public sealed class ScreenSpaceTextMetrics : ITextMetrics
    {
        readonly StringBuilder _buffer = new StringBuilder();

        public Vector2 Measure(string font, string text, float scale)
        {
            _buffer.Clear();
            _buffer.Append(string.IsNullOrEmpty(text) ? "M" : text);
            return MyScreenSpaceText.MeasurePixels(
                string.IsNullOrWhiteSpace(font) ? "White" : font,
                _buffer,
                scale);
        }
    }
}
