using Content.Server._NC.Bank;
using Content.Server._NC.CitiNet.Delivery;
using Content.Server.Chat.Managers;
using Content.Server.Station.Systems;
using Content.Shared._NC.Bank;
using Content.Shared._NC.Bank.Components;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Content.Shared._NC.CitiNet.Store;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._NC.CitiNet.Store;

public sealed class CitiNetStoreSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BankSystem _bankSystem = default!;
    [Dependency] private readonly DeliverySystem _deliverySystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;

    /// <summary>
    /// GLOBAL SCARCITY STORAGE
    /// Key: Product Prototype ID
    /// Value: Remaining city-wide stock
    /// </summary>
    private readonly Dictionary<string, int> _globalStock = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<NetBrowserComponent>(NetBrowserUiKey.Key, subs =>
        {
            subs.Event<CitiNetStoreBuyRequestMessage>(OnBuyRequest);
            subs.Event<CitiNetStoreRequestDataMessage>(OnRequestData);
        });
    }

    private void OnRequestData(EntityUid uid, NetBrowserComponent component, CitiNetStoreRequestDataMessage msg)
    {
        var user = msg.Actor;
        if (user == default) return;

        UpdateStoreState(uid, component, user);
    }

    private void OnBuyRequest(EntityUid uid, NetBrowserComponent component, CitiNetStoreBuyRequestMessage msg)
    {
        var user = msg.Actor;
        if (user == default) return;
        if (msg.Amount <= 0) return;

        var siteProto = GetSiteForUrl(component.CurrentUrl);
        if (siteProto?.StorePreset == null) return;

        if (!_prototypeManager.TryIndex<CitiNetStorePresetPrototype>(siteProto.StorePreset, out var preset))
            return;

        CitiNetStoreEntry? targetEntry = null;
        foreach (var catId in preset.Categories)
        {
            if (catId != msg.CategoryId) continue;
            if (!_prototypeManager.TryIndex<CitiNetStoreCategoryPrototype>(catId, out var category)) continue;

            targetEntry = category.Entries.FirstOrDefault(e => e.ProductId == msg.EntryProtoId);
            break;
        }

        if (targetEntry == null)
            return;

        // Check global stock.
        if (targetEntry.InitialCount.HasValue)
        {
            var currentStock = _globalStock.GetValueOrDefault(targetEntry.ProductId, targetEntry.InitialCount.Value);
            if (currentStock < msg.Amount)
            {
                SendStoreMessage(user, "Товар закончился на складах города!");
                return;
            }
        }

        ProcessTransaction(uid, user, targetEntry, msg.Amount, component, preset);
    }

    private async void ProcessTransaction(
        EntityUid uid,
        EntityUid user,
        CitiNetStoreEntry entry,
        int amount,
        NetBrowserComponent browser,
        CitiNetStorePresetPrototype preset)
    {
        var totalPrice = entry.Price * amount;
        var totalDataPrice = entry.DataPrice * amount;
        var usesCorporateAccount = preset.BankAccount != SectorBankAccount.Invalid;
        var station = GetStation(uid);
        var accountInfo = usesCorporateAccount && station != null
            ? GetCorporateAccountInfo(station.Value, preset.BankAccount)
            : null;

        if (usesCorporateAccount && accountInfo == null)
        {
            SendStoreMessage(user, "Корпоративный счет недоступен.");
            return;
        }

        if (usesCorporateAccount && accountInfo!.DataBalance < totalDataPrice)
        {
            SendStoreMessage(user, "Недостаточно корпоративных данных на счете фракции.");
            return;
        }

        var moneyWithdrawn = totalPrice <= 0 || (usesCorporateAccount
            ? _bankSystem.TryFactionWithdraw(station!.Value, preset.BankAccount, totalPrice)
            : await _bankSystem.TryBankWithdraw(user, totalPrice));

        if (!moneyWithdrawn)
        {
            SendStoreMessage(user, usesCorporateAccount
                ? "Недостаточно средств на корпоративном счете."
                : "Недостаточно средств на личном счете.");
            return;
        }

        if (usesCorporateAccount)
        {
            accountInfo!.DataBalance -= totalDataPrice;
            Dirty(station!.Value, Comp<StationBankComponent>(station.Value));
        }

        if (_deliverySystem.TryDeliverItem(user, entry.ProductId, amount, preset.DefaultDelivery, out var deliveryMsg))
        {
            if (entry.InitialCount.HasValue)
            {
                var currentStock = _globalStock.GetValueOrDefault(entry.ProductId, entry.InitialCount.Value);
                _globalStock[entry.ProductId] = currentStock - amount;
            }

            SendStoreMessage(user, deliveryMsg);
            UpdateAllBrowsers();
            return;
        }

        // Delivery failed: restore both currencies.
        if (totalPrice <= 0)
        {
            // No eddies were withdrawn for data-only purchases.
        }
        else if (usesCorporateAccount)
            _bankSystem.TryFactionDeposit(station!.Value, preset.BankAccount, totalPrice);
        else
            await _bankSystem.TryBankDeposit(user, totalPrice);

        if (usesCorporateAccount)
        {
            accountInfo!.DataBalance += totalDataPrice;
            Dirty(station!.Value, Comp<StationBankComponent>(station.Value));
        }

        SendStoreMessage(user, "Ошибка доставки: " + deliveryMsg + " Средства и данные возвращены на счет.");
        UpdateStoreState(uid, browser, user);
    }

    private void SendStoreMessage(EntityUid user, string message)
    {
        if (TryComp<ActorComponent>(user, out var actor))
            _chatManager.DispatchServerMessage(actor.PlayerSession, message);
    }

    private void UpdateAllBrowsers()
    {
        var query = EntityQueryEnumerator<NetBrowserComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            UpdateAllBrowsersFor(uid, component);
        }
    }

    private void UpdateAllBrowsersFor(EntityUid uid, NetBrowserComponent component)
    {
        foreach (var actor in _uiSystem.GetActors(uid, NetBrowserUiKey.Key))
        {
            UpdateStoreState(uid, component, actor);
        }
    }

    public void UpdateStoreState(EntityUid uid, NetBrowserComponent component, EntityUid user)
    {
        var siteProto = GetSiteForUrl(component.CurrentUrl);
        if (siteProto?.StorePreset == null) return;

        if (!_prototypeManager.TryIndex<CitiNetStorePresetPrototype>(siteProto.StorePreset, out var preset))
            return;

        var usesCorporateAccount = preset.BankAccount != SectorBankAccount.Invalid;
        var accountInfo = usesCorporateAccount && GetStation(uid) is { } station
            ? GetCorporateAccountInfo(station, preset.BankAccount)
            : null;
        var balance = usesCorporateAccount
            ? accountInfo?.Balance ?? 0
            : _bankSystem.GetBalance(user);
        var dataBalance = accountInfo?.DataBalance ?? 0;
        var categories = new List<CitiNetStoreCategoryData>();

        foreach (var catId in preset.Categories)
        {
            if (!_prototypeManager.TryIndex<CitiNetStoreCategoryPrototype>(catId, out var category))
                continue;

            var entries = new List<CitiNetStoreEntryData>();
            foreach (var entry in category.Entries)
            {
                if (!_prototypeManager.TryIndex<EntityPrototype>(entry.ProductId, out var proto))
                    continue;

                var stock = entry.InitialCount.HasValue
                    ? _globalStock.GetValueOrDefault(entry.ProductId, entry.InitialCount.Value)
                    : (int?) null;

                // Sync the value back to dictionary if it is missing on first access.
                if (entry.InitialCount.HasValue && !_globalStock.ContainsKey(entry.ProductId))
                    _globalStock[entry.ProductId] = entry.InitialCount.Value;

                entries.Add(new CitiNetStoreEntryData(
                    catId,
                    entry.ProductId,
                    entry.NameOverride ?? proto.Name,
                    entry.DescriptionOverride ?? proto.Description,
                    entry.Price,
                    entry.DataPrice,
                    stock
                ));
            }

            categories.Add(new CitiNetStoreCategoryData(category.Name, entries));
        }

        var state = new CitiNetStoreUpdateState(balance, dataBalance, usesCorporateAccount, categories);
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
    }

    private StationBankAccountInfo? GetCorporateAccountInfo(EntityUid station, SectorBankAccount account)
    {
        var stationBank = _bankSystem.EnsureStationBank(station);
        return stationBank.Accounts.TryGetValue(account, out var info) ? info : null;
    }

    private EntityUid? GetStation(EntityUid console)
    {
        var station = _stationSystem.GetOwningStation(console);
        if (station != null)
            return station;

        foreach (var stationUid in _stationSystem.GetStationsSet())
            return stationUid;

        var queryBank = EntityQueryEnumerator<StationBankComponent>();
        return queryBank.MoveNext(out var bankUid, out _) ? bankUid : null;
    }

    private NetSitePrototype? GetSiteForUrl(string url)
    {
        foreach (var site in _prototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == url) return site;
        }
        return null;
    }
}
