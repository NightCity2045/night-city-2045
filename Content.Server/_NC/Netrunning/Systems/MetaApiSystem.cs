using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Content.Server.Doors.Systems;
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
using Content.Server.PDA;
using Content.Server._NC.Cyberware.Systems;
using Content.Shared._NC.Cyberware.Components;
using Content.Shared._NC.Cyberware;
using Content.Server.Chat.Managers;
using Content.Shared.Damage.Prototypes;
using Content.Server.Popups;
using Content.Shared.Popups;
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
using Content.Server._NC.CitiNet.Cartridges;
using Content.Shared.CartridgeLoader;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Live;
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
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly SurveillanceCameraSystem _cameraSystem = default!;
    [Dependency] private readonly VendingMachineSystem _vending = default!;

    private static readonly HashSet<string> AllowedOverrideKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOOR_STATE",
        "DOOR_OPEN",
        "DOOR_BOLT",
        "POWER_TOGGLE",
        "POWER",
        "APC_BREAKER",
        "SMES_OUTPUT",
        "TURRET_STATE",
        "TURRET_FACTION",
        "CAMERA_ACTIVE",
        "VENDING_MACHINE",
        "CYBERLIMB_LOCK",
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
        var found = new List<EntityUid>();
        foreach (var uid in _lookup.GetEntitiesInRange(target, 8f, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (uid == target)
                continue;

            if (HasComp<ApcPowerReceiverComponent>(uid) || HasComp<DoorComponent>(uid) || 
                HasComp<IceHealthComponent>(uid) || HasComp<ApcComponent>(uid) || 
                HasComp<DeployableTurretComponent>(uid))
                found.Add(uid);
        }

        return found;
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

    public bool Inject(EntityUid attacker, EntityUid target, int damage)
    {
        if (!TryComp<IceHealthComponent>(target, out var ice))
            return false;

        if (TryComp<CyberdeckComponent>(attacker, out var deck))
        {
            deck.TraceLevel = Math.Min(100, deck.TraceLevel + 10);
            Dirty(attacker, deck);
        }

        _daemon.NotifyIntrusion(target, attacker);
        ice.CurrentHealth = Math.Max(0, ice.CurrentHealth - Math.Max(0, damage));
        Dirty(target, ice);
        return true;
    }

    public bool Override(EntityUid target, string key, int value)
    {
        if (!AllowedOverrideKeys.Contains(key))
            return false;

        switch (key.ToUpperInvariant())
        {
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

            case "CYBERLIMB_LOCK":
                if (!TryComp<CyberwareComponent>(target, out var cyber))
                    return false;
                
                bool hasLimbs = false;
                foreach (var implant in cyber.InstalledImplants.Values)
                {
                    if (TryComp<CyberwareImplantComponent>(implant, out var imp) && 
                        (imp.Category == CyberwareCategory.LeftArm || imp.Category == CyberwareCategory.RightArm ||
                         imp.Category == CyberwareCategory.LeftLeg || imp.Category == CyberwareCategory.RightLeg))
                    {
                        hasLimbs = true;
                        break;
                    }
                }

                if (hasLimbs && value > 0)
                {
                    _stun.TryParalyze(target, TimeSpan.FromSeconds(2), true);
                    _popup.PopupEntity("Your cyberlimbs lock up!", target, target, PopupType.LargeCaution);
                }
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
        _stun.TryParalyze(target, TimeSpan.FromSeconds(1.5), true);
        SendNetrunningFeedback(target, "DUMPSHOCK", "Connection forcibly severed.", true);
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

    public bool Breach(EntityUid attacker, EntityUid target)
    {
        if (!TryComp<NetFirewallComponent>(target, out var firewall))
            return false;

        // In a full implementation, this would involve a progress bar or minigame.
        // For prototype: Immediate removal.
        QueueDel(target);
        
        MetaLog(attacker, $"BREACH SUCCESSFUL: Firewall at {target} bypassed.");
        return true;
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

        var physicalTarget = target;
        if (TryComp<NetAvatarComponent>(target, out var avatar) && avatar.PhysicalBody is { } body && !Deleted(body))
            physicalTarget = body;

        var spec = new DamageSpecifier();
        spec.DamageDict["Heat"] = amount;
        _damageable.TryChangeDamage(physicalTarget, spec, ignoreResistances: false, interruptsDoAfters: true);
        SendNetrunningFeedback(target, "NEURAL BURN", $"Digital feedback scorches your link for {amount}.", true);
    }

    private EntityUid? ResolveServer(EntityUid uid)
    {
        if (HasComp<NetServerComponent>(uid))
            return uid;

        if (TryComp<NetDeviceNodeComponent>(uid, out var node) && node.Server is { } server && !Deleted(server))
            return server;

        if (TryComp<NetDefenseComponent>(uid, out var defense) && defense.Server is { } defenseServer && !Deleted(defenseServer))
            return defenseServer;

        if (TryComp<NetModuleComponent>(uid, out var module) && module.Server is { } moduleServer && !Deleted(moduleServer))
            return moduleServer;

        return null;
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
        foreach (var uid in _lookup.GetEntitiesInRange(deckUid, Math.Max(1, radius), LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (uid == deckUid || Deleted(uid))
                continue;

            var cls = GetClass(uid);
            if (!cls.Contains(className, StringComparison.OrdinalIgnoreCase))
                continue;

            var dist = (_transform.GetWorldPosition(deckUid) - _transform.GetWorldPosition(uid)).Length();
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

    public string InterceptPda(EntityUid target)
    {
        if (!TryResolveCitiNetCartridge(target, out var cartridgeUid, out var cartridge))
            return "ERROR: No CitiNet traffic available.";

        var latestKind = string.Empty;
        var latestSender = string.Empty;
        var latestContent = string.Empty;
        var latestTimestamp = TimeSpan.MinValue;

        foreach (var history in cartridge.ChatHistories.Values)
        {
            if (history.Count == 0)
                continue;

            var msg = history[^1];
            if (msg.Timestamp <= latestTimestamp)
                continue;

            latestTimestamp = msg.Timestamp;
            latestKind = "P2P";
            latestSender = msg.SenderName;
            latestContent = msg.Content;
        }

        if (cartridge.GroupMessages.Count > 0)
        {
            var msg = cartridge.GroupMessages[^1];
            if (msg.Timestamp > latestTimestamp)
            {
                latestTimestamp = msg.Timestamp;
                latestKind = "GROUP";
                latestSender = msg.SenderName;
                latestContent = msg.Content;
            }
        }

        foreach (var history in cartridge.ChannelMessages)
        {
            if (history.Value.Count == 0)
                continue;

            var msg = history.Value[^1];
            if (msg.Timestamp <= latestTimestamp)
                continue;

            latestTimestamp = msg.Timestamp;
            latestKind = $"BBS:{history.Key}";
            latestSender = msg.SenderName;
            latestContent = msg.Content;
        }

        if (latestTimestamp == TimeSpan.MinValue)
            return "ERROR: No CitiNet traffic available.";

        return $"[{latestKind}] {latestSender}: {latestContent}";
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

    private bool TryResolveCitiNetCartridge(EntityUid target, out EntityUid cartridgeUid, out CitiNetCartridgeComponent cartridge)
    {
        cartridgeUid = EntityUid.Invalid;
        cartridge = default!;

        if (TryComp<CitiNetCartridgeComponent>(target, out CitiNetCartridgeComponent? directCartridge))
        {
            cartridgeUid = target;
            cartridge = directCartridge;
            return true;
        }

        if (!TryComp<CartridgeLoaderComponent>(target, out var loader))
            return false;

        if (loader.ActiveProgram is { } activeProgram &&
            TryComp<CitiNetCartridgeComponent>(activeProgram, out CitiNetCartridgeComponent? activeCartridge))
        {
            cartridgeUid = activeProgram;
            cartridge = activeCartridge;
            return true;
        }

        foreach (var programUid in loader.BackgroundPrograms)
        {
            if (!TryComp<CitiNetCartridgeComponent>(programUid, out CitiNetCartridgeComponent? backgroundCartridge))
                continue;

            cartridgeUid = programUid;
            cartridge = backgroundCartridge;
            return true;
        }

        return false;
    }
}
