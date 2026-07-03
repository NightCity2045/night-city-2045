using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Rigger.Components;

/// <summary>
/// Runtime state stored on the temporary rigger eye while a console session is active.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RiggerConsoleUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Console;

    [DataField, AutoNetworkedField]
    public EntityUid OriginalBody;

    [DataField, AutoNetworkedField]
    public EntityUid? LinkedMind;

    [DataField, AutoNetworkedField]
    public List<EntityUid> LinkedDrones = new();

    [DataField]
    public EntProtoId ExitAction = "ActionNCRiggerExitConsole";

    [DataField]
    public EntProtoId ToggleRtsAction = "ActionNCRiggerToggleRTS";

    [DataField]
    public EntProtoId DroneStatusAction = "ActionNCRiggerDroneStatus";

    [DataField, AutoNetworkedField]
    public EntityUid? ExitActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleRtsActionEntity;

    [DataField, AutoNetworkedField]
    public EntityUid? DroneStatusActionEntity;

    [DataField, AutoNetworkedField]
    public bool RtsEnabled;

    [DataField]
    public List<EntityUid> SessionOverrides = new();
}
