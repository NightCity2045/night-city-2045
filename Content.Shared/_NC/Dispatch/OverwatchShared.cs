using System;
using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Dispatch
{
    [Serializable, NetSerializable]
    public enum OverwatchConsoleUiKey : byte
    {
        Key
    }

    /// <summary>
    ///     An individual alert entry sent to the console UI.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class OverwatchAlertData
    {
        public int Id;
        public string Type;
        public string Sector;
        public string CameraName;
        public string TimeStr;
        public NetEntity CameraUid;
        public NetCoordinates Coordinates;
        public bool Dispatched;

        /// <summary>
        /// If set, this alert is tracking a live entity (wanted / cyberpsycho).
        /// When dispatched to tablet, the entity will be bound for real-time tracking.
        /// </summary>
        public NetEntity? TargetUid;

        public OverwatchAlertData(int id, string type, string sector, string cameraName, string timeStr, NetEntity cameraUid, NetCoordinates coordinates, bool dispatched = false, NetEntity? targetUid = null)
        {
            Id = id;
            Type = type;
            Sector = sector;
            CameraName = cameraName;
            TimeStr = timeStr;
            CameraUid = cameraUid;
            Coordinates = coordinates;
            Dispatched = dispatched;
            TargetUid = targetUid;
        }
    }

    [Serializable, NetSerializable]
    public sealed class OverwatchConsoleState : BoundUserInterfaceState
    {
        public List<OverwatchAlertData> Alerts;

        public OverwatchConsoleState(List<OverwatchAlertData> alerts)
        {
            Alerts = alerts;
        }
    }

    [Serializable, NetSerializable]
    public enum OverwatchAlertAction : byte
    {
        ConnectCamera,
        PrintTicket,
        Archive,
        DispatchToTablet
    }

    [Serializable, NetSerializable]
    public sealed class OverwatchAlertActionMessage : BoundUserInterfaceMessage
    {
        public int AlertId;
        public OverwatchAlertAction Action;

        public OverwatchAlertActionMessage(int alertId, OverwatchAlertAction action)
        {
            AlertId = alertId;
            Action = action;
        }
    }
}
