using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.GameModes.Objectives;

/// <summary>
/// Система, которая выдает задачи игрокам при старте правила Night City.
/// </summary>
public sealed class NightCityObjectiveRuleSystem : GameRuleSystem<NightCityObjectiveRuleComponent>
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly ObjectivesSystem _objective = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JobRoleComponent, ComponentAdd>(OnJobRoleAdded);
    }

    protected override void Started(EntityUid uid, NightCityObjectiveRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        Log.Info("NightCityObjectiveRule started. Assigning objectives...");

        var query = EntityQueryEnumerator<MindComponent>();
        var count = 0;
        while (query.MoveNext(out var mindId, out var mind))
        {
            count++;
            AssignObjectives(mindId, mind, component);
        }
        Log.Info($"NightCityObjectiveRule: Objectives assigned to {count} minds.");
    }

    private void OnJobRoleAdded(EntityUid uid, JobRoleComponent component, ComponentAdd args)
    {
        if (!TryComp<MindRoleComponent>(uid, out var role))
            return;

        var mindId = role.Mind.Owner;
        if (!TryComp<MindComponent>(mindId, out var mind))
            return;

        Log.Info($"JobRole added for {mind.CharacterName}. Checking for active NC rules...");

        // Если правило активно, выдаем цели новому игроку
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var rule, out _))
        {
            AssignObjectives(mindId, mind, rule);
        }
    }

    private void AssignObjectives(EntityUid mindId, MindComponent mind, NightCityObjectiveRuleComponent component)
    {
        Log.Info($"Assigning objectives for mind: {mind.CharacterName} ({mindId})");

        // 1. Выдаем базовую задачу (Выжить)
        if (component.BaseObjective != null)
        {
            Log.Info($"Adding base objective: {component.BaseObjective}");
            AddObjectiveIfMissing(mindId, mind, component.BaseObjective.Value);
        }

        // 2. Ищем департамент игрока по его роли
        var department = GetDepartment(mind);
        if (department == null)
            return;

        // 3. Выдаем задачу департамента
        if (component.DepartmentObjectives.TryGetValue(department.Value, out var deptObjective))
        {
            AddObjectiveIfMissing(mindId, mind, deptObjective);
        }
    }

    private void AddObjectiveIfMissing(EntityUid mindId, MindComponent mind, EntProtoId objectiveProtoId)
    {
        foreach (var objId in mind.Objectives)
        {
            var protoId = MetaData(objId).EntityPrototype?.ID;
            if (protoId != null && protoId == objectiveProtoId)
                return;
        }

        _mind.TryAddObjective(mindId, mind, objectiveProtoId);
    }

    private ProtoId<DepartmentPrototype>? GetDepartment(MindComponent mind)
    {
        foreach (var roleId in mind.MindRoles)
        {
            if (!TryComp<JobRoleComponent>(roleId, out var jobRole) || jobRole.Prototype == null)
                continue;

            var jobId = jobRole.Prototype.Value;
            
            // Перебираем все департаменты, чтобы найти тот, к которому относится роль
            foreach (var dept in _proto.EnumeratePrototypes<DepartmentPrototype>())
            {
                if (dept.Roles.Contains(jobId))
                    return dept.ID;
            }
        }

        return null;
    }
}
