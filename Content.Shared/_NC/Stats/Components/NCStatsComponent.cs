using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Stores all base stats for an entity.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCStatsComponent : Component
{
    [DataField("stats")]
    [AutoNetworkedField]
    public List<NCStatEntry> Stats = new();
}
