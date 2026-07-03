using Content.Shared._NC.Armor.Components;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;

namespace Content.Server._NC.Armor;

/// <summary>
/// Applies the NC layered armor contract on worn items and keeps its durability state in sync.
/// </summary>
public sealed class NCLayeredArmorSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<NCLayeredArmorComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);
        SubscribeLocalEvent<NCLayeredArmorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnDamageModify(Entity<NCLayeredArmorComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        var damageArgs = args.Args;
        if (damageArgs.TargetPart == TargetBodyPart.All)
            return;

        if (damageArgs.DamageSource is null
            || !TryComp<NCPenetrationComponent>(damageArgs.DamageSource.Value, out var penetrationComp))
            return;

        var targetPart = damageArgs.TargetPart ?? TargetBodyPart.Torso;
        var impactDurability = MathF.Max(damageArgs.OriginalDamage.GetTotal().Float(), 1f);
        var penetration = penetrationComp.Penetration;
        var changed = false;

        // The soft layer can wear out independently, but it must not disable the hard plate logic.
        if (ent.Comp.SoftLayer.CurrentDurability > 0f && Covers(ent.Comp.SoftLayer, targetPart))
        {
            ent.Comp.SoftLayer.CurrentDurability = MathF.Max(0f, ent.Comp.SoftLayer.CurrentDurability - impactDurability);
            changed = true;
        }

        if (!Covers(ent.Comp.HardLayer, targetPart) || ent.Comp.HardLayer.CurrentDurability <= 0f)
        {
            if (changed)
                Dirty(ent);
            return;
        }

        // The hard layer is the actual plate check: stop, partial penetration, or full pass-through.
        ent.Comp.HardLayer.CurrentDurability = MathF.Max(0f, ent.Comp.HardLayer.CurrentDurability - impactDurability);
        changed = true;
        var hardArmorClass = ent.Comp.HardLayer.CurrentDurability > 0f ? ent.Comp.HardLayer.ArmorClass : 0;

        if (changed)
            Dirty(ent);

        if (penetration < hardArmorClass)
        {
            // The closer penetration gets to the plate class, the harsher the blunt trauma.
            var bluntMultiplier = Math.Clamp(
                penetration / (float) hardArmorClass * ent.Comp.EqualPenetrationDamageMultiplier,
                0f,
                ent.Comp.EqualPenetrationDamageMultiplier);

            damageArgs.Damage = ConvertToBlunt(damageArgs.OriginalDamage, bluntMultiplier);
            return;
        }

        if (penetration == hardArmorClass)
            damageArgs.Damage = ConvertToBlunt(damageArgs.OriginalDamage, ent.Comp.EqualPenetrationDamageMultiplier);
    }

    private void OnExamined(Entity<NCLayeredArmorComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(NCLayeredArmorComponent)))
        {
            args.PushText(
                $"Soft layer: {ent.Comp.SoftLayer.CurrentDurability:0.##}/{ent.Comp.SoftLayer.MaxDurability:0.##}\n" +
                $"Hard layer: {ent.Comp.HardLayer.CurrentDurability:0.##}/{ent.Comp.HardLayer.MaxDurability:0.##}");
        }
    }

    private static bool Covers(NCLayeredArmorLayer layer, TargetBodyPart targetPart)
    {
        foreach (var coveredPart in layer.Coverage)
        {
            if ((coveredPart & targetPart) != 0)
                return true;
        }

        return false;
    }

    private static DamageSpecifier ConvertToBlunt(DamageSpecifier damage, float multiplier = 1f)
    {
        var total = damage.GetTotal() * multiplier;
        var converted = new DamageSpecifier();

        if (total > FixedPoint2.Zero)
            converted.DamageDict["Blunt"] = total;

        return converted;
    }
}
