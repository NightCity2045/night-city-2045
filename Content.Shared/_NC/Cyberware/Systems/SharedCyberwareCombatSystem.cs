using Content.Shared._NC.Armor.Components;
using Content.Shared._NC.Cyberware.Components;
using Content.Shared._NC.Cyberware.Events;
using Content.Shared._NC.Trail;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._NC.Cyberware.Systems;

/// <summary>
/// Handles combat-side cyberware effects such as dodge implants and subdermal armor.
/// </summary>
public sealed class SharedCyberwareCombatSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityManager _entManager = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    private readonly List<(EntityUid Uid, TimeSpan RemoveTime)> _activeTrails = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberwareComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, CyberwareComponent component, DamageModifyEvent args)
    {
        var isAttack = args.Origin != null && args.Origin != uid;

        if (isAttack)
        {
            foreach (var implantUid in component.InstalledImplants.Values)
            {
                if (!_entManager.TryGetComponent<CyberwareDodgeComponent>(implantUid, out var dodge))
                    continue;

                if (_timing.CurTime < dodge.NextDodgeTime || !_random.Prob(dodge.Chance))
                    continue;

                args.Damage *= 0;
                dodge.NextDodgeTime = _timing.CurTime + TimeSpan.FromSeconds(dodge.Cooldown);
                Dirty(implantUid, dodge);

                if (_netManager.IsServer)
                {
                    RaiseNetworkEvent(new CyberwareDodgeEvent(GetNetEntity(uid)));
                    TriggerDodgeTrail(uid);
                }

                return;
            }
        }

        if (!TryGetArmorAttack(args, out var attack))
            return;

        foreach (var implantUid in component.InstalledImplants.Values)
        {
            if (!_entManager.TryGetComponent<PhysicalArmorComponent>(implantUid, out var armor))
                continue;

            ApplySubdermalArmor(implantUid, armor, args, attack);
        }
    }

    private void ApplySubdermalArmor(
        EntityUid implantUid,
        PhysicalArmorComponent armor,
        DamageModifyEvent args,
        PhysicalArmorAttack attack)
    {
        var targetPart = args.TargetPart ?? TargetBodyPart.Torso;
        if (!IsActive(armor) || !Covers(armor, targetPart))
            return;

        DamageArmor(armor, attack.Damage);
        Dirty(implantUid, armor);

        if (attack.Kind == PhysicalArmorAttackKind.Blunt)
        {
            // Per GDD, blunt force travels through subdermal chrome into bones and organs.
            return;
        }

        var sp = GetEffectiveStoppingPower(armor);
        if (attack.Penetration > sp)
            args.Damage = ConvertToDamage(ApplyPenetrationDamageReduction(attack.Damage, sp), attack.Ballistic ? "Piercing" : attack.DamageType);
        else
            args.Damage = new DamageSpecifier();
    }

    private bool TryGetArmorAttack(DamageModifyEvent args, out PhysicalArmorAttack attack)
    {
        var total = args.Damage.GetTotal().Float();

        if (args.DamageSource is { } source
            && _entManager.TryGetComponent<BallisticProjectileComponent>(source, out var projectile))
        {
            attack = new PhysicalArmorAttack(
                PhysicalArmorAttackKind.Penetrating,
                projectile.Damage > 0f ? projectile.Damage : total,
                projectile.Penetration,
                "Piercing",
                Ballistic: true);

            return true;
        }

        if (HasPositiveDamage(args.Damage, "Blunt"))
        {
            attack = new PhysicalArmorAttack(PhysicalArmorAttackKind.Blunt, total, 0f, "Blunt", Ballistic: false);
            return true;
        }

        if (HasPositiveDamage(args.Damage, "Slash"))
        {
            attack = new PhysicalArmorAttack(PhysicalArmorAttackKind.Penetrating, total, 0f, "Slash", Ballistic: false);
            return true;
        }

        if (HasPositiveDamage(args.Damage, "Piercing"))
        {
            attack = new PhysicalArmorAttack(PhysicalArmorAttackKind.Penetrating, total, 0f, "Piercing", Ballistic: false);
            return true;
        }

        attack = default;
        return false;
    }

    private static bool HasPositiveDamage(DamageSpecifier damage, string damageType)
    {
        return damage.DamageDict.TryGetValue(damageType, out var amount) && amount > FixedPoint2.Zero;
    }

    private static bool Covers(PhysicalArmorComponent armor, TargetBodyPart targetPart)
    {
        foreach (var coveredPart in armor.Coverage)
        {
            if ((coveredPart & targetPart) != 0)
                return true;
        }

        return false;
    }

    private static bool IsActive(PhysicalArmorComponent armor)
    {
        return armor.CurrentDurability > 0f && (armor.StoppingPower > 0f || armor.BluntDamageMultiplier < 1f);
    }

    private static void DamageArmor(PhysicalArmorComponent armor, float impactDamage)
    {
        var durabilityDamage = impactDamage * MathF.Max(armor.DurabilityDamageMultiplier, 0f);
        armor.CurrentDurability = MathF.Max(0f, armor.CurrentDurability - durabilityDamage);
    }

    private static float GetEffectiveStoppingPower(PhysicalArmorComponent armor)
    {
        if (armor.MaxDurability <= 0f)
            return 0f;

        var durabilityRatio = Math.Clamp(armor.CurrentDurability / armor.MaxDurability, 0f, 1f);
        return MathF.Max(armor.StoppingPower, 0f) * durabilityRatio;
    }

    private static float ApplyPenetrationDamageReduction(float damage, float effectiveStoppingPower)
    {
        // SP and penetration use a 0-100 scale; use SP as a percentage reducer so high-SP chrome
        // does not erase all body damage after a successful penetration.
        var reduction = Math.Clamp(effectiveStoppingPower / 100f, 0f, 0.95f);
        return MathF.Max(0f, damage * (1f - reduction));
    }

    private static DamageSpecifier ConvertToDamage(float amount, string damageType)
    {
        var converted = new DamageSpecifier();

        if (amount > 0f)
            converted.DamageDict[damageType] = FixedPoint2.New(amount);

        return converted;
    }

    private void TriggerDodgeTrail(EntityUid uid)
    {
        var trail = EnsureComp<TrailComponent>(uid);
        trail.RenderedEntity = uid;
        trail.Color = Color.FromHex("#00FFFF").WithAlpha(0.5f);
        trail.Frequency = 0.03f;
        trail.Lifetime = 0.2f;
        trail.AlphaLerpAmount = 0.2f;
        _activeTrails.Add((uid, _timing.CurTime + TimeSpan.FromSeconds(0.2)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_netManager.IsServer || _activeTrails.Count == 0)
            return;

        var curTime = _timing.CurTime;
        for (var i = _activeTrails.Count - 1; i >= 0; i--)
        {
            var (uid, removeTime) = _activeTrails[i];
            if (curTime < removeTime)
                continue;

            if (Exists(uid))
                RemComp<TrailComponent>(uid);

            _activeTrails.RemoveAt(i);
        }
    }

    private readonly record struct PhysicalArmorAttack(
        PhysicalArmorAttackKind Kind,
        float Damage,
        float Penetration,
        string DamageType,
        bool Ballistic);

    private enum PhysicalArmorAttackKind : byte
    {
        Penetrating,
        Blunt,
    }
}
