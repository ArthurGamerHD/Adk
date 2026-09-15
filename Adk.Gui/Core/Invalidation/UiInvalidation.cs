using System;

namespace Adk.Gui.Core.Invalidation
{
    [Flags]
    public enum UiInvalidation
    {
        None = 0,
        Render = 1,
        Arrange = 2,
        Measure = 4,
        Style = 8
    }

    public interface IUiInvalidatable
    {
        UiInvalidation PendingInvalidation { get; }
        void Invalidate(UiInvalidation invalidation);
    }
}
