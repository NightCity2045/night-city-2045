using Content.Client._White.UserInterface.Systems.CombatMode.Widgets;
using Content.Client.CombatMode;
using Content.Client.Gameplay;
using Content.Shared.CombatMode;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;

namespace Content.Client._White.UserInterface.Systems.CombatMode;

public sealed class CombatModeUIController : UIController, IOnStateEntered<GameplayState>, IOnSystemChanged<CombatModeSystem>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private CombatModeComponent? _combatModeComponent;

    private CombatModeGui? CombatModGui => UIManager.GetActiveUIWidgetOrNull<CombatModeGui>();

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(Content.Shared._White.CCVar.WhiteCVars.CombatModeAction, _ => UpdateVisibility());
    }

    public void OnSystemLoaded(CombatModeSystem system)
    {
        system.LocalPlayerCombatModeUpdated += OnCombatModeUpdated;
        system.LocalPlayerCombatModeAdded += OnCombatModeAdded;
        system.LocalPlayerCombatModeRemoved += OnCombatModeRemoved;
    }

    public void OnSystemUnloaded(CombatModeSystem system)
    {
        system.LocalPlayerCombatModeUpdated -= OnCombatModeUpdated;
        system.LocalPlayerCombatModeAdded -= OnCombatModeAdded;
        system.LocalPlayerCombatModeRemoved -= OnCombatModeRemoved;
    }

    public void OnStateEntered(GameplayState state)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (CombatModGui != null)
        {
            var useAction = _cfg.GetCVar(Content.Shared._White.CCVar.WhiteCVars.CombatModeAction);
            CombatModGui.Visible = !useAction && _combatModeComponent is { Enable: true, };
        }
    }

    private void OnCombatModeUpdated(bool inCombatMode)
    {
        CombatModGui?.OnCombatModeUpdated(inCombatMode);
    }

    private void OnCombatModeAdded(CombatModeComponent component)
    {
        _combatModeComponent = component;
        UpdateVisibility();
        OnCombatModeUpdated(component.IsInCombatMode);
    }

    private void OnCombatModeRemoved()
    {
        _combatModeComponent = null;
        UpdateVisibility();
    }

    public void ToggleCombatMode() => EntityManager.RaisePredictiveEvent(new ToggleCombatModeRequestEvent());
}
