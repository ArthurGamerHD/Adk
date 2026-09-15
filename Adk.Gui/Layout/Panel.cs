using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Rendering;
using VRageMath;

namespace Adk.Gui.Layout
{
    public class Panel : VisualElement
    {
        public Color BackgroundColor { get; set; } = new Color(12, 20, 29, 230);
        public Color BorderColor { get; set; } = new Color(72, 118, 148, 220);
        public int BorderThickness { get; set; } = 1;

        protected override void ArrangeChildren(RectangleF bounds)
        {
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (child.Visible)
                    child.ArrangeInSlot(bounds);
            }
        }

        protected override void DrawSelf(RenderContext context)
        {
            context.Fill(Bounds, BackgroundColor);
            context.Border(Bounds, BorderColor, BorderThickness);
        }
    }
}
