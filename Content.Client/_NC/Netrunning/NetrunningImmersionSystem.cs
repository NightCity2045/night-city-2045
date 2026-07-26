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
        SubscribeNetworkEvent<NetrunningDefenseResponseStatusEvent>(OnDefenseResponseStatus);
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
        if (_defense != null && _defense.TransactionId == ev.TransactionId)
        {
            _defense.RefreshState(ev.ResponseMilliseconds, ev.ThreatHealth, ev.ThreatMaxHealth);
            return;
        }

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
            _defense?.MarkResponsePending();
        };
        _uiManager.WindowRoot.AddChild(_defense);
    }

    private void OnDefenseResponseStatus(NetrunningDefenseResponseStatusEvent ev)
    {
        if (_defense == null || _defense.TransactionId != ev.TransactionId)
            return;

        _defense.ShowResponseStatus(ev);
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
    private readonly Label _health;
    private readonly Label _status;
    private readonly ProgressBar _execution;
    private readonly List<Button> _responseButtons = new();
    private float _totalTime;
    private float _timeLeft;
    private bool _responseSent;

    public NetrunningDefenseControl(NetrunningDefenseWindowEvent state)
    {
        TransactionId = state.TransactionId;
        _timeLeft = Math.Max(0f, state.ResponseMilliseconds / 1000f);
        _totalTime = Math.Max(0.001f, _timeLeft);

        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);

        var overlay = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 5,
            MinSize = new Vector2(420, 0),
            MaxSize = new Vector2(420, 460),
            MouseFilter = MouseFilterMode.Ignore,
        };

        overlay.AddChild(CreateSignalLine(Color.FromHex("#ff2438"), 3));
        overlay.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-overlay-title"),
            FontColorOverride = Color.FromHex("#ff2438"),
        });
        overlay.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-overlay-threat", ("threat", state.DefenseName)),
            FontColorOverride = Color.FromHex("#ff9b38"),
        });

        _health = new Label
        {
            FontColorOverride = Color.FromHex("#ff5263"),
        };
        overlay.AddChild(_health);

        _countdown = new Label
        {
            FontColorOverride = Color.FromHex("#ff2438"),
        };
        overlay.AddChild(_countdown);

        _execution = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            MinHeight = 8,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#25050a").WithAlpha(0.55f),
            },
            ForegroundStyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#ff2438"),
            },
        };
        overlay.AddChild(_execution);

        overlay.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-overlay-payload"),
            FontColorOverride = Color.FromHex("#77d8ff"),
        });
        overlay.AddChild(new Label
        {
            Text = string.Join("  //  ", state.Consequences.Select(GetConsequenceText)),
            FontColorOverride = Color.White,
        });

        overlay.AddChild(CreateSignalLine(Color.FromHex("#77d8ff"), 1));
        overlay.AddChild(new Label
        {
            Text = Loc.GetString("netrunning-defense-overlay-countermeasures"),
            FontColorOverride = Color.FromHex("#77d8ff"),
        });

        foreach (var shard in state.Shards)
        {
            var button = new Button
            {
                Text = Loc.GetString("netrunning-defense-overlay-script",
                    ("name", shard.Name),
                    ("ram", shard.RamCost)),
                HorizontalExpand = true,
                MouseFilter = MouseFilterMode.Stop,
                StyleBoxOverride = CreateResponseStyle(),
            };
            var shardUid = shard.Shard;
            button.OnPressed += _ => SubmitResponse(shardUid);
            _responseButtons.Add(button);
            overlay.AddChild(button);
        }

        _status = new Label
        {
            Text = state.Shards.Count == 0
                ? Loc.GetString("netrunning-defense-window-no-scripts")
                : Loc.GetString("netrunning-defense-overlay-select"),
            FontColorOverride = state.Shards.Count == 0 ? Color.FromHex("#ff2438") : Color.LightGray,
        };
        overlay.AddChild(_status);
        overlay.AddChild(CreateSignalLine(Color.FromHex("#ff2438"), 2));

        AddChild(overlay);
        LayoutContainer.SetAnchorPreset(overlay, LayoutContainer.LayoutPreset.CenterRight);
        LayoutContainer.SetMarginRight(overlay, 32);
        UpdateHealth(state.ThreatHealth, state.ThreatMaxHealth);
        UpdateCountdown();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_timeLeft <= 0f)
            return;

        _timeLeft = Math.Max(0f, _timeLeft - args.DeltaSeconds);
        UpdateCountdown();
        if (_timeLeft <= 0f && !_responseSent)
        {
            SetResponseButtonsDisabled(true);
            _status.Text = Loc.GetString("netrunning-defense-overlay-expired");
            _status.FontColorOverride = Color.FromHex("#ff2438");
        }
    }

    public void MarkResponsePending()
    {
        _responseSent = true;
        SetResponseButtonsDisabled(true);
        _status.Text = Loc.GetString("netrunning-defense-window-response-pending");
        _status.FontColorOverride = Color.FromHex("#ff9b38");
    }

    public void ShowResponseStatus(NetrunningDefenseResponseStatusEvent state)
    {
        UpdateHealth(state.ThreatHealth, state.ThreatMaxHealth);
        switch (state.Status)
        {
            case NetrunningDefenseResponseStatus.Accepted when state.ThreatHealth <= 0 && state.DamageDealt > 0:
                _status.Text = Loc.GetString("netrunning-defense-overlay-destroyed",
                    ("damage", state.DamageDealt));
                _status.FontColorOverride = Color.FromHex("#77d8ff");
                break;
            case NetrunningDefenseResponseStatus.Accepted when state.DamageDealt > 0:
                _status.Text = Loc.GetString("netrunning-defense-overlay-hit",
                    ("damage", state.DamageDealt),
                    ("health", state.ThreatHealth));
                _status.FontColorOverride = Color.FromHex("#77d8ff");
                break;
            case NetrunningDefenseResponseStatus.Accepted:
                _status.Text = Loc.GetString("netrunning-defense-window-response-sent");
                _status.FontColorOverride = Color.FromHex("#77d8ff");
                break;
            case NetrunningDefenseResponseStatus.Expired:
                _status.Text = Loc.GetString("netrunning-defense-overlay-too-late");
                _status.FontColorOverride = Color.FromHex("#ff2438");
                break;
            default:
                _responseSent = false;
                SetResponseButtonsDisabled(_timeLeft <= 0f);
                _status.Text = Loc.GetString("netrunning-defense-window-response-rejected");
                _status.FontColorOverride = Color.FromHex("#ff2438");
                break;
        }
    }

    public void RefreshState(int milliseconds, int threatHealth, int threatMaxHealth)
    {
        _timeLeft = Math.Max(0f, milliseconds / 1000f);
        _totalTime = Math.Max(0.001f, _timeLeft);
        UpdateHealth(threatHealth, threatMaxHealth);
        if (!_responseSent)
        {
            SetResponseButtonsDisabled(_timeLeft <= 0f);
            if (_timeLeft > 0f)
            {
                _status.Text = Loc.GetString("netrunning-defense-overlay-select");
                _status.FontColorOverride = Color.LightGray;
            }
        }
        UpdateCountdown();
    }

    public void ShowResolution(bool attackApplied)
    {
        SetResponseButtonsDisabled(true);
        _status.Text = Loc.GetString(attackApplied
            ? "netrunning-defense-window-attack-applied"
            : "netrunning-defense-window-attack-cancelled");
        _status.FontColorOverride = attackApplied ? Color.FromHex("#ff9b38") : Color.FromHex("#77d8ff");
    }

    private void UpdateCountdown()
    {
        _execution.Value = 1f - _timeLeft / _totalTime;
        _countdown.Text = Loc.GetString("netrunning-defense-window-countdown",
            ("seconds", MathF.Ceiling(_timeLeft * 10f) / 10f));
    }

    private void SubmitResponse(NetEntity shard)
    {
        if (_responseSent || _timeLeft <= 0f)
            return;

        MarkResponsePending();
        OnResponseSelected?.Invoke(shard);
    }

    private void SetResponseButtonsDisabled(bool disabled)
    {
        foreach (var button in _responseButtons)
            button.Disabled = disabled;
    }

    private void UpdateHealth(int health, int maxHealth)
    {
        _health.Text = maxHealth > 0
            ? Loc.GetString("netrunning-defense-overlay-integrity",
                ("health", Math.Max(0, health)),
                ("max", maxHealth))
            : Loc.GetString("netrunning-defense-overlay-integrity-unknown");
    }

    private static PanelContainer CreateSignalLine(Color color, int height)
    {
        return new PanelContainer
        {
            MinHeight = height,
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = color,
            },
        };
    }

    private static StyleBoxFlat CreateResponseStyle()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#160308").WithAlpha(0.78f),
            BorderColor = Color.FromHex("#ff2438"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 7,
            ContentMarginBottomOverride = 7,
        };
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
