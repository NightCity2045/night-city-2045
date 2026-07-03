using Robust.Shared.GameStates;

namespace Content.Shared._NC.Weapons.Ranged.SlowOnShoot;

/// <summary>
/// When a gun with this component is fired, it applies a temporary movement slowdown to the user.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlowOnShootComponent : Component
{
    [DataField("walkModifier")]
    public float WalkModifier = 0.5f;

    [DataField("sprintModifier")]
    public float SprintModifier = 0.5f;

    /// <summary>
    /// How long the slowdown lasts after a shot.
    /// </summary>
    [DataField("duration")]
    public TimeSpan Duration = TimeSpan.FromSeconds(0.5);
}
