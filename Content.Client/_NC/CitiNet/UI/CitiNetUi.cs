using Content.Client.UserInterface.Fragments;
using Content.Shared._NC.CitiNet;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;


namespace Content.Client._NC.CitiNet.UI;

/// <summary>
/// NC — Клиентский UI-контроллер картриджа CitiNet.
/// Связывает UI-фрагмент с BUI-сообщениями.
/// </summary>
public sealed partial class CitiNetUi : UIFragment
{
    private CitiNetUiFragment? _fragment;

    private int _lastCallMessagesCount = 0;
    private int _lastGroupMessagesCount = 0;
    private int _lastBbsMessagesCount = 0;
    private CitiNetCallState _lastCallState = CitiNetCallState.None;


    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new CitiNetUiFragment();

        // Подписка на обычные действия CitiNet
        _fragment.OnSendMessage += (type, targetId, content) =>
        {
            var citiNetMessage = new CitiNetUiMessageEvent(type, targetId, content);
            var message = new CartridgeUiMessage(citiNetMessage);
            userInterface.SendMessage(message);
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not CitiNetUiState cast) return;

        _lastCallMessagesCount = cast.CallMessages.Count;
        _lastGroupMessagesCount = cast.GroupMessages.Count;
        _lastBbsMessagesCount = cast.ChannelMessages.Count;
        _lastCallState = cast.CallState;


        _fragment?.UpdateState(cast);
    }
}
