using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Rigger.Events;

public sealed partial class RiggerExitConsoleActionEvent : InstantActionEvent;

public sealed partial class RiggerToggleRTSModeActionEvent : InstantActionEvent;

public sealed partial class RiggerDroneStatusActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public enum RiggerDroneStatusUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class RiggerDroneStatusBuiState : BoundUserInterfaceState
{
    public readonly List<RiggerDroneStatusEntry> Drones;

    public RiggerDroneStatusBuiState(List<RiggerDroneStatusEntry> drones)
    {
        Drones = drones;
    }
}

[Serializable, NetSerializable]
public readonly struct RiggerDroneStatusEntry
{
    public readonly NetEntity Drone;
    public readonly string Name;
    public readonly bool IsAlive;
    public readonly float? HealthFraction;
    public readonly float? BatteryFraction;

    public RiggerDroneStatusEntry(
        NetEntity drone,
        string name,
        bool isAlive,
        float? healthFraction,
        float? batteryFraction)
    {
        Drone = drone;
        Name = name;
        IsAlive = isAlive;
        HealthFraction = healthFraction;
        BatteryFraction = batteryFraction;
    }
}
