using System;
using System.Collections.Generic;
using Adk.Gui.Core.Binding;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using VRageMath;

namespace Adk.Gui.Core
{
    /// <summary>
    /// Retained visual node shared by renderer hosts. It owns children, bindings,
    /// hit testing, and routed input while drawing through RenderContext.
    /// </summary>
    public abstract class VisualElement : Control, IControlContainer
    {
        readonly List<VisualElement> _logicalChildren = new List<VisualElement>();
        readonly List<IBind> _bindings = new List<IBind>();
        object _dataContext;
        ITextMetrics _textMetrics;
        public VisualElement Parent { get; private set; }
        protected override IUiInvalidatable InvalidationParent => Parent;
        public IReadOnlyList<VisualElement> LogicalChildren => _logicalChildren;
        public virtual IReadOnlyList<VisualElement> VisualChildren => _logicalChildren;
        protected ITextMetrics TextMetrics => _textMetrics;

        internal void AttachTextMetrics(ITextMetrics textMetrics)
        {
            _textMetrics = textMetrics;
            for (int i = 0; i < _logicalChildren.Count; i++)
                _logicalChildren[i].AttachTextMetrics(textMetrics);
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
        }

        public virtual VisualElement SetEnabled(bool enabled)
        {
            Enabled = enabled;
            return this;
        }

        public virtual void SetRect(RectangleF bounds)
        {
            Arrange(bounds);
        }
        bool _isPointerOver;
        bool _isPressed;
        bool _isFocused;
        bool _isFocusWithin;
        bool _isFocusVisible;

        public bool IsPointerOver
        {
            get { return _isPointerOver; }
            internal set
            {
                if (_isPointerOver == value) return;
                _isPointerOver = value;
                PseudoClasses.Set(PseudoClassNames.PointerOver, value);
            }
        }
        public bool IsPressed
        {
            get { return _isPressed; }
            internal set { _isPressed = value; }
        }
        public bool IsFocused
        {
            get { return _isFocused; }
            internal set
            {
                if (_isFocused == value) return;
                _isFocused = value;
                PseudoClasses.Set(PseudoClassNames.Focus, value);
            }
        }
        public bool IsFocusWithin
        {
            get { return _isFocusWithin; }
            internal set
            {
                if (_isFocusWithin == value) return;
                _isFocusWithin = value;
                PseudoClasses.Set(PseudoClassNames.FocusWithin, value);
            }
        }
        public bool IsFocusVisible
        {
            get { return _isFocusVisible; }
            internal set
            {
                if (_isFocusVisible == value) return;
                _isFocusVisible = value;
                PseudoClasses.Set(PseudoClassNames.FocusVisible, value);
            }
        }
        public virtual bool AcceptsKeyboardFocus => false;
        public bool IsEffectivelyEnabled
        {
            get
            {
                for (VisualElement current = this; current != null; current = current.Parent)
                    if (!current.Enabled)
                        return false;
                return true;
            }
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(PseudoClassNames.Disabled, !IsEffectivelyEnabled);
        }

        public object DataContext
        {
            get { return _dataContext; }
            set
            {
                if (ReferenceEquals(_dataContext, value))
                    return;
                _dataContext = value;
                for (int i = 0; i < _bindings.Count; i++)
                    _bindings[i].Attach(value as ObservableObject);
            }
        }

        public Bind<T> Bind<T>(Action<T> setter, string propertyName)
        {
            var binding = new Bind<T>(setter, propertyName);
            _bindings.Add(binding);
            binding.Attach(_dataContext as ObservableObject);
            return binding;
        }

        public T Add<T>(T child) where T : VisualElement
        {
            return AddChild(child);
        }

        void IControlContainer.AddTemplateChild(Control child)
        {
            VisualElement element = child as VisualElement;
            if (element == null)
                throw new InvalidOperationException(
                    "The Minimap compatibility tree only accepts VisualElement children.");
            AddChild(element);
        }

