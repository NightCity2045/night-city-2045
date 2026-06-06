using System;
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

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NetrunningImmersionEvent>(OnImmersionEvent);
        SubscribeNetworkEvent<NetrunningFeedbackEvent>(OnFeedbackEvent);
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
