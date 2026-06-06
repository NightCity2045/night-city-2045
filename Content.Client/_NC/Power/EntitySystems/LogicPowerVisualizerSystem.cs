using Content.Shared._NC.Power.Components;
using Content.Shared.Inventory;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._NC.Power.EntitySystems;

public sealed class LogicPowerVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private LogicPowerOverlay _overlay = default!;
    private bool _overlayEnabled = false;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new LogicPowerOverlay(EntityManager, _transform);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _playerManager.LocalPlayer?.ControlledEntity;
        if (player == null)
        {
            SetOverlayEnabled(false);
            return;
        }

        SetOverlayEnabled(HasARVisor(player.Value));
    }

    private bool HasARVisor(EntityUid user)
    {
        if (_inventory.TryGetSlotEntity(user, "eyes", out var eyesItem))
        {
            if (HasComp<ARPowerVisorComponent>(eyesItem))
                return true;
        }
        return HasComp<ARPowerVisorComponent>(user);
    }

    private void SetOverlayEnabled(bool enabled)
    {
        if (_overlayEnabled == enabled)
            return;

        _overlayEnabled = enabled;
        if (enabled)
            _overlayManager.AddOverlay(_overlay);
        else
            _overlayManager.RemoveOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay(_overlay);
    }
}
