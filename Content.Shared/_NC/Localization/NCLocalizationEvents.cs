using Robust.Shared.Serialization;

namespace Content.Shared._NC.Localization;

[Serializable, NetSerializable]
public sealed class NCClientCultureChangedEvent : EntityEventArgs
{
    public readonly string CultureName;

    public NCClientCultureChangedEvent(string cultureName)
    {
        CultureName = cultureName;
    }
}
