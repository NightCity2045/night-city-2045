using Content.Server._NC.Objectives.Components;
using Content.Shared._NC.Bank.Components;
using Content.Shared.Objectives.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Objectives.Systems;

/// <summary>
/// Система, которая отслеживает прогресс накопления денег департаментом.
/// </summary>
public sealed class DepartmentBankConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DepartmentBankConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnGetProgress(EntityUid uid, DepartmentBankConditionComponent comp, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = GetProgress(comp);
    }

    private float GetProgress(DepartmentBankConditionComponent comp)
    {
        // Ищем баланс на всех станциях (обычно станция одна)
        var query = EntityQueryEnumerator<StationBankComponent>();
        while (query.MoveNext(out _, out var bank))
        {
            if (bank.Accounts.TryGetValue(comp.BankAccount, out var info))
            {
                if (comp.TargetBalance <= 0)
                    return 1f;

                var progress = (float) info.Balance / comp.TargetBalance;
                return Math.Clamp(progress, 0f, 1f);
            }
        }

        return 0f;
    }
}
