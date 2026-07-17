using System.Numerics;
using Content.Shared._NC.Rigger.Events;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._NC.Rigger.UI;

public sealed class RiggerDroneStatusWindow : DefaultWindow
{
    private readonly BoxContainer _droneRows;
    private readonly Label _emptyLabel;

    private static readonly Color HealthyColor = Color.FromHex("#2ecc71");
    private static readonly Color WarningColor = Color.FromHex("#f1c40f");
    private static readonly Color CriticalColor = Color.FromHex("#e74c3c");
    private static readonly Color BatteryColor = Color.FromHex("#3498db");
    private static readonly Color DisabledColor = Color.FromHex("#555555");

    public RiggerDroneStatusWindow()
    {
        Title = Loc.GetString("nc-rigger-drone-status-title");
        SetSize = new Vector2(520, 420);
        MinSize = new Vector2(420, 300);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            VerticalExpand = true,
        };

        _emptyLabel = new Label
        {
            Text = Loc.GetString("nc-rigger-drone-status-empty"),
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            VerticalExpand = true,
            StyleClasses = { "LabelBig" },
        };

        _droneRows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        scroll.AddChild(_droneRows);

        root.AddChild(_emptyLabel);
        root.AddChild(scroll);
        Contents.AddChild(root);
    }

    public void UpdateState(RiggerDroneStatusBuiState state)
    {
        _droneRows.RemoveAllChildren();
        _emptyLabel.Visible = state.Drones.Count == 0;

        foreach (var drone in state.Drones)
        {
            _droneRows.AddChild(CreateDroneRow(drone));
        }
    }

    private Control CreateDroneRow(RiggerDroneStatusEntry drone)
    {
        var borderColor = drone.IsAlive ? HealthyColor : CriticalColor;
        var row = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101316"),
                BorderColor = borderColor,
                BorderThickness = new Thickness(1),
            },
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6),
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        header.AddChild(new Label
        {
            Text = drone.Name,
            HorizontalExpand = true,
            ClipText = true,
        });

        header.AddChild(new Label
        {
            Text = Loc.GetString(drone.IsAlive
                ? "nc-rigger-drone-state-alive"
                : "nc-rigger-drone-state-offline"),
            FontColorOverride = borderColor,
            Align = Label.AlignMode.Right,
        });

        content.AddChild(header);
        content.AddChild(CreateStatusBar(
            Loc.GetString("nc-rigger-drone-health"),
            drone.HealthFraction,
            GetHealthColor(drone.HealthFraction),
            Loc.GetString("nc-rigger-drone-status-unknown")));
        content.AddChild(CreateStatusBar(
            Loc.GetString("nc-rigger-drone-battery"),
            drone.BatteryFraction,
            BatteryColor,
            Loc.GetString("nc-rigger-drone-battery-missing")));

        row.AddChild(content);
        return row;
    }

    private Control CreateStatusBar(string label, float? fraction, Color color, string unknownText)
    {
        var value = fraction == null ? 0f : Math.Clamp(fraction.Value, 0f, 1f);
        var text = fraction == null
            ? $"{label}: {unknownText}"
            : $"{label}: {value:P0}";

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = value,
            SetHeight = 22,
            HorizontalExpand = true,
            Margin = new Thickness(0, 4, 0, 0),
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = fraction == null ? DisabledColor : color },
            BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#22262b") },
        };

        // The label is drawn over the progress bar so health and charge read as background strips.
        var textBackdrop = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new Color(0, 0, 0, 0.55f),
            },
        };

        textBackdrop.AddChild(new Label
        {
            Text = text,
            Align = Label.AlignMode.Center,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
            HorizontalExpand = true,
        });

        bar.AddChild(textBackdrop);
        return bar;
    }

    private Color GetHealthColor(float? health)
    {
        if (health == null)
            return DisabledColor;

        return health.Value switch
        {
            <= 0.25f => CriticalColor,
            <= 0.55f => WarningColor,
            _ => HealthyColor,
        };
    }
}
