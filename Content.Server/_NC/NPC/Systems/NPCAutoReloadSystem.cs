// Content.Server/_NC/NPC/Systems/NPCAutoReloadSystem.cs
// Abstract reload support for NPC firearms.

using Content.Shared._NC.NPC;
using Content.Server.NPC.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._NC.NPC.Systems;

/// <summary>
/// NPCs do not need physical spare magazines. When a held gun runs dry,
/// they wait for a short reload delay and then restore the currently loaded
/// ammo provider into a combat-ready state.
/// </summary>
public sealed class NPCAutoReloadSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private const string MagazineSlot = "gun_magazine";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCAutoReloadComponent, HandsComponent>();

        while (query.MoveNext(out var uid, out var reload, out var hands))
        {
            if (hands.ActiveHandEntity is not { } heldEntity)
            {
                ResetReloadState(reload);
                continue;
            }

            reload.Accumulator += frameTime;
            if (reload.Accumulator < reload.CheckInterval)
                continue;

            reload.Accumulator = 0f;

            if (!TryGetAmmoCount(heldEntity, out var ammoCount) || ammoCount > 0)
            {
                ResetReloadState(reload);
                continue;
            }

            if (reload.ReloadWeapon != heldEntity)
            {
                reload.ReloadWeapon = heldEntity;
                reload.ReloadRemaining = reload.ReloadDelay;
                continue;
            }

            reload.ReloadRemaining -= reload.CheckInterval;
            if (reload.ReloadRemaining > 0f)
                continue;

            if (TryAbstractReload(heldEntity))
                ReactivateRangedCombat(uid);

            ResetReloadState(reload);
        }
    }

    private bool TryGetAmmoCount(EntityUid gun, out int ammoCount)
    {
        if (!HasComp<BallisticAmmoProviderComponent>(gun) &&
            !HasComp<ChamberMagazineAmmoProviderComponent>(gun) &&
            !HasComp<MagazineAmmoProviderComponent>(gun))
        {
            ammoCount = 0;
            return false;
        }

        var ammoEv = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammoEv);
        ammoCount = ammoEv.Count;
        return true;
    }

    private bool TryAbstractReload(EntityUid gun)
    {
        if (TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamberMagazine))
        {
            return ReloadChamberMagazineWeapon(gun, chamberMagazine);
        }

        if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
        {
            ReloadBallisticWeapon(gun, ballistic);
            return true;
        }

        return false;
    }

    private bool ReloadChamberMagazineWeapon(EntityUid gun, ChamberMagazineAmmoProviderComponent chamberMagazine)
    {
        var magEntity = EnsureMagazineEntity(gun);
        if (magEntity == null || !TryComp<BallisticAmmoProviderComponent>(magEntity.Value, out var magBallistic))
            return false;

        _gun.SetBallisticUnspawned((magEntity.Value, magBallistic), magBallistic.Capacity);

        // Force one full chambering pass so the weapon becomes immediately usable again.
        if (chamberMagazine.BoltClosed == true)
            _gun.SetBoltClosed(gun, chamberMagazine, false);

        _gun.SetBoltClosed(gun, chamberMagazine, true);
        return true;
    }

    private void ReloadBallisticWeapon(EntityUid gun, BallisticAmmoProviderComponent ballistic)
    {
        _gun.SetBallisticUnspawned((gun, ballistic), ballistic.Capacity);
    }

    private EntityUid? GetMagazineEntity(EntityUid gun)
    {
        if (!_container.TryGetContainer(gun, MagazineSlot, out var container) ||
            container is not ContainerSlot slot)
        {
            return null;
        }

        return slot.ContainedEntity;
    }

    private EntityUid? EnsureMagazineEntity(EntityUid gun)
    {
        if (GetMagazineEntity(gun) is { } existingMagazine)
            return existingMagazine;

        if (!_itemSlots.TryGetSlot(gun, MagazineSlot, out var magazineSlot) ||
            string.IsNullOrEmpty(magazineSlot.StartingItem))
        {
            return null;
        }

        // Restore the magazine declared by the weapon prototype if normal interaction ejected it.
        var magazine = Spawn(magazineSlot.StartingItem, Transform(gun).Coordinates);
        if (_itemSlots.TryInsert(gun, magazineSlot, magazine, null, excludeUserAudio: true))
            return magazine;

        QueueDel(magazine);
        return null;
    }

    private void ReactivateRangedCombat(EntityUid uid)
    {
        if (!TryComp<NPCRangedCombatComponent>(uid, out var ranged))
            return;

        // Vanilla ranged combat parks itself as Unspecified on dry ammo.
        // After our abstract reload succeeds, revive the existing target state.
        ranged.Status = CombatStatus.Normal;
        ranged.LOSAccumulator = 0f;
        ranged.TargetInLOS = false;
    }

    private static void ResetReloadState(NPCAutoReloadComponent reload)
    {
        reload.ReloadWeapon = null;
        reload.ReloadRemaining = 0f;
    }
}
