using Content.Shared._NC.Netrunning.Components;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Netrunning.UI;

public sealed class NetNodeBoundUserInterface : BoundUserInterface
{
    private NetNodeWindow? _window;

    public NetNodeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new NetNodeWindow();
        _window.OnClose += Close;
        _window.OnControlAction += action => SendMessage(new NetNodeControlMessage(action));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is NetNodeUiState s)
        {
            _window?.UpdateState(s);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
