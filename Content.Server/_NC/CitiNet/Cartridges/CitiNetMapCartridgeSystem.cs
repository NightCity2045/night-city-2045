using Content.Server.CartridgeLoader;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.Forensics;
using Content.Shared.CartridgeLoader;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared._NC.CitiNet.Delivery;
using Robust.Shared.Utility;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server._NC.CitiNet.Cartridges;

public sealed class CitiNetMapCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridge = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly CitiNetMapSystem _citiNet = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    private float _updateTimer = 0f;
    private const float UpdateInterval = 1.0f; 

    [Dependency] private readonly Content.Shared.Inventory.InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeNetworkEvent<OpenCitiNetMapUiMessage>(OnOpenCitiNetMapUiMessage);

        SubscribeLocalEvent<CitiNetMapCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<CitiNetMapCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnOpenCitiNetMapUiMessage(OpenCitiNetMapUiMessage msg, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;
        if (user == null)
            return;

        // Ищем PDA в слоте ID
        if (!_inventory.TryGetSlotEntity(user.Value, "id", out var pdaUid))
            return;

        // Убеждаемся что это КПК с установленным CartridgeLoader
        if (!TryComp<CartridgeLoaderComponent>(pdaUid, out var loader))
            return;

        // Ищем внутри CitiNetMapCartridgeComponent
        if (!_cartridge.TryGetProgram<CitiNetMapCartridgeComponent>(pdaUid.Value, out var mapUid, false, loader))
            return;

        // Если Карта найдена, делаем ее активной программой
        if (loader.ActiveProgram != mapUid)
            _cartridge.ActivateProgram(pdaUid.Value, mapUid.Value, loader);

        // Открываем UI PDA для игрока
        _uiSystem.OpenUi(pdaUid.Value, Content.Shared.PDA.PdaUiKey.Key, user.Value);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTimer += frameTime;
        if (_updateTimer >= UpdateInterval)
        {
            _updateTimer = 0f;
            RefreshAllInterfaces();
        }
    }

    private void RefreshAllInterfaces()
    {
        var query = EntityQueryEnumerator<CitiNetMapCartridgeComponent, CartridgeLoaderComponent>();
        while (query.MoveNext(out var uid, out var cart, out var loader))
        {
            // Update if someone is looking at the PDA UI
            if (_uiSystem.GetActors(uid, Content.Shared.PDA.PdaUiKey.Key).Any())
                UpdateUI(uid, uid);
        }

        var consoleQuery = EntityQueryEnumerator<CitiNetMapComponent, UserInterfaceComponent>();
        while (consoleQuery.MoveNext(out var uid, out var config, out var ui))
        {
            // Update if someone is looking at the Map UI directly (e.g. wall console)
            if (_uiSystem.GetActors(uid, CitiNetMapUiKey.Key).Any())
                UpdateUI(uid, uid);
        }
    }

    private void OnUiReady(Entity<CitiNetMapCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUI(ent, args.Loader);
    }

    private void OnMessage(Entity<CitiNetMapCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not CitiNetUiMessageEvent)
            return;
        UpdateUI(ent, GetEntity(args.LoaderUid));
    }

    private void UpdateUI(EntityUid uid, EntityUid loader)
    {
        var sectors = new List<CitiNetMapSectorData>();
        var beacons = new List<CitiNetMapBeaconData>();
        var pings = new List<CitiNetMapPingData>();

        // 1. Identify the viewer (person looking at the map)
        EntityUid? viewer = null;

        // Priority 1: Direct Map UI
        foreach (var actor in _uiSystem.GetActors(loader, CitiNetMapUiKey.Key))
        {
            viewer = actor;
            break;
        }

        // Priority 2: PDA/Loader UI
        if (viewer == null)
        {
            foreach (var actor in _uiSystem.GetActors(loader, Content.Shared.PDA.PdaUiKey.Key))
            {
                viewer = actor;
                break;
            }
        }

        // Fallback: If no actors found via UI, check if the PDA is in someone's inventory/hands
        if (viewer == null && TryComp(loader, out TransformComponent? xformLoader))
        {
            viewer = xformLoader.ParentUid;
        }

        string? viewerJob = null;
        if (viewer != null && _mind.TryGetMind(viewer.Value, out var mindId, out var mind))
        {
            if (_job.MindTryGetJobId(mindId, out var jobId))
            {
                viewerJob = jobId?.Id;
            }
        }

        List<string> allowedGroups = new() { "Public" };
        if (TryComp<CitiNetMapComponent>(loader, out var mapConfig))
        {
            allowedGroups = mapConfig.VisibleGroups;
        }

        var xform = Transform(loader);
        var gridUid = xform.GridUid;

        if (gridUid != null)
        {
            // 2. Add SELF (Viewer) manually if they are on the grid
            if (viewer != null && TryComp(viewer.Value, out TransformComponent? viewerXform) && viewerXform.GridUid == gridUid)
            {
                var viewerPos = Vector2.Transform(_transform.GetWorldPosition(viewer.Value), _transform.GetInvWorldMatrix(gridUid.Value));
                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(viewer.Value),
                    "YOU",
                    null,
                    Color.FromHex("#00f2ff"),
                    viewerPos,
                    12,
                    false,
                    true
                ));
            }

            // 3. Scan for Sectors
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent, TransformComponent>();
            while (sectorQuery.MoveNext(out var sUid, out var sector, out var sXform))
            {
                if (sXform.GridUid != gridUid) continue;

                var sectorPos = Vector2.Transform(_transform.GetWorldPosition(sUid), _transform.GetInvWorldMatrix(gridUid.Value));
                sectors.Add(new CitiNetMapSectorData(sector.SectorName, sector.Color, sector.Bounds.Translated(sectorPos), sector.FontSize));
            }

            // 4. Scan for Beacons
            var beaconQuery = EntityQueryEnumerator<MapBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var bUid, out var beacon, out var bXform))
            {
                if (bXform.GridUid != gridUid || !beacon.IsVisible) continue;

                // Skip if this is the viewer (we already added them as 'YOU')
                if (bUid == viewer) continue;

                if (!allowedGroups.Contains(beacon.Group))
                    continue;

                bool isDead = false;
                if (TryComp<MobStateComponent>(bUid, out var mobState))
                {
                    isDead = _mobState.IsDead(bUid, mobState);
                }

                var label = string.IsNullOrWhiteSpace(beacon.Label) ? MetaData(bUid).EntityName : beacon.Label;
                var beaconPos = Vector2.Transform(_transform.GetWorldPosition(bUid), _transform.GetInvWorldMatrix(gridUid.Value));

                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(bUid),
                    label,
                    beacon.Icon,
                    beacon.Color,
                    beaconPos,
                    beacon.FontSize,
                    isDead,
                    false
                ));
            }

            // 5. Scan for Forensic Chips in devices
            var chipQuery = EntityQueryEnumerator<ForensicChipComponent, TransformComponent>();
            while (chipQuery.MoveNext(out var cUid, out var chip, out var cXform))
            {
                // Only show if the chip is inserted into a device with a CartridgeLoader (PDA, etc.)
                if (!HasComp<CartridgeLoaderComponent>(cXform.ParentUid)) continue;

                var chipGrid = _transform.GetGrid(cUid);
                if (chipGrid != gridUid) continue;

                // Position extraction from NetCoordinates
                var deathPos = chip.Coordinates.Position;

                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(cUid),
                    Loc.GetString("nc-forensics-map-marker", ("name", chip.VictimName)),
                    null,
                    Color.Red,
                    deathPos,
                    12,
                    true,
                    false
                ));
            }

            // 6. Scan for Delivery Chips in devices
            var deliveryQuery = EntityQueryEnumerator<DeliveryChipComponent, TransformComponent>();
            while (deliveryQuery.MoveNext(out var cUid, out var chip, out var cXform))
            {
                if (!HasComp<CartridgeLoaderComponent>(cXform.ParentUid)) continue;
                if (chip.TargetDropPoint == null) continue;

                var chipGrid = _transform.GetGrid(cUid);
                if (chipGrid != gridUid) continue;

                var targetPos = Vector2.Transform(_transform.GetWorldPosition(chip.TargetDropPoint.Value), _transform.GetInvWorldMatrix(gridUid.Value));

                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(cUid),
                    Loc.GetString("nc-delivery-map-marker", ("location", chip.LocationName)),
                    new SpriteSpecifier.Rsi(new ResPath("/Textures/Interface/Misc/gps_icons.rsi"), "waypoint"),
                    Color.Yellow,
                    targetPos,
                    12,
                    false,
                    false
                ));
            }

            pings.AddRange(_citiNet.GetActivePings(gridUid.Value));
        }

        var state = new CitiNetMapBoundUserInterfaceState(gridUid != null ? GetNetEntity(gridUid.Value) : null, sectors, beacons, pings);        
        if (HasComp<CitiNetMapCartridgeComponent>(uid))
            _cartridge.UpdateCartridgeUiState(loader, state);
        else
            _uiSystem.SetUiState(loader, CitiNetMapUiKey.Key, state);
    }
}
