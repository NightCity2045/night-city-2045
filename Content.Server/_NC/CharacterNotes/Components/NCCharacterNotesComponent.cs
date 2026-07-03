using Content.Shared._NC.CharacterNotes;

namespace Content.Server._NC.CharacterNotes.Components;

/// <summary>
/// Server-only cache of notes created by this character about other profile-backed characters.
/// </summary>
[RegisterComponent]
public sealed partial class NCCharacterNotesComponent : Component
{
    [DataField]
    public int OwnerProfileId;

    [DataField]
    public Dictionary<int, NCCharacterNoteEntry> Notes = new();
}

[DataDefinition]
public sealed partial class NCCharacterNoteEntry
{
    [DataField]
    public string CustomName = string.Empty;

    [DataField]
    public NCCharacterNoteColorTag ColorTag = NCCharacterNoteColorTag.Neutral;

    [DataField]
    public string Description = string.Empty;
}
