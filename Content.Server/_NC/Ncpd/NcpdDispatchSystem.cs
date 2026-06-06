using Content.Shared._NC.CitiNet;
using Content.Shared._NC.Ncpd;
using Content.Shared.Paper;
using Content.Server._NC.CitiNet;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.GameObjects;
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

                // Overwrite the call's coordinates with the target's current position
                call.Coordinates = GetNetCoordinates(xform.Coordinates);
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

            var gridUid = _transform.GetGrid(uid);
            var mapUid = _transform.GetMap(uid);
            var displayGrid = gridUid ?? mapUid ?? uid;

            // NC Edit Start: If we have an active call selected, use its grid for the map display
            if (component.ActiveCallId is { } activeCallId)
            {
                var activeCall = _activeCalls.FirstOrDefault(c => c.Id == activeCallId);
                if (activeCall.Id != 0)
                {
                    var callCoords = EntityManager.GetCoordinates(activeCall.Coordinates);
                    if (callCoords.EntityId.Valid)
                    {
                        displayGrid = callCoords.EntityId;
                    }
                }
            }
            // NC Edit End

            var sectors = new List<CitiNetMapSectorData>();
            var sectorQuery = EntityQueryEnumerator<MapSectorComponent>();
            while (sectorQuery.MoveNext(out var sUid, out var sComp))
            {
                // Only show sectors belonging to the current display grid
                if (_transform.GetGrid(sUid) != displayGrid && _transform.GetMap(sUid) != displayGrid)
                    continue;

                sectors.Add(new CitiNetMapSectorData(sComp.SectorName, sComp.Color, sComp.Bounds, sComp.FontSize));
            }

            var beacons = new List<CitiNetMapBeaconData>();
            var beaconQuery = EntityQueryEnumerator<MapBeaconComponent, TransformComponent>();
            while (beaconQuery.MoveNext(out var bUid, out var bComp, out var bXform))
            {
                if (!bComp.IsVisible) continue;

                // Only show beacons for the current grid
                if (bXform.GridUid != displayGrid && bXform.MapID != MapId.Nullspace)
                    continue;
                
                // SHOW ONLY PUBLIC BEACONS: No required role AND group is Public
                if (!string.IsNullOrEmpty(bComp.RequiredRole)) continue;
                if (bComp.Group != "Public") continue;

                var bPos = _transform.GetGridOrMapTilePosition(bUid, bXform);
                beacons.Add(new CitiNetMapBeaconData(
                    GetNetEntity(bUid),
                    bComp.Label,
                    bComp.Icon,
                    bComp.Color,
                    bPos,
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
                        // Check if target is on the grid we are looking at
                        if (targetXform.GridUid != displayGrid && targetXform.MapID != MapId.Nullspace)
                        {
                            // Optional: could show an indicator that target is off-grid
                        }
                        else
                        {
                            var targetPos = _transform.GetGridOrMapTilePosition(targetEnt, targetXform);

                            // Determine tracker color by call type:
                            // Cyberpsycho = bright red, Wanted = yellow
                            var isCP = activeCall.Title.Contains("CYBERPSYCHO", System.StringComparison.OrdinalIgnoreCase);
                            var trackerColor = isCP ? Color.Red : Color.Yellow;

                            pings.Add(new CitiNetMapPingData(
                                targetPos,
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

