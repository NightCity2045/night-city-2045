using Content.Client.Clothing;
using Content.Shared._NC.Clothing;
using Content.Shared.Clothing;

namespace Content.Client._NC.Clothing;

/// <summary>
/// Adds data-driven overlay layers without replacing the normal clothing layer selected by foldable prefixes.
/// </summary>
public sealed class NCClothingOverlaySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Run after the standard clothing visualizer so the overlay is drawn above the jacket body.
        SubscribeLocalEvent<NCClothingOverlayComponent, GetEquipmentVisualsEvent>(
            OnGetEquipmentVisuals,
            after: [typeof(ClientClothingSystem)]);
    }

    private static void OnGetEquipmentVisuals(
        Entity<NCClothingOverlayComponent> entity,
        ref GetEquipmentVisualsEvent args)
    {
        if (!entity.Comp.ClothingVisuals.TryGetValue(args.Slot, out var layers))
            return;

        for (var index = 0; index < layers.Count; index++)
        {
            // A unique key keeps the overlay separate from the normal outer-clothing layer.
            var key = $"{args.Slot}-nc-overlay-{index}";
            args.Layers.Add((key, layers[index]));
        }
    }
}
