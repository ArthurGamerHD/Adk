using System;
using Adk.Gui.Core.Invalidation;

namespace Adk.Gui.Core.PropertySystem
{
    public interface IStyledProperty
    {
        string Name { get; }
        Type OwnerType { get; }
        Type ValueType { get; }
        object DefaultValueObject { get; }
        UiInvalidation Invalidation { get; }
        bool Matches(Control control);
        object GetValueObject(Control control);
        void ApplyStyle(Control control, object value, IStyleValueSource source);
    }

    public interface IStyleValueSource
    {
        Func<T> ConvertProvider<T>(object value);
    }

    public sealed class StyledProperty<T> : IStyledProperty
    {
        internal StyledProperty(
            string name,
            Type ownerType,
            T defaultValue,
            UiInvalidation invalidation,
            Func<Control, bool> matches)
        {
            Name = name;
            OwnerType = ownerType;
            DefaultValue = defaultValue;
            Invalidation = invalidation;
            _matches = matches;
        }

        readonly Func<Control, bool> _matches;

        public string Name { get; }
        public Type OwnerType { get; }
        public Type ValueType => typeof(T);
        public T DefaultValue { get; }
        public object DefaultValueObject => DefaultValue;
        public UiInvalidation Invalidation { get; }

        public bool Matches(Control control)
        {
            return control != null && _matches(control);
        }

        public object GetValueObject(Control control)
        {
            if (!Matches(control))
                throw new InvalidOperationException(
                    control.GetType().Name + " cannot use " + OwnerType.Name + "." + Name + ".");
            return control.GetValue(this);
        }

        public void ApplyStyle(Control control, object value, IStyleValueSource source)
        {
            if (control == null)
                throw new ArgumentNullException(nameof(control));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (!Matches(control))
                throw new InvalidOperationException(
                    control.GetType().Name + " cannot use " + OwnerType.Name + "." + Name + ".");
            control.SetStyleValue(this, source.ConvertProvider<T>(value));
        }
    }

    public static class StyledProperty
    {
        public static StyledProperty<T> Register<TOwner, T>(
            string name,
            T defaultValue,
            UiInvalidation invalidation = UiInvalidation.Render)
            where TOwner : Control
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A styled property requires a name.", nameof(name));
            var property = new StyledProperty<T>(
                name,
                typeof(TOwner),
                defaultValue,
                invalidation,
                control => control is TOwner);
            StyledPropertyCatalog.Register(property);
            return property;
        }

        /// <summary>
        /// Registers an owner-qualified property that can be attached to any control
        /// assignable to TTarget.  The qualified style name mirrors Avalonia/XAML
        /// attached-property syntax, e.g. TextOptions.DropShadow.
        /// </summary>
        public static StyledProperty<T> RegisterAttached<TTarget, T>(
            Type attachedOwnerType,
            string name,
            T defaultValue,
            UiInvalidation invalidation = UiInvalidation.Render)
            where TTarget : Control
        {
            if (attachedOwnerType == null)
                throw new ArgumentNullException(nameof(attachedOwnerType));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An attached styled property requires a name.", nameof(name));

            string qualifiedName = attachedOwnerType.Name + "." + name;
            var property = new StyledProperty<T>(
                qualifiedName,
                typeof(TTarget),
                defaultValue,
                invalidation,
                control => control is TTarget);
            StyledPropertyCatalog.Register(property);
            return property;
        }
    }
}
