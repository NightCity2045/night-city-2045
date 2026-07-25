using System;
using Robust.Shared.IoC;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Interaction;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Robust.Shared.Timing;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaSchedulerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly MetaProgramSystem _program = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<ActiveMetaProcessComponent, CyberdeckComponent>();

        while (query.MoveNext(out var uid, out var active, out var deck))
        {
            for (var i = active.SuspendedProcesses.Count - 1; i >= 0; i--)
            {
                var proc = active.SuspendedProcesses[i];

                if (proc.DoAfterIndex != null)
                {
                    var user = GetEntity(proc.UserUid);
                    var status = _doAfter.GetStatus(new DoAfterId(user, proc.DoAfterIndex.Value));
                    
                    if (status == DoAfterStatus.Running)
                        continue;

                    if (status == DoAfterStatus.Finished)
                    {
                        active.SuspendedProcesses.RemoveAt(i);
                        proc.DoAfterIndex = null;
                        var runResult = _vm.Resume(proc);
                        HandleVmResult(uid, GetEntity(proc.ShardUid), runResult, user);
                    }
                    else // Cancelled or Invalid
                    {
                        active.SuspendedProcesses.RemoveAt(i);
                        proc.DoAfterIndex = null;
                        ReleaseRam(uid, proc);
                        _popup.PopupEntity(Loc.GetString("netrunning-meta-link-desync"), uid, user,
                            Content.Shared.Popups.PopupType.LargeCaution);
                    }
                    continue;
                }

                if (curTime < proc.ResumeAtTime)
                    continue;

                if (!HasActiveLink(uid, deck))
                {
                    active.SuspendedProcesses.RemoveAt(i);
                    ReleaseRam(uid, proc);
                    continue;
                }

                active.SuspendedProcesses.RemoveAt(i);
                var resumeResult = _vm.Resume(proc);
                HandleVmResult(uid, GetEntity(proc.ShardUid), resumeResult, GetEntity(proc.UserUid));
            }
        }
    }

    public void HandleVmResult(
        EntityUid deckUid,
        EntityUid shardUid,
        MetaVmRunResult runResult,
        EntityUid user)
    {
        if (runResult.Continuation != null)
        {
            if (TryComp<CyberdeckComponent>(deckUid, out var runningDeck))
            {
                if (!_program.UpdateRunningExecution(deckUid, runningDeck, shardUid, runResult.Result, user))
                    return;
            }

            if (runResult.Result.SuspensionReason == MetaSuspensionReason.SchedulerPreemption)
            {
                runResult.Continuation.ResumeAtTime = _timing.CurTime.TotalSeconds;
                EnsureComp<ActiveMetaProcessComponent>(deckUid).SuspendedProcesses.Add(runResult.Continuation);
                return;
            }

            if (user.Valid && TryComp<CyberdeckComponent>(deckUid, out var deck))
            {
                var delay = (float)runResult.Continuation.ResumeAtTime; 
                var doAfterArgs = new DoAfterArgs(EntityManager, user, delay / 1000f, new AwaitedDoAfterEvent(), deckUid, target: user)
                {
                    BreakOnMove = true,
                    BreakOnDamage = true,
                    NeedHand = true,
                };

                if (_doAfter.TryStartDoAfter(doAfterArgs, out var id))
                {
                    runResult.Continuation.DoAfterIndex = id.Value.Index;
                    var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
                    active.SuspendedProcesses.Add(runResult.Continuation);
                    _popup.PopupEntity(Loc.GetString("netrunning-meta-processing"), deckUid, user);
                    return;
                }
            }

            // Fallback to time-based yield if no user or DoAfter failed
            {
                var active = EnsureComp<ActiveMetaProcessComponent>(deckUid);
                var curTime = _timing.CurTime.TotalSeconds;
                runResult.Continuation.ResumeAtTime = curTime + (runResult.Continuation.ResumeAtTime / 1000.0);
                active.SuspendedProcesses.Add(runResult.Continuation);
            }
        }
        else
        {
            if (TryComp<CyberdeckComponent>(deckUid, out var deck))
                _program.FinishExecution(deckUid, deck, shardUid, runResult.Result, user);
        }
    }

    private void ReleaseRam(EntityUid deckUid, MetaContinuationState proc)
    {
        if (!TryComp<CyberdeckComponent>(deckUid, out var deck))
            return;

        _program.CancelExecution(deckUid, deck, proc);
    }

    private bool HasActiveLink(EntityUid deckUid, CyberdeckComponent deck)
    {
        if (deck.ActiveTarget == null)
            return false;

        var target = deck.ActiveTarget.Value;
        if (Deleted(target))
            return false;

        if (!TryComp<TransformComponent>(deckUid, out var xform) || xform.ParentUid == EntityUid.Invalid)
            return false;

        var user = xform.ParentUid;
        return _interaction.InRangeUnobstructed(user, target, deck.Range);
    }
}
