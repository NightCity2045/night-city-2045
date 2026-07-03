using Content.Shared._NC.CitiNet.Delivery;
using Content.Shared.Lock;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._NC.CitiNet.Delivery;

public sealed class OTPKeypadSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly LockSystem _lockSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OTPKeypadComponent, LockToggleAttemptEvent>(OnLockAttempt);

        Subs.BuiEvents<OTPKeypadComponent>(OTPKeypadUiKey.Key, subs => {
            subs.Event<OTPKeypadSubmitPinMessage>(OnSubmitPin);
        });
    }

    private void OnLockAttempt(EntityUid uid, OTPKeypadComponent component, ref LockToggleAttemptEvent args)
    {
        // If the keypad is currently managing the lock, block all standard attempts to toggle it
        // This includes context menu verbs and clicking.
        if (component.IsLocked)
        {
            args.Cancelled = true;
        }
    }

    private void OnSubmitPin(EntityUid uid, OTPKeypadComponent component, OTPKeypadSubmitPinMessage msg)
    {
        if (msg.Pin == component.CurrentPin)
        {
            // First, allow the system to toggle the lock by disabling our block
            component.IsLocked = false;
            Dirty(uid, component);

            if (TryComp<LockComponent>(uid, out var lockComp))
            {
                _lockSystem.Unlock(uid, msg.Actor, lockComp);
            }

            // Clear PIN after successful use
            component.CurrentPin = null;
            Dirty(uid, component);

            _popup.PopupEntity("Код верный. Замок открыт.", uid);

            if (TryComp<ActorComponent>(msg.Actor, out var actor))
            {
                _chatManager.DispatchServerMessage(actor.PlayerSession, "Доступ разрешен. Заберите ваш товар.");
            }

            _uiSystem.CloseUi(uid, OTPKeypadUiKey.Key);
        }
        else
        {
            _popup.PopupEntity("Неверный код!", uid, msg.Actor, Content.Shared.Popups.PopupType.MediumCaution);
        }
    }
}





