using Content.Shared._NC.Netrunning.Prototypes;
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

    public int ProcessQuantum => Math.Max(1, _settings.ProcessOperationsPerTick);

    public override void Initialize()
    {
        base.Initialize();
        _settings = _prototypes.Index<MetaRuntimeSettingsPrototype>(MetaRuntimeSettingsPrototype.DefaultId);
    }

    public bool TryConsume()
    {
        if (_budgetTick != _timing.CurTick)
        {
            _budgetTick = _timing.CurTick;
            _remainingOperations = Math.Max(1, _settings.GlobalOperationsPerTick);
        }

        if (_remainingOperations <= 0)
            return false;

        _remainingOperations--;
        return true;
    }
}
