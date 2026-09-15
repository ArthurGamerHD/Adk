using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;
using IMyInput = VRage.ModAPI.IMyInput;

namespace Adk.Gui.ScreenSpace
{
    public sealed class ScreenSpaceInputSource : IInputSource
    {
        static readonly char[] EmptyTextInput = Array.Empty<char>();
        readonly IMyInput _input;

        public ScreenSpaceInputSource(IMyInput input)
        {
            _input = input;
        }

        public IEnumerable<char> TextInput
        {
            get
            {
                if (_input == null)
                    return EmptyTextInput;
                return _input.TextInput;
            }
        }

        public bool TryGetPointerPosition(out Vector2 position)
        {
            position = Vector2.Zero;
            return _input != null && MyScreenSpace.TryGetCursorPositionPixels(out position);
        }

        public int DeltaMouseScrollWheelValue()
        {
            return _input == null ? 0 : _input.DeltaMouseScrollWheelValue();
        }

        public bool IsPrimaryButtonPressed() =>
            _input != null && _input.IsPrimaryButtonPressed();
        public bool IsNewPrimaryButtonPressed() =>
            _input != null && _input.IsNewPrimaryButtonPressed();
        public bool IsNewPrimaryButtonReleased() =>
            _input != null && _input.IsNewPrimaryButtonReleased();
        public bool IsMiddleButtonPressed() =>
            _input != null && _input.IsMiddleMousePressed();
        public bool IsNewMiddleButtonPressed() =>
            _input != null && _input.IsNewMiddleMousePressed();
        public bool IsNewMiddleButtonReleased() =>
            _input != null && _input.IsNewMiddleMouseReleased();
        public bool IsSecondaryButtonPressed() =>
            _input != null && _input.IsSecondaryButtonPressed();
        public bool IsNewSecondaryButtonPressed() =>
            _input != null && _input.IsNewSecondaryButtonPressed();
        public bool IsNewSecondaryButtonReleased() =>
            _input != null && _input.IsNewSecondaryButtonReleased();
        public bool IsKeyPressed(MyKeys key) =>
            _input != null && _input.IsKeyPress(key);
        public bool IsNewKeyPressed(MyKeys key) =>
            _input != null && _input.IsNewKeyPressed(key);

        public void GetPressedKeys(List<MyKeys> keys)
        {
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            if (_input != null)
                _input.GetPressedKeys(keys);
        }
    }
}
