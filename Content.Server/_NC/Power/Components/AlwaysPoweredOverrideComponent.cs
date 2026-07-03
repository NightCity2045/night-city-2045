namespace Content.Server._NC.Power.Components;

/// <summary>
/// Stores the original power requirement so it can be restored when the entity leaves an always-powered map.
/// </summary>
[RegisterComponent]
public sealed partial class AlwaysPoweredOverrideComponent : Component
{
    [DataField]
    public bool OriginalNeedsPower = true;
}
