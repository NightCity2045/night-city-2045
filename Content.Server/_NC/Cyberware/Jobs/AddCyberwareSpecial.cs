using Content.Server.Jobs;
using Content.Server._NC.Cyberware.Systems;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Server._NC.Cyberware.Jobs;

/// <summary>
///     Устанавливает киберимпланты сущности при спавне роли.
/// </summary>
[UsedImplicitly]
public sealed partial class AddCyberwareSpecial : JobSpecial
{
    [DataField("cyberware", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<EntityPrototype>))]
    public HashSet<string> Cyberware { get; private set; } = new();

    [DataField("loseHum")]
    public bool LoseHum { get; private set; } = true;

    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var cyberwareSystem = entMan.System<CyberwareSystem>();

        var coords = entMan.GetComponent<TransformComponent>(mob).Coordinates;

        foreach (var id in Cyberware)
        {
            var implant = entMan.SpawnEntity(id, coords);

            if (!cyberwareSystem.TryInstallImplant(mob, implant, deductHumanity: LoseHum))
            {
                // Если не удалось установить (например, нет подходящих слотов или превышен лимит), удаляем объект из мира
                entMan.QueueDeleteEntity(implant);
            }
        }
    }
}
