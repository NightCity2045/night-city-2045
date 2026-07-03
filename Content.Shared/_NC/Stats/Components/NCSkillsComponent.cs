using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Stores all skills for an entity, including specialization-based variants.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCSkillsComponent : Component
{
    [DataField("skills")]
    [AutoNetworkedField]
    public List<NCSkillEntry> Skills = new();
}
