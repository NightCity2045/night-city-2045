using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Events;
using Content.Shared._NC.Stats.Prototypes;
using Content.Shared.Alert;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Stats.Systems;

/// <summary>
/// Computes BODY-based carried weight and applies its derived movement penalties.
/// </summary>
public sealed class SharedNCBodySystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedNCStatsSystem _stats = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCBodyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NCBodyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<NCBodyComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<NCBodyComponent, NCStatChangedEvent>(OnStatChanged);
        SubscribeLocalEvent<NCBodyComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<NCBodyComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ItemComponent, EntInsertedIntoContainerMessage>(OnItemContainerModified);
        SubscribeLocalEvent<ItemComponent, EntRemovedFromContainerMessage>(OnItemContainerModified);
        SubscribeLocalEvent<NCWeightComponent, AfterAutoHandleStateEvent>(OnWeightStateChanged);
        SubscribeLocalEvent<NCBodyComponent, AfterAutoHandleStateEvent>(OnBodyHandleState);
    }

    private void OnBodyHandleState(EntityUid uid, NCBodyComponent component, ref AfterAutoHandleStateEvent args)
    {
        RaiseLocalEvent(uid, new NCBodyStateHandledEvent());
    }

    private void OnStartup(EntityUid uid, NCBodyComponent component, ComponentStartup args)
    {
        RefreshBody(uid, component);
    }

    private void OnStatChanged(EntityUid uid, NCBodyComponent component, ref NCStatChangedEvent args)
    {
        if (!string.Equals(args.StatId, NCStatIds.Body, StringComparison.Ordinal))
            return;

        RefreshBody(uid, component);
    }

    private void OnContainerModified(EntityUid uid, NCBodyComponent component, ContainerModifiedMessage args)
    {
        RefreshBody(uid, component);
    }

    private void OnItemContainerModified(EntityUid uid, ItemComponent component, ContainerModifiedMessage args)
    {
        if (TryFindBodyOwner(uid, out var owner))
            RefreshBody(owner);
    }

    private void OnWeightStateChanged(EntityUid uid, NCWeightComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (TryFindBodyOwner(uid, out var owner))
            RefreshBody(owner);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, NCBodyComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (!_prototype.TryIndex<NCStatPrototype>(NCStatIds.Body, out var settings))
            return;

        var (walk, sprint) = component.Level switch
        {
            NCBodyLoadLevel.Light => (settings.BodyLightWalkModifier, settings.BodyLightSprintModifier),
            NCBodyLoadLevel.Heavy => (settings.BodyHeavyWalkModifier, settings.BodyHeavySprintModifier),
            NCBodyLoadLevel.Overloaded => (settings.BodyOverloadedWalkModifier, settings.BodyOverloadedSprintModifier),
            _ => (1f, 1f),
        };

        // Carry weight is an inherent BODY/inventory result, so speed immunity should not suppress it.
        args.ModifySpeed(walk, sprint, bypassImmunity: true);
    }

    private void OnPickupAttempt(EntityUid uid, NCBodyComponent component, PickupAttemptEvent args)
    {
        if (component.Level != NCBodyLoadLevel.Overloaded)
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("nc-body-overloaded-pickup"), uid, uid, PopupType.SmallCaution);
    }

    public void RefreshBody(EntityUid uid, NCBodyComponent? component = null, NCStatsComponent? stats = null)
    {
        if (!Resolve(uid, ref component, false) ||
            !_prototype.TryIndex<NCStatPrototype>(NCStatIds.Body, out var settings))
        {
            return;
        }

        var oldLevel = component.Level;

        component.CurrentWeight = CalculateCarriedMass(uid, settings);
        component.Body = Resolve(uid, ref stats, false) && _stats.TryGetStatValue(stats, NCStatIds.Body, out var body)
            ? body
            : 0;
        component.MaxWeight = MathF.Max(0f, component.Body * settings.BodyKgPerPoint);
        component.Level = GetLevel(component, settings);

        Dirty(uid, component);
        UpdateAlert(uid, component, settings);

        if (oldLevel != component.Level)
            _movement.RefreshMovementSpeedModifiers(uid);
    }

    private NCBodyLoadLevel GetLevel(NCBodyComponent component, NCStatPrototype settings)
    {
        if (component.MaxWeight <= 0f || component.CurrentWeight <= component.MaxWeight * settings.BodyLightThreshold)
            return NCBodyLoadLevel.None;

        if (component.CurrentWeight <= component.MaxWeight * settings.BodyHeavyThreshold)
            return component.IgnoreLightLoad ? NCBodyLoadLevel.None : NCBodyLoadLevel.Light;

        if (component.CurrentWeight <= component.MaxWeight * settings.BodyOverloadedThreshold)
            return NCBodyLoadLevel.Heavy;

        return NCBodyLoadLevel.Overloaded;
    }

    private void UpdateAlert(EntityUid uid, NCBodyComponent component, NCStatPrototype settings)
    {
        var severity = component.Level switch
        {
            NCBodyLoadLevel.Light => (short) 1,
            NCBodyLoadLevel.Heavy => (short) 2,
            NCBodyLoadLevel.Overloaded => (short) 3,
            _ => (short) 0,
        };

        if (severity <= 0)
        {
            _alerts.ClearAlert(uid, settings.BodyLoadAlert);
            return;
        }

        _alerts.ShowAlert(uid, settings.BodyLoadAlert, severity);
    }

    private float CalculateCarriedMass(EntityUid uid, NCStatPrototype settings)
    {
        var total = 0f;
        var visited = new HashSet<EntityUid>();

        if (TryComp<InventoryComponent>(uid, out var inventory))
        {
            var enumerator = _inventory.GetSlotEnumerator((uid, inventory));
            while (enumerator.NextItem(out var item))
            {
                total += CalculateItemWeightRecursive(item, settings, visited);
            }
        }

        if (TryComp<HandsComponent>(uid, out var hands))
        {
            foreach (var held in _hands.EnumerateHeld(uid, hands))
            {
                total += CalculateItemWeightRecursive(held, settings, visited);
            }
        }

        return total;
    }

    private float CalculateItemWeightRecursive(EntityUid uid, NCStatPrototype settings, HashSet<EntityUid> visited)
    {
        if (!visited.Add(uid) || !TryComp<ItemComponent>(uid, out var item))
            return 0f;

        var total = GetItemWeight(uid, item, settings);

        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return total;

        foreach (var container in manager.Containers.Values)
        {
            foreach (var entity in container.ContainedEntities)
            {
                total += CalculateItemWeightRecursive(entity, settings, visited);
            }
        }

        return total;
    }

    private float GetItemWeight(EntityUid uid, ItemComponent item, NCStatPrototype settings)
    {
        if (TryComp<NCWeightComponent>(uid, out var weight))
            return MathF.Max(0f, weight.Weight);

        return settings.BodyItemSizeWeights.TryGetValue(item.Size.Id, out var fallback)
            ? fallback
            : 0f;
    }

    private bool TryFindBodyOwner(EntityUid uid, out EntityUid owner)
    {
        if (HasComp<NCBodyComponent>(uid))
        {
            owner = uid;
            return true;
        }

        var visited = new HashSet<EntityUid>();
        return TryFindBodyOwnerRecursive(uid, visited, out owner);
    }

    private bool TryFindBodyOwnerRecursive(EntityUid uid, HashSet<EntityUid> visited, out EntityUid owner)
    {
        if (!visited.Add(uid))
        {
            owner = default;
            return false;
        }

        if (HasComp<NCBodyComponent>(uid))
        {
            owner = uid;
            return true;
        }

        foreach (var container in _container.GetContainingContainers((uid, null)))
        {
            if (TryFindBodyOwnerRecursive(container.Owner, visited, out owner))
                return true;
        }

        owner = default;
        return false;
    }
}
