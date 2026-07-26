using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._NC.Netrunning.Components;
using Robust.Shared.Map;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Bridges META-selected targets into movement-only HTN pursuit.
/// </summary>
public sealed class NetDemonPursuitSystem : EntitySystem
{
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly NetServerSystem _netServer = default!;
    [Dependency] private readonly NPCSystem _npc = default!;

    private float _validationAccumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _validationAccumulator += frameTime;
        if (_validationAccumulator < 0.5f)
            return;

        _validationAccumulator %= 0.5f;
        var query = EntityQueryEnumerator<NetDemonControllerComponent, NetDefenseComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var controller, out var defense, out var htn))
        {
            if (controller.Target is not { } target)
                continue;

            if (Deleted(target) ||
                !HasComp<NetAvatarComponent>(target) ||
                defense.Server == null ||
                _netServer.ResolveNetworkServer(target) != defense.Server)
            {
                StopInternal(uid, controller, htn);
            }
        }
    }

    public bool TryFollow(EntityUid caller, EntityUid demonUid, EntityUid targetUid)
    {
        if (!TryComp<NetDemonControllerComponent>(demonUid, out var controller) ||
            !TryComp<NetDefenseComponent>(demonUid, out var defense) ||
            defense.Kind != NetDefenseKind.Demon ||
            !TryComp<HTNComponent>(demonUid, out var htn) ||
            !HasComp<NetAvatarComponent>(targetUid) ||
            defense.Server == null ||
            _netServer.ResolveNetworkServer(targetUid) != defense.Server ||
            (caller != demonUid && caller != defense.OwnerDeck))
        {
            return false;
        }

        controller.Target = targetUid;
        Dirty(demonUid, controller);

        _npc.SetBlackboard(
            demonUid,
            NPCBlackboard.FollowTarget,
            new EntityCoordinates(targetUid, Vector2.Zero),
            htn);
        _htn.SetHTNEnabled((demonUid, htn), true);
        _npc.WakeNPC(demonUid, htn);
        return true;
    }

    public bool TryStop(EntityUid caller, EntityUid demonUid)
    {
        if (!TryComp<NetDemonControllerComponent>(demonUid, out var controller) ||
            !TryComp<NetDefenseComponent>(demonUid, out var defense) ||
            !TryComp<HTNComponent>(demonUid, out var htn) ||
            (caller != demonUid && caller != defense.OwnerDeck))
        {
            return false;
        }

        StopInternal(demonUid, controller, htn);
        return true;
    }

    private void StopInternal(
        EntityUid demonUid,
        NetDemonControllerComponent controller,
        HTNComponent htn)
    {
        controller.Target = null;
        Dirty(demonUid, controller);
        htn.Blackboard.Remove<object>(NPCBlackboard.FollowTarget);
        _htn.SetHTNEnabled((demonUid, htn), false);
        _npc.SleepNPC(demonUid, htn);
    }
}
