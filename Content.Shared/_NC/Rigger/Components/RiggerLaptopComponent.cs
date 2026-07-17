using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Rigger.Components;

/// <summary>
/// Portable rigger controller used from the operator's hands instead of a fixed console.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RiggerLaptopComponent : Component
{
    [DataField]
    public float AutoLinkRange = 20f;

    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> AllowedDroneFactions = new();

    [DataField]
    public EntProtoId ToggleRtsAction = "ActionNCRiggerToggleRTS";
}