        public T AddChild<T>(T child) where T : VisualElement
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));
            if (ReferenceEquals(child, this))
                throw new InvalidOperationException("A visual element cannot contain itself.");
            for (VisualElement parent = this; parent != null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, child))
                    throw new InvalidOperationException("Adding the child would create a cycle.");
            }

            ValidateChildForAdd(child);

            if (ReferenceEquals(child.Parent, this))
            {
                if (!_logicalChildren.Contains(child))
                {
                    _logicalChildren.Add(child);
                    Invalidate(UiInvalidation.Measure);
                }
                return child;
            }
            if (child.Parent != null)
                child.Parent.RemoveChild(child);

            child.Parent = this;
            child.AttachTextMetrics(_textMetrics);
            _logicalChildren.Add(child);
            Invalidate(UiInvalidation.Measure);
            return child;
        }

        protected virtual void ValidateChildForAdd(VisualElement child)
        {
        }

        public void AddChildren(IEnumerable<VisualElement> children)
        {
            if (children == null)
                return;
            foreach (VisualElement child in children)
                AddChild(child);
        }

        public virtual bool RemoveChild(VisualElement child)
        {
            if (child == null || !_logicalChildren.Remove(child))
                return false;
            if (ReferenceEquals(child.Parent, this))
                child.Parent = null;
            Invalidate(UiInvalidation.Measure);
            return true;
        }

        public virtual void ClearChildren()
        {
            for (int i = 0; i < _logicalChildren.Count; i++)
            {
                VisualElement child = _logicalChildren[i];
                if (child != null && ReferenceEquals(child.Parent, this))
                    child.Parent = null;
            }
            if (_logicalChildren.Count == 0)
                return;
            _logicalChildren.Clear();
            Invalidate(UiInvalidation.Measure);
        }

        public bool MoveChild(VisualElement child, int index)
        {
            if (child == null || !ReferenceEquals(child.Parent, this))
                return false;
            int current = _logicalChildren.IndexOf(child);
            if (current < 0)
                return false;
            int target = Math.Max(0, Math.Min(index, _logicalChildren.Count - 1));
            if (current == target)
                return false;
            _logicalChildren.RemoveAt(current);
            _logicalChildren.Insert(target, child);
            Invalidate(UiInvalidation.Arrange);
            return true;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            float width = 0f;
            float height = 0f;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                Vector2 desired = child.Measure(availableSize);
                width = Math.Max(width, Math.Max(0f, desired.X));
                height = Math.Max(height, Math.Max(0f, desired.Y));
            }
            return new Vector2(width, height);
        }

        internal VisualElement HitTest(Vector2 point)
        {
            EnsureStyleApplied();
            if (!Visible || !Enabled || !Bounds.Contains(point))
                return null;

            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                VisualElement hit = children[i].HitTest(point);
                if (hit != null)
                    return hit;
            }

            return HitTestSelf(point) ? this : null;
        }

        protected virtual bool HitTestSelf(Vector2 point)
        {
            return false;
        }

        internal void DrawTree(RenderContext context)
        {
            EnsureStyleApplied();
            if (!Visible)
                return;

            DrawSelf(context);
            ClearInvalidation(UiInvalidation.Render | UiInvalidation.Style);
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                IDisposable childDrawScope = BeginChildDraw(context, child);
                try
                {
                    children[i].DrawTree(context);
                }
                finally
                {
                    if (childDrawScope != null)
                        childDrawScope.Dispose();
                }
            }
        }

        protected virtual IDisposable BeginChildDraw(
            RenderContext context,
            VisualElement child)
        {
            return null;
        }

        protected virtual void DrawSelf(RenderContext context)
        {
        }

        internal virtual void PointerPressed(Vector2 point)
        {
        }

        internal virtual void PointerMoved(Vector2 point, Vector2 delta)
        {
        }

        internal virtual void PointerReleased(Vector2 point, bool clicked)
        {
        }

        internal virtual void PointerHovered(Vector2 point)
        {
        }

        internal virtual bool PointerScrolled(Vector2 point, int delta)
        {
            return false;
        }

        internal virtual void KeyboardInput(IInputSource input)
        {
        }

        internal virtual void MiddlePointerPressed(Vector2 point)
        {
        }

        internal virtual void MiddlePointerMoved(Vector2 point, Vector2 delta)
        {
        }

        internal virtual void MiddlePointerReleased(Vector2 point)
        {
        }

        internal virtual void SecondaryPointerPressed(Vector2 point)
        {
        }

        internal virtual void SecondaryPointerMoved(Vector2 point, Vector2 delta)
        {
        }

        internal virtual void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
        }

        public override void Dispose()
        {
            for (int i = 0; i < _bindings.Count; i++)
                _bindings[i].Dispose();
            _bindings.Clear();
            _dataContext = null;
            for (int i = 0; i < _logicalChildren.Count; i++)
                _logicalChildren[i].Dispose();
            _logicalChildren.Clear();
            Parent = null;
            base.Dispose();
        }
    }
}
