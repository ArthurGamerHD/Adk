using System;
using System.Collections.Generic;
using Adk.Gui.Rendering;
using VRageMath;

namespace Adk.Gui.Core
{
    internal interface IOverlayProvider
    {
        bool IsOverlayOpen { get; }
        VisualElement OverlayElement { get; }
        void UpdateOverlayLayout();
        void CloseOverlay();
    }

    internal interface IModalOverlayProvider
    {
        void HandleOverlayInput(IInputSource input);
    }

    public sealed class VisualTree
    {
        readonly VisualElement _root;
        readonly IUiRenderer _renderer;
        VisualElement _hovered;
        VisualElement _pressed;
        VisualElement _middlePressed;
        VisualElement _secondaryPressed;
        VisualElement _focused;
        readonly List<VisualElement> _pointerOverPath = new List<VisualElement>();
        readonly List<VisualElement> _pressedPath = new List<VisualElement>();
        readonly List<VisualElement> _focusWithinPath = new List<VisualElement>();
        Vector2 _lastPointer;
        bool _hasLastPointer;

        public VisualTree(VisualElement root, IUiRenderer renderer)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (renderer == null)
                throw new ArgumentNullException(nameof(renderer));
            _root = root;
            _renderer = renderer;
            _root.AttachTextMetrics(_renderer.TextMetrics);
        }

        public VisualElement Root => _root;
        public VisualElement FocusedElement => _focused;
        public bool HasKeyboardFocus => _focused != null && _focused.Visible && _focused.IsEffectivelyEnabled;

        public void Draw(float transitionAlpha)
        {
            var context = new RenderContext(_renderer, transitionAlpha);
            _root.DrawTree(context);

            IOverlayProvider overlay = FindOpenOverlay(_root);
            if (overlay != null)
            {
                overlay.UpdateOverlayLayout();
                VisualElement element = overlay.OverlayElement;
                if (element != null)
                    element.DrawTree(context);
            }
        }

        public void HandleInput(IInputSource input)
        {
            if (_focused != null && (!_focused.Visible || !_focused.IsEffectivelyEnabled))
                SetFocus(null, false);

            Vector2 pointer;
            if (input == null || !input.TryGetPointerPosition(out pointer))
                return;

            Vector2 delta = _hasLastPointer ? pointer - _lastPointer : Vector2.Zero;
            _lastPointer = pointer;
            _hasLastPointer = true;

            IOverlayProvider overlay = FindOpenOverlay(_root);
            if (overlay != null)
                overlay.UpdateOverlayLayout();

            VisualElement overlayElement = overlay == null ? null : overlay.OverlayElement;
            VisualElement overlayHit = overlayElement == null ? null : overlayElement.HitTest(pointer);
            VisualElement hit = overlayHit ?? _root.HitTest(pointer);
            if (!ReferenceEquals(hit, _hovered))
            {
                SetPointerOverPath(hit);
                _hovered = hit;
            }
            if (hit != null)
                hit.PointerHovered(pointer);

            if (input.IsNewPrimaryButtonPressed())
            {
                if (overlay != null && overlayHit == null)
                {
                    overlay.CloseOverlay();
                    ClearPointerState();
                    return;
                }

                SetFocus(FindFocusable(hit), false);
                _pressed = hit;
                SetPressedPath(_pressed);
                if (_pressed != null)
                    _pressed.PointerPressed(pointer);
            }

            if (_pressed != null && input.IsPrimaryButtonPressed())
                _pressed.PointerMoved(pointer, delta);

            if (input.IsNewPrimaryButtonReleased())
            {
                VisualElement pressed = _pressed;
                _pressed = null;
                ClearPressedPath();
                if (pressed != null)
                    pressed.PointerReleased(pointer, ReferenceEquals(pressed, hit));
            }

            if (input.IsNewMiddleButtonPressed())
            {
                _middlePressed = hit;
                if (_middlePressed != null)
                    _middlePressed.MiddlePointerPressed(pointer);
            }

            if (_middlePressed != null && input.IsMiddleButtonPressed())
                _middlePressed.MiddlePointerMoved(pointer, delta);

            if (input.IsNewMiddleButtonReleased())
            {
                VisualElement middlePressed = _middlePressed;
                _middlePressed = null;
                if (middlePressed != null)
                    middlePressed.MiddlePointerReleased(pointer);
            }

            if (input.IsNewSecondaryButtonPressed())
            {
                _secondaryPressed = hit;
                if (_secondaryPressed != null)
                    _secondaryPressed.SecondaryPointerPressed(pointer);
            }

            if (_secondaryPressed != null && input.IsSecondaryButtonPressed())
                _secondaryPressed.SecondaryPointerMoved(pointer, delta);

            if (input.IsNewSecondaryButtonReleased())
            {
                VisualElement secondaryPressed = _secondaryPressed;
                _secondaryPressed = null;
                if (secondaryPressed != null)
                    secondaryPressed.SecondaryPointerReleased(
                        pointer,
                        ReferenceEquals(secondaryPressed, hit));
            }

            int wheel = input.DeltaMouseScrollWheelValue();
            if (wheel != 0 && hit != null)
            {
                for (VisualElement current = hit; current != null; current = current.Parent)
                {
                    if (current.PointerScrolled(pointer, wheel))
                        break;
                }
            }

            if (overlay != null && input.IsNewKeyPressed(VRage.Input.MyKeys.Escape))
            {
                overlay.CloseOverlay();
                return;
            }

            IModalOverlayProvider modalOverlay = overlay as IModalOverlayProvider;
            if (modalOverlay != null)
            {
                modalOverlay.HandleOverlayInput(input);
                return;
            }

            if (_focused != null && _focused.Visible && _focused.IsEffectivelyEnabled)
                _focused.KeyboardInput(input);
        }

