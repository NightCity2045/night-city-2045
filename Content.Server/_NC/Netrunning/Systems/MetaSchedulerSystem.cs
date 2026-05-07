using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Timing;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
/// Ticks every server frame. Checks all cyberdecks with ActiveMetaProcessComponent for
/// suspended META processes whose YIELD delay has expired. Resumes them via the VM.
///
/// If a process finishes (no more YIELD), it is removed from the list.
/// When all processes on a deck are done, the component is removed.
///
/// Also handles RAM refund for completed processes and memory leak application.
/// </summary>
public sealed class MetaSchedulerSystem : EntitySystem
{
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<ActiveMetaProcessComponent, CyberdeckComponent>();

        while (query.MoveNext(out var deckUid, out var active, out var deck))
        {
            for (var i = active.SuspendedProcesses.Count - 1; i >= 0; i--)
            {
                var proc = active.SuspendedProcesses[i];

                // Not ready yet — YIELD delay hasn't expired.
                if (curTime < proc.ResumeAtTime)
                    continue;

                // Validate that the deck still has an active link (target is alive and in range).
                // If not, kill the process with a connection-lost error.
                if (deck.ActiveTarget == null || Deleted(deck.ActiveTarget.Value))
                {
                    active.SuspendedProcesses.RemoveAt(i);
                    RefundRam(deckUid, deck, proc);
                    continue;
                }

                // Resume execution from saved continuation state.
                var runResult = _vm.Resume(proc);

                if (runResult.Continuation != null)
                {
                    // Still yielded — update resume time and keep in the list.
                    // Continuation.ResumeAtTime currently holds the ms delay from YIELD instruction.
                    var delayMs = runResult.Continuation.ResumeAtTime;
                    runResult.Continuation.ResumeAtTime = curTime + (delayMs / 1000.0);
                    active.SuspendedProcesses[i] = runResult.Continuation;
                }
                else
                {
                    // Process completed (or errored). Remove it and refund RAM.
                    active.SuspendedProcesses.RemoveAt(i);
                    RefundRam(deckUid, deck, proc);

                    // Log fatal errors as popups (the user who holds the deck).
                    if (runResult.Result.FatalError != null)
                        Logger.InfoS("meta.scheduler", $"Process on {ToPrettyString(deckUid)} failed: {runResult.Result.FatalError}");
                }
            }

            // If no more suspended processes, clean up the component.
            if (active.SuspendedProcesses.Count == 0)
                RemComp<ActiveMetaProcessComponent>(deckUid);
        }
    }

    /// <summary>
    /// Refund the base RAM reservation for a completed/killed process.
    /// Memory leaks have already been applied by the VM on completion.
    /// </summary>
    private void RefundRam(EntityUid deckUid, CyberdeckComponent deck, MetaContinuationState proc)
    {
        if (!TryComp<DataShardComponent>(proc.ShardUid, out var shard))
            return;

        var effectiveMax = Math.Max(0, deck.MaxRam - deck.LeakedRam);
        deck.CurrentRam = Math.Min(effectiveMax, deck.CurrentRam + shard.RequiredRam);
        Dirty(deckUid, deck);
    }
}
