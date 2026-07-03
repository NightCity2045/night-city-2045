using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.GameModes.Objectives;

/// <summary>
/// Компонент для правила игры, которое выдает личные задачи игрокам.
/// </summary>
[RegisterComponent]
public sealed partial class NightCityObjectiveRuleComponent : Component
{
    /// <summary>
    /// Общая задача для всех (Выжить).
    /// </summary>
    [DataField("baseObjective")]
    public EntProtoId? BaseObjective = "SurvivalObjective";

    /// <summary>
    /// Задачи для конкретных департаментов.
    /// </summary>
    [DataField("departmentObjectives")]
    public Dictionary<ProtoId<DepartmentPrototype>, EntProtoId> DepartmentObjectives = new();
}
