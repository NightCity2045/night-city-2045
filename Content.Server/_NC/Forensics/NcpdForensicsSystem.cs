using System;
using System.Collections.Generic;
using Content.Server.Station.Systems;
using Content.Shared._NC.Forensics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Paper;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.GameStates;
using Content.Server._NC.Ncpd;
using Content.Shared._NC.Ncpd;
using System.Linq;
using Content.Shared._NC.CitiNet.Components;

namespace Content.Server._NC.Forensics;

public sealed class NcpdForensicsSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly NcpdDispatchSystem _dispatchSystem = default!;

    private float _updateTimer = 0f;
    private const float UpdateInterval = 0.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        
        // Browser support
        Subs.BuiEvents<NetBrowserComponent>(NetBrowserUiKey.Key, subs => {
            subs.Event<NcpdForensicsAlertActionMessage>(OnBrowserAlertAction);
        });
    }

    private void OnBrowserAlertAction(EntityUid uid, NetBrowserComponent component, NcpdForensicsAlertActionMessage msg)
    {
        if (component.CurrentUrl != "ncpd.gov/forensics")
            return;

        ProcessAlertAction(uid, msg);
    }

    private void ProcessAlertAction(EntityUid uid, NcpdForensicsAlertActionMessage msg)
    {
        var stationUid = _stationSystem.GetOwningStation(uid);
        if (stationUid == null && _stationSystem.GetStationsSet().Count > 0)
        {
            stationUid = _stationSystem.GetStationsSet().First();
        }

        if (stationUid == null)
            return;

        var station = EnsureComp<NcpdForensicsStationComponent>(stationUid.Value);
        if (msg.AlertIndex < 0 || msg.AlertIndex >= station.Alerts.Count)
            return;

        var alert = station.Alerts[msg.AlertIndex];

        switch (msg.Action)
        {
            case NcpdForensicsAlertAction.DispatchToTablet:
                if (alert.Dispatched)
                    break;

                var mapCoords = new MapCoordinates(alert.X, alert.Y, _transformSystem.GetMapCoordinates(uid).MapId);
                var netCoords = GetNetCoordinates(_transformSystem.ToCoordinates(mapCoords));
                _dispatchSystem.AddCall(Loc.GetString("nspd-call-type-flatline"), alert.Location, Loc.GetString("nspd-call-desc-victim", ("name", alert.Victim)), netCoords, $"forensics_{msg.AlertIndex}");
                
                alert.Dispatched = true;
                station.Alerts[msg.AlertIndex] = alert; // Update the struct in list
                break;
            case NcpdForensicsAlertAction.PrintTicket:
                SpawnDispatchTicket(uid, alert);
                break;
            case NcpdForensicsAlertAction.Archive:
                alert.Archived = true;
                station.Alerts[msg.AlertIndex] = alert;
                break;
        }
        UpdateConsoleUi(uid); 
    }

    private void UpdateConsoleUi(EntityUid uid)
    {
        var stationUid = _stationSystem.GetOwningStation(uid);
        if (stationUid == null && _stationSystem.GetStationsSet().Count > 0)
        {
            stationUid = _stationSystem.GetStationsSet().First();
        }

        if (stationUid == null)
            return;

        var alerts = EnsureComp<NcpdForensicsStationComponent>(stationUid.Value).Alerts;
        var state = new NcpdForensicsConsoleBuiState(new List<ForensicsAlertData>(alerts));
        
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTimer += frameTime;
        if (_updateTimer >= UpdateInterval)
        {
            _updateTimer = 0f;
            UpdateAllBrowserInterfaces();
        }
    }

    private void UpdateAllBrowserInterfaces()
    {
        var query = EntityQueryEnumerator<NetBrowserComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.CurrentUrl != "ncpd.gov/forensics")
                continue;

            if (!_uiSystem.GetActors(uid, NetBrowserUiKey.Key).Any())
                continue;

            UpdateConsoleUi(uid);
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead) return;

        var target = args.Target;

        // Ищем станцию, чтобы записать туда алерт
        var stationUid = _stationSystem.GetOwningStation(target);
        if (stationUid == null && _stationSystem.GetStationsSet().Count > 0)
        {
            stationUid = _stationSystem.GetStationsSet().First();
        }

        if (stationUid == null)
            return;

        // Пытаемся получить имя с ID карты, если её нет - берем базовое имя сущности
        string victimName = Name(target);
        if (_idCardSystem.TryFindIdCard(target, out var idCard) && !string.IsNullOrWhiteSpace(idCard.Comp.FullName))
        {
            victimName = idCard.Comp.FullName;
        }

        var coords = _transformSystem.GetMapCoordinates(target);
        var gridUid = _transformSystem.GetGrid(target);
        
        // Use GridUid if possible for NavMap, otherwise MapEntity
        var mapUid = gridUid ?? _mapManager.GetMapEntityId(coords.MapId);
        
        var locString = Name(mapUid);
        var pos = coords.Position;

        var alert = new ForensicsAlertData
        {
            Victim = victimName,
            Location = locString,
            X = (float)Math.Round(pos.X, 1),
            Y = (float)Math.Round(pos.Y, 1),
            Coordinates = GetNetCoordinates(_transformSystem.ToCoordinates(mapUid, coords)),
            Time = _timing.CurTime
        };

        var station = EnsureComp<NcpdForensicsStationComponent>(stationUid.Value);
        station.Alerts.Insert(0, alert);
        if (station.Alerts.Count > 50)
            station.Alerts.RemoveAt(station.Alerts.Count - 1);

        // Обновляем все открытые браузеры на этой странице
        var browserQuery = EntityQueryEnumerator<NetBrowserComponent>();
        while (browserQuery.MoveNext(out var browserUid, out var browser))
        {
            if (browser.CurrentUrl != "ncpd.gov/forensics")
                continue;

            var browserStation = _stationSystem.GetOwningStation(browserUid);
            if (browserStation == null && _stationSystem.GetStationsSet().Count > 0)
                browserStation = _stationSystem.GetStationsSet().First();

            if (browserStation == stationUid)
                UpdateConsoleUi(browserUid);
        }
    }

    public void SpawnDispatchTicket(EntityUid consoleUid, ForensicsAlertData alert)
    {
        var coords = _transformSystem.GetMoverCoordinates(consoleUid);
        var chip = EntityManager.SpawnEntity("NcpdEvidenceChip", coords);
        
        var metaSystem = EntityManager.System<MetaDataSystem>();
        metaSystem.SetEntityName(chip, Loc.GetString("nc-forensics-chip-name", ("name", alert.Victim)));
        metaSystem.SetEntityDescription(chip, Loc.GetString("nc-forensics-chip-desc"));

        if (alert.Coordinates != null)
        {
            var forensic = EnsureComp<ForensicChipComponent>(chip);
            forensic.VictimName = alert.Victim;
            forensic.Coordinates = alert.Coordinates.Value;
            forensic.Timestamp = alert.Time;
            Dirty(chip, forensic);
        }
    }
}
