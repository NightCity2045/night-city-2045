using Content.Shared._NC.Armor.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;

namespace Content.Server._NC.Armor;

/// <summary>
/// Applies the NC physical armor GDD to worn armor and armor plates inserted into it.
/// </summary>
public sealed class PhysicalArmorSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PhysicalArmorComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);
        SubscribeLocalEvent<PhysicalArmorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnDamageModify(Entity<PhysicalArmorComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        var damageArgs = args.Args;
        if (damageArgs.TargetPart == TargetBodyPart.All)
            return;

        if (!TryGetArmorAttack(damageArgs, out var attack) || attack.Damage <= 0f)
            return;

        var targetPart = damageArgs.TargetPart ?? TargetBodyPart.Torso;

        if (TryApplyLayer(ent, targetPart, ref attack, out var finalDamage))
        {
            damageArgs.Damage = finalDamage;
            return;
        }

        if (TryComp<ItemSlotsComponent>(ent, out var slots))
        {
            foreach (var slot in slots.Slots.Values)
            {
                if (slot.Item is not { } plate || !TryComp<PhysicalArmorComponent>(plate, out var plateArmor))
                    continue;

                if (!TryApplyLayer((plate, plateArmor), targetPart, ref attack, out finalDamage))
                    continue;

                damageArgs.Damage = finalDamage;
                return;
            }
        }

        if (attack.Modified)
        {
            damageArgs.Damage = ConvertToDamage(attack.Damage, attack.DamageType);
        }
    }

    private void OnExamined(Entity<PhysicalArmorComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(PhysicalArmorComponent)))
        {
            args.PushText(Loc.GetString(GetExamineLocale(ent.Comp)));
        }
    }

    private bool TryApplyLayer(
        Entity<PhysicalArmorComponent> ent,
        TargetBodyPart targetPart,
        ref PhysicalArmorAttack attack,
        out DamageSpecifier finalDamage)
    {
        finalDamage = default!;
        var armor = ent.Comp;

        if (!IsActive(armor) || !Covers(armor, targetPart))
            return false;

        DamageArmor(ent, attack.Damage);
        attack.Modified = true;

        if (attack.Kind == PhysicalArmorAttackKind.Blunt)
        {
            finalDamage = ConvertToDamage(attack.Damage * armor.BluntDamageMultiplier, "Blunt");
            return true;
        }

        var sp = GetEffectiveStoppingPower(armor);
        if (attack.Penetration > sp)
        {
            attack.Penetration = MathF.Max(0f, attack.Penetration - sp);
            attack.Damage = ApplyPenetrationDamageReduction(attack.Damage, sp);
            attack.DamageType = attack.Ballistic ? "Piercing" : attack.DamageType;
            return false;
        }

        finalDamage = ConvertToDamage(attack.Damage * armor.BluntDamageMultiplier, "Blunt");
        return true;
    }

    private bool TryGetArmorAttack(DamageModifyEvent args, out PhysicalArmorAttack attack)
    {
        var total = args.Damage.GetTotal().Float();

        if (args.DamageSource is { } source
            && TryComp<BallisticProjectileComponent>(source, out var projectile))
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

    private void DamageArmor(Entity<PhysicalArmorComponent> ent, float impactDamage)
    {
        var armor = ent.Comp;
        var durabilityDamage = impactDamage * MathF.Max(armor.DurabilityDamageMultiplier, 0f);
        armor.CurrentDurability = MathF.Max(0f, armor.CurrentDurability - durabilityDamage);
        Dirty(ent);
    }

    private static bool HasPositiveDamage(DamageSpecifier damage, string damageType)
    {
        return damage.DamageDict.TryGetValue(damageType, out var amount) && amount > FixedPoint2.Zero;
    }

    private static bool IsActive(PhysicalArmorComponent armor)
    {
        return armor.CurrentDurability > 0f && (armor.StoppingPower > 0f || armor.BluntDamageMultiplier < 1f);
    }

    private static string GetExamineLocale(PhysicalArmorComponent armor)
    {
        if (armor.CurrentDurability <= 0f)
            return "nc-physical-armor-examine-destroyed";

        var ratio = GetDurabilityRatio(armor);
        if (ratio > 0.8f)
            return "nc-physical-armor-examine-pristine";

        if (ratio >= 0.3f)
            return "nc-physical-armor-examine-worn";

        return "nc-physical-armor-examine-critical";
    }

    private static float GetDurabilityRatio(PhysicalArmorComponent armor)
    {
        if (armor.MaxDurability <= 0f)
            return 0f;

        return Math.Clamp(armor.CurrentDurability / armor.MaxDurability, 0f, 1f);
    }

    private static float GetEffectiveStoppingPower(PhysicalArmorComponent armor)
    {
        return MathF.Max(armor.StoppingPower, 0f) * GetDurabilityRatio(armor);
    }

    private static float ApplyPenetrationDamageReduction(float damage, float effectiveStoppingPower)
    {
        // SP and penetration use a 0-100 scale; use SP as a percentage reducer so high-SP armor
        // does not erase all body damage after a successful penetration.
        var reduction = Math.Clamp(effectiveStoppingPower / 100f, 0f, 0.95f);
        return MathF.Max(0f, damage * (1f - reduction));
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

    private static DamageSpecifier ConvertToDamage(float amount, string damageType)
    {
        var converted = new DamageSpecifier();

        if (amount > 0f)
            converted.DamageDict[damageType] = FixedPoint2.New(amount);

        return converted;
    }

    private record struct PhysicalArmorAttack(
        PhysicalArmorAttackKind Kind,
        float Damage,
        float Penetration,
        string DamageType,
        bool Ballistic,
        bool Modified = false);

    private enum PhysicalArmorAttackKind : byte
    {
        Penetrating,
        Blunt,
    }
}
