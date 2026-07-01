namespace Content.Shared._NC.Armor.Components;

/// <summary>
/// Marks a projectile-like entity with the penetration class used by NC layered armor.
/// </summary>
[RegisterComponent]
public sealed partial class NCPenetrationComponent : Component
{
    [DataField(required: true)]
    public int Penetration;
}
