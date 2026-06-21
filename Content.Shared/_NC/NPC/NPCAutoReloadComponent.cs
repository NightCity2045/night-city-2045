using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;

namespace Content.Shared._NC.NPC;

/// <summary>
///     Added to an NPC that should automatically attempt to reload its held gun
///     if it runs out of ammunition.
/// </summary>
[RegisterComponent]
public sealed partial class NPCAutoReloadComponent : Component
{
    /// <summary>
    ///     How often to check for reload in seconds.
    /// </summary>
    [DataField]
    public float CheckInterval = 0.1f;

    /// <summary>
    ///     Abstract reload duration for NPCs once they realize the current weapon is dry.
    /// </summary>
    [DataField]
    public float ReloadDelay = 1f;

    [ViewVariables(VVAccess.ReadWrite)]
    public float Accumulator = 0f;

    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? ReloadWeapon;

    [ViewVariables(VVAccess.ReadWrite)]
    public float ReloadRemaining = 0f;
}
