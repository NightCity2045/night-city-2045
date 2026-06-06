using Content.Server.Stack;
using Content.Server.Popups;
using Content.Shared._NC.Bank.Components;
using Content.Server._NC.Bank; // Ваша BankSystem
using Content.Shared._NC.Bank;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Server.Station.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Server.Player;
using System.Linq;
using System.Runtime.InteropServices;
using Robust.Shared.Localization;
using System.Collections.Generic;

namespace Content.Server._NC.Bank.ATM
{
    public sealed class AtmSystem : EntitySystem
    {
        [Dependency] private readonly StackSystem _stackSystem = default!;
        [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
        [Dependency] private readonly ActivatableUISystem _activatableUi = default!;

        // Работаем с вашей системой БД
        [Dependency] private readonly BankSystem _bankSystem = default!;

        private const string CurrencyPrototypeId = "SpaceCash";
        private const string CurrencyStackId = "Credit";
        
        private ISawmill _log = default!;
        
        // Маппинг Player -> AccountEntity (кто авторизован в банкомате)
        private readonly Dictionary<EntityUid, EntityUid> _atmSessions = new();
        
        // Маппинг ATM -> Player (кто сейчас занял терминал)
        private readonly Dictionary<EntityUid, EntityUid> _atmOccupiedBy = new();

        public override void Initialize()
        {
            base.Initialize();
            _log = Logger.GetSawmill("bank.atm");
            SubscribeLocalEvent<AtmComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<AtmComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
            SubscribeLocalEvent<AtmComponent, EntRemovedFromContainerMessage>(OnContainerModified);
            SubscribeLocalEvent<AtmComponent, AtmWithdrawMessage>(OnWithdraw);
            SubscribeLocalEvent<AtmComponent, AtmDepositMessage>(OnDeposit);
            SubscribeLocalEvent<AtmComponent, BoundUIOpenedEvent>(OnUiOpened);
            SubscribeLocalEvent<AtmComponent, BoundUIClosedEvent>(OnUiClosed);
            SubscribeLocalEvent<AtmComponent, AtmLoginMessage>(OnLogin);
            SubscribeLocalEvent<AtmComponent, AtmLogoutMessage>(OnLogout);
        }

        private void OnUiClosed(EntityUid uid, AtmComponent component, BoundUIClosedEvent args)
        {
            if (args.Actor is not { Valid: true } player) return;

            if (_atmOccupiedBy.TryGetValue(uid, out var occupant) && occupant == player)
            {
                _atmOccupiedBy.Remove(uid);
                _atmSessions.Remove(player);
                _log.Info($"ATM {uid} released by {player}");
            }
        }
        
        private void OnLogin(EntityUid uid, AtmComponent component, AtmLoginMessage args)
        {
            if (args.Actor is not { Valid: true } player) return;
            
            if (_atmOccupiedBy.TryGetValue(uid, out var occupant) && occupant != player)
            {
                _popupSystem.PopupEntity("Банкомат уже занят другим пользователем", uid, player);
                _uiSystem.CloseUi(uid, AtmUiKey.Key, player);
                return;
            }

            var query = EntityQueryEnumerator<BankAccountComponent>();
            while (query.MoveNext(out var accUid, out var accnt))
            {
                if (accnt.AccountNumber == args.AccountNumber && accnt.PIN == args.PIN)
                {
                    _atmSessions[player] = accUid;
                    _atmOccupiedBy[uid] = player; // Занимаем банкомат
                    UpdateUi(uid, component);
                    return;
                }
            }
            
            _popupSystem.PopupEntity("Неверный номер счета или ПИН-код", uid, player);
        }
        
        private void OnLogout(EntityUid uid, AtmComponent component, AtmLogoutMessage args)
        {
            if (args.Actor is not { Valid: true } player) return;

            if (_atmOccupiedBy.TryGetValue(uid, out var occupant) && occupant == player)
            {
                _atmSessions.Remove(player);
                _atmOccupiedBy.Remove(uid);
                UpdateUi(uid, component);
            }
        }

        // === ВСТАВКА ДЕНЕГ РУКАМИ ===
        private void OnInteractUsing(EntityUid uid, AtmComponent component, InteractUsingEvent args)
        {
            if (_atmOccupiedBy.TryGetValue(uid, out var occupant) && occupant != args.User)
            {
                _popupSystem.PopupEntity("Банкомат занят", uid, args.User);
                return;
            }

            if (!TryComp<StackComponent>(args.Used, out var stack) ||
                stack.StackTypeId != CurrencyStackId) return;

            if (_containerSystem.TryGetContainer(uid, AtmComponent.CashSlotId, out var cashContainer))
            {
                if (_containerSystem.Insert(args.Used, cashContainer))
                {
                    args.Handled = true;
                    UpdateUi(uid, component);
                }
            }
        }

        private void OnContainerModified(EntityUid uid, AtmComponent component, ContainerModifiedMessage args) => UpdateUi(uid, component);
        
        private void OnUiOpened(EntityUid uid, AtmComponent component, BoundUIOpenedEvent args)
        {
            if (args.Actor is not { Valid: true } player) return;

            // Если банкомат занят кем-то другим — закрываем UI для наглеца
            if (_atmOccupiedBy.TryGetValue(uid, out var occupant) && occupant != player)
            {
                _popupSystem.PopupEntity("Банкомат уже занят другим пользователем", uid, player);
                _uiSystem.CloseUi(uid, AtmUiKey.Key, player);
                return;
            }

            _atmOccupiedBy[uid] = player; // Занимаем банкомат при открытии UI
            UpdateUi(uid, component);
        }

        // === СНЯТИЕ (ИЗ БД ИГРОКА) ===
        private async void OnWithdraw(EntityUid uid, AtmComponent component, AtmWithdrawMessage args)
        {
            if (args.Actor is not { Valid: true } player) return;
            if (args.Amount <= 0) return;

            if (!IsLoggedIn(player, out var accountUid))
            {
                _popupSystem.PopupEntity(Loc.GetString("atm-popup-insert-card-auth"), uid, player);
                return;
            }

            if (await _bankSystem.TryBankWithdraw(accountUid, args.Amount))
            {
                try
                {
                    var coords = Transform(uid).Coordinates;
                    _stackSystem.SpawnMultiple(CurrencyPrototypeId, args.Amount, coords);
                }
                catch (Exception e)
                {
                    _log.Error($"[ATM] КРИТИЧЕСКАЯ ОШИБКА СПАВНА ДЕНЕГ: {e}");
                    _popupSystem.PopupEntity("Ошибка выдачи наличности (битый прототип!)", uid, player);
                }

                _popupSystem.PopupEntity(Loc.GetString("atm-popup-withdraw-success", ("amount", args.Amount)), uid, player);
            }
            else
            {
                _popupSystem.PopupEntity(Loc.GetString("atm-popup-insufficient-funds"), uid, player);
            }

            // ОБНОВЛЯЕМ UI В ЛЮБОМ СЛУЧАЕ
            UpdateUi(uid, component);
        }

        // === ВНЕСЕНИЕ (В БД ИГРОКА) ===
        private async void OnDeposit(EntityUid uid, AtmComponent component, AtmDepositMessage args)
        {
            if (args.Actor is not { Valid: true } player) return;

            if (!IsLoggedIn(player, out var accountUid))
            {
                _popupSystem.PopupEntity(Loc.GetString("atm-popup-insert-card"), uid, player);
                return;
            }

            if (!_containerSystem.TryGetContainer(uid, AtmComponent.CashSlotId, out var cashContainer) ||
                cashContainer.ContainedEntities.Count == 0) return;

            var item = cashContainer.ContainedEntities[0];
            if (!TryComp<StackComponent>(item, out var stack)) return;

            int totalAmount = _stackSystem.GetCount(item, stack);
            int tax = (int) (totalAmount * component.TaxRate);
            int finalDeposit = totalAmount - tax;

            if (finalDeposit <= 0)
            {
                _popupSystem.PopupEntity(Loc.GetString("atm-popup-amount-too-small"), uid, player);
                return;
            }

            if (await _bankSystem.TryBankDeposit(accountUid, finalDeposit))
            {
                PayTaxToStation(uid, tax);

                _popupSystem.PopupEntity(Loc.GetString("atm-popup-deposit-success", ("amount", finalDeposit), ("tax", tax)), uid, player);
                QueueDel(item);
                UpdateUi(uid, component);
            }
        }

        private void PayTaxToStation(EntityUid atmUid, int taxAmount)
        {
            if (taxAmount <= 0) return;
            var stationUid = _stationSystem.GetOwningStation(atmUid);

            if (stationUid != null && TryComp<StationBankComponent>(stationUid, out var stationBank))
            {
                ref var cityAccount = ref CollectionsMarshal.GetValueRefOrNullRef(stationBank.Accounts, SectorBankAccount.CityAdmin);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref cityAccount))
                {
                    cityAccount.Balance += taxAmount;
                    Dirty(stationUid.Value, stationBank);
                }
            }
        }

