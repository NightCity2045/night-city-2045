using Content.Shared._NC.World.Components;
using Content.Shared.Construction.Components;
using Content.Shared.DragDrop;
using Content.Shared.Movement.Pulling.Events;

namespace Content.Shared._NC.World.Systems;

public sealed class SharedNCFixedWorldObjectSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCFixedWorldObjectComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<NCFixedWorldObjectComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<NCFixedWorldObjectComponent, CanDragEvent>(OnCanDrag);
    }

    private void OnUnanchorAttempt(Entity<NCFixedWorldObjectComponent> ent, ref UnanchorAttemptEvent args)
    {
        // Mapper-owned fixtures, like post terminals and trash bins, should not be removed with tools.
        args.Cancel();
    }

    private void OnPullAttempt(Entity<NCFixedWorldObjectComponent> ent, ref PullAttemptEvent args)
    {
        // Pulling is the common way to drag anchored storage once it is loosened or movable.
        args.Cancelled = true;
    }

    private void OnCanDrag(Entity<NCFixedWorldObjectComponent> ent, ref CanDragEvent args)
    {
        // Drag/drop uses this event as an opt-in. Keep it explicitly disabled for fixed objects.
        args.Handled = false;
    }
}
