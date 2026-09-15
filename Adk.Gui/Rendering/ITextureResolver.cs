namespace Adk.Gui.Rendering
{
    public interface ITextureResolver
    {
        TextureSource MissingTexture { get; }
        bool TryResolve(TextureSource source, out object backendTexture);
        bool IsAvailable(TextureSource source);
    }
}
