using System;
using System.Linq;
using Content.Shared._NC.Netrunning;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.Animations;
using Robust.Shared.Animations;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Client._NC.Netrunning;

public sealed class NetrunningImmersionSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    
    private NetrunningFadeControl? _fade;
    private NetrunningFeedbackControl? _feedback;
    private NetrunningDefenseControl? _defense;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NetrunningImmersionEvent>(OnImmersionEvent);
        SubscribeNetworkEvent<NetrunningFeedbackEvent>(OnFeedbackEvent);
        SubscribeNetworkEvent<NetrunningDefenseWindowEvent>(OnDefenseWindow);
        SubscribeNetworkEvent<NetrunningDefenseResolvedEvent>(OnDefenseResolved);
    }

    private void OnImmersionEvent(NetrunningImmersionEvent ev)
    {
        if (ev.Start)
        {
            if (_fade != null)
                _uiManager.WindowRoot.RemoveChild(_fade);

            _fade = new NetrunningFadeControl();
            _uiManager.WindowRoot.AddChild(_fade);
            _fade.Fade(0f, 1f, 1.5f); // Fade to black
        }
        else
        {
            if (_fade == null) return;
            _fade.Fade(1f, 0f, 1.5f, () => {
                _uiManager.WindowRoot.RemoveChild(_fade);
                _fade = null;
            });
        }
    }

    private void OnFeedbackEvent(NetrunningFeedbackEvent ev)
    {
        if (_feedback != null)
            _uiManager.WindowRoot.RemoveChild(_feedback);

        _feedback = new NetrunningFeedbackControl();
        _uiManager.WindowRoot.AddChild(_feedback);
        _feedback.Show(ev.Title, ev.Message, ev.Critical, () =>
        {
            if (_feedback == null)
                return;

            _uiManager.WindowRoot.RemoveChild(_feedback);
            _feedback = null;
        });
    }

    private void OnDefenseWindow(NetrunningDefenseWindowEvent ev)
    {
        if (_defense != null)
            _uiManager.WindowRoot.RemoveChild(_defense);

        _defense = new NetrunningDefenseControl(ev);
        _defense.OnResponseSelected += shard =>
        {
            RaiseNetworkEvent(new NetrunningDefenseResponseEvent(
                ev.Deck,
                ev.Server,
                ev.TransactionId,
                shard));
            _defense?.MarkResponseSent();
        };
        _uiManager.WindowRoot.AddChild(_defense);
    }

    private void OnDefenseResolved(NetrunningDefenseResolvedEvent ev)
    {
        if (_defense == null || _defense.TransactionId != ev.TransactionId)
            return;

        _defense.ShowResolution(ev.AttackApplied);
        Timer.Spawn(TimeSpan.FromSeconds(1.5), () =>
        {
            if (_defense == null || _defense.TransactionId != ev.TransactionId)
                return;

            _uiManager.WindowRoot.RemoveChild(_defense);
            _defense = null;
        });
    }
}

public sealed class NetrunningFadeControl : Control
{
    private PanelContainer _panel;

    public NetrunningFadeControl()
    {
        _panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Black },
            MouseFilter = MouseFilterMode.Stop
        };
        AddChild(_panel);
        
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
    }

    public void Fade(float from, float to, float duration, Action? onComplete = null)
    {
        var anim = new Animation
        {
            Length = TimeSpan.FromSeconds(duration),
            AnimationTracks = {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames = {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(from), 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(to), duration)
                    }
                }
            }
        };

        this.StopAnimation("fade");
        this.PlayAnimation(anim, "fade");
        
        Timer.Spawn(TimeSpan.FromSeconds(duration), () => onComplete?.Invoke());
    }
}

public sealed class NetrunningFeedbackControl : Control
{
    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _message;

    public NetrunningFeedbackControl()
    {
        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);

        _panel = new PanelContainer
        {
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0b1118").WithAlpha(0.92f),
                BorderColor = Color.FromHex("#39ff14"),
                BorderThickness = new Thickness(2)
            },
            MinSize = new Vector2(420, 90),
        };

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(12)
        };

        _title = new Label
        {
            FontColorOverride = Color.FromHex("#39ff14")
        };

        _message = new Label
        {
            FontColorOverride = Color.White
        };

        box.AddChild(_title);
        box.AddChild(_message);
        _panel.AddChild(box);
        AddChild(_panel);

        LayoutContainer.SetAnchorPreset(_panel, LayoutContainer.LayoutPreset.CenterTop);
        LayoutContainer.SetMarginTop(_panel, 32);
    }

    public void Show(string title, string message, bool critical, Action onComplete)
    {
        _title.Text = title;
        _message.Text = message;

        if (_panel.PanelOverride is StyleBoxFlat style)
        {
            style.BorderColor = critical ? Color.FromHex("#ff3131") : Color.FromHex("#39ff14");
        }

        _title.FontColorOverride = critical ? Color.FromHex("#ff3131") : Color.FromHex("#39ff14");
        Modulate = Color.White.WithAlpha(0f);

        var anim = new Animation
        {
            Length = TimeSpan.FromSeconds(2.1f),
            AnimationTracks =
            {
                new AnimationTrackControlProperty
                {
                    Property = nameof(Control.Modulate),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), 0f),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0.15f),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 1.2f),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), 2.1f),
                    }
                }
            }
        };

        StopAnimation("feedback");
        PlayAnimation(anim, "feedback");
        Timer.Spawn(TimeSpan.FromSeconds(2.1f), onComplete);
    }
}

