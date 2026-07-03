using Content.Shared._NC.CitiNet.Delivery;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._NC.CitiNet.UI.Delivery;

public sealed class OTPKeypadBoundUserInterface : BoundUserInterface
{
    private OTPKeypadWindow? _window;

    public OTPKeypadBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<OTPKeypadWindow>();
        _window.OnSubmit += (pin) => SendMessage(new OTPKeypadSubmitPinMessage(pin));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Dispose();
            _window = null;
        }
    }
}
