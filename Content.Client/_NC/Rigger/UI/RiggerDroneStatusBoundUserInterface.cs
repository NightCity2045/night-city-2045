using Content.Shared._NC.Rigger.Events;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Rigger.UI;

public sealed class RiggerDroneStatusBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private RiggerDroneStatusWindow? _window;

    public RiggerDroneStatusBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new RiggerDroneStatusWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is RiggerDroneStatusBuiState droneState)
            _window?.UpdateState(droneState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();
    }
}
