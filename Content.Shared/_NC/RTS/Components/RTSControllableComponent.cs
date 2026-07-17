using Content.Shared._NC.RTS.Events;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._NC.RTS.Components;

/// <summary>
/// Marks an NPC that can be manually controlled through the GM RTS layer.
/// The component only stores the replicated override state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RTSControllableComponent : Component
{
    /// <summary>
    /// The current world destination for move-like orders.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public EntityCoordinates? Destination;

    /// <summary>
    /// The active RTS order. Null returns control to the normal HTN flow.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public RTSCommandType? ActiveCommand;

    /// <summary>
    /// The explicitly assigned attack target for focus-fire orders.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public EntityUid? TargetEntity;

    /// <summary>
    /// Distance at which move-like RTS orders count as completed.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float ArrivalRange = 0.5f;

    /// <summary>
    /// Distance between destinations assigned to units receiving the same group order.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float FormationSpacing = 1.25f;

    /// <summary>
    /// Maximum unobstructed distance at which an ordered unit stops and fires.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float AttackRange = 8f;

    /// <summary>
    /// Pathfinding tolerance used while the unit is still outside a valid firing position.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float AttackApproachRange = 1.5f;

    /// <summary>
    /// Radius used by RTS attack orders to scan and validate hostile targets.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float ScanRadius = 14f;
}
