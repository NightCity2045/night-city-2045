using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.RTS.Components;

[Serializable, NetSerializable]
public enum RTSAggressionMode : byte
{
    Peaceful,
    Normal
}

/// <summary>
/// Stores the autonomous aggression mode for RTS drones. Systems swap faction
/// membership from this data instead of hardcoding faction names in C#.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RTSAggressionModeComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public RTSAggressionMode CurrentMode = RTSAggressionMode.Normal;

    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> PeacefulFactions = new();

    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> NormalFactions = new();
}
