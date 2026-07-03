using Robust.Shared.GameStates;

namespace Content.Shared._NC.Stats.Components;

/// <summary>
/// Stores the consumable Luck resource separately from the Luck base stat.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCLuckComponent : Component
{
    [DataField("current")]
    [AutoNetworkedField]
    public int Current;

    [DataField("max")]
    [AutoNetworkedField]
    public int Max;
}
