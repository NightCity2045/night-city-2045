using Content.Shared._NC.Power.Components;
using Content.Client.Hands.Systems;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._NC.Power;

public sealed class LogicPowerOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;
    private readonly HandsSystem _hands;
    private readonly IInputManager _input;
    private readonly IEyeManager _eye;
    private readonly IPlayerManager _player;
    private readonly List<(EntityUid Provider, MapCoordinates End)> _temporaryFrozenPoints = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public LogicPowerOverlay(
        IEntityManager entManager,
        SharedTransformSystem transform,
        HandsSystem hands,
        IInputManager input,
        IEyeManager eye,
        IPlayerManager player)
    {
        _entManager = entManager;
        _transform = transform;
        _hands = hands;
        _input = input;
        _eye = eye;
        _player = player;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var query = _entManager.EntityQueryEnumerator<LogicPowerProviderComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var provider, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var startPos = _transform.GetWorldPosition(xform);
            
            // Basic PVS check
            if (!args.WorldBounds.Enlarged(5f).Contains(startPos))
            {
                // Check if any receiver is in view
                bool anyInView = false;
                foreach (var receiverUid in provider.Receivers)
                {
                    if (_entManager.TryGetComponent<TransformComponent>(receiverUid, out var rXform) &&
                        args.WorldBounds.Enlarged(5f).Contains(_transform.GetWorldPosition(rXform)))
                    {
                        anyInView = true;
                        break;
                    }
                }
                if (!anyInView)
                    continue;
            }

            foreach (var receiverUid in provider.Receivers)
            {
                if (!_entManager.TryGetComponent<TransformComponent>(receiverUid, out var receiverXform))
                    continue;

                if (receiverXform.MapID != args.MapId)
                    continue;

                var endPos = _transform.GetWorldPosition(receiverXform);
                
                handle.DrawLine(startPos, endPos, Color.Lime.WithAlpha(0.4f));
            }
        }

        DrawTemporaryFrozenPoints(args);
        DrawPendingConnection(args);
    }

    public void AddTemporaryFrozenPoint(EntityUid providerUid, MapCoordinates endCoordinates)
    {
        // Frozen maps do not run the real wiring interaction immediately, so store a visual-only endpoint.
        foreach (var point in _temporaryFrozenPoints)
        {
            if (point.Provider != providerUid || point.End.MapId != endCoordinates.MapId)
                continue;

            if ((point.End.Position - endCoordinates.Position).LengthSquared() < 0.04f)
                return;
        }

        _temporaryFrozenPoints.Add((providerUid, endCoordinates));
    }

    private void DrawTemporaryFrozenPoints(in OverlayDrawArgs args)
    {
        if (_temporaryFrozenPoints.Count == 0)
            return;

        List<(EntityUid Provider, MapCoordinates End)>? stalePoints = null;
        foreach (var point in _temporaryFrozenPoints)
        {
            if (!_entManager.TryGetComponent<TransformComponent>(point.Provider, out var providerXform))
            {
                stalePoints ??= new List<(EntityUid Provider, MapCoordinates End)>();
                stalePoints.Add(point);
                continue;
            }

            if (providerXform.MapID != args.MapId || point.End.MapId != args.MapId)
                continue;

            var startPos = _transform.GetWorldPosition(providerXform);
            var endPos = point.End.Position;
            args.WorldHandle.DrawLine(startPos, endPos, Color.Lime.WithAlpha(0.4f));
            args.WorldHandle.DrawCircle(endPos, 0.12f, Color.Lime.WithAlpha(0.4f));
        }

        if (stalePoints == null)
            return;

        foreach (var point in stalePoints)
        {
            _temporaryFrozenPoints.Remove(point);
        }
    }

    private void DrawPendingConnection(in OverlayDrawArgs args)
    {
        // While wiring is in progress, draw a preview from the selected provider to the current mouse position.
        if (_player.LocalEntity is not EntityUid playerUid)
            return;

        var activeItem = _hands.GetActiveItem(playerUid);
        if (activeItem == null ||
            !_entManager.TryGetComponent<WireSpoolComponent>(activeItem.Value, out var spool) ||
            spool.ActiveProvider == null)
        {
            return;
        }

        if (!_entManager.TryGetComponent<TransformComponent>(spool.ActiveProvider.Value, out var providerXform) ||
            providerXform.MapID == MapId.Nullspace ||
            providerXform.MapID != args.MapId)
        {
            return;
        }

        var mouseScreenPos = _input.MouseScreenPosition.Position;
        var mouseMapPos = _eye.ScreenToMap(mouseScreenPos);

        if (mouseMapPos.MapId != args.MapId)
            return;

        var startPos = _transform.GetWorldPosition(providerXform);
        var endPos = mouseMapPos.Position;
        var distance = (endPos - startPos).Length();

        // Preview uses the same gameplay limit as the spool so sandbox users see exactly when a link is valid.
        var color = distance <= spool.MaxDistance
            ? Color.Cyan.WithAlpha(0.75f)
            : Color.Red.WithAlpha(0.75f);

        args.WorldHandle.DrawLine(startPos, endPos, color);
        args.WorldHandle.DrawCircle(endPos, 0.12f, color);
    }
}
