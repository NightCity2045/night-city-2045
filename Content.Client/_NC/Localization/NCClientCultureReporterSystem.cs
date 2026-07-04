using Content.Shared._NC.Localization;
using Content.Shared.CCVar;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._NC.Localization;

/// <summary>
/// Reports the selected UI language to the server because loc.culture_name is an
/// archived client CVar, not a replicated one.
/// </summary>
public sealed class NCClientCultureReporterSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(CVars.LocCultureName, OnCultureChanged, true);
    }

    public override void Shutdown()
    {
        _cfg.UnsubValueChanged(CVars.LocCultureName, OnCultureChanged);
        base.Shutdown();
    }

    private void OnCultureChanged(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return;

        _cfg.SetCVar(CCVars.NCPreferredCulture, cultureName);
        RaiseNetworkEvent(new NCClientCultureChangedEvent(cultureName));
    }
}
