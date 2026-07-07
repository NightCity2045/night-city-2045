using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Networked BODY-derived carried-weight state for a character.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(RaiseAfterAutoHandleState = true)]
public sealed partial class NCBodyComponent : Component
{
    [ViewVariables]
    [AutoNetworkedField]
    public float CurrentWeight;

    [ViewVariables]
    [AutoNetworkedField]
    public float MaxWeight;

    [ViewVariables]
    [AutoNetworkedField]
    public int Body;

    [ViewVariables]
    [AutoNetworkedField]
    public NCBodyLoadLevel Level = NCBodyLoadLevel.None;

    [DataField("ignoreLightLoad")]
    [AutoNetworkedField]
    public bool IgnoreLightLoad;
}
