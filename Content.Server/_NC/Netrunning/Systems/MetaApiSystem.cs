using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Content.Server.Doors.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Stunnable;
using Robust.Server.GameObjects;
using Content.Server.Turrets;
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
using Robust.Shared.Prototypes;
using Content.Shared.Access;
using Content.Shared.Chat;
using Robust.Shared.Player;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaApiSystem : EntitySystem, IMetaRuntimeApi
{
    [Dependency] private readonly DoorSystem _doorSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly ApcSystem _apc = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaDaemonSystem _daemon = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly MetaVirtualMachineSystem _vm = default!;
    [Dependency] private readonly TurretTargetSettingsSystem _turretAccess = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    private static readonly HashSet<string> AllowedOverrideKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOOR_STATE",
        "POWER_TOGGLE",
        "APC_BREAKER",
        "TURRET_STATE",
        "TURRET_FACTION",
        "CYBERLIMB_LOCK",
    };

    private readonly Dictionary<EntityUid, EntityUid?> _eventSources = new();
    private readonly Dictionary<EntityUid, EntityUid?> _intruders = new();
    private readonly Dictionary<EntityUid, EntityUid?> _activeUsers = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _deckFiles = new();

    public EntityUid? GetTarget(EntityUid deckUid)
    {
        return TryComp<CyberdeckComponent>(deckUid, out var deck) ? deck.ActiveTarget : null;
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
        return TryComp<MetaDataComponent>(target, out var meta) ? meta.EntityPrototype?.ID ?? meta.EntityName : string.Empty;
    }

    public bool Inject(EntityUid attacker, EntityUid target, int damage)
    {
        if (!TryComp<IceHealthComponent>(target, out var ice))
            return false;

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
                if (!TryComp<DoorComponent>(target, out var door))
                    return false;

                if (value == 0)
                    _doorSystem.TryClose(target, door);
                else
                    _doorSystem.TryOpen(target, door);
                return true;

            case "POWER_TOGGLE":
                if (!TryComp<ApcPowerReceiverComponent>(target, out var receiver))
                    return false;

                _powerReceiver.TogglePower(target, playSwitchSound: false, receiver: receiver);
                return true;

            case "APC_BREAKER":
                if (!TryComp<ApcComponent>(target, out var apc))
                    return false;
                _apc.ApcToggleBreaker(target, apc);
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

    public int GetTrace(EntityUid deckUid) => 0;

    public void Cloak(EntityUid deckUid, int strength) { }

    public void Ping(EntityUid target)
    {
        _audio.PlayPvs("/Audio/Effects/sparks4.ogg", target);
    }

    public EntityUid? GetIntruder(EntityUid deckUid)
    {
        return _intruders.GetValueOrDefault(deckUid);
    }

    public void BurnNeuroport(EntityUid target, int damage)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict["Heat"] = Math.Max(0, damage);
        _damageable.TryChangeDamage(target, spec, ignoreResistances: false, interruptsDoAfters: true);
    }

    public void Disconnect(EntityUid target)
    {
        _stun.TryParalyze(target, TimeSpan.FromSeconds(1.5), true);
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
        return new[] { $"syslog_{target.Id}", $"access_{target.Id}" };
    }

    public bool Download(EntityUid deckUid, EntityUid target, string fileId)
    {
        if (!IsValid(target))
            return false;
        if (!_deckFiles.TryGetValue(deckUid, out var files))
        {
            files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _deckFiles[deckUid] = files;
        }
        files.Add(fileId);
        return true;
    }

    public bool Upload(EntityUid deckUid, EntityUid target, string fileId)
    {
        if (!IsValid(target))
            return false;
        return _deckFiles.TryGetValue(deckUid, out var files) && files.Contains(fileId);
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
        return "ERROR: Message encrypted.";
    }

    public void MetaLog(EntityUid deckUid, string text)
    {
        Logger.InfoS("meta", $"[{ToPrettyString(deckUid)}] {text}");
        
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
}
