using System;

namespace Adk.Gui.Rendering
{
    /// <summary>
    /// Renderer-neutral texture reference.
    /// Sprites can use string while dynamic textures can use native handles.
    /// </summary>
    public struct TextureSource : IEquatable<TextureSource>
    {
        readonly string _key;
        readonly object _nativeHandle;

        TextureSource(string key, object nativeHandle)
        {
            _key = key;
            _nativeHandle = nativeHandle;
        }

        public string Key => _key;
        public object NativeHandle => _nativeHandle;
        public bool IsEmpty => string.IsNullOrWhiteSpace(_key) && _nativeHandle == null;

        public static TextureSource FromKey(string key)
        {
            return new TextureSource(key, null);
        }

        public static TextureSource FromNative(object nativeHandle)
        {
            return new TextureSource(null, nativeHandle);
        }

        public bool Equals(TextureSource other)
        {
            return string.Equals(_key, other._key, StringComparison.Ordinal) &&
                   Equals(_nativeHandle, other._nativeHandle);
        }

        public override bool Equals(object obj)
        {
            return obj is TextureSource && Equals((TextureSource)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_key == null ? 0 : _key.GetHashCode()) * 397) ^
                       (_nativeHandle == null ? 0 : _nativeHandle.GetHashCode());
            }
        }

        public override string ToString()
        {
            return _key ?? (_nativeHandle == null ? string.Empty : _nativeHandle.ToString());
        }
    }
}
