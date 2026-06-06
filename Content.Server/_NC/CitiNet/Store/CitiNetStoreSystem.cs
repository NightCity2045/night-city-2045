using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Content.Shared._NC.CitiNet.Store;
using Content.Server._NC.Bank;
using Content.Server._NC.CitiNet.Delivery;
using Content.Server.Chat.Managers;
using Robust.Shared.Player;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server._NC.CitiNet.Store;

public sealed class CitiNetStoreSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BankSystem _bankSystem = default!;
    [Dependency] private readonly DeliverySystem _deliverySystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;

    /// <summary>
    /// GLOBAL SCARCITY STORAGE
    /// Key: Product Prototype ID
    /// Value: Remaining city-wide stock
    /// </summary>
    private readonly Dictionary<string, int> _globalStock = new();

    public override void Initialize()
    {
        base.Initialize();
        
        Subs.BuiEvents<NetBrowserComponent>(NetBrowserUiKey.Key, subs => {
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

        if (targetEntry == null) return;

        // Check global stock
        if (targetEntry.InitialCount.HasValue)
        {
            var currentStock = _globalStock.GetValueOrDefault(targetEntry.ProductId, targetEntry.InitialCount.Value);
            if (currentStock < msg.Amount)
            {
                if (TryComp<ActorComponent>(user, out var actor))
                    _chatManager.DispatchServerMessage(actor.PlayerSession, "Товар закончился на складах города!");
                return;
            }
        }

        ProcessTransaction(uid, user, targetEntry, msg.Amount, component, preset.DefaultDelivery);
    }

    private async void ProcessTransaction(EntityUid uid, EntityUid user, CitiNetStoreEntry entry, int amount, NetBrowserComponent component, Shared._NC.CitiNet.Delivery.DropType deliveryType)
    {
        var totalPrice = entry.Price * amount;

        if (await _bankSystem.TryBankWithdraw(user, totalPrice))
        {
            // Transaction successful, trigger delivery
            if (_deliverySystem.TryDeliverItem(user, entry.ProductId, amount, deliveryType, out var deliveryMsg))
            {
                // Update global stock
                if (entry.InitialCount.HasValue)
                {
                    var currentStock = _globalStock.GetValueOrDefault(entry.ProductId, entry.InitialCount.Value);
                    _globalStock[entry.ProductId] = currentStock - amount;
                }

                // Notify player
                if (TryComp<ActorComponent>(user, out var actor))
                {
                    _chatManager.DispatchServerMessage(actor.PlayerSession, deliveryMsg);
                }

                // Update ALL browsers to reflect new global stock
                UpdateAllBrowsers();
            }
            else 
            {
                // Delivery failed (no free points), refund money
                await _bankSystem.TryBankWithdraw(user, -totalPrice); // Negative withdraw is a deposit
                if (TryComp<ActorComponent>(user, out var actor))
                {
                    _chatManager.DispatchServerMessage(actor.PlayerSession, "Ошибка доставки: " + deliveryMsg + " Деньги возвращены на счет.");
                }
            }
        }
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

    public void UpdateStoreState(EntityUid uid, NetBrowserComponent component, EntityUid user)
    {
        var siteProto = GetSiteForUrl(component.CurrentUrl);
        if (siteProto?.StorePreset == null) return;

        if (!_prototypeManager.TryIndex<CitiNetStorePresetPrototype>(siteProto.StorePreset, out var preset))
            return;

        var balance = _bankSystem.GetBalance(user);
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
                    : (int?)null;

                // Sync the value back to dictionary if it's missing (first access)
                if (entry.InitialCount.HasValue && !_globalStock.ContainsKey(entry.ProductId))
                    _globalStock[entry.ProductId] = entry.InitialCount.Value;

                entries.Add(new CitiNetStoreEntryData(
                    catId,
                    entry.ProductId,
                    entry.NameOverride ?? proto.Name,
                    entry.DescriptionOverride ?? proto.Description,
                    entry.Price,
                    stock
                ));
            }

            categories.Add(new CitiNetStoreCategoryData(category.Name, entries));
        }

        var state = new CitiNetStoreUpdateState(balance, categories);
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
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
