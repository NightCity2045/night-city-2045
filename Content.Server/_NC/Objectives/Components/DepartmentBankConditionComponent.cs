using Content.Shared._NC.Bank;
using Robust.Shared.Serialization;

namespace Content.Server._NC.Objectives.Components;

/// <summary>
/// Компонент для условия цели "Заработать денег для департамента".
/// </summary>
[RegisterComponent]
public sealed partial class DepartmentBankConditionComponent : Component
{
    [DataField("bankAccount", required: true)]
    public SectorBankAccount BankAccount = SectorBankAccount.Invalid;

    [DataField("targetBalance", required: true)]
    public int TargetBalance;
}
