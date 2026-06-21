// Content.Server/_NC/NPC/Systems/NPCAutoReloadSystem.cs
// Abstract reload support for NPC firearms.

using Content.Shared._NC.NPC;
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

            TryAbstractReload(heldEntity);
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

    private void TryAbstractReload(EntityUid gun)
    {
        if (TryComp<ChamberMagazineAmmoProviderComponent>(gun, out var chamberMagazine))
        {
            ReloadChamberMagazineWeapon(gun, chamberMagazine);
            return;
        }

        if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
        {
            ReloadBallisticWeapon(gun, ballistic);
        }
    }

    private void ReloadChamberMagazineWeapon(EntityUid gun, ChamberMagazineAmmoProviderComponent chamberMagazine)
    {
        var magEntity = GetMagazineEntity(gun);
        if (magEntity == null || !TryComp<BallisticAmmoProviderComponent>(magEntity.Value, out var magBallistic))
            return;

        _gun.SetBallisticUnspawned((magEntity.Value, magBallistic), magBallistic.Capacity);

        // Force one full chambering pass so the weapon becomes immediately usable again.
        if (chamberMagazine.BoltClosed == true)
            _gun.SetBoltClosed(gun, chamberMagazine, false);

        _gun.SetBoltClosed(gun, chamberMagazine, true);
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

    private static void ResetReloadState(NPCAutoReloadComponent reload)
    {
        reload.ReloadWeapon = null;
        reload.ReloadRemaining = 0f;
    }
}
