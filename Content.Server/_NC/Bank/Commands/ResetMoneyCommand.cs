using Content.Shared.Administration;
using Content.Server.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.GameObjects;
using Content.Server._NC.Bank;

namespace Content.Server._NC.Bank.Commands
{
    [AdminCommand(AdminFlags.Admin)]
    public sealed class ResetMoneyCommand : IConsoleCommand
    {
        public string Command => "resetmoney";
        public string Description => "Сбрасывает банковский баланс ВСЕХ игроков в базе данных и в текущем раунде до стартового значения.";
        public string Help => $"Использование: {Command}";

        public async void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var systems = IoCManager.Resolve<IEntitySystemManager>();
            var bankSystem = systems.GetEntitySystem<BankSystem>();
            
            shell.WriteLine("Начинаю глобальный сброс балансов...");
            await bankSystem.ResetAllBalances();
            shell.WriteLine("Глобальный сброс балансов завершен успешно.");
        }
    }
}
