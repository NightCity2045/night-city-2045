using Content.Shared._NC.CitiNet.Delivery;
using Content.Shared.Lock;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Server._NC.CitiNet.Delivery;

public sealed class DeliverySystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LockSystem _lockSystem = default!;

    private const string DeliveryContainerId = "entity_storage";
    private readonly TimeSpan CorporateExpiryDelay = TimeSpan.FromMinutes(15);
    private readonly List<PendingCorporateDelivery> _pendingCorporateDeliveries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DropPointComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
    }

    private void OnItemRemoved(EntityUid uid, DropPointComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != DeliveryContainerId)
            return;

        if (args.Container.ContainedEntities.Count > 0)
            return;

        ResetDropPoint(uid, component);
    }

    private void ResetDropPoint(EntityUid uid, DropPointComponent component)
    {
        component.IsOccupied = false;
        component.ContainedItem = null;
        component.DeliveryTime = null;

        if (TryComp<OTPKeypadComponent>(uid, out var keypad))
        {
            keypad.CurrentPin = null;
            keypad.IsLocked = false;
            Dirty(uid, keypad);
        }

        if (TryComp<LockComponent>(uid, out var lockComp))
            _lockSystem.Lock(uid, null, lockComp);

        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        ProcessPendingCorporateDeliveries();
        ExpireOldCorporateDropBoxes();
    }

    /// <summary>
    /// Delivers a complete cart to one pickup route. Corporate routes are queued separately.
    /// </summary>
    public bool TryDeliverOrder(EntityUid buyer, List<DeliveryOrderItem> items, DropType preferredType, out string message)
    {
        if (items.Count == 0)
        {
            message = Loc.GetString("citinet-store-cart-empty");
            return false;
        }

        if (preferredType == DropType.CorporateZone)
            return TryQueueCorporateDelivery(buyer, items, out message);

        var candidates = GetAvailableDropPoints(preferredType);
        if (candidates.Count == 0)
        {
            message = Loc.GetString("citinet-delivery-no-drop-points");
            return false;
        }

        var selected = _random.Pick(candidates);
        var dropCoordinates = Transform(selected.Uid).Coordinates;
        var container = _container.EnsureContainer<Container>(selected.Uid, DeliveryContainerId);

        // Astrozon and Night Market orders are placed directly into the pickup container.
        if (!TryInsertLooseItems(dropCoordinates, items, container, out message))
        {
            return false;
        }

        selected.Comp.IsOccupied = true;
        selected.Comp.ContainedItem = null;
        selected.Comp.DeliveryTime = _timing.CurTime;

        if (selected.Comp.DropType == DropType.Corporate)
            PrepareCorporateDropBox(selected.Uid, selected.Comp, items, out message);
        else
            message = Loc.GetString(
            "citinet-delivery-dead-drop-ready",
            ("count", CountItems(items)),
            ("location", selected.Comp.LocationName));

        // Navigation chips are generated per checkout, not per item, so a cart maps to one pickup route.
        var chip = EntityManager.SpawnEntity("CitiNetDeliveryChip", Transform(buyer).Coordinates);
        var chipComp = EnsureComp<DeliveryChipComponent>(chip);
        chipComp.TargetDropPoint = selected.Uid;
        chipComp.LocationName = selected.Comp.LocationName;
        chipComp.ReadyAt = _timing.CurTime;
        chipComp.IsReady = true;
        Dirty(chip, chipComp);

        Dirty(selected.Uid, selected.Comp);
        return true;
    }

    private List<(EntityUid Uid, DropPointComponent Comp)> GetAvailableDropPoints(DropType preferredType)
    {
        var points = EntityQueryEnumerator<DropPointComponent>();
        var candidates = new List<(EntityUid Uid, DropPointComponent Comp)>();

        while (points.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsOccupied && comp.DropType == preferredType)
                candidates.Add((uid, comp));
        }

        return candidates;
    }

    private void PrepareCorporateDropBox(EntityUid uid, DropPointComponent dropPoint, List<DeliveryOrderItem> items, out string message)
    {
        var pin = _random.Next(1000, 9999).ToString();
        if (TryComp<OTPKeypadComponent>(uid, out var keypad))
        {
            keypad.CurrentPin = pin;
            keypad.IsLocked = true;
            Dirty(uid, keypad);
        }

        if (TryComp<LockComponent>(uid, out var lockComp))
            _lockSystem.Lock(uid, null, lockComp);

        message = Loc.GetString(
            "citinet-delivery-corporate-dropbox-ready",
            ("count", CountItems(items)),
            ("location", dropPoint.LocationName),
            ("pin", pin));
    }

    private bool TryQueueCorporateDelivery(EntityUid buyer, List<DeliveryOrderItem> items, out string message)
    {
        var zones = EntityQueryEnumerator<CorporateDeliveryZoneComponent>();
        var candidates = new List<(EntityUid Uid, CorporateDeliveryZoneComponent Comp)>();

        while (zones.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsOccupied)
                candidates.Add((uid, comp));
        }

        if (candidates.Count == 0)
        {
            message = Loc.GetString("citinet-delivery-no-corporate-zones");
            return false;
        }

        var selected = _random.Pick(candidates);
        selected.Comp.IsOccupied = true;
        Dirty(selected.Uid, selected.Comp);

        var readyAt = _timing.CurTime + selected.Comp.DeliveryDelay;

        var chip = EntityManager.SpawnEntity("CitiNetDeliveryChip", Transform(buyer).Coordinates);
        var chipComp = EnsureComp<DeliveryChipComponent>(chip);
        chipComp.TargetDropPoint = selected.Uid;
        chipComp.LocationName = selected.Comp.LocationName;
        chipComp.ReadyAt = readyAt;
        chipComp.IsReady = false;
        Dirty(chip, chipComp);

        _pendingCorporateDeliveries.Add(new PendingCorporateDelivery(
            selected.Uid,
            selected.Comp.CratePrototype,
            new List<DeliveryOrderItem>(items),
            readyAt,
            chip));

        var minutes = Math.Max(1, (int) Math.Ceiling(selected.Comp.DeliveryDelay.TotalMinutes));
        message = Loc.GetString(
            "citinet-delivery-corporate-zone-scheduled",
            ("count", CountItems(items)),
            ("location", selected.Comp.LocationName),
            ("minutes", minutes));
        return true;
    }

    private void ProcessPendingCorporateDeliveries()
    {
        for (var i = _pendingCorporateDeliveries.Count - 1; i >= 0; i--)
        {
            var pending = _pendingCorporateDeliveries[i];
            if (_timing.CurTime < pending.ReadyAt)
                continue;

            _pendingCorporateDeliveries.RemoveAt(i);

            if (!TryComp<CorporateDeliveryZoneComponent>(pending.Zone, out var zone))
                continue;

            EntityUid? firstCrate = null;
            foreach (var item in pending.Items)
            {
                // Corporate supply arrives as separate crates: one cart line, one crate.
                var spawnCoordinates = GetRandomZoneCoordinates(pending.Zone, zone);
                if (TryCreatePackedCrate(spawnCoordinates, new List<DeliveryOrderItem> { item }, pending.CratePrototype, out var crate, out _))
                    firstCrate ??= crate;
            }

            zone.DeliveredCrate = firstCrate;

            zone.IsOccupied = false;
            Dirty(pending.Zone, zone);

            if (TryComp<DeliveryChipComponent>(pending.Chip, out var chip))
            {
                chip.IsReady = true;
                Dirty(pending.Chip, chip);
            }
        }
    }

    private bool TryInsertLooseItems(
        EntityCoordinates coordinates,
        List<DeliveryOrderItem> items,
        BaseContainer container,
        out string message)
    {
        var inserted = new List<EntityUid>();

        foreach (var item in items)
        {
            for (var i = 0; i < item.Amount; i++)
            {
                var spawned = EntityManager.SpawnEntity(item.ProtoId, coordinates);
                if (_container.Insert(spawned, container))
                {
                    inserted.Add(spawned);
                    continue;
                }

                EntityManager.DeleteEntity(spawned);
                foreach (var uid in inserted)
                    EntityManager.DeleteEntity(uid);

                message = Loc.GetString("citinet-delivery-packaging-error");
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private EntityCoordinates GetRandomZoneCoordinates(EntityUid zoneUid, CorporateDeliveryZoneComponent zone)
    {
        var halfX = Math.Max(1, zone.Size.X) / 2f;
        var halfY = Math.Max(1, zone.Size.Y) / 2f;
        var offset = new Vector2(
            _random.NextFloat(-halfX, halfX),
            _random.NextFloat(-halfY, halfY));

        return Transform(zoneUid).Coordinates.Offset(offset);
    }

    private bool TryCreatePackedCrate(
        EntityCoordinates coordinates,
        List<DeliveryOrderItem> items,
        EntProtoId crateProto,
        out EntityUid crate,
        out string message)
    {
        crate = EntityManager.SpawnEntity(crateProto, coordinates);
        var crateContainer = _container.EnsureContainer<Container>(crate, DeliveryContainerId);

        foreach (var item in items)
        {
            for (var i = 0; i < item.Amount; i++)
            {
                var spawned = EntityManager.SpawnEntity(item.ProtoId, coordinates);
                if (_container.Insert(spawned, crateContainer))
                    continue;

                EntityManager.DeleteEntity(spawned);
                EntityManager.DeleteEntity(crate);
                crate = EntityUid.Invalid;
                message = Loc.GetString("citinet-delivery-packaging-error");
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void ExpireOldCorporateDropBoxes()
    {
        var query = EntityQueryEnumerator<DropPointComponent>();
        while (query.MoveNext(out var uid, out var dropPoint))
        {
            if (!dropPoint.IsOccupied || dropPoint.DropType != DropType.Corporate || dropPoint.DeliveryTime == null)
                continue;

            if (_timing.CurTime - dropPoint.DeliveryTime > CorporateExpiryDelay)
                ExpireDelivery(uid, dropPoint);
        }
    }

    private void ExpireDelivery(EntityUid uid, DropPointComponent dropPoint)
    {
        if (_container.TryGetContainer(uid, DeliveryContainerId, out var container))
            _container.CleanContainer(container);

        dropPoint.IsOccupied = false;
        dropPoint.ContainedItem = null;
        dropPoint.DeliveryTime = null;

        if (TryComp<OTPKeypadComponent>(uid, out var keypad))
        {
            keypad.CurrentPin = null;
            keypad.IsLocked = false;
            Dirty(uid, keypad);
        }

        Dirty(uid, dropPoint);
    }

    private static int CountItems(List<DeliveryOrderItem> items)
    {
        var total = 0;
        foreach (var item in items)
        {
            total += item.Amount;
        }

        return total;
    }

    private sealed record PendingCorporateDelivery(
        EntityUid Zone,
        EntProtoId CratePrototype,
        List<DeliveryOrderItem> Items,
        TimeSpan ReadyAt,
        EntityUid Chip);
}

public readonly record struct DeliveryOrderItem(string ProtoId, int Amount);
