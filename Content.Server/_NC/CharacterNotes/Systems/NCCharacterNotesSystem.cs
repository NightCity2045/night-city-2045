using Content.Server._NC.CharacterNotes.Components;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared._NC.CharacterNotes;
using Content.Shared._NC.CharacterNotes.Components;
using Content.Shared._NC.CharacterNotes.Events;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NC.CharacterNotes.Systems;

public sealed class NCCharacterNotesSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;

    private const int MaxCustomNameLength = 32;
    private const int MaxDescriptionLength = 4096;
    private const float NotesUiRange = 2f;
    private const string UnknownIdentityName = "Неизвестно";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<NCCharacterIdentityComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<NCCharacterIdentityComponent, NCResolveViewerDisplayNameEvent>(OnResolveViewerDisplayName);
        SubscribeLocalEvent<NCCharacterNotesComponent, NCCharacterNoteSaveMessage>(OnSaveNote);
    }

    private async void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (ev.Mob == EntityUid.Invalid)
            return;

        var slot = _prefs.GetPreferences(ev.Player.UserId).SelectedCharacterIndex;
        var profileId = await _db.GetProfileIdForSlotAsync(ev.Player.UserId, slot);
        if (profileId == null || Deleted(ev.Mob))
            return;

        var loadedNotes = await _db.GetNCCharacterNotesAsync(profileId.Value);
        if (Deleted(ev.Mob))
            return;

        var identity = EnsureComp<NCCharacterIdentityComponent>(ev.Mob);
        identity.ProfileId = profileId.Value;

        var notes = EnsureComp<NCCharacterNotesComponent>(ev.Mob);
        notes.OwnerProfileId = profileId.Value;
        notes.Notes = loadedNotes;

        // The BUI is attached to the viewer's mob because notes are a personal action, not a target-owned device.
        _ui.SetUi(ev.Mob, NCCharacterNotesUiKey.Key, new InterfaceData("NCCharacterNotesBoundUserInterface", NotesUiRange));

        SendNotesCache(ev.Player, notes);
        NotifyObserversAboutTarget(ev.Mob, profileId.Value);
    }

    private void OnGetAlternativeVerbs(EntityUid uid, NCCharacterIdentityComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<NCCharacterNotesComponent>(args.User, out _))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Заметки",
            Act = () => OpenNotesUi(args.User, uid),
        });
    }

    private void OpenNotesUi(EntityUid user, EntityUid target)
    {
        if (!TryComp<NCCharacterNotesComponent>(user, out var notes) ||
            !TryComp<NCCharacterIdentityComponent>(target, out var targetIdentity))
            return;

        notes.Notes.TryGetValue(targetIdentity.ProfileId, out var entry);
        var displayName = entry == null || string.IsNullOrWhiteSpace(entry.CustomName)
            ? UnknownIdentityName
            : entry.CustomName;

        _ui.SetUi(user, NCCharacterNotesUiKey.Key, new InterfaceData("NCCharacterNotesBoundUserInterface", NotesUiRange));
        _ui.SetUiState(user, NCCharacterNotesUiKey.Key, new NCCharacterNoteBoundUserInterfaceState(
            GetNetEntity(target),
            displayName,
            entry?.CustomName ?? string.Empty,
            entry?.ColorTag ?? NCCharacterNoteColorTag.Neutral,
            entry?.Description ?? string.Empty));
        _ui.OpenUi(user, NCCharacterNotesUiKey.Key, user);
    }

    private async void OnSaveNote(EntityUid uid, NCCharacterNotesComponent component, NCCharacterNoteSaveMessage args)
    {
        if (!TryGetEntity(args.Target, out var target) ||
            !TryComp<NCCharacterIdentityComponent>(target.Value, out var targetIdentity))
            return;

        if (!_interaction.InRangeAndAccessible(uid, target.Value, NotesUiRange))
            return;

        var customName = TrimToLimit(args.CustomName, MaxCustomNameLength);
        var description = TrimToLimit(args.Description, MaxDescriptionLength);

        var entry = new NCCharacterNoteEntry
        {
            CustomName = customName,
            ColorTag = args.ColorTag,
            Description = description,
        };

        component.Notes[targetIdentity.ProfileId] = entry;

        await _db.SaveNCCharacterNoteAsync(component.OwnerProfileId, targetIdentity.ProfileId, customName, args.ColorTag, description);

        if (Deleted(uid))
            return;

        SendNote(uid, target.Value, entry);
        _popup.PopupEntity("Заметка сохранена.", uid, uid);
        OpenNotesUi(uid, target.Value);
    }

    private void SendNotesCache(ICommonSession session, NCCharacterNotesComponent notes)
    {
        var entries = new List<NCCharacterObservedIdentity>();
        var query = EntityQueryEnumerator<NCCharacterIdentityComponent>();
        while (query.MoveNext(out var target, out var identity))
        {
            if (!notes.Notes.TryGetValue(identity.ProfileId, out var note))
                continue;

            entries.Add(new NCCharacterObservedIdentity(GetNetEntity(target), note.CustomName, note.ColorTag));
        }

        RaiseNetworkEvent(new NCCharacterNotesCacheMessage(entries), session.Channel);
    }

    private void NotifyObserversAboutTarget(EntityUid target, int targetProfileId)
    {
        var query = EntityQueryEnumerator<NCCharacterNotesComponent>();
        while (query.MoveNext(out var observer, out var notes))
        {
            if (!notes.Notes.TryGetValue(targetProfileId, out var note))
                continue;

            SendNote(observer, target, note);
        }
    }

    private void SendNote(EntityUid observer, EntityUid target, NCCharacterNoteEntry note)
    {
        if (!_players.TryGetSessionByEntity(observer, out var session))
            return;

        var entries = new List<NCCharacterObservedIdentity>
        {
            new(GetNetEntity(target), note.CustomName, note.ColorTag)
        };

        RaiseNetworkEvent(new NCCharacterNotesCacheMessage(entries), session.Channel);
    }

    public string GetDisplayNameForViewer(EntityUid target, EntityUid viewer, string fallbackName)
    {
        if (!TryComp<NCCharacterIdentityComponent>(target, out var targetIdentity))
            return fallbackName;

        if (target == viewer)
            return fallbackName;

        if (TryComp<NCCharacterNotesComponent>(viewer, out var notes) &&
            notes.Notes.TryGetValue(targetIdentity.ProfileId, out var note) &&
            !string.IsNullOrWhiteSpace(note.CustomName))
        {
            return note.CustomName;
        }

        return UnknownIdentityName;
    }

    private void OnResolveViewerDisplayName(EntityUid uid, NCCharacterIdentityComponent component, ref NCResolveViewerDisplayNameEvent args)
    {
        args.DisplayName = GetDisplayNameForViewer(uid, args.Viewer, args.DisplayName);
    }

    private static string TrimToLimit(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
