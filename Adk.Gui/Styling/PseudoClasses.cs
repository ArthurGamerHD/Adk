using System;
using System.Collections.Generic;

namespace Adk.Gui.Styling
{
    /// <summary>
    /// Avalonia-compatible pseudoclass names used by the retained UI. The selector
    /// engine accepts arbitrary custom pseudoclasses; these constants cover the
    /// framework/common states and states emitted by the built-in controls.
    /// </summary>
    public static class PseudoClassNames
    {
        public const string Disabled = ":disabled";
        public const string PointerOver = ":pointerover";
        public const string Focus = ":focus";
        public const string FocusWithin = ":focus-within";
        public const string FocusVisible = ":focus-visible";
        public const string Pressed = ":pressed";
        public const string Checked = ":checked";
        public const string Unchecked = ":unchecked";
        public const string Indeterminate = ":indeterminate";
        public const string Selected = ":selected";
        public const string Open = ":open";
        public const string Error = ":error";
    }

    /// <summary>
    /// Protected control-state collection modeled after Avalonia's PseudoClasses API.
    /// Names are normalized to a leading ':' and compared case-insensitively.
    /// </summary>
    public sealed class PseudoClassCollection
    {
        readonly HashSet<string> _values =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Action _changed;

        internal PseudoClassCollection(Action changed)
        {
            _changed = changed;
        }

        public bool Contains(string pseudoClass)
        {
            return _values.Contains(Normalize(pseudoClass));
        }

        public void Set(string pseudoClass, bool active)
        {
            string normalized = Normalize(pseudoClass);
            bool changed = active
                ? _values.Add(normalized)
                : _values.Remove(normalized);
            if (changed && _changed != null)
                _changed();
        }

        static string Normalize(string pseudoClass)
        {
            if (string.IsNullOrWhiteSpace(pseudoClass))
                throw new ArgumentException("A pseudoclass name is required.", nameof(pseudoClass));
            string value = pseudoClass.Trim();
            return value[0] == ':' ? value : ":" + value;
        }
    }

}
