using Content.Shared._NC.Power.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._NC.Power;

public sealed class LogicPowerOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public LogicPowerOverlay(IEntityManager entManager, SharedTransformSystem transform)
    {
        _entManager = entManager;
        _transform = transform;
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
    }
}
