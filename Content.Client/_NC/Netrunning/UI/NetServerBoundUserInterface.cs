using System;
using Content.Shared._NC.Netrunning.Components;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Netrunning.UI;

public sealed class NetServerBoundUserInterface : BoundUserInterface
{
    private NetServerConsoleWindow? _window;

    public NetServerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new NetServerConsoleWindow();
        _window.OnClose += Close;
        _window.OnRefreshRequested += () => SendMessage(new NetServerScanMessage());
        _window.OnConstructRequested += (moduleId, anchor) => SendMessage(new NetServerConstructMessage(moduleId, anchor));
        _window.OnAdminRequested += () => SendMessage(new NetServerAdminMessage());
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NetServerUiState serverState)
            _window?.UpdateState(serverState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
