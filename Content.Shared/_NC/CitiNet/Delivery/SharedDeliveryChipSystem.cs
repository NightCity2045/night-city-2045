using Content.Shared.Examine;
using Robust.Shared.Timing;

namespace Content.Shared._NC.CitiNet.Delivery;

public sealed class SharedDeliveryChipSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeliveryChipComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, DeliveryChipComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        // The chip is the portable contract receipt, so it mirrors the same timer shown on CitiNet maps.
        if (component.IsReady)
        {
            args.PushMarkup(Loc.GetString("citinet-delivery-chip-examine-ready", ("location", component.LocationName)));
            return;
        }

        args.PushMarkup(Loc.GetString(
            "citinet-delivery-chip-examine-pending",
            ("location", component.LocationName),
            ("seconds", GetRemainingSeconds(component))));
    }

    private int GetRemainingSeconds(DeliveryChipComponent component)
    {
        if (component.ReadyAt == null)
            return 0;

        return Math.Max(0, (int) Math.Ceiling((component.ReadyAt.Value - _timing.CurTime).TotalSeconds));
    }
}
