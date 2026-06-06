using System.Threading.Tasks;
using Content.Shared._NC.Bank;
using Content.Shared._NC.Bank.Components;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using Content.Server.Database;
using Robust.Shared.Player;
using Content.Shared.GameTicking;
using Content.Server.Chat.Managers;
using Robust.Shared.Enums;
using Content.Shared.Roles.Jobs;
using Content.Shared.Mind;
using Content.Shared.Ghost;
using Content.Server.Popups;

namespace Content.Server._NC.Bank
{
    /// <summary>
    /// Основная система экономики. Отвечает за БД, транзакции и автоматическую зарплату.
    /// </summary>
    public sealed class BankSystem : EntitySystem
    {
        // === ЗАВИСИМОСТИ ===
        [Dependency] private readonly IServerPreferencesManager _prefsManager = default!;
        [Dependency] private readonly IServerDbManager _db = default!;
        [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly SharedJobSystem _jobSystem = default!;
        [Dependency] private readonly SharedMindSystem _mindSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly Robust.Shared.Random.IRobustRandom _random = default!;

        private ISawmill _log = default!;

        private Dictionary<int, int> _factionBalances = new();

        // === НАСТРОЙКИ ТАЙМЕРА ===
        private const float PaydayInterval = 1800.0f;
        private float _paydayTimer = 0.0f;


        public override void Initialize()
        {
            base.Initialize();
            _log = Logger.GetSawmill("bank");

            SubscribeLocalEvent<StationBankComponent, MapInitEvent>(OnStationBankInit);
            SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
            SubscribeLocalEvent<BankAccountComponent, Content.Shared.Verbs.GetVerbsEvent<Content.Shared.Verbs.ActivationVerb>>(OnGetVerbs);

            LoadFactionBalances();
        }

        private async void LoadFactionBalances()
        {
            _factionBalances = await _db.GetFactionBankBalancesAsync();

            var query = EntityQueryEnumerator<StationBankComponent>();
            while (query.MoveNext(out var uid, out var bank))
            {
                EnsureDefaultAccounts(bank);
                Dirty(uid, bank);
            }
        }

        private void OnGetVerbs(EntityUid uid, BankAccountComponent component, Content.Shared.Verbs.GetVerbsEvent<Content.Shared.Verbs.ActivationVerb> args)
        {
            if (args.User != uid) return;

            args.Verbs.Add(new Content.Shared.Verbs.ActivationVerb
            {
                Text = "Реквизиты счета",
                Act = () => _popupSystem.PopupEntity($"Счет: {component.AccountNumber} | ПИН: {component.PIN}", uid, uid)
            });
        }

        private void OnStationBankInit(EntityUid uid, StationBankComponent component, MapInitEvent args)
        {
            EnsureDefaultAccounts(component);
        }

        private void OnPlayerSpawn(PlayerSpawnCompleteEvent ev)
        {
            if (ev.Mob == EntityUid.Invalid)
                return;

            var bankComp = EnsureComp<BankAccountComponent>(ev.Mob);

            // Берем баланс напрямую из профиля, загруженного при спавне
            bankComp.Balance = ev.Profile.BankBalance;

            // Устанавливаем индекс слота персонажа для корректного сохранения в БД
            bankComp.ProfileSlot = _prefsManager.GetPreferences(ev.Player.UserId).SelectedCharacterIndex;

            if (string.IsNullOrEmpty(bankComp.AccountNumber))
            {
                bankComp.AccountNumber = $"NC-{_random.Next(100000, 999999)}";
                bankComp.PIN = _random.Next(1000, 9999).ToString();
            }

            Dirty(ev.Mob, bankComp);

            _chatManager.DispatchServerMessage(ev.Player, $"Ваш банковский счет: {bankComp.AccountNumber}, ПИН-код: {bankComp.PIN}. Никому не сообщайте эти данные.");
        }

        public StationBankComponent EnsureStationBank(EntityUid stationUid)
        {
            var bank = EnsureComp<StationBankComponent>(stationUid);
            EnsureDefaultAccounts(bank);
            return bank;
        }

        private void EnsureDefaultAccounts(StationBankComponent component)
        {
            EnsureAccount(component, SectorBankAccount.CityAdmin, 0);
            EnsureAccount(component, SectorBankAccount.TraumaTeam, 10000);
            EnsureAccount(component, SectorBankAccount.Militech, 25000);
            EnsureAccount(component, SectorBankAccount.Biotechnica, 15000);
            EnsureAccount(component, SectorBankAccount.Ncpd, 5000);
        }

        private void EnsureAccount(StationBankComponent component, SectorBankAccount account, int defaultBalance)
        {
            var balance = defaultBalance;
            if (_factionBalances.TryGetValue((int)account, out var storedBalance))
                balance = storedBalance;

            if (!component.Accounts.TryGetValue(account, out var info))
            {
                component.Accounts[account] = new StationBankAccountInfo
                {
                    Balance = balance,
                };
            }
            else
            {
                info.Balance = balance;
            }
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            _paydayTimer += frameTime;
            if (_paydayTimer >= PaydayInterval)
            {
                _paydayTimer -= PaydayInterval;
                ProcessPayday();
            }
        }

        public bool TryFactionWithdraw(EntityUid stationUid, SectorBankAccount accountType, int amount)
        {
            if (amount <= 0) return false;

            var bank = EnsureStationBank(stationUid);
            if (!bank.Accounts.TryGetValue(accountType, out var account)) return false;

            if (account.Balance < amount) return false;

            account.Balance -= amount;
            _factionBalances[(int)accountType] = account.Balance;
            _db.SaveFactionBankBalanceAsync((int)accountType, account.Balance);
            Dirty(stationUid, bank);
            return true;
        }

        public bool TryFactionDeposit(EntityUid stationUid, SectorBankAccount accountType, int amount)
        {
            if (amount <= 0) return false;

            var bank = EnsureStationBank(stationUid);

            if (!bank.Accounts.TryGetValue(accountType, out var account)) return false;

            account.Balance += amount;
            _factionBalances[(int)accountType] = account.Balance;
            _db.SaveFactionBankBalanceAsync((int)accountType, account.Balance);
            Dirty(stationUid, bank);
            return true;
        }

        private async void ProcessPayday()
        {
            _log.Info("PAYDAY: Начало начисления зарплат...");
            int count = 0;

            foreach (var session in _playerManager.Sessions)
            {
                if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { Valid: true } playerUid)
                    continue;

                if (HasComp<GhostComponent>(playerUid))
                    continue;

                int salary = GetSalaryForPlayer(playerUid);

                if (await TryBankDeposit(playerUid, salary))
                {
                    count++;

                    _popupSystem.PopupEntity(Loc.GetString("bank-payday-message", ("amount", salary)), playerUid, playerUid);
                }
            }

            _log.Info($"PAYDAY: Зарплата выдана {count} игрокам.");
        }

