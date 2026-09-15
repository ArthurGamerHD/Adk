using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRage.Utils;

namespace Adk.Gui.ScreenSpace
{
    public sealed class ScreenSpaceTextureResolver : ITextureResolver
    {
        public TextureSource MissingTexture => TextureSource.FromKey("Minimap_MissingIcon");

        public bool TryResolve(TextureSource source, out object backendTexture)
        {
            if (source.NativeHandle is MyStringId)
            {
                backendTexture = (MyStringId)source.NativeHandle;
                return (MyStringId)backendTexture != MyStringId.NullOrEmpty;
            }

            if (!string.IsNullOrWhiteSpace(source.Key))
            {
                backendTexture = MyStringId.GetOrCompute(source.Key);
                return true;
            }

            backendTexture = null;
            return false;
        }

        public bool IsAvailable(TextureSource source)
        {
            object resolved;
            return TryResolve(source, out resolved) &&
                   MyScreenSpaceSprite.IsMaterialAvailable((MyStringId)resolved);
        }
    }
}
