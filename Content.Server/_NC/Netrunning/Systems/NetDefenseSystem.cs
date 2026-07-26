using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using System.Linq;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
///     Owns persistent physical META programs after they are materialized.
///     C# only detects entrants and manages server load; program effects remain in META.
/// </summary>
public sealed class NetDefenseSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaDaemonSystem _metaDaemon = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetDefenseComponent, ComponentShutdown>(OnDefenseShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateHostedPrograms(frameTime);
    }

    private void UpdateHostedPrograms(float frameTime)
    {
        var query = EntityQueryEnumerator<MetaHostedProgramComponent, NetDefenseComponent>();
        while (query.MoveNext(out var uid, out var hosted, out var defense))
        {
            if (hosted.ProgramShard is not { } programShard || Deleted(programShard))
            {
                QueueDel(uid);
                continue;
            }

            hosted.ScanAccumulator += frameTime;
            if (hosted.ScanAccumulator < hosted.ScanInterval)
                continue;

            hosted.ScanAccumulator %= Math.Max(0.01f, hosted.ScanInterval);
            ScanHostedProgram(uid, hosted);
        }
    }

    private void ScanHostedProgram(
        EntityUid uid,
        MetaHostedProgramComponent hosted)
    {
        var present = new HashSet<EntityUid>();
        foreach (var target in _lookup.GetEntitiesInRange<NetAvatarComponent>(
                     Transform(uid).Coordinates,
                     hosted.TriggerRadius))
        {
            if (target.Comp.Cyberdeck is not { } intruderDeck)
                continue;

            present.Add(target.Owner);
            if (!hosted.IntrudersInRange.Add(target.Owner))
                continue;

            // Every entrant reaches META; the script decides whether owner/admin status matters.
            // A transaction gives the intruder a response window while the hosted
            // program is suspended on YIELD.
            _metaDaemon.TryBeginIntrusion(
                uid,
                intruderDeck,
                MetaIntrusionOperationKind.Encounter,
                0,
                out _,
                target.Owner);
        }

        foreach (var previous in hosted.IntrudersInRange.ToArray())
        {
            if (!present.Contains(previous))
                hosted.IntrudersInRange.Remove(previous);
        }
    }

    private void OnDefenseShutdown(EntityUid uid, NetDefenseComponent component, ComponentShutdown args)
    {
        if (component.Server is not { } serverUid ||
            Deleted(serverUid) ||
            !TryComp<NetServerComponent>(serverUid, out var server))
            return;

        server.SpawnedDefenses.Remove(uid);
        server.UsedLoad = Math.Max(0, server.UsedLoad - component.ReservedLoad);
        Dirty(serverUid, server);
    }
}
