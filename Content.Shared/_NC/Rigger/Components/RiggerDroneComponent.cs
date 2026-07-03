using Robust.Shared.GameStates;

namespace Content.Shared._NC.Rigger.Components;

/// <summary>
/// Marks a drone that can be controlled through a rigger console.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RiggerDroneComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Console;

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public bool Occluded = true;

    [DataField, AutoNetworkedField]
    public float VisionRange = 7.5f;
}
