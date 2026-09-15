using System;
using System.Collections.Generic;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Core
{
    /// <summary>Base styled control with pseudoclass and property state.</summary>
    public abstract class Control : FrameworkElement
    {
        public static readonly StyledProperty<float> WidthProperty =
            StyledProperty.Register<Control, float>(
                nameof(Width), float.NaN, UiInvalidation.Measure);
        public static readonly StyledProperty<float> HeightProperty =
            StyledProperty.Register<Control, float>(
                nameof(Height), float.NaN, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MinWidthProperty =
            StyledProperty.Register<Control, float>(
                nameof(MinWidth), 0f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MinHeightProperty =
            StyledProperty.Register<Control, float>(
                nameof(MinHeight), 0f, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MaxWidthProperty =
            StyledProperty.Register<Control, float>(
                nameof(MaxWidth), float.PositiveInfinity, UiInvalidation.Measure);
        public static readonly StyledProperty<float> MaxHeightProperty =
            StyledProperty.Register<Control, float>(
                nameof(MaxHeight), float.PositiveInfinity, UiInvalidation.Measure);
        public static readonly StyledProperty<Vector4> MarginProperty =
            StyledProperty.Register<Control, Vector4>(
                nameof(Margin), Vector4.Zero, UiInvalidation.Measure);
        public static readonly StyledProperty<HorizontalAlignment> HorizontalAlignmentProperty =
            StyledProperty.Register<Control, HorizontalAlignment>(
                nameof(HorizontalAlignment), HorizontalAlignment.Stretch, UiInvalidation.Arrange);
        public static readonly StyledProperty<VerticalAlignment> VerticalAlignmentProperty =
            StyledProperty.Register<Control, VerticalAlignment>(
                nameof(VerticalAlignment), VerticalAlignment.Stretch, UiInvalidation.Arrange);

        static Control() { }

        readonly PropertyStore _properties = new PropertyStore();
        readonly List<IDisposable> _lifetimes = new List<IDisposable>();
        string _styleClass = "default";
        StyleSheet _styles;
        StyleSheet _appliedStyles;
        string _appliedStyleClass;
        int _appliedStyleVersion = -1;
        readonly PseudoClassCollection _pseudoClasses;
        int _pseudoClassVersion;
        int _appliedPseudoClassVersion = -1;

        protected Control()
        {
            _pseudoClasses = new PseudoClassCollection(OnPseudoClassesChanged);
        }

        /// <summary>
        /// Optional identifier used by the nearest control-template namescope.
        /// Names are local to a single template instance.
        /// </summary>
        public string Name { get; set; }

        /// <summary>The control whose template created this control.</summary>
        public TemplatedControl TemplatedParent { get; internal set; }

        protected PseudoClassCollection PseudoClasses => _pseudoClasses;

        public override bool Enabled
        {
            get { return base.Enabled; }
            set
            {
                if (base.Enabled == value)
                    return;
                base.Enabled = value;
                PseudoClasses.Set(PseudoClassNames.Disabled, !value);
            }
        }

        public override float? Width
        {
            get
            {
                float value = GetValue(WidthProperty);
                return float.IsNaN(value) ? (float?)null : value;
            }
            set { SetValue(WidthProperty, value.HasValue ? value.Value : float.NaN); }
        }

        public override float? Height
        {
            get
            {
                float value = GetValue(HeightProperty);
                return float.IsNaN(value) ? (float?)null : value;
            }
            set { SetValue(HeightProperty, value.HasValue ? value.Value : float.NaN); }
        }

        public override float MinWidth
        {
            get { return GetValue(MinWidthProperty); }
            set { SetValue(MinWidthProperty, Math.Max(0f, value)); }
        }

        public override float MinHeight
        {
            get { return GetValue(MinHeightProperty); }
            set { SetValue(MinHeightProperty, Math.Max(0f, value)); }
        }

        public override float MaxWidth
        {
            get { return GetValue(MaxWidthProperty); }
            set
            {
                SetValue(MaxWidthProperty, float.IsNaN(value)
                    ? float.PositiveInfinity
                    : Math.Max(0f, value));
            }
        }

        public override float MaxHeight
        {
            get { return GetValue(MaxHeightProperty); }
            set
            {
                SetValue(MaxHeightProperty, float.IsNaN(value)
                    ? float.PositiveInfinity
                    : Math.Max(0f, value));
            }
        }

        public override Vector4 Margin
        {
            get { return GetValue(MarginProperty); }
            set { SetValue(MarginProperty, value); }
        }

        public override HorizontalAlignment HorizontalAlignment
        {
            get { return GetValue(HorizontalAlignmentProperty); }
            set { SetValue(HorizontalAlignmentProperty, value); }
        }

        public override VerticalAlignment VerticalAlignment
        {
            get { return GetValue(VerticalAlignmentProperty); }
            set { SetValue(VerticalAlignmentProperty, value); }
        }

        public string StyleClass
        {
            get { return _styleClass; }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
                if (string.Equals(_styleClass, next, StringComparison.Ordinal))
                    return;
                _styleClass = next;
                Invalidate(UiInvalidation.Style | UiInvalidation.Measure |
                           UiInvalidation.Arrange | UiInvalidation.Render);
            }
        }
        
        public StyleSheet Styles
        {
            get { return _styles; }
            set
            {
                if (ReferenceEquals(_styles, value))
                    return;
                _styles = value;
                Invalidate(UiInvalidation.Style | UiInvalidation.Measure |
                           UiInvalidation.Arrange | UiInvalidation.Render);
            }
        }

        protected StyleSheet ResolveStyleSheet()
        {
            Control current = this;
            while (current != null)
            {
                if (current._styles != null)
                    return current._styles;
                VisualElement visual = current as VisualElement;
                current = visual == null ? null : visual.Parent;
            }
            return StyleSheet.Application;
        }

        internal void EnsureStyleApplied()
        {
            UpdatePseudoClasses();
            StyleSheet styles = ResolveStyleSheet();
            if (ReferenceEquals(styles, _appliedStyles) && string.Equals(
                    StyleClass,
                    _appliedStyleClass,
                    StringComparison.Ordinal) &&
                _appliedStyleVersion == styles.Version &&
                _appliedPseudoClassVersion == _pseudoClassVersion)
            {
                TemplatedControl unchangedTemplateOwner = this as TemplatedControl;
                if (unchangedTemplateOwner != null)
                    unchangedTemplateOwner.EnsureTemplateApplied();
                return;
            }
            if (_appliedStyles != null)
                _appliedStyles.Reset(this);
            styles.Apply(this);
            _appliedStyles = styles;
            _appliedStyleClass = StyleClass;
            _appliedStyleVersion = styles.Version;
            _appliedPseudoClassVersion = _pseudoClassVersion;

            TemplatedControl templateOwner = this as TemplatedControl;
            if (templateOwner != null)
                templateOwner.EnsureTemplateApplied();
        }


        /// <summary>Returns whether a pseudoclass is currently active.</summary>
        public bool HasPseudoClass(string pseudoClass)
        {
            return _pseudoClasses.Contains(pseudoClass);
        }

        protected virtual void UpdatePseudoClasses()
        {
            PseudoClasses.Set(PseudoClassNames.Disabled, !Enabled);
        }

        void OnPseudoClassesChanged()
        {
            _pseudoClassVersion++;
            Invalidate(UiInvalidation.Style | UiInvalidation.Render);
        }

        public T GetValue<T>(StyledProperty<T> property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            return _properties.GetValue(property);
        }

        public void SetValue<T>(StyledProperty<T> property, T value)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            if (!_properties.SetValue(property, value))
                return;
            Invalidate(property.Invalidation);
            OnPropertyChanged(property);
        }

        public void ClearValue<T>(StyledProperty<T> property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            if (!_properties.ClearValue(property))
                return;
            Invalidate(property.Invalidation);
            OnPropertyChanged(property);
        }

        internal void SetStyleValue<T>(StyledProperty<T> property, Func<T> provider)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            _properties.SetStyleValue(property, provider);
            Invalidate(property.Invalidation);
            OnPropertyChanged(property);
        }

        internal void ClearStyleValues()
        {
            UiInvalidation invalidation = _properties.ClearStyleValues(OnPropertyChanged);
            if (invalidation != UiInvalidation.None)
                Invalidate(invalidation);
        }

        protected virtual void OnPropertyChanged(IStyledProperty property)
        {
            Action<Control, IStyledProperty> changed = StyledPropertyChanged;
            if (changed != null)
                changed(this, property);
        }

        internal event Action<Control, IStyledProperty> StyledPropertyChanged;

        internal void RegisterLifetime(IDisposable lifetime)
        {
            if (lifetime != null)
                _lifetimes.Add(lifetime);
        }

        public override void Dispose()
        {
            for (int i = 0; i < _lifetimes.Count; i++)
                _lifetimes[i].Dispose();
            _lifetimes.Clear();
            StyledPropertyChanged = null;
            base.Dispose();
        }
    }
}
