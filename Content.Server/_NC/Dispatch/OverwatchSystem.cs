using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Localization;
using Content.Shared._NC.Dispatch;
using Content.Shared._NC.Dispatch.Components;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.SurveillanceCamera;
using Content.Shared.Audio;
using Robust.Shared.Audio;
using Content.Server.SurveillanceCamera;
using Content.Server.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Content.Server.Interaction;
using Content.Shared.Paper;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Server._NC.Ncpd;
using Robust.Shared.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.GameStates;
using Robust.Shared.Log;
using Robust.Shared.GameObjects;

namespace Content.Server._NC.Dispatch
{
    public sealed class OverwatchSystem : EntitySystem
    {
        [Dependency] private readonly UserInterfaceSystem _ui = default!;
        [Dependency] private readonly InteractionSystem _interaction = default!;
        [Dependency] private readonly TransformSystem _transform = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly SurveillanceCameraSystem _cameraSystem = default!;
        [Dependency] private readonly SurveillanceCameraMonitorSystem _cameraMonitorSystem = default!;
        [Dependency] private readonly IPrototypeManager _proto = default!;
        [Dependency] private readonly GameTicker _gameTicker = default!;
        [Dependency] private readonly NcpdDispatchSystem _dispatchSystem = default!;

        private const float GunshotCooldown = 30f; // seconds per camera

        public override void Initialize()
        {
            base.Initialize();

            // Listen to every gunshot in the world so we can forward it to sensors
            SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);


            // UI messages from consoles
            Subs.BuiEvents<OverwatchConsoleComponent>(OverwatchConsoleUiKey.Key, subs => {
                subs.Event<OverwatchAlertActionMessage>(OnAlertAction);
            });
            SubscribeLocalEvent<OverwatchConsoleComponent, BoundUIOpenedEvent>(OnUiOpen);

            // Listen for sensor destruction (Connection Lost)
            SubscribeLocalEvent<AcousticSensorComponent, ComponentShutdown>(OnSensorShutdown);
        }

        private void OnSensorShutdown(EntityUid uid, AcousticSensorComponent component, ComponentShutdown args)
        {
            if (!TryComp<SurveillanceCameraComponent>(uid, out var surv))
                return;

            var sector = surv.CameraId ?? uid.ToString();
            AddAlert(uid, Loc.GetString("overwatch-alert-loss-of-connection"), sector, false);
        }

        private void OnUiOpen(EntityUid uid, OverwatchConsoleComponent comp, BoundUIOpenedEvent args)
        {
            UpdateConsoleUi(uid, comp);
        }

        private void OnAlertAction(Entity<OverwatchConsoleComponent> ent, ref OverwatchAlertActionMessage msg)
        {
            var uid = ent.Owner;
            var comp = ent.Comp;
            if (!comp.ActiveAlerts.TryGetValue(msg.AlertId, out var alert))
                return;

            switch (msg.Action)
            {
                case OverwatchAlertAction.ConnectCamera:
                {
                    if (!TryResolveAlertCamera(alert, out var cameraUid))
                        break;

                    // open the surveillance camera monitor UI for that user on this console
                    _ui.TryOpenUi(uid, SurveillanceCameraMonitorUiKey.Key, msg.Actor);

                    // switch the console's monitor to the selected camera
                    _cameraMonitorSystem.SwitchCameraToUid(uid, cameraUid);
                    break;
                }
                case OverwatchAlertAction.PrintTicket:
                    SpawnTicket(uid, alert);
                    break;
                case OverwatchAlertAction.Archive:
                    _dispatchSystem.RemoveCallBySource($"overwatch_{msg.AlertId}");
                    comp.ActiveAlerts.Remove(msg.AlertId);
                    break;
                case OverwatchAlertAction.DispatchToTablet:
                    if (alert.Dispatched)
                        break;

                    // If we're tracking an entity (wanted/cyberpsycho), use its current position
                    // and pass targetUid for real-time tracking on tablets.
                    EntityUid? trackTarget = null;
                    NetCoordinates dispatchNetCoords = alert.Coordinates;

                    if (alert.TargetUid is { } targetNet)
                    {
                        var targetEnt = EntityManager.GetEntity(targetNet);
                        if (EntityManager.EntityExists(targetEnt))
                        {
                            dispatchNetCoords = GetNetCoordinates(_transform.ToCoordinates(_transform.GetMapCoordinates(targetEnt)));
                            trackTarget = targetEnt;
                        }
                    }

                    var desc = (alert.Type == "TRAUMA SOS" || alert.Type == "CIVILIAN SOS") 
                        ? alert.CameraName 
                        : Loc.GetString("nspd-call-desc-camera", ("name", alert.CameraName));

                    if (alert.Type == "TRAUMA SOS")
                    {
                        var traumaSys = EntityManager.System<Content.Server._NC.Trauma.TraumaComputerSystem>();
                        traumaSys.AddEmergencyCall(
                            "Overwatch",
                            alert.Sector,
                            desc
                        );
                    }
                    else
                    {
                        _dispatchSystem.AddCall(
                            alert.Type,
                            alert.Sector,
                            desc,
                            dispatchNetCoords,
                            $"overwatch_{msg.AlertId}",
                            trackTarget);
                    }
                    alert.Dispatched = true;
                    break;
            }

            UpdateConsoleUi(uid, comp);
        }

