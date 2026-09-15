using System;
using System.Collections.Generic;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Core
{
    /// <summary>
    /// A control whose visual children are supplied by a style ControlTemplate.
    /// Each instance owns an independent template tree and namescope.
    /// </summary>
    public abstract class TemplatedControl : VisualElement
    {
        static TemplatedControl() { }

        public static readonly StyledProperty<ControlTemplate> TemplateProperty =
            StyledProperty.Register<TemplatedControl, ControlTemplate>(
                nameof(Template), null, UiInvalidation.Measure);

        ControlTemplate _appliedTemplate;
        VisualElement _templateRoot;
        IDictionary<string, Control> _templateNames =
            new Dictionary<string, Control>(StringComparer.Ordinal);
        bool _applyingTemplate;

        public ControlTemplate Template
        {
            get { return GetValue(TemplateProperty); }
            set { SetValue(TemplateProperty, value); }
        }

        protected VisualElement TemplateRoot => _templateRoot;
        protected bool IsApplyingTemplate => _applyingTemplate;

        internal void EnsureTemplateApplied()
        {
            ControlTemplate template = Template;
            if (ReferenceEquals(template, _appliedTemplate))
                return;

            OnTemplateChanging();
            VisualElement previous = _templateRoot;
            _templateRoot = null;
            _templateNames = new Dictionary<string, Control>(StringComparer.Ordinal);
            if (previous != null)
            {
                base.RemoveChild(previous);
                previous.Dispose();
            }

            _appliedTemplate = template;
            if (template != null)
            {
                ControlTemplateInstance instance = template.BuildInstance(this);
                _templateRoot = instance.Root;
                _templateNames = instance.Names;
                if (_templateRoot != null)
                {
                    _applyingTemplate = true;
                    try
                    {
                        base.AddChild(_templateRoot);
                    }
                    finally
                    {
                        _applyingTemplate = false;
                    }
                }
            }

            OnApplyTemplate();
            Invalidate(UiInvalidation.Measure | UiInvalidation.Arrange | UiInvalidation.Render);
        }

        public Control GetTemplateChild(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            EnsureStyleApplied();
            Control value;
            return _templateNames.TryGetValue(name.Trim(), out value) ? value : null;
        }

        protected T GetTemplateChild<T>(string name) where T : Control
        {
            return GetTemplateChild(name) as T;
        }

        protected virtual void OnApplyTemplate()
        {
        }

        /// <summary>
        /// Lets content controls detach user-owned content before the old template
        /// visual tree is disposed.
        /// </summary>
        protected virtual void OnTemplateChanging()
        {
        }

        protected void ArrangeTemplate(RectangleF bounds)
        {
            if (_templateRoot != null && _templateRoot.Visible)
                _templateRoot.ArrangeInSlot(bounds);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            ArrangeTemplate(bounds);
        }

        protected override void ValidateChildForAdd(VisualElement child)
        {
            if (_applyingTemplate && ReferenceEquals(child, _templateRoot))
                return;
            base.ValidateChildForAdd(child);
        }

        public override bool RemoveChild(VisualElement child)
        {
            if (ReferenceEquals(child, _templateRoot) && !_applyingTemplate)
                return false;
            return base.RemoveChild(child);
        }

        public override void ClearChildren()
        {
            // Template visuals are owned by Template. Derived content controls clear
            // only their logical content through their own content property.
        }
    }
}
