using Content.Shared._NC.CharacterNotes;
using Content.Shared._NC.CharacterNotes.Components;
using Content.Shared._NC.CharacterNotes.Events;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Robust.Client.Player;

namespace Content.Client._NC.CharacterNotes;

public sealed class NCCharacterNotesSystem : EntitySystem
{
    private const string UnknownIdentityName = "Неизвестно";

    [Dependency] private readonly IPlayerManager _player = default!;

    private readonly Dictionary<EntityUid, NCCharacterObservedIdentity> _knownIdentities = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<NCCharacterNotesCacheMessage>(OnNotesCache);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => _knownIdentities.Clear());
        SubscribeLocalEvent<NCCharacterIdentityComponent, NCResolveViewerDisplayNameEvent>(OnResolveViewerDisplayName);
    }

    private void OnNotesCache(NCCharacterNotesCacheMessage msg)
    {
        foreach (var entry in msg.Entries)
        {
            var target = GetEntity(entry.Target);
            if (Deleted(target))
                continue;

            _knownIdentities[target] = entry;
        }
    }

    public string GetDisplayName(EntityUid target, EntityUid viewer)
    {
        var fallback = Identity.Name(target, EntityManager, viewer);

        if (!HasComp<NCCharacterIdentityComponent>(target))
            return fallback;

        if (_player.LocalEntity == target)
            return fallback;

        if (_knownIdentities.TryGetValue(target, out var note) &&
            !string.IsNullOrWhiteSpace(note.CustomName))
        {
            return note.CustomName;
        }

        return UnknownIdentityName;
    }

    public string GetLocalDisplayName(EntityUid target)
    {
        var viewer = _player.LocalEntity;
        if (viewer == null)
            return Identity.Name(target, EntityManager);

        return GetDisplayName(target, viewer.Value);
    }

    public string GetLocalChatNameColor(EntityUid target, string fallbackName)
    {
        if (!_knownIdentities.TryGetValue(target, out var note))
            return SharedChatSystem.GetNameColor(fallbackName);

        return note.ColorTag switch
        {
            NCCharacterNoteColorTag.Green => "#5AAE64",
            NCCharacterNoteColorTag.Yellow => "#D6B44B",
            NCCharacterNoteColorTag.Red => "#C85C5C",
            _ => SharedChatSystem.GetNameColor(fallbackName),
        };
    }

    private void OnResolveViewerDisplayName(EntityUid uid, NCCharacterIdentityComponent component, ref NCResolveViewerDisplayNameEvent args)
    {
        args.DisplayName = GetDisplayName(uid, args.Viewer);
    }
}
