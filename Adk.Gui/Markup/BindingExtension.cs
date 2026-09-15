using System;

namespace Adk.Gui.Markup
{
    public sealed class BindingExtension
    {
        public BindingExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A binding requires a property path.", nameof(path));
            Path = path.Trim();
        }

        public string Path { get; }

        public static bool TryParse(string value, out BindingExtension binding)
        {
            const string Prefix = "{Binding ";
            binding = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !value.StartsWith(Prefix, StringComparison.Ordinal) ||
                !value.EndsWith("}", StringComparison.Ordinal))
                return false;
            string path = value.Substring(Prefix.Length, value.Length - Prefix.Length - 1).Trim();
            if (path.Length == 0)
                return false;
            binding = new BindingExtension(path);
            return true;
        }
    }

    public sealed class TemplateBindingExtension
    {
        public TemplateBindingExtension(string property)
        {
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException(
                    "A template binding requires a property name.", nameof(property));
            Property = property.Trim();
        }

        public string Property { get; }

        public static bool TryParse(string value, out TemplateBindingExtension binding)
        {
            const string Prefix = "{TemplateBinding ";
            binding = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !value.StartsWith(Prefix, StringComparison.Ordinal) ||
                !value.EndsWith("}", StringComparison.Ordinal))
                return false;
            string property = value.Substring(
                Prefix.Length, value.Length - Prefix.Length - 1).Trim();
            if (property.Length == 0)
                return false;
            binding = new TemplateBindingExtension(property);
            return true;
        }
    }
}
