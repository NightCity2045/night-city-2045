using Robust.Shared.Serialization;

namespace Content.Shared._NC.Crafting.ArmorWorkbench.Components;

/// <summary>
/// Defines how an item contributes to a crafted armor layer.
/// </summary>
[Serializable, NetSerializable]
public enum ArmorMaterialType : byte
{
    Base,
    Armor,
}

[Serializable, NetSerializable]
public enum ArmorWorkbenchLayerSlot : byte
{
    Base,
    Soft,
    Hard,
}

/// <summary>
/// Marks an entity as a valid armor crafting material.
/// </summary>
[RegisterComponent]
public sealed partial class ArmorMaterialComponent : Component
{
    [DataField("layerType")]
    public ArmorMaterialType LayerType = ArmorMaterialType.Armor;

    [DataField("grantedArmorClass")]
    public int GrantedArmorClass;

    [DataField("grantedDurability")]
    public float GrantedDurability = 100f;
}
