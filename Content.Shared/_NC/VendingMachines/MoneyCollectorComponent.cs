using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Content.Shared._NC.Bank;

namespace Content.Shared._NC.VendingMachines
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class MoneyCollectorComponent : Component
    {
        /// <summary>
        /// The corporate bank account to deposit money to.
        /// If not specified and collects money is true, wait for player with required access to deposit it.
        /// </summary>
        [DataField("bankAccount")]
        public SectorBankAccount BankAccount = SectorBankAccount.Invalid;

        /// <summary>
        /// Percentage of money to collect instead of depositing to bank.
        /// </summary>
        [DataField("collectPercentage")]
        public float CollectPercentage = 0.0f;

        /// <summary>
        /// The total amount of money collected in this vending machine.
        /// </summary>
        [DataField, AutoNetworkedField]
        public FixedPoint2 CollectedMoney = 0;

        /// <summary>
        /// The sound specifier to play when money is extracted.
        /// </summary>
        [DataField("extractSound")]
        public SoundSpecifier ExtractSound = new SoundPathSpecifier("/Audio/Machines/machine_vend.ogg");

        /// <summary>
        /// The access combinations required to extract money from this vending machine.
        /// </summary>
        [DataField]
        public List<List<string>> Access = new() { new() { "Biotech_HeadOfDepartment" } };
    }
}
