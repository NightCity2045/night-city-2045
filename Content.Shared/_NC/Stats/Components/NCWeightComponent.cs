using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Explicit Night City carried weight in kilograms.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCWeightComponent : Component
{
    [DataField("weight")]
    [ViewVariables]
    [AutoNetworkedField]
    public float Weight;
}
