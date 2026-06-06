using Content.Client.UserInterface.Fragments;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.CitiNet.UI;

public sealed class NetBrowserBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private NetBrowserWindow? _window;
    private UIFragment? _activeSiteUI;
    private Control? _activeUiFragment;
    private string? _activeUrl;

    public NetBrowserBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    private NetBrowserUiState? _lastState;

    protected override void Open()
    {
        try 
        {
            Logger.InfoS("citinet.browser", $"Opening browser window for {Owner}...");
            _window = this.CreateWindowCenteredLeft<NetBrowserWindow>();
            _window.OnNavigate += (url) => SendMessage(new NetBrowserNavigateMessage(url));
            
            if (_lastState != null)
                _window.UpdateState(_lastState);

            Logger.InfoS("citinet.browser", "Browser window created successfully.");
        }
        catch (Exception e)
        {
            Logger.ErrorS("citinet.browser", $"CRASH DURING OPEN: {e}");
        }
        base.Open();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        try 
        {
            UpdateStateInternal(state);
        }
        catch (Exception e)
        {
            Logger.ErrorS("citinet.browser", $"CRITICAL ERROR IN BUI UPDATE: {e}");
        }
    }

    private void UpdateStateInternal(BoundUserInterfaceState state)
    {
        if (state is not NetBrowserUiState browserState)
        {
            _activeSiteUI?.UpdateState(state);
            return;
        }

        _lastState = browserState;
        _window?.UpdateState(browserState);

        if (_activeUrl == browserState.CurrentUrl && _activeSiteUI != null)
        {
            _activeSiteUI?.UpdateState(state);
            return;
        }

        _activeUrl = browserState.CurrentUrl;
        
        base.UpdateState(state);

        // Find the site prototype for the current URL
        NetSitePrototype? currentSite = null;
        foreach (var site in _prototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == browserState.CurrentUrl)
            {
                currentSite = site;
                break;
            }
        }

        if (currentSite == null)
        {
            Logger.WarningS("citinet.browser", $"No site found for URL: {browserState.CurrentUrl}");
            DetachSiteUI();
            return;
        }

        var ui = GetUIFragment(currentSite.UiKey);
        if (ui == null)
        {
            Logger.WarningS("citinet.browser", $"No UI fragment found for key: {currentSite.UiKey}");
            DetachSiteUI();
            return;
        }
        
        // Setup before GetUIFragmentRoot to ensure it's initialized
        ui.Setup(this, Owner);
        var control = ui.GetUIFragmentRoot();

        if (control == null)
        {
            DetachSiteUI();
            return;
        }

        if (_activeUiFragment?.GetType() == control.GetType())
        {
            _activeSiteUI?.UpdateState(state);
            return;
        }

        DetachSiteUI();
        AttachSiteUI(ui, control);
    }

    private void AttachSiteUI(UIFragment ui, Control control)
    {
        if (_window == null)
        {
            Logger.ErrorS("citinet.browser", "Failed to attach Site UI: Browser window is null!");
            return;
        }

        Logger.InfoS("citinet.browser", $"Attaching site UI fragment: {control.GetType().Name} to Viewport.");
        _activeSiteUI = ui;
        _activeUiFragment = control;
        _window.Viewport.AddChild(control);
    }

    private void DetachSiteUI()
    {
        if (_activeUiFragment != null)
        {
            Logger.DebugS("citinet.browser", "Detaching active site UI fragment.");
            if (_window is { Disposed: false })
                _window.Viewport.RemoveChild(_activeUiFragment);
            
            _activeUiFragment = null;
        }
        _activeSiteUI = null;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (_activeSiteUI is ICitiNetSiteFragment fragment)
            fragment.ReceiveMessage(message);
    }

    private UIFragment? GetUIFragment(string uiKey)
    {
        return uiKey switch
        {
            "NetHome" => new NetHomeSiteUIFragment(),
            "CitiNetComm" => new CitiNetUi(),
            "NcpdForensics" => new Forensics.NcpdForensicsUIFragment(),
            "FixerMarket" => new FixerMarket.FixerMarketUIFragment(),
            _ => null
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            DetachSiteUI();
            _window?.Dispose();
            _window = null;
        }
    }
}
