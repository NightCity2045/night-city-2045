using System.Linq;
using Content.Shared._NC.Rigger.Components;
using Content.Shared._NC.Rigger.Events;
using Content.Shared.Actions;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Wieldable;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._NC.Rigger;

/// <summary>
/// Grants RTS control while a portable rigger laptop is held in both hands.
/// </summary>
public sealed class RiggerLaptopSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _nextRefresh;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RiggerLaptopComponent, ItemWieldedEvent>(OnLaptopWielded);
        SubscribeLocalEvent<RiggerLaptopComponent, ItemUnwieldedEvent>(OnLaptopUnwielded);
        SubscribeLocalEvent<RiggerLaptopComponent, ComponentShutdown>(OnLaptopShutdown);
        SubscribeLocalEvent<RiggerLaptopUserComponent, RiggerToggleRTSModeActionEvent>(OnToggleRtsAction);
        SubscribeLocalEvent<RiggerLaptopUserComponent, ComponentShutdown>(OnUserShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;

        var query = EntityQueryEnumerator<RiggerLaptopUserComponent>();
        while (query.MoveNext(out var uid, out var user))
        {
            if (!TryComp<RiggerLaptopComponent>(user.Laptop, out var laptop))
            {
                RemCompDeferred<RiggerLaptopUserComponent>(uid);
                continue;
            }

            RefreshLinkedDrones((uid, user), (user.Laptop, laptop));
            SyncSessionOverrides((uid, user));
        }
    }

    private void OnLaptopWielded(Entity<RiggerLaptopComponent> ent, ref ItemWieldedEvent args)
    {
        var user = EnsureComp<RiggerLaptopUserComponent>(args.User);
        user.Laptop = ent.Owner;
        user.ToggleRtsAction = ent.Comp.ToggleRtsAction;
        user.RtsEnabled = false;

        RefreshLinkedDrones((args.User, user), ent);
        // The laptop action must remain available even when no drones are currently in range.
        // Drone availability is validated when the user attempts to enable RTS mode.
        _actions.AddAction(args.User, ref user.ToggleRtsActionEntity, user.ToggleRtsAction, args.User);
        Dirty(args.User, user);
        SyncSessionOverrides((args.User, user));
    }

    private void OnLaptopUnwielded(Entity<RiggerLaptopComponent> ent, ref ItemUnwieldedEvent args)
    {
        if (TryComp<RiggerLaptopUserComponent>(args.User, out var user) && user.Laptop == ent.Owner)
            RemCompDeferred<RiggerLaptopUserComponent>(args.User);
    }

    private void OnLaptopShutdown(Entity<RiggerLaptopComponent> ent, ref ComponentShutdown args)
    {
        var query = EntityQueryEnumerator<RiggerLaptopUserComponent>();
        while (query.MoveNext(out var uid, out var user))
        {
            if (user.Laptop == ent.Owner)
                RemCompDeferred<RiggerLaptopUserComponent>(uid);
        }
    }

    private void OnToggleRtsAction(Entity<RiggerLaptopUserComponent> ent, ref RiggerToggleRTSModeActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.Toggle = true;

        if (!TryComp<RiggerLaptopComponent>(ent.Comp.Laptop, out var laptop))
            return;

        RefreshLinkedDrones(ent, (ent.Comp.Laptop, laptop));
        if (!HasAnyLiveDrone(ent.Comp))
        {
            ent.Comp.RtsEnabled = false;
            _actions.SetToggled(ent.Comp.ToggleRtsActionEntity, false);
            Dirty(ent);
            _popup.PopupEntity(Loc.GetString("nc-rigger-console-no-drones"), ent, ent);
            return;
        }

        ent.Comp.RtsEnabled = !ent.Comp.RtsEnabled;
        _actions.SetToggled(ent.Comp.ToggleRtsActionEntity, ent.Comp.RtsEnabled);
        Dirty(ent);

        var text = ent.Comp.RtsEnabled
            ? Loc.GetString("nc-rigger-rts-enabled")
            : Loc.GetString("nc-rigger-rts-disabled");
        _popup.PopupEntity(text, ent, ent);
    }

    private void OnUserShutdown(Entity<RiggerLaptopUserComponent> ent, ref ComponentShutdown args)
    {
        RemoveSessionOverrides(ent);
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleRtsActionEntity);
    }

    private void RefreshLinkedDrones(Entity<RiggerLaptopUserComponent> user, Entity<RiggerLaptopComponent> laptop)
    {
        user.Comp.LinkedDrones.Clear();

        var query = EntityQueryEnumerator<RiggerDroneComponent>();
        while (query.MoveNext(out var droneUid, out var drone))
        {
            if (!BelongsToLaptop(droneUid, drone, user.Owner, laptop))
                continue;

            user.Comp.LinkedDrones.Add(droneUid);
        }

        Dirty(user);
    }

    private bool BelongsToLaptop(
        EntityUid droneUid,
        RiggerDroneComponent drone,
        EntityUid user,
        Entity<RiggerLaptopComponent> laptop)
    {
        if (!drone.Enabled || !HasAllowedDroneFaction(drone, laptop.Comp))
            return false;

        var userMap = Transform(user).MapUid;
        var droneMap = Transform(droneUid).MapUid;
        if (userMap == null || userMap != droneMap)
            return false;

        if (laptop.Comp.AutoLinkRange > 0f &&
            !Transform(droneUid).Coordinates.InRange(EntityManager, _transform, Transform(user).Coordinates, laptop.Comp.AutoLinkRange))
        {
            return false;
        }

        return true;
    }

    private bool HasAllowedDroneFaction(RiggerDroneComponent drone, RiggerLaptopComponent laptop)
    {
        if (laptop.AllowedDroneFactions.Count == 0)
            return true;

        return drone.DroneFactions.Overlaps(laptop.AllowedDroneFactions);
    }

    private bool HasAnyLiveDrone(RiggerLaptopUserComponent user)
    {
        foreach (var drone in user.LinkedDrones)
        {
            if (Exists(drone) && _mobState.IsAlive(drone))
                return true;
        }

        return false;
    }

    private void SyncSessionOverrides(Entity<RiggerLaptopUserComponent> ent)
    {
        if (!_player.TryGetSessionByEntity(ent.Owner, out var session))
            return;

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

    private void RemoveSessionOverrides(Entity<RiggerLaptopUserComponent> ent)
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