        private void SpawnTicket(EntityUid uid, OverwatchAlertData alert)
        {
            // spawn a physical dispatch ticket and put a description on it
            var ticket = EntityManager.SpawnEntity("Paper", Transform(uid).Coordinates);
            if (TryComp<PaperComponent>(ticket, out var paper))
            {
                var content = Loc.GetString("overwatch-ticket-content",
                    ("type", alert.Type.ToLower()),
                    ("sector", alert.Sector),
                    ("time", alert.TimeStr));
                paper.Content = content;
                Dirty(ticket, paper);
            }
        }

        private void UpdateConsoleUi(EntityUid uid, OverwatchConsoleComponent comp)
        {
            var list = new List<OverwatchAlertData>(comp.ActiveAlerts.Values);
            _ui.SetUiState(uid, OverwatchConsoleUiKey.Key, new OverwatchConsoleState(list));
        }

        private void OnGunShot(EntityUid uid, GunComponent component, GunShotEvent ev)
        {
            // Determine whether the gunshot sound should be treated as suppressed.
            bool suppressed = false;
            if (component.SoundGunshot is SoundPathSpecifier path && path.Path.CanonPath.Contains("silenced"))
                suppressed = true;
            else if (component.SoundGunshot is SoundCollectionSpecifier col && col.Collection?.ToLowerInvariant().Contains("silenced") == true)
                suppressed = true;

            var shooter = ev.User;
            var originPos = _transform.ToMapCoordinates(Transform(shooter).Coordinates);
            DispatchAcousticEvent(originPos, suppressed, shooter);
        }


        private void DispatchAcousticEvent(MapCoordinates origin, bool suppressed, EntityUid? shooter)
        {
            var mapPos = origin;
            // iterate over all sensors
            var query = EntityQueryEnumerator<AcousticSensorComponent, TransformComponent, SurveillanceCameraComponent>();
            while (query.MoveNext(out var camUid, out var sensor, out var xform, out var surv))
            {
                if (!sensor.Enabled)
                    continue;

                // camera must be powered / active
                if (!surv.Active)
                    continue;

                float maxRange = suppressed ? sensor.SuppressedRange : sensor.GunRange;

                var camMapPos = _transform.ToMapCoordinates(xform.Coordinates);
                if (!mapPos.InRange(camMapPos, maxRange))
                    continue;

                if (shooter.HasValue)
                {
                    // require line-of-sight on unsuppressed gunshots
                    if (!_interaction.InRangeUnobstructed(camUid, shooter.Value, maxRange))
                        continue;
                }

                // Cooldown check
                if (TryComp<OverwatchConsoleComponent>(camUid, out var _))
                {
                    // camera also a console? ignore
                }

                // Build alert
                var alertType = Loc.GetString("overwatch-alert-gunfire");
                var sector = surv.CameraId ?? Loc.GetString("overwatch-alert-unknown-sector");

                AddAlert(camUid, alertType, sector, true);
            }
        }

        public void AddAlert(EntityUid cameraUid, string type, string sector, bool playSound)
        {
            var timeStr = _gameTicker.RoundDuration().ToString(@"hh\:mm\:ss");
            var transform = Transform(cameraUid);
            var gridPos = _transform.GetGridOrMapTilePosition(cameraUid, transform);
            var sectorWithCoords = $"({gridPos.X}, {gridPos.Y}) {sector}";
            
            // NC Edit: Capture coordinates immediately so they remain valid even if camera is destroyed later.
            var coordinates = GetNetCoordinates(_transform.ToCoordinates(_transform.GetMapCoordinates(cameraUid)));

            // update every console on station
            var consoles = EntityQueryEnumerator<OverwatchConsoleComponent>();
            while (consoles.MoveNext(out var uid, out var comp))
            {
                // cooldown per camera
                var now = (float) _timing.CurTime.TotalSeconds;
                if (comp.LastAlertTime.TryGetValue(cameraUid, out var last) && now - last < GunshotCooldown)
                {
                    var netCam = EntityManager.GetNetEntity(cameraUid);
                    // update existing alert timestamp but do not create new
                    foreach (var a in comp.ActiveAlerts.Values)
                    {
                        if (a.CameraUid == netCam)
                        {
                            a.TimeStr = timeStr;
                            a.Sector = sectorWithCoords;
                            a.Coordinates = coordinates; // Update to last known pos
                            break;
                        }
                    }
                }
                else
                {
                    var id = comp.NextAlertId++;
                    // derive camera name from surveillance component if available
                    var camName = sector;
                    var hasCamera = TryComp<SurveillanceCameraComponent>(cameraUid, out var camera) && camera.Active;
                    comp.ActiveAlerts[id] = new OverwatchAlertData(
                        id,
                        type,
                        sectorWithCoords,
                        camName,
                        timeStr,
                        EntityManager.GetNetEntity(cameraUid),
                        coordinates,
                        hasCamera: hasCamera);
                    comp.LastAlertTime[cameraUid] = now;

                    // play alarm sound at the console if high priority
                    if (playSound)
                        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/alert.ogg"), uid);
                }

                UpdateConsoleUi(uid, comp);
            }
        }

