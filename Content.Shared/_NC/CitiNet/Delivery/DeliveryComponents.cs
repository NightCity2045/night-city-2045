using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.CitiNet.Delivery;

[Serializable, NetSerializable]
public enum DropType : byte
{
    Corporate,
    DeadDrop
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DropPointComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsOccupied = false;

    [DataField("dropType"), AutoNetworkedField]
    public DropType DropType = DropType.DeadDrop;

    [DataField, AutoNetworkedField]
    public string LocationName = "Unknown Location";

    /// <summary>
    /// The entity currently stored inside this drop point.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ContainedItem;

    /// <summary>
    /// For corporate drops, when the item was delivered.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? DeliveryTime;
}

[Serializable, NetSerializable]
public enum OTPKeypadUiKey : byte
{
    Key
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DeliveryChipComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? TargetDropPoint;

    [DataField, AutoNetworkedField]
    public string LocationName = string.Empty;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OTPKeypadComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? CurrentPin;

    [DataField, AutoNetworkedField]
    public bool IsLocked = false;
}

[Serializable, NetSerializable]
public sealed class OTPKeypadSubmitPinMessage : BoundUserInterfaceMessage
{
    public string Pin { get; }

    public OTPKeypadSubmitPinMessage(string pin)
    {
        Pin = pin;
    }
}

