using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Stores all skills for an entity, including specialization-based variants.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(RaiseAfterAutoHandleState = true)]
public sealed partial class NCSkillsComponent : Component
{
    [DataField("skills")]
    [AutoNetworkedField]
    public List<NCSkillEntry> Skills = new();

    /// <summary>
    /// Transient O(1) lookup cache mapping skill ID to FinalValue.
    /// Rebuilt by SharedNCStatsSystem on MapInit and every recalculation.
    /// Not networked — each side rebuilds it locally.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> SkillCache = new();
}
