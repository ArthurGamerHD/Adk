using System;
using System.Collections.Generic;
using Adk.Gui.Core.Invalidation;

namespace Adk.Gui.Core.PropertySystem
{
    internal sealed class PropertyStore
    {
        readonly Dictionary<IStyledProperty, object> _localValues =
            new Dictionary<IStyledProperty, object>();
        readonly Dictionary<IStyledProperty, object> _styleValues =
            new Dictionary<IStyledProperty, object>();

        public T GetValue<T>(StyledProperty<T> property)
        {
            object value;
            if (_localValues.TryGetValue(property, out value))
                return (T)value;
            if (_styleValues.TryGetValue(property, out value))
                return ((StyleValue<T>)value).Provider();
            return property.DefaultValue;
        }

        public bool SetValue<T>(StyledProperty<T> property, T value)
        {
            object currentLocal;
            if (_localValues.TryGetValue(property, out currentLocal) &&
                EqualityComparer<T>.Default.Equals((T)currentLocal, value))
                return false;

            // A local value has precedence even when it happens to equal the current
            // style/default value. Recording it is what keeps later style changes from
            // unexpectedly replacing an explicit assignment.
            _localValues[property] = value;
            return true;
        }

        public bool ClearValue<T>(StyledProperty<T> property)
        {
            return _localValues.Remove(property);
        }

        public void SetStyleValue<T>(StyledProperty<T> property, Func<T> provider)
        {
            _styleValues[property] = new StyleValue<T>(provider);
        }

        public UiInvalidation ClearStyleValues(Action<IStyledProperty> cleared = null)
        {
            UiInvalidation invalidation = UiInvalidation.None;
            foreach (IStyledProperty property in _styleValues.Keys)
            {
                invalidation |= property.Invalidation;
                if (cleared != null)
                    cleared(property);
            }
            _styleValues.Clear();
            return invalidation;
        }

        sealed class StyleValue<T>
        {
            public StyleValue(Func<T> provider)
            {
                Provider = provider;
            }

            public Func<T> Provider { get; }
        }
    }
}
