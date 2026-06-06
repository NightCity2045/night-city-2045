using Content.Shared._NC.CitiNet.Delivery;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.CitiNet.Store;

/// <summary>
/// A collection of categories that define a complete webstore catalog.
/// </summary>
[Serializable, NetSerializable, Prototype("citiNetStorePreset")]
public sealed class CitiNetStorePresetPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("categories")]
    public List<ProtoId<CitiNetStoreCategoryPrototype>> Categories { get; private set; } = new();

    [DataField("defaultDelivery")]
    public DropType DefaultDelivery = DropType.Corporate;
}

/// <summary>
/// A category (tab) in the webstore containing multiple entries.
/// </summary>
[Serializable, NetSerializable, Prototype("citiNetStoreCategory")]
public sealed class CitiNetStoreCategoryPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("entries")]
    public List<CitiNetStoreEntry> Entries { get; private set; } = new();
}

/// <summary>
/// A single item entry in a webstore category.
/// </summary>
[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class CitiNetStoreEntry
{
    [DataField("proto", required: true)]
    public ProtoId<EntityPrototype> ProductId { get; private set; } = default!;

    [DataField("price")]
    public int Price { get; private set; } = 0;

    [DataField("count")]
    public int? InitialCount { get; private set; }

    /// <summary>
    /// Optional override for the name shown on the website.
    /// </summary>
    [DataField("name")]
    public string? NameOverride { get; private set; }

    /// <summary>
    /// Optional override for the description shown on the website.
    /// </summary>
    [DataField("description")]
    public string? DescriptionOverride { get; private set; }
}
