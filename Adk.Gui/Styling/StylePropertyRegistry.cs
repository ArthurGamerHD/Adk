using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Markup;
using Sandbox.ModAPI;
using VRageMath;

namespace Adk.Gui.Styling
{
    /// <summary>
    /// Selector/property dispatch table populated by StyledProperty.Register.
    /// No assembly or field scanning is used.
    /// </summary>
    public sealed class StylePropertyRegistry
    {
        sealed class TypeEntry
        {
            public Type OwnerType;
            public Func<Control, bool> Matches;
            public readonly Dictionary<string, IStyledProperty> Properties =
                new Dictionary<string, IStyledProperty>(StringComparer.Ordinal);
        }

        static readonly StylePropertyRegistry GlobalRegistry = new StylePropertyRegistry();

        readonly Dictionary<string, TypeEntry> _types =
            new Dictionary<string, TypeEntry>(StringComparer.Ordinal);

        public StylePropertyRegistry()
        {
            IList<IStyledProperty> properties = StyledPropertyCatalog.Snapshot();
            for (int i = 0; i < properties.Count; i++)
                Register(properties[i]);
            StyledPropertyCatalog.Registered += Register;
        }

        public static StylePropertyRegistry Global => GlobalRegistry;
        internal event Action<string> MetadataChanged;

        public void RegisterType<TControl>(string selectorName)
            where TControl : Control
        {
            if (string.IsNullOrWhiteSpace(selectorName))
                throw new ArgumentException("A style type requires a selector name.", nameof(selectorName));
            GetOrCreate(selectorName, typeof(TControl));
        }

        public void Register<TControl, TValue>(
            string selectorName,
            StyledProperty<TValue> property)
            where TControl : Control
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            Register(property, selectorName, typeof(TControl));
        }

        internal bool ContainsType(string selectorName)
        {
            return _types.ContainsKey(selectorName);
        }

        internal bool ContainsProperty(string selectorName, string propertyName)
        {
            TypeEntry entry;
            IStyledProperty property;
            return _types.TryGetValue(selectorName, out entry) &&
                   TryResolveProperty(entry, propertyName, out property);
        }

        internal bool Matches(string selectorName, Control control)
        {
            TypeEntry entry;
            return _types.TryGetValue(selectorName, out entry) &&
                   entry.Matches(control);
        }

        internal void Reset(Control control)
        {
            control.ClearStyleValues();
        }

        internal void Apply(
            string selectorName,
            string propertyName,
            Control control,
            object value,
            StyleSetterContext context)
        {
            TypeEntry entry = GetTypeEntry(selectorName);
            IStyledProperty property;
            if (!TryResolveProperty(entry, propertyName, out property))
                throw new InvalidOperationException(
                    "Unknown styled property '" + selectorName + "." + propertyName + "'.");
            try
            {
                property.ApplyStyle(control, value, context);
            }
            catch (Exception exception)
            {
                string displayValue = value == null ? "<null>" : value.ToString();
                throw new FormatException(
                    "Could not apply style setter '" + selectorName + "." +
                    propertyName + "' value '" + displayValue + "' as " +
                    property.ValueType.Name + ".",
                    exception);
            }
        }

        bool TryResolveProperty(
            TypeEntry entry,
            string propertyName,
            out IStyledProperty property)
        {
            if (entry.Properties.TryGetValue(propertyName, out property))
                return true;

            TypeEntry best = null;
            IStyledProperty bestProperty = null;

            foreach (TypeEntry candidate in _types.Values)
            {
                IStyledProperty candidateProperty;
                if (!candidate.Properties.TryGetValue(propertyName, out candidateProperty))
                    continue;

                // Space Engineers does not whitelist Type.IsAssignableFrom for mods.
                // Resolve inherited styled properties through the ModAPI reflection gateway.
                if (!MyAPIGateway.Reflection.IsAssignableFrom(
                        candidate.OwnerType,
                        entry.OwnerType))
                    continue;

                // Prefer the most-derived matching owner. In a Control class hierarchy,
                // any two matching class owners are ordered by assignability.
                if (best == null ||
                    MyAPIGateway.Reflection.IsAssignableFrom(
                        best.OwnerType,
                        candidate.OwnerType))
                {
                    best = candidate;
                    bestProperty = candidateProperty;
                }
            }

            if (bestProperty != null)
            {
                property = bestProperty;
                return true;
            }

            property = null;
            return false;
        }

