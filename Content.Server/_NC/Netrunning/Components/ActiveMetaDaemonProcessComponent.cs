using Content.Shared._NC.Netrunning.Meta;

namespace Content.Server._NC.Netrunning.Components;

/// <summary>
/// Server-only continuation state for a defensive META daemon.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveMetaDaemonProcessComponent : Component
{
    [ViewVariables]
    public MetaContinuationState Continuation = default!;

    [ViewVariables]
    public EntityUid Server;

    [ViewVariables]
    public EntityUid Shard;

    [ViewVariables]
    public EntityUid Intruder;

    [ViewVariables]
    public EntityUid FeedbackTarget;

    [ViewVariables]
    public double ResumeAtTime;

    [ViewVariables]
    public bool Completed;
}
