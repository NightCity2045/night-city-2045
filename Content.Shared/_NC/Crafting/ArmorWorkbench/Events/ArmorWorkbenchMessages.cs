using Content.Shared._NC.Crafting.ArmorWorkbench.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Crafting.ArmorWorkbench.Events;

/// <summary>
/// Lightweight UI status for the armor workbench.
/// </summary>
[Serializable, NetSerializable]
public enum ArmorWorkbenchUiStatus : byte
{
    WaitingInput,
    MissingBlueprint,
    MissingBaseMaterial,
    MissingRecipeMaterials,
    Ready,
    Crafting,
}

[Serializable, NetSerializable]
public sealed class ArmorWorkbenchMaterialEntry
{
    public NetEntity Entity { get; }
    public string Name { get; }
    public float StoppingPower { get; }
    public float Durability { get; }

    public ArmorWorkbenchMaterialEntry(NetEntity entity, string name, float stoppingPower, float durability)
    {
        Entity = entity;
        Name = name;
        StoppingPower = stoppingPower;
        Durability = durability;
    }
}

[Serializable, NetSerializable]
public sealed class ArmorWorkbenchBoundUserInterfaceState : BoundUserInterfaceState
{
    public ArmorWorkbenchUiStatus Status { get; }
    public bool IsCrafting { get; }
    public float CraftDuration { get; }
    public NetEntity? BlueprintEntity { get; }
    public string? BlueprintName { get; }
    public string? ResultName { get; }
    public int BaseMaterialAmount { get; }
    public int CarrierMaterialAmount { get; }
    public int PlateMaterialAmount { get; }
    public List<ArmorWorkbenchMaterialEntry> BaseMaterials { get; }
    public List<ArmorWorkbenchMaterialEntry> CarrierMaterials { get; }
    public List<ArmorWorkbenchMaterialEntry> PlateMaterials { get; }
    public NetEntity? SelectedBaseMaterial { get; }
    public NetEntity? SelectedCarrierMaterial { get; }
    public NetEntity? SelectedPlateMaterial { get; }

    public ArmorWorkbenchBoundUserInterfaceState(
        ArmorWorkbenchUiStatus status,
        bool isCrafting,
        float craftDuration,
        NetEntity? blueprintEntity,
        string? blueprintName,
        string? resultName,
        int baseMaterialAmount,
        int carrierMaterialAmount,
        int plateMaterialAmount,
        List<ArmorWorkbenchMaterialEntry> baseMaterials,
        List<ArmorWorkbenchMaterialEntry> carrierMaterials,
        List<ArmorWorkbenchMaterialEntry> plateMaterials,
        NetEntity? selectedBaseMaterial,
        NetEntity? selectedCarrierMaterial,
        NetEntity? selectedPlateMaterial)
    {
        Status = status;
        IsCrafting = isCrafting;
        CraftDuration = craftDuration;
        BlueprintEntity = blueprintEntity;
        BlueprintName = blueprintName;
        ResultName = resultName;
        BaseMaterialAmount = baseMaterialAmount;
        CarrierMaterialAmount = carrierMaterialAmount;
        PlateMaterialAmount = plateMaterialAmount;
        BaseMaterials = baseMaterials;
        CarrierMaterials = carrierMaterials;
        PlateMaterials = plateMaterials;
        SelectedBaseMaterial = selectedBaseMaterial;
        SelectedCarrierMaterial = selectedCarrierMaterial;
        SelectedPlateMaterial = selectedPlateMaterial;
    }
}

[Serializable, NetSerializable]
public sealed class ArmorWorkbenchSelectMaterialMessage : BoundUserInterfaceMessage
{
    public ArmorWorkbenchLayerSlot LayerType { get; }
    public NetEntity Material { get; }

    public ArmorWorkbenchSelectMaterialMessage(ArmorWorkbenchLayerSlot layerType, NetEntity material)
    {
        LayerType = layerType;
        Material = material;
    }
}

[Serializable, NetSerializable]
public enum ArmorWorkbenchEjectTarget : byte
{
    Blueprint,
    Materials,
}

[Serializable, NetSerializable]
public sealed class ArmorWorkbenchEjectRequestMessage : BoundUserInterfaceMessage
{
    public ArmorWorkbenchEjectTarget Target { get; }

    public ArmorWorkbenchEjectRequestMessage(ArmorWorkbenchEjectTarget target)
    {
        Target = target;
    }
}

[Serializable, NetSerializable]
public sealed class ArmorWorkbenchStartCraftMessage : BoundUserInterfaceMessage
{
}
