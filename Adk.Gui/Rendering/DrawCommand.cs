using VRage.Utils;
using VRageMath;

namespace Adk.Gui.Rendering
{
    public enum DrawCommandKind
    {
        FillRectangle,
        Texture,
        Text
    }

    /// <summary>
    /// Optional command representation for diagnostics, snapshot renderers, and
    /// future batching. The current screen-space backend executes calls directly.
    /// </summary>
    public struct DrawCommand
    {
        public DrawCommandKind Kind;
        public RectangleF Bounds;
        public TextureSource Texture;
        public string Font;
        public string Text;
        public Vector2 Position;
        public float Scale;
        public float Rotation;
        public Color Color;
        public MyGuiDrawAlignEnum Alignment;
    }
}
