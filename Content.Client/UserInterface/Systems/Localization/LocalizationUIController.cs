using System.Globalization;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.UserInterface.Systems.Actions;
using Content.Client.UserInterface.Systems.Character;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Hotbar;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client.UserInterface.Systems.Sandbox;
using Content.Client.Localization;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client.UserInterface.Systems.Localization;

public sealed class LocalizationUIController : UIController
{
    private const string FallbackCultureName = "en-US";
    private readonly ISawmill _sawmill = Logger.GetSawmill("localization.ui");

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IStateManager _state = default!;

    private bool _suppressCultureChanged;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CVars.LocCultureName, OnCultureNameChanged, true);
    }

    private void OnCultureNameChanged(string cultureName)
    {
        if (_suppressCultureChanged)
            return;

        try
        {
            var culture = ResolveSupportedCultureOrFallback(cultureName);
            _loc.SetCulture(culture);
            _loc.ReloadLocalizations();
            RefreshCurrentCulture();

            if (string.Equals(cultureName, culture.Name, StringComparison.OrdinalIgnoreCase))
                return;

            _suppressCultureChanged = true;
            _cfg.SetCVar(CVars.LocCultureName, culture.Name);
            _suppressCultureChanged = false;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to switch localization culture to '{cultureName}': {e}");
        }
    }

    public void RefreshCurrentCulture()
    {
        RefreshControllersAndScreens();
        RefreshLocalizedControls();
    }

    private void RefreshControllersAndScreens()
    {
        switch (_state.CurrentState)
        {
            case GameplayState:
                TryRefresh(() => UIManager.GetUIController<HotbarUIController>().ReloadHotbar());
                TryRefresh(() => UIManager.GetUIController<InventoryUIController>().ReloadSlots());
                TryRefresh(() => UIManager.GetUIController<ActionUIController>().RefreshLocalization());
                TryRefresh(() => UIManager.GetUIController<CharacterUIController>().RefreshLocalization());
                TryRefresh(() => UIManager.GetUIController<SandboxUIController>().RefreshLocalization());
                break;
            case LobbyState lobby:
                TryRefresh(lobby.RefreshLocalization);
                break;
        }

        TryRefresh(() => UIManager.GetUIController<ChatUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<OptionsUIController>().RefreshLocalization());
        TryRefresh(() => UIManager.GetUIController<EscapeUIController>().RefreshLocalization());
    }

    private void RefreshLocalizedControls()
    {
        foreach (var root in UIManager.AllRoots)
        {
            RelocalizeRecursive(root);
        }
    }

    private void RelocalizeRecursive(Control control)
    {
        if (control is ILocalizedControl localized && !control.Disposed)
            TryRefresh(localized.Relocalize);

        foreach (var child in control.Children)
        {
            RelocalizeRecursive(child);
        }
    }

    private void TryRefresh(Action refresh)
    {
        try
        {
            refresh();
        }
        catch (Exception e)
        {
            _sawmill.Debug($"Skipped localization refresh step: {e}");
        }
    }

    private CultureInfo ResolveSupportedCultureOrFallback(string cultureName)
    {
        var supported = _loc.GetFoundCultures();

        if (TryParseCulture(cultureName, out var culture) &&
            TryResolveSupportedCulture(culture, supported, out var resolved))
            return resolved;

        if (TryParseCulture(FallbackCultureName, out var fallback) &&
            TryResolveSupportedCulture(fallback, supported, out resolved))
            return resolved;

        return supported.Count > 0
            ? supported[0]
            : CultureInfo.GetCultureInfo(FallbackCultureName, false);
    }

    private static bool TryResolveSupportedCulture(
        CultureInfo requested,
        IReadOnlyList<CultureInfo> supported,
        out CultureInfo resolved)
    {
        foreach (var culture in supported)
        {
            if (!string.Equals(culture.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            resolved = culture;
            return true;
        }

        resolved = default!;
        return false;
    }

    private static bool TryParseCulture(string? cultureName, out CultureInfo culture)
    {
        culture = default!;

        if (string.IsNullOrWhiteSpace(cultureName))
            return false;

        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName, false);
            return !string.IsNullOrWhiteSpace(culture.Name);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
