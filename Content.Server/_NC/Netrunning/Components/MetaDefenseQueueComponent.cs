using Content.Shared._NC.Netrunning.Meta;

namespace Content.Server._NC.Netrunning.Components;

/// <summary>
/// Server-owned queue that serializes defensive META programs for a local network.
/// </summary>
[RegisterComponent]
public sealed partial class MetaDefenseQueueComponent : Component
{
    [ViewVariables]
    public readonly List<MetaDefenseInvocation> Pending = new();

    [ViewVariables]
    public EntityUid? ActiveHost;

    [ViewVariables]
    public int ActiveTransactionId;

    [ViewVariables]
    public readonly Dictionary<int, MetaIntrusionTransaction> Transactions = new();
}

/// <summary>
/// One defensive program activation caused by an intrusion.
/// </summary>
public sealed class MetaDefenseInvocation
{
    public EntityUid Host;
    public EntityUid Shard;
    public EntityUid Intruder;
    public EntityUid FeedbackTarget;
    public int TransactionId;

    public MetaDefenseInvocation(
        EntityUid host,
        EntityUid shard,
        EntityUid intruder,
        EntityUid feedbackTarget,
        int transactionId = 0)
    {
        Host = host;
        Shard = shard;
        Intruder = intruder;
        FeedbackTarget = feedbackTarget;
        TransactionId = transactionId;
    }
}

/// <summary>
/// Deferred hostile SYS call waiting for the local defensive chain.
/// </summary>
public sealed class MetaIntrusionTransaction
{
    public int Id;
    public EntityUid Intruder;
    public EntityUid FeedbackTarget;
    public EntityUid Target;
    public MetaIntrusionOperationKind Operation;
    public int Value;
    public bool Cancelled;
    public bool Completed;
    public bool Applied;
}
