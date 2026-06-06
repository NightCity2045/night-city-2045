using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Netrunning.Prototypes;

/// <summary>
///     Defines a buildable module for Local Area Networks.
/// </summary>
[Prototype("netModule")]
public sealed partial class NetModulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    ///     Permanent RAM reservation required to keep this module active.
    /// </summary>
    [DataField("ramCost")]
    public int RamCost { get; private set; } = 0;

    /// <summary>
    ///     One-time price in Eddies to construct this module.
    /// </summary>
    [DataField("price")]
    public int Price { get; private set; } = 0;

    /// <summary>
    ///     Path to the grid template YAML file.
    /// </summary>
    [DataField("templatePath")]
    public string TemplatePath { get; private set; } = string.Empty;
}
