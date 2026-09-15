using System;
using Adk.Gui.Rendering;

namespace Adk.Gui
{
    /// <summary>
    /// Configures the renderer-neutral GUI for the current host.
    /// </summary>
    public sealed class Gui
    {
        readonly IUiRenderer _renderer;

        Gui(IUiRenderer renderer)
        {
            _renderer = renderer;
        }

        public IUiRenderer Renderer => _renderer;

        public static Gui PrepareRender(IUiRenderer renderer)
        {
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));

            return new Gui(renderer);
        }

        public Core.VisualTree CreateVisualTree(Core.VisualElement root)
        {
            return new Core.VisualTree(root, _renderer);
        }
    }
}