        private int GetSalaryForPlayer(EntityUid uid)
        {
            if (!_mindSystem.TryGetMind(uid, out var mindId, out _))
                return 50;

            if (_jobSystem.MindTryGetJob(mindId, out var jobProto))
            {
                return jobProto.Salary;
            }

            return 50;
        }

        // ==========================================
        //      РАБОТА С БАЗОЙ ДАННЫХ
        // ==========================================

        public int GetBalance(EntityUid mobUid)
        {
            // ПРИОРИТЕТ 1: Если игрок в раунде, берем данные из его компонента
            if (TryComp<BankAccountComponent>(mobUid, out var bankComp))
            {
                return bankComp.Balance;
            }

            // ПРИОРИТЕТ 2: Если компонента нет, лезем в профиль (БД)
            if (!_playerManager.TryGetSessionByEntity(mobUid, out var session)) return 0;
            var prefs = _prefsManager.GetPreferences(session.UserId);
            if (prefs.SelectedCharacter is not HumanoidCharacterProfile profile) return 0;

            return profile.BankBalance;
        }

        public async Task<bool> TryBankWithdraw(EntityUid mobUid, int amount)
        {
            if (amount <= 0) return false;
            return await ModifyBalance(mobUid, -amount);
        }

        public async Task<bool> TryBankDeposit(EntityUid mobUid, int amount)
        {
            if (amount <= 0) return false;
            return await ModifyBalance(mobUid, amount);
        }

        private async Task<bool> ModifyBalance(EntityUid mobUid, int delta)
        {
            if (!TryComp<BankAccountComponent>(mobUid, out var bankComp))
            {
                _log.Error($"[BANK] Ошибка: Компонент BankAccountComponent не найден на {mobUid}");
                return false;
            }

            // 1. Проверка на баланс
            if (delta < 0 && bankComp.Balance < -delta)
            {
                return false;
            }

            // 2. Обновляем значение в компоненте МГНОВЕННО
            bankComp.Balance += delta;
            Dirty(mobUid, bankComp);

            // 3. Сохранение в БД
            // Ищем сессию игрока. Сначала по сущности, потом по номеру счета во всем мире.
            if (!_playerManager.TryGetSessionByEntity(mobUid, out var session))
            {
                // Если сущность не привязана к сессии (например, это карта или банкомат),
                // ищем живого игрока с таким же номером счета.
                foreach (var s in _playerManager.Sessions)
                {
                    if (s.AttachedEntity is { Valid: true } attached &&
                        TryComp<BankAccountComponent>(attached, out var otherBank) &&
                        otherBank.AccountNumber == bankComp.AccountNumber)
                    {
                        session = s;
                        bankComp.ProfileSlot = otherBank.ProfileSlot;
                        break;
                    }
                }
            }

            if (session != null && bankComp.ProfileSlot != -1)
            {
                var prefs = _prefsManager.GetPreferences(session.UserId);
                if (prefs.Characters.TryGetValue(bankComp.ProfileSlot, out var iProfile) &&
                    iProfile is HumanoidCharacterProfile profile)
                {
                    var newProfile = profile.WithBankBalance(bankComp.Balance);
                    await _prefsManager.SetProfile(session.UserId, bankComp.ProfileSlot, newProfile);
                }
            }

            return true;
        }

        /// <summary>
        /// Сбрасывает баланс ВСЕХ игроков во всей базе данных и в текущем раунде.
        /// </summary>
        public async Task ResetAllBalances()
        {
            _log.Warning("RESET: Глобальный сброс всех банковских счетов!");

            // 1. Сброс в БД (Bulk update)
            await _db.ResetAllBankBalances(BankAccountComponent.StartingBalance);

            // 2. Сброс кэша преференций (для всех загруженных игроков)
            _prefsManager.ResetAllBalances();

            // 3. Обновление всех активных компонентов в раунде
            var query = EntityQueryEnumerator<BankAccountComponent>();
            while (query.MoveNext(out var uid, out var bank))
            {
                bank.Balance = BankAccountComponent.StartingBalance;
                Dirty(uid, bank);
            }

            _log.Info("RESET: Сброс завершен.");
        }
    }
}
