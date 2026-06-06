using Content.Shared._NC.CitiNet.Delivery;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lock;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._NC.CitiNet.Delivery;

public sealed class DeliverySystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LockSystem _lockSystem = default!;

    private const string DeliveryContainerId = "entity_storage";
    private readonly TimeSpan CorporateExpiryDelay = TimeSpan.FromMinutes(15);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DropPointComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
    }

    private void OnItemRemoved(EntityUid uid, DropPointComponent component, EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != DeliveryContainerId)
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
        {
            _lockSystem.Lock(uid, null, lockComp);
        }

        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DropPointComponent>();
        while (query.MoveNext(out var uid, out var dropPoint))
        {
            if (!dropPoint.IsOccupied || dropPoint.DropType != DropType.Corporate || dropPoint.DeliveryTime == null)
                continue;

            if (_timing.CurTime - dropPoint.DeliveryTime > CorporateExpiryDelay)
            {
                ExpireDelivery(uid, dropPoint);
            }
        }
    }

    /// <summary>
    /// Delivers multiple items to a suitable drop point.
    /// </summary>
    public bool TryDeliverItem(EntityUid buyer, string itemProto, int amount, DropType preferredType, out string message)
    {
        message = string.Empty;
        
        var points = EntityQueryEnumerator<DropPointComponent>();
        var candidates = new List<(EntityUid Uid, DropPointComponent Comp)>();

        while (points.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsOccupied && comp.DropType == preferredType)
                candidates.Add((uid, comp));
        }

        if (candidates.Count == 0)
        {
            message = "Нет доступных точек доставки. Попробуйте позже.";
            return false;
        }

        // Pick a random candidate
        var selected = _random.Pick(candidates);
        var container = _container.EnsureContainer<Container>(selected.Uid, DeliveryContainerId);
        
        for (int i = 0; i < amount; i++)
        {
            var item = EntityManager.SpawnEntity(itemProto, Transform(selected.Uid).Coordinates);
            if (!_container.Insert(item, container))
            {
                // If it fails to insert (e.g. storage full), we stop spawning
                if (i == 0)
                {
                    EntityManager.DeleteEntity(item);
                    message = "Ошибка при упаковке товара. Свяжитесь с техподдержкой.";
                    return false;
                }
                break;
            }
        }

        selected.Comp.IsOccupied = true;
        selected.Comp.DeliveryTime = _timing.CurTime;

        if (selected.Comp.DropType == DropType.Corporate)
        {
            var pin = _random.Next(1000, 9999).ToString();
            if (TryComp<OTPKeypadComponent>(selected.Uid, out var keypad))
            {
                keypad.CurrentPin = pin;
                keypad.IsLocked = true;
                Dirty(selected.Uid, keypad);
            }

            if (TryComp<LockComponent>(selected.Uid, out var lockComp))
            {
                _lockSystem.Lock(selected.Uid, null, lockComp);
            }

            message = $"Груз ({amount} шт.) доставлен. Локация: {selected.Comp.LocationName}. Код: {pin}. Срок хранения: 15 минут. Чип навигации выдан.";
        }
        else
        {
            message = $"Фиксер оставил товар ({amount} шт.) в: {selected.Comp.LocationName}. Поторопись, пока не нашли другие. Чип навигации выдан.";
        }

        // Spawn navigation chip at the terminal (Phase 1/2 integration)
        var chip = EntityManager.SpawnEntity("CitiNetDeliveryChip", Transform(buyer).Coordinates);
        var chipComp = EnsureComp<DeliveryChipComponent>(chip);
        chipComp.TargetDropPoint = selected.Uid;
        chipComp.LocationName = selected.Comp.LocationName;
        Dirty(chip, chipComp);

        Dirty(selected.Uid, selected.Comp);
        return true;
    }

    private void ExpireDelivery(EntityUid uid, DropPointComponent dropPoint)
    {
        if (_container.TryGetContainer(uid, DeliveryContainerId, out var container))
        {
            _container.CleanContainer(container);
        }

        dropPoint.IsOccupied = false;
        dropPoint.DeliveryTime = null;

        if (TryComp<OTPKeypadComponent>(uid, out var keypad))
        {
            keypad.CurrentPin = null;
            keypad.IsLocked = false;
            Dirty(uid, keypad);
        }

        Dirty(uid, dropPoint);
    }
}