        void Register(IStyledProperty property)
        {
            Register(property, property.OwnerType.Name, property.OwnerType);
        }

        void Register(IStyledProperty property, string selectorName, Type ownerType)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            TypeEntry entry = GetOrCreate(selectorName, ownerType);
            entry.Matches = property.Matches;
            entry.Properties[property.Name] = property;
            Action<string> changed = MetadataChanged;
            if (changed != null)
                changed(selectorName);
        }

        TypeEntry GetOrCreate(string selectorName, Type ownerType)
        {
            TypeEntry entry;
            if (_types.TryGetValue(selectorName, out entry))
            {
                if (entry.OwnerType != ownerType)
                    throw new InvalidOperationException(
                        "Style selector '" + selectorName + "' is already owned by " +
                        entry.OwnerType.FullName + ".");
                return entry;
            }
            entry = new TypeEntry
            {
                OwnerType = ownerType,
                Matches = control => false
            };
            _types.Add(selectorName, entry);
            Action<string> changed = MetadataChanged;
            if (changed != null)
                changed(selectorName);
            return entry;
        }

        TypeEntry GetTypeEntry(string selectorName)
        {
            TypeEntry entry;
            if (!_types.TryGetValue(selectorName, out entry))
                throw new InvalidOperationException(
                    "Style selector type '" + selectorName + "' is not registered.");
            return entry;
        }
    }

    public sealed class StyleSetterContext
        : IStyleValueSource
    {
        readonly ResourceDictionary _resources;
        readonly PropertyConverter _converter = new PropertyConverter();

        public StyleSetterContext(ResourceDictionary resources)
        {
            _resources = resources ?? new ResourceDictionary();
        }

        public TValue Convert<TValue>(string value)
        {
            return ConvertProvider<TValue>(value)();
        }

        public Func<TValue> ConvertProvider<TValue>(object value)
        {
            if (value is TValue)
            {
                TValue direct = (TValue)value;
                return () => direct;
            }

            string text = value as string;
            if (text == null)
                throw new NotSupportedException(
                    "No style conversion is registered from " +
                    value.GetType().FullName + " to " + typeof(TValue).FullName + ".");

            string key;
            bool dynamic;
            if (TryParseResource(text, out key, out dynamic))
            {
                Func<TValue> provider;
                if (_resources.TryGetValue(key, out provider))
                {
                    if (dynamic)
                        return provider;
                    TValue resolved = provider();
                    return () => resolved;
                }
                TValue resource;
                if (_resources.TryGetValue(key, out resource))
                    return () => resource;
                if (typeof(TValue) == typeof(BackgroundBrush))
                {
                    Func<Color> colorProvider;
                    if (_resources.TryGetValue(key, out colorProvider))
                    {
                        if (dynamic)
                            return (Func<TValue>)(object)new Func<BackgroundBrush>(
                                () => new SolidColorBrush(colorProvider()));
                        BackgroundBrush resolved =
                            new SolidColorBrush(colorProvider());
                        return () => (TValue)(object)resolved;
                    }
                    Color color;
                    if (_resources.TryGetValue(key, out color))
                    {
                        BackgroundBrush resolved = new SolidColorBrush(color);
                        return () => (TValue)(object)resolved;
                    }
                }
                throw new InvalidOperationException("Unknown style resource '" + key + "'.");
            }
            TValue converted = (TValue)_converter.Convert(text, typeof(TValue));
            return () => converted;
        }

        static bool TryParseResource(string value, out string key, out bool dynamic)
        {
            key = null;
            dynamic = false;
            if (string.IsNullOrWhiteSpace(value) || value[0] != '{' ||
                value[value.Length - 1] != '}')
                return false;
            string expression = value.Substring(1, value.Length - 2).Trim();
            const string DynamicPrefix = "DynamicResource ";
            const string ResourcePrefix = "Resource ";
            if (expression.StartsWith(DynamicPrefix, StringComparison.Ordinal))
            {
                key = expression.Substring(DynamicPrefix.Length).Trim();
                dynamic = true;
            }
            else if (expression.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                key = expression.Substring(ResourcePrefix.Length).Trim();
            }
            return !string.IsNullOrEmpty(key);
        }
    }
}
