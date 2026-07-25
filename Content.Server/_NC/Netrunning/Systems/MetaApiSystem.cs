using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Content.Server.Doors.Systems;
using Content.Server._NC.Netrunning.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.VendingMachines;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Stunnable;
using Robust.Server.GameObjects;
using Content.Server.Turrets;
using Content.Server.SurveillanceCamera;
using Content.Shared.Turrets;
using Content.Server.Chat.Managers;
using Content.Shared.Damage.Prototypes;
using Content.Server.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Log;
using Content.Shared.Access;
using Content.Shared.Chat;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.VendingMachines;
using Content.Shared._NC.Netrunning;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaApiSystem : EntitySystem, IMetaRuntimeApi
{
    private readonly ISawmill _sawmill = Logger.GetSawmill("meta");

    [Dependency] private readonly DoorSystem _doorSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly ApcSystem _apc = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaDaemonSystem _daemon = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly TurretTargetSettingsSystem _turretAccess = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SurveillanceCameraSystem _cameraSystem = default!;
    [Dependency] private readonly VendingMachineSystem _vending = default!;
    [Dependency] private readonly NetServerSystem _netServer = default!;

    private static readonly HashSet<string> AllowedOverrideKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOOR_STATE",
        "DOOR_OPEN",
        "DOOR_TOGGLE",
        "DOOR_BOLT",
        "SHORT_CIRCUIT",
        "POWER_TOGGLE",
        "POWER",
        "APC_BREAKER",
        "SMES_OUTPUT",
        "TURRET_STATE",
        "TURRET_FACTION",
        "CAMERA_ACTIVE",
        "VENDING_MACHINE",
    };

    private readonly Dictionary<EntityUid, EntityUid?> _eventSources = new();
    private readonly Dictionary<EntityUid, EntityUid?> _intruders = new();
    private readonly Dictionary<EntityUid, EntityUid?> _activeUsers = new();
    public EntityUid? GetTarget(EntityUid deckUid)
    {
        return TryComp<CyberdeckComponent>(deckUid, out var deck) ? deck.ActiveTarget : null;
    }

    public EntityUid? GetServer(EntityUid deckUid)
    {
        return TryComp<CyberdeckComponent>(deckUid, out var deck) ? deck.ActiveServer : null;
    }

    public EntityUid GetSelf(EntityUid deckUid) => deckUid;

    public int GetIce(EntityUid target)
    {
        return TryComp<IceHealthComponent>(target, out var ice) ? ice.CurrentHealth : 0;
    }

    public IReadOnlyList<EntityUid> GetConnected(EntityUid target)
    {
        var serverUid = ResolveServer(target);
        if (serverUid is not { } server)
            return Array.Empty<EntityUid>();

        var connected = new List<EntityUid>();
        foreach (var uid in _netServer.CollectNetworkDevices(server))
        {
            if (uid == target || Deleted(uid))
                continue;

            connected.Add(uid);
        }

        return connected;
    }

    public string GetClass(EntityUid target)
    {
        if (HasComp<NetServerComponent>(target))
            return "SERVER";

        if (TryComp<NetDeviceNodeComponent>(target, out var node))
        {
            return node.Kind switch
            {
                NetDeviceNodeKind.Door => "DOOR",
                NetDeviceNodeKind.CameraGroup => "CAMERA",
                NetDeviceNodeKind.DataGate => "DATA_GATE",
                _ => "DEVICE",
            };
        }

        if (TryComp<NetDefenseComponent>(target, out var defense))
        {
            return defense.Kind switch
            {
                NetDefenseKind.BlackIce => "BLACK_ICE",
                NetDefenseKind.Demon => "DEMON",
                _ => "ICE",
            };
        }

        if (HasComp<NetAvatarComponent>(target))
            return "AVATAR";

        if (HasComp<DoorComponent>(target))
            return "DOOR";

        if (HasComp<SurveillanceCameraComponent>(target))
            return "CAMERA";

        if (HasComp<ApcComponent>(target))
            return "APC";

        if (HasComp<PowerNetworkBatteryComponent>(target))
            return "SMES";

        if (HasComp<DeployableTurretComponent>(target))
            return "TURRET";

        if (HasComp<VendingMachineComponent>(target))
            return "VENDING";

        var meta = MetaData(target);
        return meta.EntityPrototype?.ID ?? meta.EntityName;
    }

    public MetaIntrusionWait? Inject(EntityUid attacker, EntityUid target, int damage, bool bypassDefense = false)
    {
        if (!TryComp<IceHealthComponent>(target, out var ice))
            return null;

        if (TryComp<CyberdeckComponent>(attacker, out var deck))
        {
            deck.TraceLevel = Math.Min(100, deck.TraceLevel + 10);
            Dirty(attacker, deck);
        }

        if (!bypassDefense && _daemon.TryBeginIntrusion(
                target,
                attacker,
                MetaIntrusionOperationKind.Inject,
                damage,
                out var wait))
        {
            return wait;
        }

        ice.CurrentHealth = Math.Max(0, ice.CurrentHealth - Math.Max(0, damage));
        Dirty(target, ice);
        return null;
    }

    public bool Override(EntityUid target, string key, int value)
    {
        if (!AllowedOverrideKeys.Contains(key))
            return false;

        switch (key.ToUpperInvariant())
        {
            case "DOOR_TOGGLE":
                if (!TryComp<DoorComponent>(target, out var toggleDoor))
                    return false;

                _doorSystem.TryToggleDoor(target, toggleDoor);
                return true;

            case "DOOR_STATE":
            case "DOOR_OPEN":
                if (!TryComp<DoorComponent>(target, out var door))
                    return false;

                if (value == 0)
                    _doorSystem.TryClose(target, door);
                else
                    _doorSystem.TryOpen(target, door);
                return true;

            case "DOOR_BOLT":
                if (!TryComp<DoorBoltComponent>(target, out var bolts))
                    return false;

                _doorSystem.SetBoltsDown((target, bolts), value != 0);
                return true;

            case "SHORT_CIRCUIT":
                if (!TryComp<DoorComponent>(target, out _))
                    return false;

                if (TryComp<ApcPowerReceiverComponent>(target, out var shortReceiver) &&
                    !shortReceiver.PowerDisabled)
                {
                    _powerReceiver.TogglePower(target, playSwitchSound: false, receiver: shortReceiver);
                }

                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/sparks4.ogg"), target);

                var thermalDamage = new DamageSpecifier();
                thermalDamage.DamageDict["Heat"] = Math.Max(5, value);

                foreach (var uid in _lookup.GetEntitiesInRange(target, 1.25f, LookupFlags.Dynamic))
                {
                    if (uid == target || !TryComp<DamageableComponent>(uid, out _))
                        continue;

                    _damageable.TryChangeDamage(uid, thermalDamage, origin: target);
                    SendNetrunningFeedback(uid, "SHORT CIRCUIT", "Electrical arc and heat burst erupt from the hacked door.", true);
                }

                return true;

            case "POWER_TOGGLE":
            case "POWER":
                if (!TryComp<ApcPowerReceiverComponent>(target, out var receiver))
                    return false;

                if (key.Equals("POWER_TOGGLE", StringComparison.OrdinalIgnoreCase))
                {
                    _powerReceiver.TogglePower(target, playSwitchSound: false, receiver: receiver);
                }
                else if (receiver.PowerDisabled != (value == 0))
                {
                    _powerReceiver.TogglePower(target, playSwitchSound: false, receiver: receiver);
                }
                return true;

            case "APC_BREAKER":
                if (!TryComp<ApcComponent>(target, out var apc))
                    return false;
                _apc.ApcToggleBreaker(target, apc);
                return true;

            case "SMES_OUTPUT":
                if (!TryComp<PowerNetworkBatteryComponent>(target, out var battery))
                    return false;

                battery.MaxSupply = Math.Max(0, value);
                Dirty(target, battery);
                return true;

            case "TURRET_STATE":
                if (!TryComp<DeployableTurretComponent>(target, out var turret))
                    return false;
                if (TryComp<ApcPowerReceiverComponent>(target, out var tReceiver))
                    _powerReceiver.TogglePower(target, false, tReceiver);
                return true;

            case "TURRET_FACTION":
                if (!TryComp<TurretTargetSettingsComponent>(target, out var tAccess))
                    return false;
                if (value == 0)
                {
                    _turretAccess.SyncAccessLevelExemptions(tAccess, new List<ProtoId<AccessLevelPrototype>>());
                }
                return true;

            case "CAMERA_ACTIVE":
                if (!TryComp<SurveillanceCameraComponent>(target, out var camera))
                    return false;

                _cameraSystem.SetActive(target, value != 0, camera);
                return true;

            case "VENDING_MACHINE":
                if (!TryComp<VendingMachineComponent>(target, out var vending))
                    return false;

                _vending.SetShooting(target, value != 0, vending);
                if (value != 0)
                    _vending.EjectRandom(target, throwItem: true, forceEject: true, vendComponent: vending);
                Dirty(target, vending);
                return true;

        }

        return false;
    }

    public int GetTrace(EntityUid deckUid)
    {
        return TryComp<CyberdeckComponent>(deckUid, out var deck) ? deck.TraceLevel : 0;
    }

    public void Cloak(EntityUid deckUid, int strength)
    {
        if (!TryComp<CyberdeckComponent>(deckUid, out var deck))
            return;

        deck.TraceLevel = Math.Max(0, deck.TraceLevel - Math.Max(0, strength));
        Dirty(deckUid, deck);
    }

    public void Ping(EntityUid target)
    {
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/sparks4.ogg"), target);
        SendNetrunningFeedback(target, "PING", "Network pulse detected.", false);
    }

    public EntityUid? GetIntruder(EntityUid deckUid)
    {
        return _intruders.GetValueOrDefault(deckUid);
    }

    public void BurnNeuroport(EntityUid target, int damage)
    {
        ApplyNeuralDamage(target, damage);
    }

    public void Disconnect(EntityUid target)
    {
        _daemon.CancelIntrusions(target);
        if (TryComp<CyberdeckComponent>(target, out var deck))
        {
            deck.ActiveTarget = null;
            Dirty(target, deck);
        }

        var feedbackTarget = ResolveFeedbackTarget(target);
        _stun.TryParalyze(feedbackTarget, TimeSpan.FromSeconds(1.5), true);
        SendNetrunningFeedback(feedbackTarget, "DUMPSHOCK", "Connection forcibly severed.", true);
    }

    public bool IsValid(EntityUid target)
    {
        return EntityManager.EntityExists(target) && !Deleted(target);
    }

    public EntityUid? GetEventSource(EntityUid deckUid)
    {
        return _eventSources.GetValueOrDefault(deckUid);
    }

    public void SetEventSource(EntityUid hostUid, EntityUid? source)
    {
        _eventSources[hostUid] = source;
    }

    public MetaIntrusionWait? Breach(EntityUid attacker, EntityUid target, bool bypassDefense = false)
    {
        if (!TryComp<NetFirewallComponent>(target, out var firewall))
            return null;

        if (!bypassDefense && _daemon.TryBeginIntrusion(
                target,
                attacker,
                MetaIntrusionOperationKind.Breach,
                0,
                out var wait))
        {
            return wait;
        }

        QueueDel(target);
        MetaLog(attacker, $"BREACH SUCCESSFUL: Firewall at {target} bypassed.");
        return null;
    }

    public bool CompleteIntrusion(MetaIntrusionTransaction transaction)
    {
        if (Deleted(transaction.Target))
            return false;

        switch (transaction.Operation)
        {
            case MetaIntrusionOperationKind.Inject:
                if (!TryComp<IceHealthComponent>(transaction.Target, out var ice))
                    return false;

                ice.CurrentHealth = Math.Max(0, ice.CurrentHealth - Math.Max(0, transaction.Value));
                Dirty(transaction.Target, ice);
                return true;

            case MetaIntrusionOperationKind.Breach:
                if (!HasComp<NetFirewallComponent>(transaction.Target))
                    return false;

                QueueDel(transaction.Target);
                MetaLog(transaction.Intruder,
                    $"BREACH SUCCESSFUL: Firewall at {transaction.Target} bypassed.");
                return true;

            case MetaIntrusionOperationKind.Program:
            case MetaIntrusionOperationKind.Immersion:
            case MetaIntrusionOperationKind.Admin:
                return true;

            default:
                return false;
        }
    }

    public bool HasRoot(EntityUid deckUid, EntityUid serverUid)
    {
        var server = ResolveServer(serverUid);
        return server != null &&
               TryComp<CyberdeckComponent>(deckUid, out var deck) &&
               deck.HackedNetworks.Contains(server.Value);
    }

    public bool TryRoot(EntityUid deckUid, EntityUid serverUid, int strength)
    {
        var server = ResolveServer(serverUid);
        if (server == null ||
            !TryComp<NetServerComponent>(server.Value, out var serverComp) ||
            !TryComp<CyberdeckComponent>(deckUid, out var deck))
            return false;

        _daemon.NotifyIntrusion(server.Value, deckUid);

        if (strength < serverComp.RootDifficulty)
        {
            MetaLog(deckUid, $"ROOT FAILED: strength {strength}/{serverComp.RootDifficulty}.");
            return false;
        }

        deck.HackedNetworks.Add(server.Value);
        Dirty(deckUid, deck);
        MetaLog(deckUid, $"ROOT GRANTED: {ToPrettyString(server.Value)}.");
        return true;
    }

    public EntityUid? SpawnIce(EntityUid deckUid, EntityUid anchor, int strength, bool blackIce)
    {
        var load = DefenseLoad(strength, blackIce ? 2 : 1);
        var serverUid = ResolveServer(anchor);
        if (serverUid == null || !CanHostDefense(deckUid, serverUid.Value, load, out var server))
            return null;

        var coords = GetDefenseSpawnCoordinates(anchor, server);
        var uid = Spawn(blackIce ? "NCNetBlackIce" : "NCNetIce", coords);
        var defense = EnsureComp<NetDefenseComponent>(uid);
        defense.Server = serverUid;
        defense.OwnerDeck = deckUid;
        defense.ReservedLoad = load;
        defense.Kind = blackIce ? NetDefenseKind.BlackIce : NetDefenseKind.Ice;

        var ice = EnsureComp<IceHealthComponent>(uid);
        ice.MaxHealth = Math.Max(25, strength);
        ice.CurrentHealth = ice.MaxHealth;

        ReserveDefense(serverUid.Value, server, uid, defense.ReservedLoad);
        MetaLog(deckUid, $"{(blackIce ? "BLACK ICE" : "ICE")} spawned: load {defense.ReservedLoad}.");
        return uid;
    }

    public EntityUid? SpawnDemon(EntityUid deckUid, EntityUid anchor, int strength)
    {
        var load = DefenseLoad(strength, 3);
        var serverUid = ResolveServer(anchor);
        if (serverUid == null || !CanHostDefense(deckUid, serverUid.Value, load, out var server))
            return null;

        var uid = Spawn("NCNetDemon", GetDefenseSpawnCoordinates(anchor, server));
        var defense = EnsureComp<NetDefenseComponent>(uid);
        defense.Server = serverUid;
        defense.OwnerDeck = deckUid;
        defense.ReservedLoad = load;
        defense.Kind = NetDefenseKind.Demon;

        var demon = EnsureComp<NetDemonComponent>(uid);
        demon.Damage = Math.Max(1, strength / 10);

        ReserveDefense(serverUid.Value, server, uid, defense.ReservedLoad);
        MetaLog(deckUid, $"DEMON spawned: load {defense.ReservedLoad}.");
        return uid;
    }

    public void ApplyNeuralDamage(EntityUid target, int damage)
    {
        var amount = Math.Max(0, damage);
        if (amount == 0)
            return;

        var physicalTarget = ResolveFeedbackTarget(target);

        var spec = new DamageSpecifier();
        spec.DamageDict["Heat"] = amount;
        _damageable.TryChangeDamage(physicalTarget, spec, ignoreResistances: false, interruptsDoAfters: true);
        SendNetrunningFeedback(physicalTarget, "NEURAL BURN", $"Digital feedback scorches your link for {amount}.", true);
    }

    public EntityUid ResolveFeedbackTarget(EntityUid target)
    {
        if (TryComp<NetAvatarComponent>(target, out var avatar) &&
            avatar.PhysicalBody is { } body &&
            !Deleted(body))
            return body;

        if (_activeUsers.TryGetValue(target, out var activeUser) &&
            activeUser is { } user &&
            !Deleted(user))
        {
            if (TryComp<NetAvatarComponent>(user, out var activeAvatar) &&
                activeAvatar.PhysicalBody is { } activeBody &&
                !Deleted(activeBody))
            {
                return activeBody;
            }

            return user;
        }

        return target;
    }

    public void SendDefenseWarning(EntityUid target, EntityUid defenseHost)
    {
        SendNetrunningFeedback(
            target,
            Loc.GetString("netrunning-defense-warning-title"),
            Loc.GetString("netrunning-defense-warning-message", ("defense", Name(defenseHost))),
            true);
    }

    private EntityUid? ResolveServer(EntityUid uid)
    {
        return _netServer.ResolveNetworkServer(uid);
    }

    private bool CanHostDefense(EntityUid deckUid, EntityUid serverUid, int load, out NetServerComponent server)
    {
        server = default!;
        if (!HasRoot(deckUid, serverUid) ||
            !TryComp<NetServerComponent>(serverUid, out var serverComp) ||
            serverComp.DigitalGrid == null)
            return false;

        server = serverComp;
        return server.UsedLoad + load <= server.MaxLoad;
    }

    private int DefenseLoad(int strength, int multiplier)
    {
        return Math.Max(1, (Math.Max(1, strength) + 24) / 25 * multiplier);
    }

    private EntityCoordinates GetDefenseSpawnCoordinates(EntityUid anchor, NetServerComponent server)
    {
        var anchorXform = Transform(anchor);
        if (anchorXform.GridUid != null)
            return anchorXform.Coordinates;

        var grid = server.DigitalGrid ?? EntityUid.Invalid;
        return new EntityCoordinates(grid, 0, 0);
    }

    private void ReserveDefense(EntityUid serverUid, NetServerComponent server, EntityUid defenseUid, int load)
    {
        server.UsedLoad += load;
        server.SpawnedDefenses.Add(defenseUid);
        Dirty(serverUid, server);
    }

    public void SetIntruder(EntityUid hostUid, EntityUid? intruder)
    {
        _intruders[hostUid] = intruder;
    }

    public void SetUser(EntityUid deckUid, EntityUid? userUid)
    {
        _activeUsers[deckUid] = userUid;
    }

    public EntityUid? FindNearest(EntityUid deckUid, string className, int radius)
    {
        EntityUid? best = null;
        float bestDist = float.MaxValue;

        var origin = GetTarget(deckUid) is { } target && !Deleted(target)
            ? target
            : deckUid;

        var candidates = GetServer(deckUid) is { } serverUid && !Deleted(serverUid)
            ? _netServer.CollectNetworkDevices(serverUid)
            : _lookup.GetEntitiesInRange(origin, Math.Max(1, radius), LookupFlags.Dynamic | LookupFlags.Sundries).ToHashSet();

        foreach (var uid in candidates)
        {
            if (uid == deckUid || uid == origin || Deleted(uid))
                continue;

            var cls = GetClass(uid);
            if (!cls.Contains(className, StringComparison.OrdinalIgnoreCase))
                continue;

            var dist = (_transform.GetWorldPosition(origin) - _transform.GetWorldPosition(uid)).Length();
            if (dist > Math.Max(1, radius))
                continue;

            if (dist < bestDist)
            {
                best = uid;
                bestDist = dist;
            }
        }

        return best;
    }

    public IReadOnlyList<string> GetFiles(EntityUid target)
    {
        if (TryComp<NetFileStoreComponent>(target, out var store))
            return store.Files;

        return Array.Empty<string>();
    }

    public bool Download(EntityUid deckUid, EntityUid target, string fileId)
    {
        if (!IsValid(target) ||
            !TryComp<CyberdeckComponent>(deckUid, out var deck) ||
            !TryComp<NetFileStoreComponent>(target, out var store) ||
            !store.Files.Any(file => file.Equals(fileId, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!deck.StoredFiles.Any(file => file.Equals(fileId, StringComparison.OrdinalIgnoreCase)) &&
            deck.StoredFiles.Count >= deck.StorageCapacity)
            return false;

        if (!deck.StoredFiles.Any(file => file.Equals(fileId, StringComparison.OrdinalIgnoreCase)))
            deck.StoredFiles.Add(fileId);

        deck.TraceLevel = Math.Min(100, deck.TraceLevel + 5);
        Dirty(deckUid, deck);
        return true;
    }

    public bool Upload(EntityUid deckUid, EntityUid target, string fileId)
    {
        if (!IsValid(target) ||
            !TryComp<CyberdeckComponent>(deckUid, out var deck) ||
            !deck.StoredFiles.Any(file => file.Equals(fileId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var store = EnsureComp<NetFileStoreComponent>(target);
        if (!store.Files.Any(file => file.Equals(fileId, StringComparison.OrdinalIgnoreCase)))
        {
            store.Files.Add(fileId);
            Dirty(target, store);
        }

        deck.TraceLevel = Math.Min(100, deck.TraceLevel + 5);
        Dirty(deckUid, deck);
        return true;
    }

    public IReadOnlyList<int> GetVitals(EntityUid target)
    {
        if (!TryComp<DamageableComponent>(target, out var damageable))
            return Array.Empty<int>();

        var list = new List<int>();
        list.Add((int)damageable.TotalDamage);
        
        if (damageable.DamagePerGroup.TryGetValue("Brute", out var brute)) list.Add((int)brute);
        else list.Add(0);
        
        if (damageable.DamagePerGroup.TryGetValue("Burn", out var burn)) list.Add((int)burn);
        else list.Add(0);

        if (damageable.DamagePerGroup.TryGetValue("Toxin", out var toxin)) list.Add((int)toxin);
        else list.Add(0);

        return list;
    }

    public void MetaLog(EntityUid deckUid, string text)
    {
        _sawmill.Info($"[{ToPrettyString(deckUid)}] {text}");
        
        _ui.ServerSendUiMessage(deckUid, CyberdeckUiKey.Key, new CyberdeckLogMessage(text));

        if (_activeUsers.TryGetValue(deckUid, out var userUid) && userUid != null)
        {
            if (TryComp<ActorComponent>(userUid, out var actor))
            {
                var message = $"{text}";
                _chatManager.DispatchServerMessage(actor.PlayerSession, message);
            }
        }
    }

    private void SendNetrunningFeedback(EntityUid target, string title, string message, bool critical)
    {
        var eventTarget = target;
        if (TryComp<NetAvatarComponent>(target, out var avatar) &&
            avatar.PhysicalBody is { } body &&
            !Deleted(body))
        {
            eventTarget = body;
        }

        RaiseNetworkEvent(new NetrunningFeedbackEvent(title, message, critical), eventTarget);
    }
}
