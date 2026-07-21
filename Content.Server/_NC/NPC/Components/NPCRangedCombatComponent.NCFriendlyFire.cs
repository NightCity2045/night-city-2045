using Content.Server.NPC.Components;

namespace Content.Server.NPC.Components;

public sealed partial class NPCRangedCombatComponent
{
    /// <summary>
    /// Prevents NPCs from firing when a friendly faction member is between them and their target.
    /// </summary>
    [DataField]
    public bool NCAvoidFriendlyFire = true;

    /// <summary>
    /// Width of the protected line-of-fire corridor around the shot segment.
    /// </summary>
    [DataField]
    public float NCFriendlyFireLineRadius = 0.35f;

    /// <summary>
    /// Nearby entities inside this distance from the shooter are ignored so clustered squads do not deadlock.
    /// </summary>
    [DataField]
    public float NCFriendlyFireIgnoreRange = 0.1f;

    /// <summary>
    /// Lateral distance the NPC tries to move when a friendly blocks its shot.
    /// </summary>
    [DataField]
    public float NCFriendlyFireRepositionDistance = 2f;

    /// <summary>
    /// Small backwards offset used together with lateral movement to open a new firing angle.
    /// </summary>
    [DataField]
    public float NCFriendlyFireRepositionBackoff = 0.5f;

    /// <summary>
    /// Minimum time between friendly-fire reposition orders.
    /// </summary>
    [DataField]
    public float NCFriendlyFireRepositionCooldown = 1f;

    /// <summary>
    /// Arrival range for the temporary reposition steering order.
    /// </summary>
    [DataField]
    public float NCFriendlyFireRepositionRange = 0.35f;

    [ViewVariables]
    public TimeSpan NCFriendlyFireNextReposition;
}
