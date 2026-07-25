namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Runtime link between a physical NET entity and its private META bytecode copy.
/// </summary>
[RegisterComponent]
public sealed partial class MetaHostedProgramComponent : Component
{
    public const string ProgramContainerId = "hosted_meta_program";

    [DataField("triggerRadius")]
    public float TriggerRadius = 3f;

    [DataField("scanInterval")]
    public float ScanInterval = 0.25f;

    [ViewVariables]
    public EntityUid? ProgramShard;

    [ViewVariables]
    public float ScanAccumulator;

    /// <summary>
    ///     Avatars currently inside the trigger radius. They can trigger again after leaving.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> IntrudersInRange = new();
}
