using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Map;
using System;
using System.Collections.Generic;

namespace Content.Shared._NC.Forensics;

[Serializable, NetSerializable]
public sealed class NcpdForensicsConsoleBuiState : BoundUserInterfaceState
{
    public readonly List<ForensicsAlertData> Alerts;

    public NcpdForensicsConsoleBuiState(List<ForensicsAlertData> alerts)
    {
        Alerts = alerts;
    }
}

[Serializable, NetSerializable]
public enum NcpdForensicsAlertAction : byte
{
    DispatchToTablet,
    PrintTicket,
    Archive
}

[Serializable, NetSerializable]
public sealed class NcpdForensicsAlertActionMessage : BoundUserInterfaceMessage
{
    public int AlertIndex;
    public NcpdForensicsAlertAction Action;

    public NcpdForensicsAlertActionMessage(int alertIndex, NcpdForensicsAlertAction action)
    {
        AlertIndex = alertIndex;
        Action = action;
    }
}

[Serializable, NetSerializable]
public struct ForensicsAlertData
{
    public string Victim;
    public string Location;
    public float X;
    public float Y;
    public NetCoordinates? Coordinates; // Added for map and printing
    public TimeSpan Time;
    public bool Dispatched;
    public bool Archived;
}
