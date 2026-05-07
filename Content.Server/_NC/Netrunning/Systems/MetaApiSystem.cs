using Content.Server.Doors.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Content.Shared.Stunnable;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;

namespace Content.Server._NC.Netrunning.Systems;

public sealed class MetaApiSystem : EntitySystem, IMetaRuntimeApi
{
    [Dependency] private readonly DoorSystem _doorSystem = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MetaDaemonSystem _daemon = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    private static readonly HashSet<string> AllowedOverrideKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOOR_STATE",
        "POWER_TOGGLE",
        "TURRET_FACTION",
    };

    private readonly Dictionary<EntityUid, EntityUid?> _eventSources = new();
    private readonly Dictionary<EntityUid, EntityUid?> _intruders = new();
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
        if (!TryComp<TransformComponent>(target, out var xform))
            return Array.Empty<EntityUid>();

        var found = new List<EntityUid>();
        foreach (var uid in _lookup.GetEntitiesInRange(xform.Coordinates, 6f, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (uid == target)
                continue;

            if (HasComp<ApcPowerReceiverComponent>(uid) || HasComp<DoorComponent>(uid) || HasComp<IceHealthComponent>(uid))
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

            case "TURRET_FACTION":
                return false;
        }

        return false;
    }

    public int GetTrace(EntityUid deckUid)
    {
        return 0;
    }

    public void Cloak(EntityUid deckUid, int strength)
    {
        // Reserved for stealth/trace systems integration.
    }

    public void Ping(EntityUid target)
    {
        // Reserved for telemetry/event hooks.
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

    public void SetIntruder(EntityUid hostUid, EntityUid? intruder)
    {
        _intruders[hostUid] = intruder;
    }

    public EntityUid? FindNearest(EntityUid deckUid, string className, int radius)
    {
        if (!TryComp<TransformComponent>(deckUid, out var xform))
            return null;

        EntityUid? best = null;
        float bestDist = float.MaxValue;
        foreach (var uid in _lookup.GetEntitiesInRange(xform.Coordinates, Math.Max(1, radius), LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (uid == deckUid || Deleted(uid))
                continue;

            var cls = GetClass(uid);
            if (!cls.Contains(className, StringComparison.OrdinalIgnoreCase))
                continue;

            if (xform.Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var dist) && dist < bestDist)
            {
                best = uid;
                bestDist = dist;
            }
        }

        return best;
    }

    public IReadOnlyList<string> GetFiles(EntityUid target)
    {
        // Placeholder deterministic file catalog tied to entity id.
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

    public void Log(EntityUid deckUid, string text)
    {
        Logger.InfoS("meta", $"[{ToPrettyString(deckUid)}] {text}");
    }
}
