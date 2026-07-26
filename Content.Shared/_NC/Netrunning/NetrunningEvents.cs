using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning;

[Serializable, NetSerializable]
public sealed class NetrunningImmersionEvent : EntityEventArgs
{
    public readonly bool Start; // true = fade out, false = fade in
    public NetrunningImmersionEvent(bool start) => Start = start;
}

[Serializable, NetSerializable]
public sealed class NetrunningFeedbackEvent : EntityEventArgs
{
    public readonly string Title;
    public readonly string Message;
    public readonly bool Critical;

    public NetrunningFeedbackEvent(string title, string message, bool critical = false)
    {
        Title = title;
        Message = message;
        Critical = critical;
    }
}

[Serializable, NetSerializable]
public sealed record NetrunningResponseShardInfo(NetEntity Shard, string Name, int RamCost);

[Serializable, NetSerializable]
public enum NetrunningDefenseConsequence : byte
{
    Unknown,
    NeuralBurn,
    Disconnect,
    IceDamage,
    Override,
}

/// <summary>
/// Opens or refreshes the netrunner's response window for an active intrusion transaction.
/// </summary>
[Serializable, NetSerializable]
public sealed class NetrunningDefenseWindowEvent : EntityEventArgs
{
    public readonly NetEntity Deck;
    public readonly NetEntity Server;
    public readonly int TransactionId;
    public readonly string DefenseName;
    public readonly int ResponseMilliseconds;
    public readonly List<NetrunningDefenseConsequence> Consequences;
    public readonly List<NetrunningResponseShardInfo> Shards;

    public NetrunningDefenseWindowEvent(
        NetEntity deck,
        NetEntity server,
        int transactionId,
        string defenseName,
        int responseMilliseconds,
        List<NetrunningDefenseConsequence> consequences,
        List<NetrunningResponseShardInfo> shards)
    {
        Deck = deck;
        Server = server;
        TransactionId = transactionId;
        DefenseName = defenseName;
        ResponseMilliseconds = responseMilliseconds;
        Consequences = consequences;
        Shards = shards;
    }
}

/// <summary>
/// Requests execution of one installed deck script while a defensive chain is active.
/// </summary>
[Serializable, NetSerializable]
public sealed class NetrunningDefenseResponseEvent : EntityEventArgs
{
    public readonly NetEntity Deck;
    public readonly NetEntity Server;
    public readonly int TransactionId;
    public readonly NetEntity Shard;

    public NetrunningDefenseResponseEvent(NetEntity deck, NetEntity server, int transactionId, NetEntity shard)
    {
        Deck = deck;
        Server = server;
        TransactionId = transactionId;
        Shard = shard;
    }
}

[Serializable, NetSerializable]
public sealed class NetrunningDefenseResponseStatusEvent : EntityEventArgs
{
    public readonly int TransactionId;
    public readonly bool Accepted;

    public NetrunningDefenseResponseStatusEvent(int transactionId, bool accepted)
    {
        TransactionId = transactionId;
        Accepted = accepted;
    }
}

[Serializable, NetSerializable]
public sealed class NetrunningDefenseResolvedEvent : EntityEventArgs
{
    public readonly int TransactionId;
    public readonly bool AttackApplied;

    public NetrunningDefenseResolvedEvent(int transactionId, bool attackApplied)
    {
        TransactionId = transactionId;
        AttackApplied = attackApplied;
    }
}
