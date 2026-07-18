using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Clothing;

/// <summary>
/// Data-only description of extra visual layers rendered over equipped clothing.
/// </summary>
[RegisterComponent]
public sealed partial class NCClothingOverlayComponent : Component
{
    /// <summary>
    /// Overlay layers keyed by the inventory slot in which the item is equipped.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, List<PrototypeLayerData>> ClothingVisuals = new();
}
