using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Networked pursuit state for a demon whose target is selected by META.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetDemonControllerComponent : Component
{
    [DataField("target"), AutoNetworkedField]
    public EntityUid? Target;
}
