using Content.Shared._NC.Netrunning;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.Animations;
using Robust.Shared.Animations;
using Robust.Shared.Timing;

namespace Content.Client._NC.Netrunning;

public sealed class NetrunningImmersionSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    
    private NetrunningFadeControl? _fade;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NetrunningImmersionEvent>(OnImmersionEvent);
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
