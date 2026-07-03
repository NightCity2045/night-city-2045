using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Bank.Components
{
    [RegisterComponent, NetworkedComponent]
    [AutoGenerateComponentState]
    public sealed partial class BankAccountComponent : Component
    {
        /// <summary>
        /// Баланс, который выдается новому персонажу.
        /// Измените это число здесь, чтобы поменять его во всем проекте.
        /// </summary>
        public const int StartingBalance = 500;

        /// <summary>
        /// Текущий баланс.
        /// </summary>
        [DataField, AutoNetworkedField]
        public int Balance = 0;

        [DataField, AutoNetworkedField]
        public string AccountNumber = string.Empty;

        [DataField, AutoNetworkedField]
        public string PIN = string.Empty;

        /// <summary>
        /// Индекс слота персонажа в базе данных.
        /// Нужно, чтобы сохранять баланс именно тому персонажу, за которого мы играем.
        /// </summary>
        [DataField]
        public int ProfileSlot = -1;
    }
}
