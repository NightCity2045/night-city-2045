using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Power.Components;

/// <summary>
/// Component for the LV wire spool tool.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WireSpoolComponent : Component
{
    /// <summary>
    /// The current APC that the spool is linked to, waiting for a receiver connection.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("activeProvider")]
    public EntityUid? ActiveProvider;

    /// <summary>
    /// Maximum distance for a logical link.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("maxDistance")]
    public float MaxDistance = 15f;
}

[Serializable, NetSerializable]
public sealed class WireSpoolComponentState : ComponentState
{
    public NetEntity? ActiveProvider;
}
