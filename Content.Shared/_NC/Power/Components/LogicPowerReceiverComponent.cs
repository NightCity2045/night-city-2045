using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Power.Components;

/// <summary>
/// Component for logical low-voltage power receivers.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LogicPowerReceiverComponent : Component
{
    /// <summary>
    /// The APC that this receiver is logically connected to.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("provider")]
    public EntityUid? Provider;

    /// <summary>
    /// Amount of power this receiver requires in Watts.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("powerLoad")]
    public float PowerLoad = 5f;

    /// <summary>
    /// Whether this receiver is currently powered.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Powered = false;
}

[Serializable, NetSerializable]
public sealed class LogicPowerReceiverComponentState : ComponentState
{
    public NetEntity? Provider;
    public bool Powered;
    public float PowerLoad;
}
