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
    /// City-wide scarcity storage. Key: product prototype ID. Value: remaining stock.
    /// </summary>
    private readonly Dictionary<string, int> _globalStock = new();

    /// <summary>
    /// Carts are scoped by user and store preset so Astrozon, Night Market and corporate stores never mix orders.
    /// </summary>
    private readonly Dictionary<CartKey, Dictionary<CartLineKey, int>> _carts = new();

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<NetBrowserComponent>(NetBrowserUiKey.Key, subs =>
        {
            subs.Event<CitiNetStoreBuyRequestMessage>(OnAddToCartRequest);
            subs.Event<CitiNetStoreRemoveFromCartMessage>(OnRemoveFromCartRequest);
            subs.Event<CitiNetStoreClearCartMessage>(OnClearCartRequest);
            subs.Event<CitiNetStoreCheckoutCartMessage>(OnCheckoutCartRequest);
            subs.Event<CitiNetStoreRequestDataMessage>(OnRequestData);
        });
    }

    private void OnRequestData(EntityUid uid, NetBrowserComponent component, CitiNetStoreRequestDataMessage msg)
    {
        var user = msg.Actor;
        if (user == default)
            return;

        UpdateStoreState(uid, component, user);
    }

    private void OnAddToCartRequest(EntityUid uid, NetBrowserComponent component, CitiNetStoreBuyRequestMessage msg)
    {
        var user = msg.Actor;
        if (user == default || msg.Amount <= 0)
            return;

        if (!TryGetCurrentPreset(component, out var preset))
            return;

        if (!TryFindEntry(preset, msg.CategoryId, msg.EntryProtoId, out var entry))
            return;

        var cart = GetCart(user, preset.ID);
        var lineKey = new CartLineKey(msg.CategoryId, msg.EntryProtoId);
        var currentAmount = cart.GetValueOrDefault(lineKey);
        var requestedAmount = currentAmount + msg.Amount;

        if (!HasStock(entry, requestedAmount))
        {
            SendStoreMessage(user, Loc.GetString("citinet-store-stock-insufficient"));
            UpdateStoreState(uid, component, user);
            return;
        }

        cart[lineKey] = requestedAmount;
        SendStoreMessage(user, Loc.GetString("citinet-store-cart-added", ("amount", msg.Amount)));
        UpdateStoreState(uid, component, user);
    }

    private void OnRemoveFromCartRequest(EntityUid uid, NetBrowserComponent component, CitiNetStoreRemoveFromCartMessage msg)
    {
        var user = msg.Actor;
        if (user == default)
            return;

        if (!TryGetCurrentPreset(component, out var preset))
            return;

        var key = new CartKey(user, preset.ID);
        if (!_carts.TryGetValue(key, out var cart))
            return;

        var lineKey = new CartLineKey(msg.CategoryId, msg.EntryProtoId);
        if (!cart.TryGetValue(lineKey, out var amount))
            return;

        if (msg.Amount <= 0 || amount <= msg.Amount)
            cart.Remove(lineKey);
        else
            cart[lineKey] = amount - msg.Amount;

        if (cart.Count == 0)
            _carts.Remove(key);

        UpdateStoreState(uid, component, user);
    }

    private void OnClearCartRequest(EntityUid uid, NetBrowserComponent component, CitiNetStoreClearCartMessage msg)
    {
        var user = msg.Actor;
        if (user == default)
            return;

        if (TryGetCurrentPreset(component, out var preset))
            _carts.Remove(new CartKey(user, preset.ID));

        UpdateStoreState(uid, component, user);
    }

    private void OnCheckoutCartRequest(EntityUid uid, NetBrowserComponent component, CitiNetStoreCheckoutCartMessage msg)
    {
        var user = msg.Actor;
        if (user == default)
            return;

        if (!TryGetCurrentPreset(component, out var preset))
            return;

        ProcessCheckout(uid, user, component, preset);
    }

    private async void ProcessCheckout(
        EntityUid uid,
        EntityUid user,
        NetBrowserComponent browser,
        CitiNetStorePresetPrototype preset)
    {
        var cartKey = new CartKey(user, preset.ID);
        if (!_carts.TryGetValue(cartKey, out var cart) || cart.Count == 0)
        {
            SendStoreMessage(user, Loc.GetString("citinet-store-cart-empty"));
            return;
        }

        if (!TryBuildCheckout(preset, cart, out var checkout, out var failMessage))
        {
            SendStoreMessage(user, failMessage);
            UpdateStoreState(uid, browser, user);
            return;
        }

        var usesCorporateAccount = preset.BankAccount != SectorBankAccount.Invalid;
        var station = GetStation(uid);
        var accountInfo = usesCorporateAccount && station != null
            ? GetCorporateAccountInfo(station.Value, preset.BankAccount)
            : null;

        if (usesCorporateAccount && accountInfo == null)
        {
            SendStoreMessage(user, Loc.GetString("citinet-store-corporate-account-unavailable"));
            return;
        }

        if (usesCorporateAccount && accountInfo!.DataBalance < checkout.TotalDataPrice)
        {
            SendStoreMessage(user, Loc.GetString("citinet-store-corporate-data-insufficient"));
            return;
        }

        var moneyWithdrawn = checkout.TotalPrice <= 0 || (usesCorporateAccount
            ? _bankSystem.TryFactionWithdraw(station!.Value, preset.BankAccount, checkout.TotalPrice)
            : await _bankSystem.TryBankWithdraw(user, checkout.TotalPrice));

        if (!moneyWithdrawn)
        {
            SendStoreMessage(user, usesCorporateAccount
                ? Loc.GetString("citinet-store-corporate-funds-insufficient")
                : Loc.GetString("citinet-store-personal-funds-insufficient"));
            return;
        }

        if (usesCorporateAccount)
        {
            accountInfo!.DataBalance -= checkout.TotalDataPrice;
            Dirty(station!.Value, Comp<StationBankComponent>(station.Value));
        }

        if (_deliverySystem.TryDeliverOrder(user, checkout.DeliveryItems, preset.DefaultDelivery, out var deliveryMsg))
        {
            ApplyStockConsumption(checkout.StockConsumption);
            _carts.Remove(cartKey);

            SendStoreMessage(user, deliveryMsg);
            UpdateAllBrowsers();
            return;
        }

        if (checkout.TotalPrice > 0)
        {
            if (usesCorporateAccount)
                _bankSystem.TryFactionDeposit(station!.Value, preset.BankAccount, checkout.TotalPrice);
            else
                await _bankSystem.TryBankDeposit(user, checkout.TotalPrice);
        }

        if (usesCorporateAccount)
        {
            accountInfo!.DataBalance += checkout.TotalDataPrice;
            Dirty(station!.Value, Comp<StationBankComponent>(station.Value));
        }

        SendStoreMessage(user, Loc.GetString("citinet-store-delivery-failed-refunded", ("reason", deliveryMsg)));
        UpdateStoreState(uid, browser, user);
    }

    private bool TryBuildCheckout(
        CitiNetStorePresetPrototype preset,
        Dictionary<CartLineKey, int> cart,
        out CheckoutData checkout,
        out string failMessage)
    {
        var deliveryItems = new List<DeliveryOrderItem>();
        var stockConsumption = new Dictionary<string, int>();
        var totalPrice = 0;
        var totalDataPrice = 0;

        foreach (var (line, amount) in cart)
        {
            if (amount <= 0)
                continue;

            if (!TryFindEntry(preset, line.CategoryId, line.ProtoId, out var entry))
            {
                checkout = default;
                failMessage = Loc.GetString("citinet-store-cart-invalid");
                return false;
            }

            if (!stockConsumption.TryAdd(entry.ProductId, amount))
                stockConsumption[entry.ProductId] += amount;

            totalPrice += entry.Price * amount;
            totalDataPrice += entry.DataPrice * amount;
            deliveryItems.Add(new DeliveryOrderItem(entry.ProductId, amount));
        }

        foreach (var (protoId, amount) in stockConsumption)
        {
            if (!TryGetEntryByProto(preset, protoId, out var entry) || !HasStock(entry, amount))
            {
                checkout = default;
                failMessage = Loc.GetString("citinet-store-stock-insufficient");
                return false;
            }
        }

        if (deliveryItems.Count == 0)
        {
            checkout = default;
            failMessage = Loc.GetString("citinet-store-cart-empty");
            return false;
        }

        checkout = new CheckoutData(deliveryItems, stockConsumption, totalPrice, totalDataPrice);
        failMessage = string.Empty;
        return true;
    }

    public void UpdateStoreState(EntityUid uid, NetBrowserComponent component, EntityUid user)
    {
        if (!TryGetCurrentPreset(component, out var preset))
            return;

        var usesCorporateAccount = preset.BankAccount != SectorBankAccount.Invalid;
        var accountInfo = usesCorporateAccount && GetStation(uid) is { } station
            ? GetCorporateAccountInfo(station, preset.BankAccount)
            : null;
        var balance = usesCorporateAccount
            ? accountInfo?.Balance ?? 0
            : _bankSystem.GetBalance(user);
        var dataBalance = accountInfo?.DataBalance ?? 0;
        var categories = BuildCategories(preset);
        var cartEntries = BuildCartEntries(user, preset);
        var cartTotalPrice = 0;
        var cartTotalDataPrice = 0;

        foreach (var entry in cartEntries)
        {
            cartTotalPrice += entry.TotalPrice;
            cartTotalDataPrice += entry.TotalDataPrice;
        }

        var canCheckout = cartEntries.Count > 0
            && balance >= cartTotalPrice
            && (!usesCorporateAccount || dataBalance >= cartTotalDataPrice);

        var state = new CitiNetStoreUpdateState(
            balance,
            dataBalance,
            usesCorporateAccount,
            preset.DefaultDelivery,
            categories,
            cartEntries,
            cartTotalPrice,
            cartTotalDataPrice,
            canCheckout);
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
    }

    private List<CitiNetStoreCategoryData> BuildCategories(CitiNetStorePresetPrototype preset)
    {
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

                var stock = GetStock(entry);
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

        return categories;
    }

    private List<CitiNetStoreCartEntryData> BuildCartEntries(EntityUid user, CitiNetStorePresetPrototype preset)
    {
        var result = new List<CitiNetStoreCartEntryData>();
        if (!_carts.TryGetValue(new CartKey(user, preset.ID), out var cart))
            return result;

        foreach (var (line, amount) in cart)
        {
            if (!TryFindEntry(preset, line.CategoryId, line.ProtoId, out var entry) ||
                !_prototypeManager.TryIndex<EntityPrototype>(entry.ProductId, out var proto))
                continue;

            result.Add(new CitiNetStoreCartEntryData(
                line.CategoryId,
                line.ProtoId,
                entry.NameOverride ?? proto.Name,
                amount,
                entry.Price * amount,
                entry.DataPrice * amount));
        }

        return result;
    }

    private Dictionary<CartLineKey, int> GetCart(EntityUid user, string presetId)
    {
        var key = new CartKey(user, presetId);
        if (!_carts.TryGetValue(key, out var cart))
        {
            cart = new Dictionary<CartLineKey, int>();
            _carts[key] = cart;
        }

        return cart;
    }

    private bool TryFindEntry(
        CitiNetStorePresetPrototype preset,
        string categoryId,
        string protoId,
        out CitiNetStoreEntry entry)
    {
        entry = default!;

        foreach (var catId in preset.Categories)
        {
            if (catId != categoryId)
                continue;

            if (!_prototypeManager.TryIndex<CitiNetStoreCategoryPrototype>(catId, out var category))
                return false;

            foreach (var candidate in category.Entries)
            {
                if (candidate.ProductId != protoId)
                    continue;

                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetEntryByProto(CitiNetStorePresetPrototype preset, string protoId, out CitiNetStoreEntry entry)
    {
        entry = default!;

        foreach (var catId in preset.Categories)
        {
            if (!_prototypeManager.TryIndex<CitiNetStoreCategoryPrototype>(catId, out var category))
                continue;

            foreach (var candidate in category.Entries)
            {
                if (candidate.ProductId != protoId)
                    continue;

                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private bool HasStock(CitiNetStoreEntry entry, int requestedAmount)
    {
        var stock = GetStock(entry);
        return !stock.HasValue || stock.Value >= requestedAmount;
    }

    private int? GetStock(CitiNetStoreEntry entry)
    {
        if (!entry.InitialCount.HasValue)
            return null;

        if (!_globalStock.TryGetValue(entry.ProductId, out var current))
        {
            current = entry.InitialCount.Value;
            _globalStock[entry.ProductId] = current;
        }

        return current;
    }

    private void ApplyStockConsumption(Dictionary<string, int> stockConsumption)
    {
        foreach (var (protoId, amount) in stockConsumption)
        {
            var currentStock = _globalStock.GetValueOrDefault(protoId);
            _globalStock[protoId] = Math.Max(0, currentStock - amount);
        }
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
            foreach (var actor in _uiSystem.GetActors(uid, NetBrowserUiKey.Key))
            {
                UpdateStoreState(uid, component, actor);
            }
        }
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
        {
            return stationUid;
        }

        var queryBank = EntityQueryEnumerator<StationBankComponent>();
        return queryBank.MoveNext(out var bankUid, out _) ? bankUid : null;
    }

    private bool TryGetCurrentPreset(NetBrowserComponent component, out CitiNetStorePresetPrototype preset)
    {
        preset = default!;
        var siteProto = GetSiteForUrl(component.CurrentUrl);
        if (siteProto?.StorePreset == null)
            return false;

        if (!_prototypeManager.TryIndex<CitiNetStorePresetPrototype>(siteProto.StorePreset, out var indexedPreset))
            return false;

        preset = indexedPreset;
        return true;
    }

    private NetSitePrototype? GetSiteForUrl(string url)
    {
        foreach (var site in _prototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == url)
                return site;
        }

        return null;
    }

    private readonly record struct CartKey(EntityUid User, string PresetId);
    private readonly record struct CartLineKey(string CategoryId, string ProtoId);
    private readonly record struct CheckoutData(
        List<DeliveryOrderItem> DeliveryItems,
        Dictionary<string, int> StockConsumption,
        int TotalPrice,
        int TotalDataPrice);
}
