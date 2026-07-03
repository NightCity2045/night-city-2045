using System.Linq;
using System.Numerics;
using Content.Server.Mind;
using Content.Server.Power.Components;
using Content.Shared._NC.Rigger.Components;
using Content.Shared._NC.Rigger.Events;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.ListViewSelector;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._NC.Rigger;

/// <summary>
/// Runs the rigger console lifecycle: eye spawn, drone linking, RTS toggles and cleanup.
/// </summary>
public sealed class RiggerConsoleSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private TimeSpan _nextRefresh;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        SubscribeLocalEvent<RiggerConsoleComponent, ActivateInWorldEvent>(OnConsoleActivate);
        SubscribeLocalEvent<RiggerConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<RiggerConsoleUserComponent, RiggerExitConsoleActionEvent>(OnExitAction);
        SubscribeLocalEvent<RiggerConsoleUserComponent, RiggerToggleRTSModeActionEvent>(OnToggleRtsAction);
        SubscribeLocalEvent<RiggerConsoleUserComponent, RiggerDroneStatusActionEvent>(OnDroneStatusAction);
        SubscribeLocalEvent<RiggerConsoleUserComponent, ComponentShutdown>(OnUserShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;

        var query = EntityQueryEnumerator<RiggerConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            RefreshLinkedDrones((uid, console));

            if (console.ActiveEye is { } eye && TryComp<RiggerConsoleUserComponent>(eye, out var user))
                SyncSessionOverrides(new Entity<RiggerConsoleUserComponent>(eye, user));
        }
    }

    private void OnConsoleActivate(Entity<RiggerConsoleComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled ||
            ent.Comp.User != null ||
            TryComp<ApcPowerReceiverComponent>(ent, out var power) && !power.Powered ||
            TryComp<AccessReaderComponent>(ent, out var access) && !_access.IsAllowed(args.User, ent, access))
        {
            return;
        }

        if (HasComp<RiggerConsoleUserComponent>(args.User))
            return;

        if (!_mind.TryGetMind(args.User, out var mindId, out _))
            return;

        RefreshLinkedDrones(ent);
        if (!HasAnyLiveDrone(ent.Comp))
        {
            _popup.PopupEntity(Loc.GetString("nc-rigger-console-no-drones"), ent, args.User);
            return;
        }

        args.Handled = true;

        var eye = Spawn(ent.Comp.EyePrototype, Transform(ent).Coordinates);
        var user = EnsureComp<RiggerConsoleUserComponent>(eye);
        user.Console = ent.Owner;
        user.OriginalBody = args.User;
        user.LinkedMind = mindId;
        user.LinkedDrones.Clear();
        user.LinkedDrones.AddRange(ent.Comp.LinkedDrones);
        user.RtsEnabled = false;

        _actions.AddAction(eye, ref user.ExitActionEntity, user.ExitAction, eye);
        _actions.AddAction(eye, ref user.ToggleRtsActionEntity, user.ToggleRtsAction, eye);
        _actions.AddAction(eye, ref user.DroneStatusActionEntity, user.DroneStatusAction, eye);

        ent.Comp.User = args.User;
        ent.Comp.ActiveEye = eye;
        Dirty(ent);
        Dirty(eye, user);

        _mind.ControlMob(args.User, eye);
        SyncSessionOverrides(new Entity<RiggerConsoleUserComponent>(eye, user));
    }

    private void OnConsoleShutdown(Entity<RiggerConsoleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ActiveEye is { } eye && TryComp<RiggerConsoleUserComponent>(eye, out var user))
            ReturnRigger(new Entity<RiggerConsoleUserComponent>(eye, user));
    }

    private void OnExitAction(Entity<RiggerConsoleUserComponent> ent, ref RiggerExitConsoleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ReturnRigger(ent);
    }

    private void OnToggleRtsAction(Entity<RiggerConsoleUserComponent> ent, ref RiggerToggleRTSModeActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.Toggle = true;
        ent.Comp.RtsEnabled = !ent.Comp.RtsEnabled;
        _actions.SetToggled(ent.Comp.ToggleRtsActionEntity, ent.Comp.RtsEnabled);
        Dirty(ent);

        var text = ent.Comp.RtsEnabled
            ? Loc.GetString("nc-rigger-rts-enabled")
            : Loc.GetString("nc-rigger-rts-disabled");
        _popup.PopupEntity(text, ent, ent);
    }

    private void OnDroneStatusAction(Entity<RiggerConsoleUserComponent> ent, ref RiggerDroneStatusActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!TryComp<RiggerConsoleComponent>(ent.Comp.Console, out var console))
            return;

        RefreshLinkedDrones((ent.Comp.Console, console));
        ent.Comp.LinkedDrones.Clear();
        ent.Comp.LinkedDrones.AddRange(console.LinkedDrones);
        Dirty(ent);

        var entries = new List<ListViewSelectorEntry>();
        foreach (var drone in ent.Comp.LinkedDrones)
        {
            if (!Exists(drone))
                continue;

            var status = _mobState.IsAlive(drone)
                ? Loc.GetString("nc-rigger-drone-state-alive")
                : Loc.GetString("nc-rigger-drone-state-offline");

            var description = status;
            if (TryComp<DamageableComponent>(drone, out var damage))
                description = $"{status}, {Loc.GetString("nc-rigger-drone-damage", ("damage", (int) damage.TotalDamage.Float()))}";

            entries.Add(new ListViewSelectorEntry(drone.ToString(), Name(drone), description));
        }

        _ui.SetUiState(ent.Owner, ListViewSelectorUiKey.Key, new ListViewSelectorState(entries));
        _ui.TryToggleUi(ent.Owner, ListViewSelectorUiKey.Key, ent.Owner);
    }

    private void OnUserShutdown(Entity<RiggerConsoleUserComponent> ent, ref ComponentShutdown args)
    {
        RemoveSessionOverrides(ent);
        CleanupActions(ent);

        if (TryComp<RiggerConsoleComponent>(ent.Comp.Console, out var console) && console.ActiveEye == ent.Owner)
        {
            console.User = null;
            console.ActiveEye = null;
            Dirty(ent.Comp.Console, console);
        }
    }

    private void ReturnRigger(Entity<RiggerConsoleUserComponent> ent)
    {
        RemoveSessionOverrides(ent);

        if (ent.Comp.LinkedMind is { } mind && Exists(ent.Comp.OriginalBody))
            _mind.TransferTo(mind, ent.Comp.OriginalBody);

        CleanupActions(ent);

        if (TryComp<RiggerConsoleComponent>(ent.Comp.Console, out var console))
        {
            console.User = null;
            console.ActiveEye = null;
            Dirty(ent.Comp.Console, console);
        }

        QueueDel(ent.Owner);
    }

    private void CleanupActions(Entity<RiggerConsoleUserComponent> ent)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ExitActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleRtsActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.DroneStatusActionEntity);
    }

    private void RefreshLinkedDrones(Entity<RiggerConsoleComponent> console)
    {
        console.Comp.LinkedDrones.Clear();

        var query = EntityQueryEnumerator<RiggerDroneComponent>();
        while (query.MoveNext(out var droneUid, out var drone))
        {
            if (drone.Console is { } linkedConsole &&
                (!Exists(linkedConsole) || !HasComp<RiggerConsoleComponent>(linkedConsole)))
            {
                drone.Console = null;
                Dirty(droneUid, drone);
            }

            if (!Exists(droneUid) || !BelongsToConsole(droneUid, drone, console))
                continue;

            drone.Console = console.Owner;
            console.Comp.LinkedDrones.Add(droneUid);
            Dirty(droneUid, drone);
        }

        Dirty(console);
    }

    private bool BelongsToConsole(EntityUid droneUid, RiggerDroneComponent drone, Entity<RiggerConsoleComponent> console)
    {
        if (drone.Console == console.Owner)
            return true;

        if (drone.Console != null)
            return false;

        var consoleMap = Transform(console).MapUid;
        var droneMap = Transform(droneUid).MapUid;
        if (consoleMap == null || consoleMap != droneMap)
            return false;
        
        return true;
    }

    private bool HasAnyLiveDrone(RiggerConsoleComponent console)
    {
        foreach (var drone in console.LinkedDrones)
        {
            if (Exists(drone) && _mobState.IsAlive(drone))
                return true;
        }

        return false;
    }

    private void SyncSessionOverrides(Entity<RiggerConsoleUserComponent> ent)
    {
        if (!_player.TryGetSessionByEntity(ent.Owner, out var session))
            return;

        if (TryComp<RiggerConsoleComponent>(ent.Comp.Console, out var console))
        {
            ent.Comp.LinkedDrones.Clear();
            ent.Comp.LinkedDrones.AddRange(console.LinkedDrones);
        }

        var desired = new HashSet<EntityUid>(ent.Comp.LinkedDrones.Where(uid => Exists(uid) && _mobState.IsAlive(uid)));

        for (var i = ent.Comp.SessionOverrides.Count - 1; i >= 0; i--)
        {
            var existing = ent.Comp.SessionOverrides[i];
            if (desired.Contains(existing))
                continue;

            _pvsOverride.RemoveSessionOverride(existing, session);
            ent.Comp.SessionOverrides.RemoveAt(i);
        }

        foreach (var drone in desired)
        {
            if (ent.Comp.SessionOverrides.Contains(drone))
                continue;

            _pvsOverride.AddSessionOverride(drone, session);
            ent.Comp.SessionOverrides.Add(drone);
        }

        Dirty(ent);
    }

    private void RemoveSessionOverrides(Entity<RiggerConsoleUserComponent> ent)
    {
        if (!_player.TryGetSessionByEntity(ent.Owner, out var session))
            return;

        foreach (var drone in ent.Comp.SessionOverrides)
        {
            _pvsOverride.RemoveSessionOverride(drone, session);
        }

        ent.Comp.SessionOverrides.Clear();
    }
}
