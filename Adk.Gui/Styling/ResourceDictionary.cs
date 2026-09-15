using System;
using System.Collections.Generic;

namespace Adk.Gui.Styling
{
    public sealed class ResourceDictionary
    {
        readonly Dictionary<string, object> _values =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public ResourceDictionary Parent { get; set; }

        public void Add(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A resource requires a key.", nameof(key));
            _values[key] = value;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            object candidate;
            if (_values.TryGetValue(key, out candidate) && candidate is T)
            {
                value = (T)candidate;
                return true;
            }
            if (Parent != null)
                return Parent.TryGetValue(key, out value);
            value = default(T);
            return false;
        }
    }
}
