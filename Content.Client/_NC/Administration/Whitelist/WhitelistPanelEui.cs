using Content.Client.Eui;
using Content.Shared._NC.Administration.Whitelist;
using Content.Shared.Eui;
using Robust.Shared.Network;

namespace Content.Client._NC.Administration.Whitelist;

/// <summary>
/// Connects whitelist panel controls to its server-authoritative EUI.
/// </summary>
public sealed class WhitelistPanelEui : BaseEui
{
    private readonly WhitelistPanelWindow _window;

    public WhitelistPanelEui()
    {
        _window = new WhitelistPanelWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnSearch += search => SendMessage(new WhitelistPanelSearchMessage(search));
        _window.OnSetPage += page => SendMessage(new WhitelistPanelSetPageMessage(page));
        _window.OnAdd += player => SendMessage(new WhitelistPanelAddMessage(player));
        _window.OnRemove += userId => SendMessage(new WhitelistPanelRemoveMessage(userId));
        _window.OnRefresh += () => SendMessage(new WhitelistPanelRefreshMessage());
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
        _window.Dispose();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WhitelistPanelEuiState whitelistState)
            _window.HandleState(whitelistState);
    }
}
