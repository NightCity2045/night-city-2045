using Content.Shared._NC.CitiNet;
using Content.Shared._NC.Ncpd;
using Content.Shared.Paper;
using Content.Server._NC.CitiNet;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.GameObjects;
using Content.Shared.Pinpointer;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Content.Server._NC.Ncpd
{
    public sealed class NcpdDispatchSystem : EntitySystem
    {
        [Dependency] private readonly UserInterfaceSystem _ui = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly TransformSystem _transform = default!;
        [Dependency] private readonly CitiNetMapSystem _citiNetMapSystem = default!;

        private readonly List<NcpdCallData> _activeCalls = new();
        private int _nextCallId = 1;
        private float _updateTimer = 0f;
        private const float UpdateInterval = 3.0f;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<NcpdTabletComponent, BoundUIOpenedEvent>(OnTabletOpened);
            SubscribeLocalEvent<NcpdTabletComponent, NcpdTabletSelectCallMsg>(OnSelectCall);
            SubscribeLocalEvent<NcpdTabletComponent, NcpdTabletClearCallMsg>(OnClearCall);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            _updateTimer += frameTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;

                // Update coordinates for calls that are tracking a live entity.
                // This ensures the call marker itself moves on the map, not just the ping.
                UpdateTrackedCallPositions();

                UpdateAllTablets();
            }
        }

        /// <summary>
        /// For every active call bound to a live entity (TargetUid),
        /// overwrite its Coordinates with the entity's current position.
        /// NcpdCallData is a struct, so we must replace it in the list by index.
        /// </summary>
        private void UpdateTrackedCallPositions()
        {
            for (var i = 0; i < _activeCalls.Count; i++)
            {
                var call = _activeCalls[i];
                if (call.TargetUid is not { } targetNet)
                    continue;

                var targetEnt = GetEntity(targetNet);
                if (!EntityManager.EntityExists(targetEnt))
                    continue;

                if (!TryComp<TransformComponent>(targetEnt, out var xform))
                    continue;

                // Store live target coordinates in map-space so tracking survives movers on sub-grids.
                call.Coordinates = GetNetCoordinates(_transform.ToCoordinates(_transform.GetMapCoordinates(targetEnt, xform)));
                _activeCalls[i] = call;
            }
        }

        private void OnTabletOpened(EntityUid uid, NcpdTabletComponent component, BoundUIOpenedEvent args)
        {
            UpdateTabletUi(uid, component);
        }

        private void OnSelectCall(EntityUid uid, NcpdTabletComponent component, NcpdTabletSelectCallMsg args)
        {
            component.ActiveCallId = args.CallId;
            UpdateTabletUi(uid, component);
        }

        private void OnClearCall(EntityUid uid, NcpdTabletComponent component, NcpdTabletClearCallMsg args)
        {
            if (component.ActiveCallId == args.CallId)
                component.ActiveCallId = null;
            
            UpdateTabletUi(uid, component);
        }

        /// <summary>
        /// Creates a new dispatch call visible on all NCPD tablets.
        /// If targetUid is provided, the call will include real-time entity tracking.
        /// </summary>
        public void AddCall(string title, string sector, string description, NetCoordinates coordinates, string sourceId = "", EntityUid? targetUid = null)
        {
            // NC Edit Start: Safety check for invalid coordinates (0,0 bug prevention)
            var coords = EntityManager.GetCoordinates(coordinates);
            if (!coords.EntityId.Valid)
                return;
            // NC Edit End

            // If already dispatched, ignore (safety check)
            if (!string.IsNullOrEmpty(sourceId) && _activeCalls.Any(c => c.SourceId == sourceId))
                return;

            var call = new NcpdCallData(
                _nextCallId++,
                title,
                sector,
                description,
                coordinates,
                _timing.CurTime,
                sourceId,
                targetUid.HasValue ? GetNetEntity(targetUid.Value) : null
            );

            _activeCalls.Add(call);
            if (_activeCalls.Count > 20)
                _activeCalls.RemoveAt(0);

            UpdateAllTablets();
        }

        public void RemoveCallBySource(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
                return;

            _activeCalls.RemoveAll(c => c.SourceId == sourceId);
            UpdateAllTablets();
        }

        public void UpdateAllTablets()
        {
            var query = EntityQueryEnumerator<NcpdTabletComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                UpdateTabletUi(uid, comp);
            }
        }

        private void UpdateTabletUi(EntityUid uid, NcpdTabletComponent component)
        {
            if (!_ui.IsUiOpen(uid, NcpdTabletUiKey.Key))
                return;

            var displayMapId = Transform(uid).MapID;

            // NC Edit Start: If we have an active call selected, use its MapID for the map display.
            if (component.ActiveCallId is { } activeCallId)
            {
                var activeCall = _activeCalls.FirstOrDefault(c => c.Id == activeCallId);
                if (activeCall.Id != 0)
                {
                    displayMapId = ResolveCallMapId(activeCall, displayMapId);
                }
            }
            // NC Edit End

            var displayGrid = ResolveDisplayMap(displayMapId, uid) ?? uid;

            var sectors = new List<CitiNetMapSectorData>();
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent, TransformComponent>();
            while (sectorQuery.MoveNext(out var sUid, out var sComp, out var sXform))
            {
                // Only show sectors belonging to the current map.
                if (sXform.MapID != displayMapId)
                    continue;

                sectors.Add(new CitiNetMapSectorData(
                    sComp.SectorName,
                    sComp.Color,
                    sComp.Bounds.Translated(_transform.GetMapCoordinates(sUid, sXform).Position),
                    sComp.FontSize));
            }

            var beacons = new List<CitiNetMapBeaconData>();
            var beaconQuery = EntityQueryEnumerator<MapBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var bUid, out var bComp, out var bXform))
            {
                if (!bComp.IsVisible) continue;

                // Only show beacons on the current map; map-space keeps sub-grid entities aligned.
                if (bXform.MapID != displayMapId)
                    continue;
                
                // SHOW ONLY PUBLIC BEACONS: No required role AND group is Public
                if (!string.IsNullOrEmpty(bComp.RequiredRole)) continue;
                if (bComp.Group != "Public") continue;

                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(bUid),
                    bComp.Label,
                    bComp.Icon,
                    bComp.Color,
                    _transform.GetMapCoordinates(bUid, bXform).Position,
                    bComp.FontSize
                ));
            }

            var pings = _citiNetMapSystem.GetActivePings(displayGrid);

            // === Live Entity Tracking ===
            // If this tablet's active call is tracking a live entity,
            // inject a real-time tracker ping at the target's current position.
            if (component.ActiveCallId is { } activeId)
            {
                var activeCall = _activeCalls.FirstOrDefault(c => c.Id == activeId);
                if (activeCall.TargetUid is { } targetNet)
                {
                    var targetEnt = GetEntity(targetNet);
                    if (EntityManager.EntityExists(targetEnt) && TryComp<TransformComponent>(targetEnt, out var targetXform))
                    {
                        // Check if target is on the map we are looking at.
                        if (targetXform.MapID != displayMapId)
                        {
                            // Optional: could show an indicator that target is off-map.
                        }
                        else
                        {
                            // Determine tracker color by call type:
                            // Cyberpsycho = bright red, Wanted = yellow
                            var isCP = activeCall.Title.Contains("CYBERPSYCHO", System.StringComparison.OrdinalIgnoreCase);
                            var trackerColor = isCP ? Color.Red : Color.Yellow;

                            pings.Add(new CitiNetMapPingData(
                                _transform.GetMapCoordinates(targetEnt, targetXform).Position,
                                trackerColor,
                                8f,  // large radius for visibility
                                CitiNetPingType.Tracker
                            ));
                        }
                    }
                }
            }

            _ui.SetUiState(uid, NcpdTabletUiKey.Key, new NcpdTabletState(
                _activeCalls, 
                component.ActiveCallId, 
                GetNetEntity(displayGrid),
                sectors, 
                beacons, 
                pings));
        }

        private MapId ResolveCallMapId(NcpdCallData call, MapId fallback)
        {
            if (call.TargetUid is { } targetNet)
            {
                var targetEnt = GetEntity(targetNet);
                if (EntityManager.EntityExists(targetEnt))
                    return Transform(targetEnt).MapID;
            }

            var callCoords = EntityManager.GetCoordinates(call.Coordinates);
            if (!callCoords.EntityId.Valid)
                return fallback;

            return _transform.ToMapCoordinates(callCoords).MapId;
        }

        private EntityUid? ResolveDisplayMap(MapId mapId, EntityUid tabletUid)
        {
            // Prefer the city grid that owns CitiNet sectors so NavMap keeps city geometry,
            // while all marker payloads are still map-space coordinates.
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent, TransformComponent>();
            while (sectorQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == mapId && xform.GridUid is { } gridUid)
                    return gridUid;
            }

            if (TryComp(tabletUid, out TransformComponent? tabletXform) &&
                tabletXform.MapID == mapId &&
                tabletXform.GridUid is { } tabletGrid &&
                HasComp<MapGridComponent>(tabletGrid) &&
                HasComp<NavMapComponent>(tabletGrid))
            {
                return tabletGrid;
            }

            var gridQuery = EntityQueryEnumerator<MapGridComponent, NavMapComponent, TransformComponent>();
            while (gridQuery.MoveNext(out var gridUid, out _, out _, out var gridXform))
            {
                if (gridXform.MapID == mapId)
                    return gridUid;
            }

            return _mapManager.GetMapEntityId(mapId);
        }

        public void SpawnDispatchTicket(EntityUid consoleUid, NcpdCallData call)
        {
            var ticket = EntityManager.SpawnEntity("Paper", Transform(consoleUid).Coordinates);
            if (TryComp<PaperComponent>(ticket, out var paper))
            {
                paper.Content = $"{Loc.GetString("nspd-dispatch-ticket-title")}\n" +
                                $"-------------------\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-case")}{call.Id}\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-type")}{call.Title}\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-sector")}{call.Sector}\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-time")}{call.CreatedTime.ToString(@"hh\:mm\:ss")}\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-details")}{call.Description}\n" +
                                $"-------------------\n" +
                                $"{Loc.GetString("nspd-dispatch-ticket-sign")}";
                Dirty(ticket, paper);
            }
        }
    }
}

