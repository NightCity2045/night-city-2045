using Robust.Shared.GameStates;

namespace Content.Shared._NC.Atmos;

/// <summary>
/// A simplified Tile-Based fire component designed for use in Night City, independent of SS14's Atmos.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NCTileFireComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DamagePerSecond = 5f;

    [DataField, AutoNetworkedField]
    public float Lifetime = 15f;

    [DataField, AutoNetworkedField]
    public float AccumulatedDamageTimer = 0f;
}
