using Content.Shared._NC.Cyberware.Components;

namespace Content.Server._NC.Cyberware.Systems;

public sealed class StartingCyberwareSystem : EntitySystem
{
    [Dependency] private readonly CyberwareSystem _cyberwareSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StartingCyberwareComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, StartingCyberwareComponent component, MapInitEvent args)
    {
        var coords = Transform(uid).Coordinates;

        foreach (var id in component.Cyberware)
        {
            var implant = Spawn(id, coords);

            if (!_cyberwareSystem.TryInstallImplant(uid, implant, deductHumanity: component.LoseHum))
            {
                QueueDel(implant);
            }
        }
    }
}
