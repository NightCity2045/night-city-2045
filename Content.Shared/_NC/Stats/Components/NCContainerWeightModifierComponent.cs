using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Reduces the carried weight contributed by items stored inside this container.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCContainerWeightModifierComponent : Component
{
    /// <summary>
    /// Multiplier applied to the total weight of the container contents.
    /// </summary>
    [DataField("contentWeightMultiplier")]
    [ViewVariables]
    [AutoNetworkedField]
    public float ContentWeightMultiplier = 1f;
}
