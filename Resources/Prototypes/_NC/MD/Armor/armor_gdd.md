# Modular Physical Armor GDD

This project uses the approved NC physical armor model. The removed legacy
layer model is intentionally not part of this system.

## Core Data

- Projectiles expose `Damage` and `Penetration` on a 0-100 penetration scale.
- Armor exposes `StoppingPower` (`SP`) on a 0-100 protection scale, `MaterialType`, `Durability`, and
  `Coverage`.
- Armor vests may contain physical plate entities through `ItemSlots`.
- Subdermal cyberware is represented as a final inner `PhysicalArmor` layer.

## Hit Resolution

Armor is evaluated from the outside inward for the hit body part.

1. If the layer does not cover the hit part, it is skipped.
2. Effective SP is scaled by durability: `effectiveSP = SP * durabilityRatio`.
3. If `Penetration > effectiveSP`, the projectile penetrates. Remaining
   penetration is reduced by effective SP, and penetrating damage is reduced by
   `effectiveSP / 100`.
4. If `Penetration <= effectiveSP`, the projectile is stopped. The victim receives
   blunt trauma equal to `Damage * BluntDamageMultiplier`.
5. Every impacted layer loses durability based on the incoming damage and its
   material multiplier.

## YAML Contract

```yaml
- type: PhysicalArmor
  stoppingPower: 60
  maxDurability: 120
  currentDurability: 120
  materialType: Ceramic
  bluntDamageMultiplier: 0.1
  durabilityDamageMultiplier: 1.25
  coverage:
  - Torso
```

```yaml
- type: BallisticProjectile
  penetration: 52
```

## Integration Notes

- Vanilla `Armor` may still be used for environmental damage such as heat,
  radiation, caustic, and shock.
- Ballistic, slash, and piercing protection must use `PhysicalArmor`.
- Do not reintroduce the removed legacy layer fields.
