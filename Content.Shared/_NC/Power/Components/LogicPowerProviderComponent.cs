using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Power.Components;

/// <summary>
/// Component for logical low-voltage power providers (APCs).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LogicPowerProviderComponent : Component
{
    /// <summary>
    /// List of receivers logically connected to this provider.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), DataField("receivers")]
    public List<EntityUid> Receivers = new();
}

[Serializable, NetSerializable]
public sealed class LogicPowerProviderComponentState : ComponentState
{
    public List<NetEntity> Receivers = new();
}
