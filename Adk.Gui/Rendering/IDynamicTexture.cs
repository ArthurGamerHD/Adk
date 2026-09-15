using System;
using VRageMath;

namespace Adk.Gui.Rendering
{
    public interface IDynamicTexture : IDisposable
    {
        TextureSource Source { get; }
        bool IsAvailable { get; }
        Vector2I Size { get; }
        bool Update(Action<PixelCanvas> paint);
    }

    public interface IDynamicTextureFactory
    {
        Vector2I FitToSupportedSize(Vector2I requestedSize);
        IDynamicTexture Create(string name, Vector2I requestedSize);
    }
}
