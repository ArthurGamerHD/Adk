using System;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Adk.Gui.ScreenSpace
{
    /// <summary>
    /// Owns one ModAPI-generated DX12 texture and its renderer-managed upload buffers.
    /// </summary>
    public sealed class DynamicMapTexture : IDynamicTexture
    {
        IMyGeneratedTexture _texture;

        public DynamicMapTexture(string name, Vector2I requestedSize)
        {
            Vector2I size = FitToRenderer(requestedSize);
            if (size.X <= 0 || size.Y <= 0)
                return;

            _texture = MyAPIGateway.GeneratedTextures.Create(
                name,
                size,
                MyGeneratedTextureFormat.Srgb);
        }

        public IMyGeneratedTexture Texture => _texture;
        public TextureSource Source => IsAvailable
            ? TextureSource.FromNative(_texture.MaterialId)
            : default(TextureSource);
        public bool IsAvailable => _texture != null && _texture.IsValid;
        public Vector2I Size => IsAvailable ? _texture.Size : Vector2I.Zero;

        public static Vector2I FitToRenderer(Vector2I requestedSize)
        {
            IMyGeneratedTextures textures = MyAPIGateway.GeneratedTextures;
            if (textures == null || !textures.IsSupported || textures.MaxTextureSide <= 0 ||
                requestedSize.X <= 0 || requestedSize.Y <= 0)
            {
                return Vector2I.Zero;
            }

            int maxSide = textures.MaxTextureSide;
            int largestRequestedSide = Math.Max(requestedSize.X, requestedSize.Y);
            if (largestRequestedSide <= maxSide)
                return requestedSize;

            double scale = maxSide / (double)largestRequestedSide;
            return new Vector2I(
                Math.Max(1, (int)Math.Round(requestedSize.X * scale)),
                Math.Max(1, (int)Math.Round(requestedSize.Y * scale)));
        }

        public bool Update(Action<PixelCanvas> paint)
        {
            if (!IsAvailable || paint == null)
                return false;

            byte[] pixels = _texture.BeginUpdate();
            if (pixels == null)
                return false;

            try
            {
                paint(new PixelCanvas(pixels, _texture.Size.X, _texture.Size.Y));
            }
            finally
            {
                _texture.EndUpdate(pixels);
            }
            return true;
        }

        public void Dispose()
        {
            if (_texture == null)
                return;
            _texture.Dispose();
            _texture = null;
        }
    }

    public sealed class ScreenSpaceDynamicTextureFactory : IDynamicTextureFactory
    {
        public Vector2I FitToSupportedSize(Vector2I requestedSize)
        {
            return DynamicMapTexture.FitToRenderer(requestedSize);
        }

        public IDynamicTexture Create(string name, Vector2I requestedSize)
        {
            return new DynamicMapTexture(name, requestedSize);
        }
    }
}