using System.Collections.Generic;
using VRage.Input;
using VRageMath;

namespace Adk.Gui.Core
{
    public interface IInputSource
    {
        IEnumerable<char> TextInput { get; }

        bool TryGetPointerPosition(out Vector2 position);
        int DeltaMouseScrollWheelValue();
        bool IsPrimaryButtonPressed();
        bool IsNewPrimaryButtonPressed();
        bool IsNewPrimaryButtonReleased();
        bool IsMiddleButtonPressed();
        bool IsNewMiddleButtonPressed();
        bool IsNewMiddleButtonReleased();
        bool IsSecondaryButtonPressed();
        bool IsNewSecondaryButtonPressed();
        bool IsNewSecondaryButtonReleased();
        bool IsKeyPressed(MyKeys key);
        bool IsNewKeyPressed(MyKeys key);
        void GetPressedKeys(List<MyKeys> keys);
    }
}
