using Content.Shared._NC.Netrunning.Meta;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
/// Attached to a CyberdeckComponent entity when one or more META programs
/// are in a YIELD-suspended state, waiting to be resumed by MetaSchedulerSystem.
/// Removed when all processes complete or are killed.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveMetaProcessComponent : Component
{
    /// <summary>
    /// List of suspended META programs waiting to resume after their YIELD delay expires.
    /// Each entry is a full continuation state that the VM can pick up and keep executing.
    /// </summary>
    [ViewVariables]
    public readonly List<MetaContinuationState> SuspendedProcesses = new();
}
