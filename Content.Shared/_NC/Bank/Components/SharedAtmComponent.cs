using Content.Shared.Containers.ItemSlots;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Bank.Components
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class AtmComponent : Component
    {
        [DataField("taxRate")]
        public float TaxRate = 0.1f;

        // === 2. Слот для Денег ===
        public const string CashSlotId = "atm_cash_slot";

        [DataField("cashSlot")]
        public ItemSlot CashSlot = new()
        {
            Name = "Приемник купюр",
            Whitelist = new EntityWhitelist
            {
                Components = new[] { "Stack" },
                Tags = new List<ProtoId<TagPrototype>> { "SpaceCash" }
            }
        };
    }

    [Serializable, NetSerializable]
    public enum AtmUiKey : byte { Key }

    [Serializable, NetSerializable]
    public sealed class AtmBoundUserInterfaceState : BoundUserInterfaceState
    {
        public readonly int BankBalance;
        public readonly string AccountName;
        public readonly bool IsLoggedIn;
        public readonly float TaxRate;
        public readonly int DepositAmount;
        public readonly string OwnAccountNumber;

        public AtmBoundUserInterfaceState(int bankBalance, string accountName, bool isLoggedIn, float taxRate, int depositAmount, string ownAccountNumber)
        {
            BankBalance = bankBalance;
            AccountName = accountName;
            IsLoggedIn = isLoggedIn;
            TaxRate = taxRate;
            DepositAmount = depositAmount;
            OwnAccountNumber = ownAccountNumber;
        }
    }

    [Serializable, NetSerializable]
    public sealed class AtmLoginMessage : BoundUserInterfaceMessage
    {
        public readonly string AccountNumber;
        public readonly string PIN;
        public AtmLoginMessage(string accNum, string pin)
        {
            AccountNumber = accNum;
            PIN = pin;
        }
    }

    [Serializable, NetSerializable]
    public sealed class AtmLogoutMessage : BoundUserInterfaceMessage { }

    [Serializable, NetSerializable]
    public sealed class AtmWithdrawMessage : BoundUserInterfaceMessage
    {
        public readonly int Amount;
        public AtmWithdrawMessage(int amount) { Amount = amount; }
    }

    [Serializable, NetSerializable]
    public sealed class AtmDepositMessage : BoundUserInterfaceMessage { }
}
