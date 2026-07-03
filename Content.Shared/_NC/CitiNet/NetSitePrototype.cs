using Content.Shared.Access;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.CitiNet;

[Prototype("netSite")]
public sealed partial class NetSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("url")]
    public string URL { get; private set; } = string.Empty;

    [DataField("icon")]
    public SpriteSpecifier? Icon { get; private set; }

    [DataField("requiredAccess")]
    public List<ProtoId<AccessLevelPrototype>> RequiredAccess { get; private set; } = new();

    /// <summary>
    /// The key for the UI fragment that will be displayed in the browser viewport.
    /// This key is mapped to a UIFragment on the client.
    /// </summary>
    [DataField("uiKey", required: true)]
    public string UiKey { get; private set; } = string.Empty;

    /// <summary>
    /// Optional store preset associated with this site.
    /// </summary>
    [DataField("storePreset")]
    public string? StorePreset { get; private set; }
}
