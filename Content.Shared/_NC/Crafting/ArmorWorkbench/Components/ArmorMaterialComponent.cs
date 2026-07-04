using Content.Shared._NC.Armor.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Crafting.ArmorWorkbench.Components;

/// <summary>
/// Defines how an item contributes to a crafted armor layer.
/// </summary>
[Serializable, NetSerializable]
public enum ArmorMaterialType : byte
{
    Base,
    Carrier,
    Plate,
}

[Serializable, NetSerializable]
public enum ArmorWorkbenchLayerSlot : byte
{
    Base,
    Carrier,
    Plate,
}

/// <summary>
/// Marks an entity as a valid armor crafting material.
/// </summary>
[RegisterComponent]
public sealed partial class ArmorMaterialComponent : Component
{
    [DataField("layerType")]
    public ArmorMaterialType LayerType = ArmorMaterialType.Carrier;

    [DataField("grantedStoppingPower")]
    public float GrantedStoppingPower;

    [DataField("grantedDurability")]
    public float GrantedDurability = 100f;

    [DataField("materialType")]
    public PhysicalArmorMaterialType MaterialType = PhysicalArmorMaterialType.Generic;

    [DataField("bluntDamageMultiplier")]
    public float BluntDamageMultiplier = 0.4f;

    [DataField("durabilityDamageMultiplier")]
    public float DurabilityDamageMultiplier = 1f;
}
