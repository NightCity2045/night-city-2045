using Content.Shared._NC.Netrunning.Prototypes;
using Content.Shared._NC.Netrunning.Components;
using Content.Shared._NC.Netrunning.Meta;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._NC.Netrunning.Systems;

/// <summary>
/// Enforces a global per-tick META budget and a fair quantum for each VM process.
/// </summary>
public sealed class MetaExecutionBudgetSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private MetaRuntimeSettingsPrototype _settings = default!;
    private GameTick _budgetTick;
    private int _remainingOperations;
    private float _runtimeRecoveryTimer;

    public int ProcessQuantum => Math.Max(1, _settings.ProcessOperationsPerTick);

    public override void Initialize()
    {
        base.Initialize();
        _settings = _prototypes.Index<MetaRuntimeSettingsPrototype>(MetaRuntimeSettingsPrototype.DefaultId);
    }

    public int ClampProgramHealth(int health)
    {
        var minimum = Math.Max(1, _settings.MinimumProgramHealth);
        var maximum = Math.Max(minimum, _settings.MaximumProgramHealth);
        return Math.Clamp(health, minimum, maximum);
    }

    public int ClampIceDamage(int damage)
    {
        return Math.Clamp(damage, 0, Math.Max(0, _settings.MaximumIceDamage));
    }

    public int ClampNeuralDamage(int damage)
    {
        return Math.Clamp(damage, 0, Math.Max(0, _settings.MaximumNeuralDamage));
    }

    public int GetProgramHealthLoad(int health)
    {
        return ScaleCost(ClampProgramHealth(health), _settings.ProgramHealthPerServerLoad);
    }

    public int GetProgramHealthGas(int health)
    {
        return ScaleCost(ClampProgramHealth(health), _settings.ProgramHealthPerGas);
    }

    public int GetIceDamageGas(int damage)
    {
        return ScaleCost(ClampIceDamage(damage), _settings.IceDamagePerGas);
    }

    public int GetNeuralDamageGas(int damage)
    {
        return ScaleCost(ClampNeuralDamage(damage), _settings.NeuralDamagePerGas);
    }

    public bool TryConsume(int operations = 1)
    {
        if (_budgetTick != _timing.CurTick)
        {
            _budgetTick = _timing.CurTick;
            _remainingOperations = Math.Max(1, _settings.GlobalOperationsPerTick);
        }

        var cost = Math.Max(1, operations);
        if (_remainingOperations < cost)
        {
            _remainingOperations = 0;
            return false;
        }

        _remainingOperations -= cost;
        return true;
    }

    public float ApplyServerRuntimeLoad(
        EntityUid serverUid,
        NetServerComponent server,
        MetaExecutionResult result)
    {
        var pressure = GetYieldPressure(result.YieldMilliseconds);
        var generated =
            (result.OperationsThisSlice * Math.Max(0f, _settings.ServerRuntimeLoadPerOperation) +
             result.SystemCallsThisSlice * Math.Max(0f, _settings.ServerRuntimeLoadPerSystemCall)) * pressure;
        server.RuntimeLoad = Math.Max(0f, server.RuntimeLoad + generated);
        Dirty(serverUid, server);

        var availableRuntime = Math.Max(1f, server.MaxLoad - server.UsedLoad);
        return server.RuntimeLoad >= availableRuntime
            ? Math.Max(1f, _settings.ServerRuntimeOverloadYieldMultiplier)
            : 1f;
    }

    private float GetYieldPressure(int yieldMilliseconds)
    {
        if (yieldMilliseconds <= 0)
            return 1f;

        var minimum = Math.Max(1, _settings.YieldPressureMinimumMilliseconds);
        var pressure = _settings.YieldPressureBaselineMilliseconds /
                       (float) Math.Max(minimum, yieldMilliseconds);
        return Math.Clamp(pressure, 1f, Math.Max(1f, _settings.YieldPressureMaximumMultiplier));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _runtimeRecoveryTimer += frameTime;
        if (_runtimeRecoveryTimer < 1f)
            return;

        var elapsed = _runtimeRecoveryTimer;
        _runtimeRecoveryTimer = 0f;
        var query = EntityQueryEnumerator<NetServerComponent>();
        while (query.MoveNext(out var uid, out var server))
        {
            if (server.RuntimeLoad <= 0f)
                continue;

            server.RuntimeLoad = Math.Max(
                0f,
                server.RuntimeLoad - Math.Max(0f, _settings.ServerRuntimeLoadRecoveryPerSecond) * elapsed);
            Dirty(uid, server);
        }
    }

    private static int ScaleCost(int value, int unitsPerCost)
    {
        if (value <= 0)
            return 0;

        return (int) Math.Ceiling(value / (double) Math.Max(1, unitsPerCost));
    }
}
