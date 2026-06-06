using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Log;
using System.Linq;

namespace Content.Server._NC.CitiNet.Systems;

public sealed class NetBrowserSystem : SharedNetBrowserSystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("citinet.browser");
        
        SubscribeLocalEvent<NetBrowserComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<NetBrowserComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<NetBrowserComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        
        Subs.BuiEvents<NetBrowserComponent>(NetBrowserUiKey.Key, subs => {
            subs.Event<NetBrowserNavigateMessage>(OnNavigateMessage);
        });
    }

    private void OnNavigateMessage(EntityUid uid, NetBrowserComponent component, NetBrowserNavigateMessage args)
    {
        var user = args.Actor;
        if (user == default) return;

        _sawmill.Info($"Navigation request: '{args.Url}' from user {ToPrettyString(user)} via {ToPrettyString(uid)}");

        // Check if the target site is restricted and if the user has access
        bool found = false;
        foreach (var site in PrototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == args.Url)
            {
                found = true;
                if (site.RequiredAccess.Count > 0 && 
                    !component.UnlockedUrls.Contains(site.URL) && 
                    !HasAccess(uid, user, site))
                {
                    _sawmill.Warning($"Access denied to '{site.URL}' for {ToPrettyString(user)}");
                    return;
                }
                break;
            }
        }

        if (!found)
        {
            _sawmill.Warning($"Navigation failed: URL '{args.Url}' not found in prototypes.");
        }

        NavigateTo(uid, component, args.Url);
    }

    private void OnUIOpened(EntityUid uid, NetBrowserComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component, args.Actor);
        RaiseLocalEvent(uid, new NetBrowserUrlChangedEvent(uid, component.CurrentUrl, args.Actor));
    }

    private void OnItemInserted(EntityUid uid, NetBrowserComponent component, EntInsertedIntoContainerMessage args)
    {
        UpdateAllUserInterfaces(uid, component);
        
        if (TryComp<DataChipComponent>(args.Entity, out var chip) && chip.UnlockedSiteId != null)
        {
            if (PrototypeManager.TryIndex<NetSitePrototype>(chip.UnlockedSiteId, out var site))
            {
                if (component.UnlockedUrls.Add(site.URL))
                {
                    Dirty(uid, component);
                    UpdateAllUserInterfaces(uid, component);
                }
            }
        }
    }

    private void OnItemRemoved(EntityUid uid, NetBrowserComponent component, EntRemovedFromContainerMessage args)
    {
        UpdateAllUserInterfaces(uid, component);
    }

    private void UpdateAllUserInterfaces(EntityUid uid, NetBrowserComponent component)
    {
        foreach (var actor in _uiSystem.GetActors(uid, NetBrowserUiKey.Key))
        {
            UpdateUserInterface(uid, component, actor);
        }
    }

    private void UpdateUserInterface(EntityUid uid, NetBrowserComponent component, EntityUid? user)
    {
        var availableSites = GetAvailableSites(uid, component, user);
        
        _sawmill.Info($"[CITINET] Sending state to {user} via {uid}. URL: '{component.CurrentUrl}'. Sites: {string.Join(", ", availableSites)}");
        
        var state = new NetBrowserUiState(component.CurrentUrl, availableSites);
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
    }

    private List<string> GetAvailableSites(EntityUid uid, NetBrowserComponent component, EntityUid? user)
    {
        var result = new List<string>();
        var sites = PrototypeManager.EnumeratePrototypes<NetSitePrototype>().ToList();

        if (sites.Count == 0)
        {
            _sawmill.Error("[CITINET] NO NETSITE PROTOTYPES FOUND IN MANAGER!");
        }

        foreach (var site in sites)
        {
            if (component.UnlockedUrls.Contains(site.URL))
            {
                result.Add(site.ID);
                continue;
            }

            if (user != null && HasAccess(uid, user.Value, site))
            {
                result.Add(site.ID);
            }
            else if (site.RequiredAccess.Count == 0)
            {
                result.Add(site.ID);
            }
        }

        return result;
    }

    private bool HasAccess(EntityUid uid, EntityUid user, NetSitePrototype site)
    {
        if (site.RequiredAccess.Count == 0)
            return true;

        var accessSources = _accessReader.FindPotentialAccessItems(user);
        
        if (TryComp<ItemSlotsComponent>(uid, out var itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
            {
                if (slot.Item is { } item)
                    accessSources.Add(item);
            }
        }

        var accessTags = _accessReader.FindAccessTags(user, accessSources);

        foreach (var required in site.RequiredAccess)
        {
            if (!accessTags.Contains(required))
                return false;
        }

        return true;
    }

    public override void NavigateTo(EntityUid uid, NetBrowserComponent component, string url)
    {
        base.NavigateTo(uid, component, url);
        UpdateAllUserInterfaces(uid, component);
        
        var actor = _uiSystem.GetActors(uid, NetBrowserUiKey.Key).FirstOrDefault();
        RaiseLocalEvent(uid, new NetBrowserUrlChangedEvent(uid, url, actor));
    }
}
