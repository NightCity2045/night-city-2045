using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Cyberware.Components;

/// <summary>
///     Компонент-маркер для отслеживания состояния киберпсихоза и таймеров эффектов.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberpsychosisComponent : Component
{
    /// <summary>
    ///     Таймер до следующего случайного сбоя (для Стадии 2).
    /// </summary>
    [DataField("glitchTimer"), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public float GlitchTimer = 0f;

    /// <summary>
    ///     Находится ли сущность под управлением NPC.
    /// </summary>
    [DataField("hasNpc"), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public bool HasNpc;

    /// <summary>
    ///     Разум, который был вытеснен при захвате контроля.
    /// </summary>
    [DataField("stolenMind"), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public EntityUid? StolenMind;

    /// <summary>
    ///     Старые фракции сущности до того, как она стала киберпсихом.
    /// </summary>
    [DataField("oldFactions")]
    [AutoNetworkedField]
    public HashSet<ProtoId<NpcFactionPrototype>> OldFactions = new();
}