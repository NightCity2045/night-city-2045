using Content.Shared._NC.Rigger.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._NC.Rigger;

public sealed class RiggerOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private RiggerOverlay? _overlay;

    public override void Initialize()
    {
        SubscribeLocalEvent<RiggerConsoleUserComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RiggerConsoleUserComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<RiggerConsoleUserComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<RiggerConsoleUserComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<RiggerOverlay>();
    }

    private void OnInit(Entity<RiggerConsoleUserComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity == ent.Owner)
            AddOverlay();
    }

    private void OnRemove(Entity<RiggerConsoleUserComponent> ent, ref ComponentRemove args)
    {
        if (_player.LocalEntity == ent.Owner)
            RemoveOverlay();
    }

    private void OnAttached(Entity<RiggerConsoleUserComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        AddOverlay();
    }

    private void OnDetached(Entity<RiggerConsoleUserComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveOverlay();
    }

    private void AddOverlay()
    {
        if (_overlay != null)
            return;

        _overlay = new RiggerOverlay();
        _overlayManager.AddOverlay(_overlay);
    }

    private void RemoveOverlay()
    {
        if (_overlay == null)
            return;

        _overlayManager.RemoveOverlay(_overlay);
        _overlay = null;
    }
}
