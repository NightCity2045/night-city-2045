// Content.Server/_NC/NPC/HTN/Preconditions/NCWeaponMaintenancePrecondition.cs
using Content.Shared._NC.Weapons.Ranged.NCWeapon;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
///     Checks if the held weapon is jammed (NCWeapon) or needs racking (ChamberMagazine/DualAmmo).
///     Also stays true if an unjamming DoAfter is already in progress to prevent HTN interruptions.
///     Now supports 'Dry Chamber' detection for weapons without bolt-catch.
/// </summary>
public sealed partial class NCWeaponMaintenancePrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (_entManager.HasComponent<ActiveDoAfterComponent>(owner))
            return true;

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var hands))
            return false;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is not { } held)
                continue;

            // 1. Jam check
            if (_entManager.TryGetComponent<NCWeaponComponent>(held, out var ncWeapon) && ncWeapon.IsJammed)
                return true;

            // 2. Rack check (ChamberMagazine)
            if (_entManager.TryGetComponent<ChamberMagazineAmmoProviderComponent>(held, out var chamber))
            {
                // Case A: Bolt is open (needs closing)
                if (chamber.BoltClosed != true && chamber.CanRack)
                    return true;

                // Case B: Bolt is closed, but chamber is empty AND mag has ammo (needs racking to load first round)
                if (chamber.BoltClosed == true && chamber.CanRack)
                {
                    if (IsChamberEmpty(held) && HasMagazineAmmo(held))
                        return true;
                }
            }
        }

        return false;
    }

    private bool IsChamberEmpty(EntityUid gun)
    {
        return _entManager.System<SharedContainerSystem>().TryGetContainer(gun, "gun_chamber", out var container) && 
               container is ContainerSlot slot && slot.ContainedEntity == null;
    }

    private bool HasMagazineAmmo(EntityUid gun)
    {
        // Try to get the magazine to check its ammo count
        if (!_entManager.System<SharedContainerSystem>().TryGetContainer(gun, "gun_magazine", out var container) ||
            container is not ContainerSlot slot || slot.ContainedEntity is not { } mag)
        {
            return false;
        }

        // Check if magazine is not empty
        var ammoEv = new GetAmmoCountEvent();
        _entManager.EventBus.RaiseLocalEvent(mag, ref ammoEv);
        return ammoEv.Count > 0;
    }
}
