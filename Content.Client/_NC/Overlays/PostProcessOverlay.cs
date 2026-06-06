using System.Numerics;
using Content.Shared._White.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.Overlays;

public sealed class PostProcessOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IClyde _clyde = default!;

    private readonly ShaderInstance _thresholdShader;
    private readonly ShaderInstance _postProcessShader;

    private IRenderTexture? _bloomTarget;
    private IRenderTexture? _blurBuffer;

    public PostProcessOverlay()
    {
        IoCManager.InjectDependencies(this);
        _thresholdShader = _prototype.Index<ShaderPrototype>("Threshold").Instance().Duplicate();
        _postProcessShader = _prototype.Index<ShaderPrototype>("PostProcess").Instance().Duplicate();
        ZIndex = 100;
    }

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || args.Viewport.Eye == null)
            return;

        var worldHandle = args.WorldHandle;
        var viewport = args.Viewport;
        var size = viewport.Size;
        var worldBounds = args.WorldBounds;

        bool bloomEnabled = _cfg.GetCVar(WhiteCVars.Bloom);
        if (bloomEnabled)
        {
            var bloomSize = size / 2;
            if (_bloomTarget?.Size != bloomSize)
            {
                _bloomTarget?.Dispose();
                _blurBuffer?.Dispose();
                _bloomTarget = _clyde.CreateRenderTarget(bloomSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "bloom-target");
                _blurBuffer = _clyde.CreateRenderTarget(bloomSize, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "bloom-blur-buffer");
            }

            var originalTransform = worldHandle.GetTransform();

            // 1. Threshold pass
            _thresholdShader.SetParameter("threshold", _cfg.GetCVar(WhiteCVars.BloomThreshold));
            _thresholdShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
            
            worldHandle.RenderInRenderTarget(_bloomTarget!, () =>
            {
                var bloomScale = (Vector2) bloomSize / size;
                var matrix = _bloomTarget!.GetWorldToLocalMatrix(viewport.Eye, viewport.RenderScale * bloomScale);
                worldHandle.SetTransform(matrix);
                
                worldHandle.UseShader(_thresholdShader);
                worldHandle.DrawRect(worldBounds, Color.White);
                worldHandle.UseShader(null);
            }, Color.Black);

            // 2. Blur pass
            _clyde.BlurRenderTarget(viewport, _bloomTarget!, _blurBuffer!, viewport.Eye, 2.0f);
            
            worldHandle.SetTransform(originalTransform);
        }

        // Final Composite pass
        _postProcessShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _postProcessShader.SetParameter("bloomEnabled", bloomEnabled);
        if (bloomEnabled && _bloomTarget != null)
        {
            _postProcessShader.SetParameter("BLOOM_TEXTURE", _bloomTarget.Texture);
            _postProcessShader.SetParameter("bloomIntensity", _cfg.GetCVar(WhiteCVars.BloomIntensity) / 100.0f);
        }

        bool vignetteEnabled = _cfg.GetCVar(WhiteCVars.Vignette);
        _postProcessShader.SetParameter("vignetteEnabled", vignetteEnabled);
        if (vignetteEnabled)
        {
            _postProcessShader.SetParameter("vignetteRadius", _cfg.GetCVar(WhiteCVars.VignetteRadius));
            _postProcessShader.SetParameter("vignetteSoftness", _cfg.GetCVar(WhiteCVars.VignetteSoftness));
            _postProcessShader.SetParameter("vignetteColor", Color.Black);
        }

        bool grainEnabled = _cfg.GetCVar(WhiteCVars.FilmGrain);
        _postProcessShader.SetParameter("grainEnabled", grainEnabled);
        if (grainEnabled)
        {
            _postProcessShader.SetParameter("grainStrength", _cfg.GetCVar(WhiteCVars.FilmGrainStrength) / 100f);
        }

        worldHandle.UseShader(_postProcessShader);
        worldHandle.DrawRect(args.WorldBounds, Color.White);
        worldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();
        _bloomTarget?.Dispose();
        _blurBuffer?.Dispose();
    }
}
