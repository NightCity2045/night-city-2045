namespace Content.Shared._NC.Armor.Components;

/// <summary>
/// Ballistic GDD data for projectile-like entities.
/// </summary>
[RegisterComponent]
public sealed partial class BallisticProjectileComponent : Component
{
    [DataField]
    public float Damage;

    [DataField(required: true)]
    public float Penetration;
}
