using System;
using System.Collections.Generic;

namespace Adk.Gui.Core.Binding
{
    public abstract class ObservableObject
    {
        public event Action<ObservableObject, string, object> PropertyChanged;

        protected bool RaiseAndSetIfChanged<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            RaisePropertyChanged(propertyName, value);
            return true;
        }

        protected void RaisePropertyChanged<T>(string propertyName, T value)
        {
            Action<ObservableObject, string, object> handlers = PropertyChanged;
            if (handlers == null)
                return;

            Exception firstError = null;
            foreach (Action<ObservableObject, string, object> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, propertyName, value);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                        firstError = error;
                }
            }

            if (firstError != null)
                throw firstError;
        }
    }

    internal interface IBind : IDisposable
    {
        void Attach(ObservableObject source);
    }

    public sealed class Bind<T> : IBind
    {
        readonly Action<T> _setter;
        readonly string _propertyName;
        ObservableObject _source;

        public Bind(Action<T> setter, string propertyName)
        {
            if (setter == null)
                throw new ArgumentNullException(nameof(setter));
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("A source property name is required.", nameof(propertyName));
            _setter = setter;
            _propertyName = propertyName;
        }

        void IBind.Attach(ObservableObject source)
        {
            Attach(source);
        }

        internal void Attach(ObservableObject source)
        {
            if (ReferenceEquals(_source, source))
                return;
            if (_source != null)
                _source.PropertyChanged -= OnPropertyChanged;

            _source = source;
            if (_source == null)
                return;
            
            _source.PropertyChanged += OnPropertyChanged;
        }

        void OnPropertyChanged(ObservableObject source, string propertyName, object value)
        {
            if (!ReferenceEquals(source, _source))
                return;
            if (propertyName != _propertyName)
                return;
            try
            {
                _setter(value == null ? default(T) : (T)value);
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(
                    "Observable property '" + propertyName +
                    "' does not match the requested binding type.");
            }
        }

        public void Dispose()
        {
            if (_source != null)
                _source.PropertyChanged -= OnPropertyChanged;
            _source = null;
        }
    }
}
