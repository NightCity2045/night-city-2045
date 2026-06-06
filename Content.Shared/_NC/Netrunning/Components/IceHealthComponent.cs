using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IceHealthComponent : Component
{
    [DataField("maxHealth"), AutoNetworkedField]
    public int MaxHealth = 100;

    [DataField("currentHealth"), AutoNetworkedField]
    public int CurrentHealth = 100;
}
