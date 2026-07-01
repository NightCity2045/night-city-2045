using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Events;
using Content.Shared._NC.Stats.Prototypes;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Stats.Systems;

/// <summary>
/// Applies movement speed modifiers derived from the Cyberpunk RED MOVE stat.
/// </summary>
public sealed class SharedNCStatMovementSpeedSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedNCStatsSystem _stats = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCStatsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<NCStatsComponent, NCStatChangedEvent>(OnStatChanged);
    }

    private void OnStatChanged(EntityUid uid, NCStatsComponent component, ref NCStatChangedEvent args)
    {
        if (!string.Equals(args.StatId, NCStatIds.Move, StringComparison.Ordinal))
            return;

        // Movement modifiers are cached by the movement system, so MOVE changes need an explicit refresh.
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, NCStatsComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (!_stats.TryGetStatValue(component, NCStatIds.Move, out var move))
            return;

        if (!_prototype.TryIndex<NCStatPrototype>(NCStatIds.Move, out var prototype))
            return;

        if (!prototype.MovementSpeedModifiers.TryGetValue(move, out var modifier))
            return;

        // MOVE is an inherent character stat, not an external slowdown, so immunity should not suppress it.
        args.ModifySpeed(modifier, modifier, bypassImmunity: true);
    }
}
