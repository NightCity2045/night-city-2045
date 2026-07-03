using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._NC.Weapons.Ranged.SlowOnShoot;

/// <summary>
/// Added to a player when they shoot a gun with SlowOnShootComponent.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShootingSlowdownComponent : Component
{
    [DataField("walkModifier")]
    public float WalkModifier = 1.0f;

    [DataField("sprintModifier")]
    public float SprintModifier = 1.0f;

    [DataField("endTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan EndTime;
}