public sealed class NetrunningDefenseControl : Control
{
    public event Action<NetEntity>? OnResponseSelected;

    public int TransactionId { get; }

    private readonly Label _countdown;
    private readonly Label _status;
    private readonly ItemList _shards;
    private readonly Button _execute;
    private float _timeLeft;
    private NetEntity? _selectedShard;

    public NetrunningDefenseControl(NetrunningDefenseWindowEvent state)
    {
        TransactionId = state.TransactionId;
        _timeLeft = Math.Max(0f, state.ResponseMilliseconds / 1000f);

        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);

        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterMode.Stop,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#09090b").WithAlpha(0.97f),
                BorderColor = Color.FromHex("#ff2438"),
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 14,
                ContentMarginRightOverride = 14,
                ContentMarginTopOverride = 12,
                ContentMarginBottomOverride = 12,
            },
            MinSize = new Vector2(470, 330),
        };

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 7,
        };
        root.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-window-title"),
            FontColorOverride = Color.FromHex("#ff2438"),
        });
        root.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-window-program", ("program", state.DefenseName)),
            FontColorOverride = Color.FromHex("#ff9b38"),
        });

        _countdown = new Label
        {
            FontColorOverride = Color.FromHex("#ff2438"),
        };
        root.AddChild(_countdown);

        root.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-window-consequences"),
            FontColorOverride = Color.FromHex("#77d8ff"),
        });
        root.AddChild(new Label
        {
            Text = string.Join("\n", state.Consequences.Select(GetConsequenceText)),
            FontColorOverride = Color.White,
        });
        root.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-window-scripts"),
            FontColorOverride = Color.FromHex("#77d8ff"),
        });

        _shards = new ItemList
        {
            VerticalExpand = true,
            MinHeight = 110,
        };
        _execute = new Button
        {
            Text = Loc.GetString("netrunning-defense-window-execute"),
            Disabled = true,
            HorizontalExpand = true,
        };
        foreach (var shard in state.Shards)
        {
            _shards.AddItem(
                Loc.GetString("netrunning-defense-window-shard",
                    ("name", shard.Name),
                    ("ram", shard.RamCost)),
                metadata: shard.Shard);
        }
        _shards.OnItemSelected += args =>
        {
            _selectedShard = (NetEntity) args.ItemList[args.ItemIndex].Metadata!;
            _execute.Disabled = false;
        };
        root.AddChild(_shards);

        _status = new Label
        {
            Text = state.Shards.Count == 0
                ? Loc.GetString("netrunning-defense-window-no-scripts")
                : Loc.GetString("netrunning-defense-window-select"),
            FontColorOverride = state.Shards.Count == 0 ? Color.FromHex("#ff2438") : Color.LightGray,
        };
        root.AddChild(_status);

        _execute.OnPressed += _ =>
        {
            if (_selectedShard is { } shard)
                OnResponseSelected?.Invoke(shard);
        };
        root.AddChild(_execute);

        panel.AddChild(root);
        AddChild(panel);
        LayoutContainer.SetAnchorPreset(panel, LayoutContainer.LayoutPreset.CenterRight);
        LayoutContainer.SetMarginRight(panel, 24);
        UpdateCountdown();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_timeLeft <= 0f)
            return;

        _timeLeft = Math.Max(0f, _timeLeft - args.DeltaSeconds);
        UpdateCountdown();
    }

    public void MarkResponseSent()
    {
        _execute.Disabled = true;
        _shards.MouseFilter = MouseFilterMode.Ignore;
        _status.Text = Loc.GetString("netrunning-defense-window-response-sent");
        _status.FontColorOverride = Color.FromHex("#ff9b38");
    }

    public void ShowResolution(bool attackApplied)
    {
        _execute.Disabled = true;
        _shards.MouseFilter = MouseFilterMode.Ignore;
        _status.Text = Loc.GetString(attackApplied
            ? "netrunning-defense-window-attack-applied"
            : "netrunning-defense-window-attack-cancelled");
        _status.FontColorOverride = attackApplied ? Color.FromHex("#ff9b38") : Color.FromHex("#77d8ff");
    }

    private void UpdateCountdown()
    {
        _countdown.Text = Loc.GetString("netrunning-defense-window-countdown",
            ("seconds", MathF.Ceiling(_timeLeft * 10f) / 10f));
    }

    private static string GetConsequenceText(NetrunningDefenseConsequence consequence)
    {
        var suffix = consequence switch
        {
            NetrunningDefenseConsequence.NeuralBurn => "neural-burn",
            NetrunningDefenseConsequence.Disconnect => "disconnect",
            NetrunningDefenseConsequence.IceDamage => "ice-damage",
            NetrunningDefenseConsequence.Override => "override",
            _ => "unknown",
        };
        return Loc.GetString($"netrunning-defense-consequence-{suffix}");
    }
}
