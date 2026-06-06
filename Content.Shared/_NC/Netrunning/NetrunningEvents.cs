using Robust.Shared.Serialization;

namespace Content.Shared._NC.Netrunning;

[Serializable, NetSerializable]
public sealed class NetrunningImmersionEvent : EntityEventArgs
{
    public readonly bool Start; // true = fade out, false = fade in
    public NetrunningImmersionEvent(bool start) => Start = start;
}
