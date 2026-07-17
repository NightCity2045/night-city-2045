using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Stores all base stats for an entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(RaiseAfterAutoHandleState = true)]
public sealed partial class NCStatsComponent : Component
{
    [DataField("stats")]
    [AutoNetworkedField]
    public List<NCStatEntry> Stats = new();

    /// <summary>
    /// Transient O(1) lookup cache mapping stat ID to FinalValue.
    /// Rebuilt by SharedNCStatsSystem on MapInit and every recalculation.
    /// Not networked — each side rebuilds it locally.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> StatCache = new();
}
