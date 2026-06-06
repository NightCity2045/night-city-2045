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
