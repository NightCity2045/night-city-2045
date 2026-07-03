using Content.Shared._NC.CharacterNotes;
using Robust.Client.GameObjects;

namespace Content.Client._NC.CharacterNotes;

public sealed class NCCharacterNotesBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private NCCharacterNotesWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new NCCharacterNotesWindow();
        _window.OnClose += Close;
        _window.OnSave += (target, customName, colorTag, description) =>
            SendMessage(new NCCharacterNoteSaveMessage(target, customName, colorTag, description));

        if (State != null)
            UpdateState(State);

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is NCCharacterNoteBoundUserInterfaceState notesState)
            _window?.UpdateState(notesState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
