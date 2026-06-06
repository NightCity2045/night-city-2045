using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.CitiNet.Store;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CitiNetStoreComponent : Component
{
    /// <summary>
    /// Tracks remaining stock for items. 
    /// Key: PrototypeId of the Entity.
    /// Value: Current stock count.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Stock = new();
}
