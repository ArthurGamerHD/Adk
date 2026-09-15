using System;
using Adk.Gui.Core;

namespace Adk.Gui.Styling
{
    public sealed class DataTemplate<TData>
    {
        readonly Func<TData, Control> _factory;

        public DataTemplate(Func<TData, Control> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _factory = factory;
        }

        public Control Build(TData value)
        {
            return _factory(value);
        }
    }
}
