using Content.Client.CharacterInfo;
using Content.Client.Stylesheets;
using Content.Shared._NC.Stats;
using Content.Shared._NC.Stats.Components;
using Content.Shared._NC.Stats.Prototypes;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.Stats.UI;

/// <summary>
/// Adds Night City RPG stats and BODY weight state to the character information window.
/// </summary>
public sealed class NCCharacterStatsInfoSystem : EntitySystem
{
    [Dependency] private readonly CharacterInfoSystem _characterInfo = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private static readonly string[] StatOrder =
    {
        NCStatIds.Intelligence,
        NCStatIds.Reflexes,
        NCStatIds.Dexterity,
        NCStatIds.Technique,
        NCStatIds.Cool,
        NCStatIds.Will,
        NCStatIds.Luck,
        NCStatIds.Move,
        NCStatIds.Body,
        NCStatIds.Empathy,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
        SubscribeLocalEvent<NCStatsComponent, AfterAutoHandleStateEvent>(OnStatsStateChanged);
        SubscribeLocalEvent<NCBodyComponent, AfterAutoHandleStateEvent>(OnBodyStateChanged);
    }

    private void OnGetCharacterInfoControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent ev)
    {
        if (!TryComp<NCStatsComponent>(ev.Entity, out var stats) &&
            !TryComp<NCBodyComponent>(ev.Entity, out _))
        {
            return;
        }

        ev.Controls.Add(BuildInfoBlock(ev.Entity, stats));
    }

    private void OnStatsStateChanged(EntityUid uid, NCStatsComponent component, ref AfterAutoHandleStateEvent args)
    {
        RefreshCharacterWindow(uid);
    }

    private void OnBodyStateChanged(EntityUid uid, NCBodyComponent component, ref AfterAutoHandleStateEvent args)
    {
        RefreshCharacterWindow(uid);
    }

    private void RefreshCharacterWindow(EntityUid uid)
    {
        // CharacterInfo is server-driven, so request a normal refresh when the local networked state changes.
        if (_player.LocalEntity == uid)
            _characterInfo.RequestCharacterInfo();
    }

    private Control BuildInfoBlock(EntityUid uid, NCStatsComponent? stats)
    {
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(5, 8, 5, 0),
        };

        root.AddChild(new Label
        {
            Text = Loc.GetString("nc-character-info-stats-header"),
            HorizontalAlignment = Control.HAlignment.Center,
            StyleClasses = { StyleNano.StyleClassTooltipActionTitle },
        });

        if (stats != null)
            root.AddChild(BuildStatsGrid(stats));

        if (TryComp<NCBodyComponent>(uid, out var body))
            root.AddChild(BuildWeightBlock(body));

        return root;
    }

    private Control BuildStatsGrid(NCStatsComponent stats)
    {
        var values = new Dictionary<string, int>();
        foreach (var entry in stats.Stats)
        {
            values[entry.StatId] = entry.Value.FinalValue;
        }

        var grid = new GridContainer
        {
            Columns = 4,
            HorizontalAlignment = Control.HAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };

        foreach (var statId in StatOrder)
        {
            if (!values.TryGetValue(statId, out var value))
                continue;

            var statName = _prototype.TryIndex<NCStatPrototype>(statId, out var proto)
                ? Loc.GetString(proto.ShortNameKey)
                : statId;

            grid.AddChild(new Label
            {
                Text = statName,
                StyleClasses = { StyleNano.StyleClassLabelSecondaryColor },
                MinWidth = 44,
            });

            grid.AddChild(new Label
            {
                Text = value.ToString(),
                MinWidth = 28,
            });
        }

        return grid;
    }

    private Control BuildWeightBlock(NCBodyComponent body)
    {
        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var load = new Label
        {
            Text = Loc.GetString("nc-character-info-weight",
                ("current", body.CurrentWeight.ToString("0.#")),
                ("max", body.MaxWeight.ToString("0.#")),
                ("level", Loc.GetString(GetLoadLevelKey(body.Level)))),
            HorizontalAlignment = Control.HAlignment.Center,
            FontColorOverride = GetLoadLevelColor(body.Level),
        };

        box.AddChild(load);
        return box;
    }

    private static string GetLoadLevelKey(NCBodyLoadLevel level)
    {
        return level switch
        {
            NCBodyLoadLevel.Light => "nc-body-load-light",
            NCBodyLoadLevel.Heavy => "nc-body-load-heavy",
            NCBodyLoadLevel.Overloaded => "nc-body-load-overloaded",
            _ => "nc-body-load-none",
        };
    }

    private static Color GetLoadLevelColor(NCBodyLoadLevel level)
    {
        return level switch
        {
            NCBodyLoadLevel.Light => StyleNano.ConcerningOrangeFore,
            NCBodyLoadLevel.Heavy => StyleNano.DangerousRedFore,
            NCBodyLoadLevel.Overloaded => Color.DarkRed,
            _ => Color.LightGray,
        };
    }
}
