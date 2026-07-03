using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Containers;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaDaemonSystem : EntitySystem
{
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly MetaApiSystem _api = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaSchedulerSystem _scheduler = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DefensiveDaemonComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, DefensiveDaemonComponent component, ComponentStartup args)
    {
        if (component.Shard != null)
            return;

        foreach (var container in _containers.GetAllContainers(uid))
        {
            foreach (var ent in container.ContainedEntities)
            {
                if (HasComp<DataShardComponent>(ent))
                {
                    component.Shard = ent;
                    Dirty(uid, component);
                    return;
                }
            }
        }
    }

    public void NotifyIntrusion(EntityUid protectedNode, EntityUid intruder)
    {
        if (!TryComp<DefensiveDaemonComponent>(protectedNode, out var daemon))
            return;

        if (daemon.Shard == null || !TryComp<DataShardComponent>(daemon.Shard.Value, out var shard))
            return;

        if (shard.Bytecode == null || shard.ProgramKind != MetaProgramKind.DaemonDefensive)
            return;

        _api.SetIntruder(protectedNode, intruder);
        var result = _vm.ExecuteEvent(protectedNode, shard.Bytecode, "INTRUSION", intruder);
        _scheduler.HandleVmResult(protectedNode, daemon.Shard.Value, result);
        _api.SetIntruder(protectedNode, null);
    }
}
