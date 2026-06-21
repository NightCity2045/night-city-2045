using Robust.Shared.Serialization;

namespace Content.Shared._NC.CharacterNotes;

[Serializable, NetSerializable]
public enum NCCharacterNotesUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum NCCharacterNoteColorTag : byte
{
    Neutral,
    Green,
    Yellow,
    Red
}

[Serializable, NetSerializable]
public sealed class NCCharacterNoteBoundUserInterfaceState(
    NetEntity target,
    string targetName,
    string customName,
    NCCharacterNoteColorTag colorTag,
    string description)
    : BoundUserInterfaceState
{
    public readonly NetEntity Target = target;
    public readonly string TargetName = targetName;
    public readonly string CustomName = customName;
    public readonly NCCharacterNoteColorTag ColorTag = colorTag;
    public readonly string Description = description;
}

[Serializable, NetSerializable]
public sealed class NCCharacterNoteSaveMessage(
    NetEntity target,
    string customName,
    NCCharacterNoteColorTag colorTag,
    string description)
    : BoundUserInterfaceMessage
{
    public readonly NetEntity Target = target;
    public readonly string CustomName = customName;
    public readonly NCCharacterNoteColorTag ColorTag = colorTag;
    public readonly string Description = description;
}

[Serializable, NetSerializable]
public sealed class NCCharacterNotesCacheMessage(List<NCCharacterObservedIdentity> entries) : EntityEventArgs
{
    public readonly List<NCCharacterObservedIdentity> Entries = entries;
}

[Serializable, NetSerializable]
public readonly record struct NCCharacterObservedIdentity(
    NetEntity Target,
    string CustomName,
    NCCharacterNoteColorTag ColorTag);
