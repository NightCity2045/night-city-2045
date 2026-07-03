using Robust.Shared.Containers;

namespace Content.Shared._NC.Crafting.ArmorWorkbench.Components;

/// <summary>
/// Stores the runtime state for the armor crafting workbench.
/// </summary>
[RegisterComponent]
public sealed partial class ArmorWorkbenchComponent : Component
{
    public const string StorageContainerId = "armor_workbench_storage";

    [DataField("craftDuration")]
    public float CraftDuration = 15f;

    /// <summary>
    /// General-purpose storage for the blueprint and all loaded materials.
    /// </summary>
    public Container Storage = default!;

    public EntityUid? SelectedBaseMaterial;
    public EntityUid? SelectedSoftMaterial;
    public EntityUid? SelectedHardMaterial;

    public bool IsCrafting;
}