        private void UpdateUi(EntityUid uid, AtmComponent component)
        {
            // NC Edit Start: Close UI if power is lost during interaction
            if (!_powerReceiver.IsPowered(uid))
            {
                _activatableUi.CloseAll(uid);
                return;
            }
            // NC Edit End

            string accountName = "Не авторизован";
            int balance = 0;
            bool isLoggedIn = false;
            int depositAmount = 0;
            string ownAccountNumber = string.Empty;

            // Берем того, кто сейчас занял банкомат
            if (_atmOccupiedBy.TryGetValue(uid, out var user))
            {
                // Для автозаполнения: находим номер счета самого пользователя
                if (TryComp<BankAccountComponent>(user, out var ownBankAcc))
                {
                    ownAccountNumber = ownBankAcc.AccountNumber;
                }

                // Информация об авторизации
                if (IsLoggedIn(user, out var accountUid) && TryComp<BankAccountComponent>(accountUid, out var bankAcc))
                {
                    isLoggedIn = true;
                    accountName = bankAcc.AccountNumber;
                    balance = bankAcc.Balance;
                }
            }

            if (_containerSystem.TryGetContainer(uid, AtmComponent.CashSlotId, out var cashContainer) &&
                cashContainer.ContainedEntities.Count > 0)
            {
                var item = cashContainer.ContainedEntities[0];
                if (TryComp<StackComponent>(item, out var stack) &&
                    stack.StackTypeId == CurrencyStackId)
                {
                    depositAmount = _stackSystem.GetCount(item, stack);
                }
            }

            var state = new AtmBoundUserInterfaceState(
                balance,
                accountName,
                isLoggedIn,
                component.TaxRate,
                depositAmount,
                ownAccountNumber
            );

            _uiSystem.SetUiState(uid, AtmUiKey.Key, state);
        }

        private bool IsLoggedIn(EntityUid user, out EntityUid accountUid)
        {
            return _atmSessions.TryGetValue(user, out accountUid);
        }
    }
}
