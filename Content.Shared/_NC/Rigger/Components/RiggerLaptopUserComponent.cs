using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Rigger.Components;

/// <summary>
/// Runtime RTS state granted to a body while it is wielding a portable rigger laptop.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RiggerLaptopUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Laptop;

    [DataField, AutoNetworkedField]
    public List<EntityUid> LinkedDrones = new();

    [DataField]
    public EntProtoId ToggleRtsAction = "ActionNCRiggerToggleRTS";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleRtsActionEntity;

    [DataField, AutoNetworkedField]
    public bool RtsEnabled;

    [DataField]
    public List<EntityUid> SessionOverrides = new();
}
