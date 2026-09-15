using System;
using System.Collections.Generic;

namespace Adk.Gui.Core.PropertySystem
{
    /// <summary>
    /// Runtime catalog populated directly by StyledProperty.Register. Style-owning
    /// controls use explicit static constructors so the CLR initializes their property
    /// fields before the first instance is created.
    /// </summary>
    public static class StyledPropertyCatalog
    {
        static readonly List<IStyledProperty> Properties = new List<IStyledProperty>();

        internal static event Action<IStyledProperty> Registered;

        internal static IList<IStyledProperty> Snapshot()
        {
            return new List<IStyledProperty>(Properties);
        }

        internal static void Register(IStyledProperty property)
        {
            if (property == null || Properties.Contains(property))
                return;
            Properties.Add(property);
            Action<IStyledProperty> registered = Registered;
            if (registered != null)
                registered(property);
        }
    }
}