        static IOverlayProvider FindOpenOverlay(VisualElement element)
        {
            if (element == null || !element.Visible || !element.Enabled)
                return null;

            IReadOnlyList<VisualElement> children = element.LogicalChildren;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                IOverlayProvider nested = FindOpenOverlay(children[i]);
                if (nested != null)
                    return nested;
            }

            IOverlayProvider provider = element as IOverlayProvider;
            return provider != null && provider.IsOverlayOpen ? provider : null;
        }

        void ClearPointerState()
        {
            ClearPointerOverPath();
            ClearPressedPath();
            _hovered = null;
            _pressed = null;
            _middlePressed = null;
            _secondaryPressed = null;
        }

        static VisualElement FindFocusable(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.Parent)
            {
                if (current.AcceptsKeyboardFocus)
                    return current;
            }
            return null;
        }

        public bool Focus(VisualElement element, bool focusVisible = true)
        {
            if (element != null && (!element.Visible || !element.IsEffectivelyEnabled ||
                                    !element.AcceptsKeyboardFocus))
                return false;
            SetFocus(element, focusVisible);
            return true;
        }

        void SetFocus(VisualElement element, bool focusVisible)
        {
            if (ReferenceEquals(_focused, element))
            {
                if (_focused != null)
                    _focused.IsFocusVisible = focusVisible;
                return;
            }
            if (_focused != null)
            {
                _focused.IsFocused = false;
                _focused.IsFocusVisible = false;
            }
            ClearFocusWithinPath();
            _focused = element;
            if (_focused != null)
            {
                _focused.IsFocused = true;
                _focused.IsFocusVisible = focusVisible;
                SetFocusWithinPath(_focused);
            }
        }

        void SetPointerOverPath(VisualElement element)
        {
            ClearPointerOverPath();
            for (VisualElement current = element; current != null; current = current.Parent)
            {
                current.IsPointerOver = true;
                _pointerOverPath.Add(current);
            }
        }

        void ClearPointerOverPath()
        {
            for (int i = 0; i < _pointerOverPath.Count; i++)
                _pointerOverPath[i].IsPointerOver = false;
            _pointerOverPath.Clear();
        }

        void SetPressedPath(VisualElement element)
        {
            ClearPressedPath();
            for (VisualElement current = element; current != null; current = current.Parent)
            {
                current.IsPressed = true;
                _pressedPath.Add(current);
            }
        }

        void ClearPressedPath()
        {
            for (int i = 0; i < _pressedPath.Count; i++)
                _pressedPath[i].IsPressed = false;
            _pressedPath.Clear();
        }

        void SetFocusWithinPath(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.Parent)
            {
                current.IsFocusWithin = true;
                _focusWithinPath.Add(current);
            }
        }

        void ClearFocusWithinPath()
        {
            for (int i = 0; i < _focusWithinPath.Count; i++)
                _focusWithinPath[i].IsFocusWithin = false;
            _focusWithinPath.Clear();
        }

        public void ResetInput()
        {
            ClearPointerOverPath();
            ClearPressedPath();
            _hovered = null;
            _pressed = null;
            _middlePressed = null;
            _secondaryPressed = null;
            SetFocus(null, false);
            _hasLastPointer = false;
        }
    }
}
