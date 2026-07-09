using Content.Shared._NC.Netrunning.Components;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Netrunning.UI;

public sealed class CyberdeckBoundUserInterface : BoundUserInterface
{
    private CyberdeckTerminalWindow? _window;

    public CyberdeckBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new CyberdeckTerminalWindow();
        _window.OnClose += Close;
        _window.OnCompileRequested += (code, name, kind, shard) =>
        {
            SendMessage(new CyberdeckCompileMessage(code, name, kind, shard));
        };

        _window.OnEjectRequested += (shard) =>
        {
            SendMessage(new CyberdeckEjectMessage(shard));
        };

        _window.OnRunRequested += (shard) =>
        {
            SendMessage(new CyberdeckExecuteMessage(shard));
        };

        _window.OnHotSimRequested += () =>
        {
            SendMessage(new CyberdeckHotSimMessage());
        };

        _window.OnConstructRequested += (moduleId, anchor) =>
        {
            SendMessage(new CyberdeckConstructMessage(moduleId, anchor));
        };

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CyberdeckUiState deckState)
            return;

        _window?.UpdateState(deckState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is CyberdeckLogMessage log)
        {
            _window?.AddLog(log.Text);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
