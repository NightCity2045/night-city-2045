using System.Numerics;
using Content.Shared._NC.Rigger;
using Content.Shared._NC.Rigger.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._NC.Rigger;

public sealed class RiggerOverlay : Overlay
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly HashSet<Vector2i> _visibleTiles = new();
    private IRenderTexture? _staticTexture;
    private IRenderTexture? _stencilTexture;
    private float _updateRate = 1f / 30f;
    private float _accumulator;

    public RiggerOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_stencilTexture?.Texture.Size != args.Viewport.Size)
        {
            _staticTexture?.Dispose();
            _stencilTexture?.Dispose();
            _stencilTexture = _clyde.CreateRenderTarget(args.Viewport.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "rigger-stencil");
            _staticTexture = _clyde.CreateRenderTarget(args.Viewport.Size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "rigger-static");
        }

        var playerEnt = _player.LocalEntity;
        if (!_entManager.TryGetComponent(playerEnt, out RiggerConsoleUserComponent? user) ||
            !_entManager.TryGetComponent(playerEnt, out TransformComponent? playerXform))
        {
            return;
        }

        var gridUid = playerXform.GridUid ?? EntityUid.Invalid;
        _entManager.TryGetComponent(gridUid, out MapGridComponent? grid);
        _entManager.TryGetComponent(gridUid, out BroadphaseComponent? broadphase);

        var worldHandle = args.WorldHandle;
        var worldBounds = args.WorldBounds;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        _accumulator -= (float) _timing.FrameTime.TotalSeconds;

        if (grid != null && broadphase != null)
        {
            var xforms = _entManager.System<SharedTransformSystem>();

            if (_accumulator <= 0f)
            {
                _accumulator = MathF.Max(0f, _accumulator + _updateRate);
                _visibleTiles.Clear();
                _entManager.System<RiggerVisionSystem>().GetView(playerEnt!.Value, (gridUid, broadphase, grid), worldBounds, _visibleTiles);
            }

            var gridMatrix = xforms.GetWorldMatrix(gridUid);
            var matrix = Matrix3x2.Multiply(gridMatrix, invMatrix);

            worldHandle.RenderInRenderTarget(_stencilTexture!, () =>
            {
                worldHandle.SetTransform(matrix);

                foreach (var tile in _visibleTiles)
                {
                    var aabb = _entManager.System<EntityLookupSystem>().GetLocalBounds(tile, grid.TileSize);
                    worldHandle.DrawRect(aabb, Color.White);
                }
            }, Color.Transparent);

            worldHandle.RenderInRenderTarget(_staticTexture!, () =>
            {
                worldHandle.SetTransform(invMatrix);
                var shader = _proto.Index<ShaderPrototype>("CameraStatic").Instance();
                worldHandle.UseShader(shader);
                worldHandle.DrawRect(worldBounds, Color.White);
            }, Color.Black);
        }
        else
        {
            worldHandle.RenderInRenderTarget(_stencilTexture!, () => { }, Color.Transparent);
            worldHandle.RenderInRenderTarget(_staticTexture!, () =>
            {
                worldHandle.SetTransform(Matrix3x2.Identity);
                worldHandle.DrawRect(worldBounds, Color.Black);
            }, Color.Black);
        }

        worldHandle.UseShader(_proto.Index<ShaderPrototype>("StencilMask").Instance());
        worldHandle.DrawTextureRect(_stencilTexture!.Texture, worldBounds);
        worldHandle.UseShader(_proto.Index<ShaderPrototype>("StencilDraw").Instance());
        worldHandle.DrawTextureRect(_staticTexture!.Texture, worldBounds);
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }
}
