using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.PropertySystem;

namespace Adk.Gui.Markup
{
    public sealed class TypeRegistry
    {
        sealed class Entry
        {
            public Func<Control> Factory;
            public readonly Dictionary<string, Action<Control, string, TemplatedControl>> Setters =
                new Dictionary<string, Action<Control, string, TemplatedControl>>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly PropertyConverter _converter;

        public TypeRegistry(PropertyConverter converter = null)
        {
            _converter = converter ?? new PropertyConverter();
        }

        public void Register<TControl>(string xmlName, Func<TControl> factory)
            where TControl : Control
        {
            if (string.IsNullOrWhiteSpace(xmlName))
                throw new ArgumentException("A registered control requires an XML name.", nameof(xmlName));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _entries[xmlName] = new Entry { Factory = () => factory() };
        }

        public void RegisterProperty<TControl, TValue>(
            string xmlName,
            string propertyName,
            Action<TControl, TValue> setter)
            where TControl : Control
        {
            Entry entry;
            if (!_entries.TryGetValue(xmlName, out entry))
                throw new InvalidOperationException(xmlName + " must be registered first.");
            entry.Setters[propertyName] = (control, text, templatedParent) =>
            {
                TemplateBindingExtension binding;
                if (TemplateBindingExtension.TryParse(text, out binding))
                {
                    AttachTemplateBinding(
                        control,
                        templatedParent,
                        binding.Property,
                        value => setter((TControl)control, (TValue)value));
                    return;
                }
                setter((TControl)control, (TValue)_converter.Convert(text, typeof(TValue)));
            };
        }

        public Control Create(string xmlName)
        {
            Entry entry;
            if (!_entries.TryGetValue(xmlName, out entry))
                throw new InvalidOperationException("Unknown UI element '" + xmlName + "'.");
            return entry.Factory();
        }

        public void SetProperty(Control control, string xmlName, string propertyName, string value)
        {
            SetProperty(control, xmlName, propertyName, value, control.TemplatedParent);
        }

        internal void SetProperty(
            Control control,
            string xmlName,
            string propertyName,
            string value,
            TemplatedControl templatedParent)
        {
            if (SetCommonProperty(control, propertyName, value, templatedParent))
                return;
            Entry entry;
            Action<Control, string, TemplatedControl> setter;
            if (!_entries.TryGetValue(xmlName, out entry) ||
                !entry.Setters.TryGetValue(propertyName, out setter))
                throw new InvalidOperationException(
                    "Unknown property '" + xmlName + "." + propertyName + "'.");
            setter(control, value, templatedParent);
        }

        bool SetCommonProperty(
            Control control,
            string propertyName,
            string value,
            TemplatedControl templatedParent)
        {
            TemplateBindingExtension binding;
            if (TemplateBindingExtension.TryParse(value, out binding))
            {
                Action<object> setter = GetCommonSetter(control, propertyName);
                if (setter == null)
                    return false;
                AttachTemplateBinding(
                    control, templatedParent, binding.Property, setter);
                return true;
            }
            switch (propertyName)
            {
                case "Name": control.Name = value; return true;
                case "StyleClass": control.StyleClass = value; return true;
                case "Visible": control.Visible = (bool)_converter.Convert(value, typeof(bool)); return true;
                case "Enabled": control.Enabled = (bool)_converter.Convert(value, typeof(bool)); return true;
                case "Width": control.Width = (float)_converter.Convert(value, typeof(float)); return true;
                case "Height": control.Height = (float)_converter.Convert(value, typeof(float)); return true;
                case "MinWidth": control.MinWidth = (float)_converter.Convert(value, typeof(float)); return true;
                case "MinHeight": control.MinHeight = (float)_converter.Convert(value, typeof(float)); return true;
                case "MaxWidth": control.MaxWidth = (float)_converter.Convert(value, typeof(float)); return true;
                case "MaxHeight": control.MaxHeight = (float)_converter.Convert(value, typeof(float)); return true;
                case "Margin": control.Margin = (VRageMath.Vector4)_converter.Convert(value, typeof(VRageMath.Vector4)); return true;
                case "HorizontalAlignment":
                    control.HorizontalAlignment = (HorizontalAlignment)_converter.Convert(
                        value, typeof(HorizontalAlignment));
                    return true;
                case "VerticalAlignment":
                    control.VerticalAlignment = (VerticalAlignment)_converter.Convert(
                        value, typeof(VerticalAlignment));
                    return true;
                default: return false;
            }
        }

        static Action<object> GetCommonSetter(Control control, string propertyName)
        {
            switch (propertyName)
            {
                case "Visible": return value => control.Visible = (bool)value;
                case "Enabled": return value => control.Enabled = (bool)value;
                case "Width": return value => control.Width = (float)value;
                case "Height": return value => control.Height = (float)value;
                case "MinWidth": return value => control.MinWidth = (float)value;
                case "MinHeight": return value => control.MinHeight = (float)value;
                case "MaxWidth": return value => control.MaxWidth = (float)value;
                case "MaxHeight": return value => control.MaxHeight = (float)value;
                case "Margin": return value => control.Margin = (VRageMath.Vector4)value;
                case "HorizontalAlignment":
                    return value => control.HorizontalAlignment = (HorizontalAlignment)value;
                case "VerticalAlignment":
                    return value => control.VerticalAlignment = (VerticalAlignment)value;
                default: return null;
            }
        }

        static void AttachTemplateBinding(
            Control target,
            TemplatedControl owner,
            string propertyName,
            Action<object> setter)
        {
            if (owner == null)
                throw new InvalidOperationException(
                    "TemplateBinding can only be used inside a ControlTemplate.");
            IStyledProperty source = FindStyledProperty(owner, propertyName);
            var subscription = new TemplateBindingSubscription(
                owner, source, setter);
            target.RegisterLifetime(subscription);
        }

        static IStyledProperty FindStyledProperty(Control owner, string propertyName)
        {
            IList<IStyledProperty> properties = StyledPropertyCatalog.Snapshot();
            IStyledProperty match = null;
            for (int i = 0; i < properties.Count; i++)
            {
                IStyledProperty property = properties[i];
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal) &&
                    property.Matches(owner))
                    match = property;
            }
            if (match == null)
                throw new InvalidOperationException(
                    "Unknown template property '" + owner.GetType().Name + "." +
                    propertyName + "'.");
            return match;
        }

        sealed class TemplateBindingSubscription : IDisposable
        {
            readonly TemplatedControl _owner;
            readonly IStyledProperty _property;
            readonly Action<object> _setter;

            public TemplateBindingSubscription(
                TemplatedControl owner,
                IStyledProperty property,
                Action<object> setter)
            {
                _owner = owner;
                _property = property;
                _setter = setter;
                _owner.StyledPropertyChanged += OnPropertyChanged;
                Apply();
            }

            void OnPropertyChanged(Control control, IStyledProperty property)
            {
                if (ReferenceEquals(property, _property))
                    Apply();
            }

            void Apply()
            {
                _setter(_property.GetValueObject(_owner));
            }

            public void Dispose()
            {
                _owner.StyledPropertyChanged -= OnPropertyChanged;
            }
        }
    }
}
