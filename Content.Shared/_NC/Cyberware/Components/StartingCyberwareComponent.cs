using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Shared._NC.Cyberware.Components;

/// <summary>
///     Позволяет сущности автоматически получить указанные киберимпланты при спавне на карте (MapInit).
///     Полезно для NPC (бандитов, боссов и т.д.), к которым нельзя применить JobSpecial.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StartingCyberwareComponent : Component
{
    [DataField("cyberware", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<EntityPrototype>))]
    public HashSet<string> Cyberware { get; private set; } = new();

    [DataField("loseHum")]
    public bool LoseHum { get; private set; } = true;
}
