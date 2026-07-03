using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Rigger.Components;

/// <summary>
/// Console that opens a remote rigger session and keeps track of linked drones.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RiggerConsoleComponent : Component
{
    [DataField]
    public List<EntityUid> LinkedDrones = new();

    [DataField]
    public float AutoLinkRange = 20f;

    [DataField]
    public EntProtoId EyePrototype = "NCRiggerEye";

    [DataField, AutoNetworkedField]
    public EntityUid? User;

    [DataField, AutoNetworkedField]
    public EntityUid? ActiveEye;
}
