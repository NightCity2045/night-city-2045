using Content.Server._NC.NPC.Components;
using Content.Shared.Inventory;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs;
using Content.Shared.Interaction.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;

namespace Content.Server._NC.NPC.Systems;

public sealed class NpcUnlootableSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcUnlootableComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<NpcUnlootableComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
    }

    private void OnItemInserted(EntityUid uid, NpcUnlootableComponent component, EntInsertedIntoContainerMessage args)
    {
        // Don't prevent removal for ammo/magazines so NPCs can reload
        if (HasComp<AmmoComponent>(args.Entity) || 
            HasComp<BallisticAmmoProviderComponent>(args.Entity) ||
            HasComp<MagazineAmmoProviderComponent>(args.Entity))
        {
            return;
        }

        // Add UnremoveableComponent with DeleteOnDrop so if it somehow drops, it gets deleted.
        EnsureComp<UnremoveableComponent>(args.Entity).DeleteOnDrop = true;
    }

    private void OnMobStateChanged(EntityUid uid, NpcUnlootableComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            // Delete all items in the inventory
            if (TryComp<InventoryComponent>(uid, out var inventory))
            {
                var enumerator = _inventory.GetSlotEnumerator((uid, inventory));
                while (enumerator.MoveNext(out var slot))
                {
                    if (slot.ID == "jumpsuit" || slot.ID == "shoes")
                        continue;

                    if (slot.ContainedEntity != null)
                        QueueDel(slot.ContainedEntity.Value);
                }
            }

            // Delete all items in hands
            if (TryComp<HandsComponent>(uid, out var hands))
            {
                foreach (var hand in hands.Hands.Values)
                {
                    if (hand.HeldEntity != null)
                        QueueDel(hand.HeldEntity.Value);
                }
            }
        }
    }
}
