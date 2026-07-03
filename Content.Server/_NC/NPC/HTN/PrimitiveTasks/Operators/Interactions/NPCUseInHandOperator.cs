// D:\projects\night-station\Content.Server\_NC\NPC\HTN\PrimitiveTasks\Operators\Interactions\NPCUseInHandOperator.cs
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._NC.Weapons.Ranged.NCWeapon;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
/// NPC operator that triggers UseInHand (mimics pressing 'Z') on whatever is held in the active hand.
/// Now features 'Dry Chamber' detection and stabilization delay.
/// </summary>
public sealed partial class NPCUseInHandOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    private const string MaintenanceTriggeredKey = "MaintenanceTriggered";
    private const string WaitTicksKey = "MaintenanceWaitTicks";
    private const int MaxWaitTicks = 3; // Wait 3 ticks after interaction for sync

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var hands))
            return (false, null);

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity != null)
                return (true, null);
        }

        return (false, null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var interactionSystem = _entManager.System<SharedInteractionSystem>();

        if (!_entManager.TryGetComponent<HandsComponent>(owner, out var hands))
            return HTNOperatorStatus.Failed;

        // 1. If we are currently performing a DoAfter, keep waiting.
        if (_entManager.HasComponent<ActiveDoAfterComponent>(owner))
        {
            return HTNOperatorStatus.Continuing;
        }

        // 2. Read state from blackboard
        blackboard.TryGetValue<bool>(MaintenanceTriggeredKey, out var triggered, _entManager);
        blackboard.TryGetValue<int>(WaitTicksKey, out var waitTicks, _entManager);

        // 3. Find if anything STILL needs maintenance
        EntityUid? weaponToFix = null;
        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is not { } held)
                continue;

            bool needsUnjam = _entManager.TryGetComponent<NCWeaponComponent>(held, out var ncW) && ncW.IsJammed;
            bool needsRack = false;

            if (_entManager.TryGetComponent<ChamberMagazineAmmoProviderComponent>(held, out var chamber))
            {
                // Rack if bolt is open OR (bolt closed AND chamber empty AND mag has ammo)
                needsRack = (chamber.BoltClosed != true && chamber.CanRack) || 
                            (chamber.BoltClosed == true && chamber.CanRack && IsChamberEmpty(held) && HasMagazineAmmo(held));
            }

            if (needsUnjam || needsRack)
            {
                weaponToFix = held;
                break;
            }
        }

        // 4. State Logic
        if (weaponToFix != null)
        {
            if (triggered)
            {
                // We triggered the rack/unjam, but state hasn't changed.
                // Wait for stabilization ticks to allow server sync.
                if (waitTicks < MaxWaitTicks)
                {
                    blackboard.SetValue(WaitTicksKey, waitTicks + 1);
                    return HTNOperatorStatus.Continuing;
                }

                // If we waited and still need fixing, finish to let HTN re-plan or branch swap.
                Cleanup(blackboard);
                return HTNOperatorStatus.Finished;
            }

            // Trigger for the first time
            interactionSystem.UseInHandInteraction(owner, weaponToFix.Value, checkCanUse: false, checkCanInteract: false);
            blackboard.SetValue(MaintenanceTriggeredKey, true);
            blackboard.SetValue(WaitTicksKey, 0);
            return HTNOperatorStatus.Continuing;
        }

        // 5. Maintenance complete! Cleanup and finish.
        Cleanup(blackboard);
        return HTNOperatorStatus.Finished;
    }

    private void Cleanup(NPCBlackboard blackboard)
    {
        blackboard.Remove<bool>(MaintenanceTriggeredKey);
        blackboard.Remove<int>(WaitTicksKey);
    }

    private bool IsChamberEmpty(EntityUid gun)
    {
        return _entManager.System<SharedContainerSystem>().TryGetContainer(gun, "gun_chamber", out var container) && 
               container is ContainerSlot slot && slot.ContainedEntity == null;
    }

    private bool HasMagazineAmmo(EntityUid gun)
    {
        if (!_entManager.System<SharedContainerSystem>().TryGetContainer(gun, "gun_magazine", out var container) ||
            container is not ContainerSlot slot || slot.ContainedEntity is not { } mag)
        {
            return false;
        }

        var ammoEv = new GetAmmoCountEvent();
        _entManager.EventBus.RaiseLocalEvent(mag, ref ammoEv);
        return ammoEv.Count > 0;
    }
}