        /// <summary>
        /// Public method for external systems (e.g. admin smites) to create an alert
        /// that tracks a live entity instead of a surveillance camera.
        /// The alert appears on all Overwatch consoles. The dispatcher then forwards it to tablets.
        /// </summary>
        public void AddEntityAlert(EntityUid targetUid, string type, string description, string sourceId = "")
        {
            var timeStr = _gameTicker.RoundDuration().ToString(@"hh\:mm\:ss");
            var transform = Transform(targetUid);
            var gridPos = _transform.GetGridOrMapTilePosition(targetUid, transform);
            var sectorWithCoords = $"({gridPos.X}, {gridPos.Y}) {description}";
            
            // NC Edit: Capture map-space coordinates so alerts survive targets on sub-grids.
            var coordinates = GetNetCoordinates(_transform.ToCoordinates(_transform.GetMapCoordinates(targetUid, transform)));

            var consoles = EntityQueryEnumerator<OverwatchConsoleComponent>();
            while (consoles.MoveNext(out var uid, out var comp))
            {
                var nearestCamera = FindNearestActiveCamera(_transform.GetMapCoordinates(targetUid, transform));
                var nearestCameraNet = nearestCamera != null ? EntityManager.GetNetEntity(nearestCamera.Value) : default;
                var hasCamera = nearestCamera != null;
                var updated = false;
                if (!string.IsNullOrEmpty(sourceId))
                {
                    foreach (var existing in comp.ActiveAlerts.Values)
                    {
                        if (existing.SourceId != sourceId)
                            continue;

                        existing.Sector = sectorWithCoords;
                        existing.CameraName = description;
                        existing.TimeStr = timeStr;
                        existing.CameraUid = nearestCameraNet;
                        existing.Coordinates = coordinates;
                        existing.TargetUid = EntityManager.GetNetEntity(targetUid);
                        existing.Dispatched = false;
                        existing.HasCamera = hasCamera;
                        UpdateConsoleUi(uid, comp);
                        updated = true;
                        break;
                    }
                }

                if (updated)
                    continue;

                var id = comp.NextAlertId++;
                comp.ActiveAlerts[id] = new OverwatchAlertData(
                    id,
                    type,
                    sectorWithCoords,
                    description,
                    timeStr,
                    nearestCameraNet,
                    coordinates,
                    dispatched: false,
                    targetUid: EntityManager.GetNetEntity(targetUid),  // TargetUid for live tracking
                    sourceId: sourceId,
                    hasCamera: hasCamera
                );

                // Always play alarm for entity alerts (high priority)
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/alert.ogg"), uid);
                UpdateConsoleUi(uid, comp);
            }
        }

        public void RemoveEntityAlert(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
                return;

            var consoles = EntityQueryEnumerator<OverwatchConsoleComponent>();
            while (consoles.MoveNext(out var uid, out var comp))
            {
                var removed = comp.ActiveAlerts
                    .Where(pair => pair.Value.SourceId == sourceId)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var alertId in removed)
                    comp.ActiveAlerts.Remove(alertId);

                if (removed.Count > 0)
                    UpdateConsoleUi(uid, comp);
            }
        }

        private bool TryResolveAlertCamera(OverwatchAlertData alert, out EntityUid cameraUid)
        {
            cameraUid = EntityUid.Invalid;

            if (alert.HasCamera)
            {
                var storedCamera = EntityManager.GetEntity(alert.CameraUid);
                if (IsUsableCamera(storedCamera))
                {
                    cameraUid = storedCamera;
                    return true;
                }
            }

            var alertCoords = EntityManager.GetCoordinates(alert.Coordinates);
            if (!alertCoords.EntityId.Valid)
                return false;

            if (FindNearestActiveCamera(_transform.ToMapCoordinates(alertCoords)) is not { } nearest)
                return false;

            cameraUid = nearest;
            return true;
        }

        private EntityUid? FindNearestActiveCamera(MapCoordinates origin)
        {
            EntityUid? best = null;
            var bestDistance = float.MaxValue;
            var query = EntityQueryEnumerator<SurveillanceCameraComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var camera, out var xform))
            {
                if (!camera.Active || xform.MapID != origin.MapId)
                    continue;

                var cameraPos = _transform.GetMapCoordinates(uid, xform).Position;
                var distance = (cameraPos - origin.Position).LengthSquared();
                if (distance >= bestDistance)
                    continue;

                best = uid;
                bestDistance = distance;
            }

            return best;
        }

        private bool IsUsableCamera(EntityUid uid)
        {
            return EntityManager.EntityExists(uid) &&
                   TryComp<SurveillanceCameraComponent>(uid, out var camera) &&
                   camera.Active;
        }
    }
}
