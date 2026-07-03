using Content.Shared._Shitmed.Targeting;

namespace Content.Shared._NC.Crafting.ArmorWorkbench.Components;

/// <summary>
/// Blueprint data for spawning and configuring a crafted armor result.
/// </summary>
[RegisterComponent]
public sealed partial class ArmorBlueprintComponent : Component
{
    [DataField("resultPrototype", required: true)]
    public string ResultPrototype = string.Empty;

    [DataField("baseMaterialAmount")]
    public int BaseMaterialAmount = 1;

    [DataField("softMaterialAmount")]
    public int SoftMaterialAmount = 1;

    [DataField("hardMaterialAmount")]
    public int HardMaterialAmount = 1;

    [DataField("coverage", required: true)]
    public List<TargetBodyPart> Coverage = new();
}
