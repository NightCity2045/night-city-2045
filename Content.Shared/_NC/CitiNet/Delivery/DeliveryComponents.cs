using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.CitiNet.Delivery;

[Serializable, NetSerializable]
public enum DropType : byte
{
    Corporate,
    DeadDrop,
    CorporateZone
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

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CorporateDeliveryZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsOccupied = false;

    [DataField, AutoNetworkedField]
    public string LocationName = "Unknown Corporate Pickup Zone";

    [DataField]
    public Vector2i Size = new(5, 5);

    [DataField]
    public TimeSpan DeliveryDelay = TimeSpan.FromMinutes(5);

    [DataField]
    public EntProtoId CratePrototype = "NCCorporateDeliveryCrate";

    [DataField, AutoNetworkedField]
    public EntityUid? DeliveredCrate;
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

    [DataField, AutoNetworkedField]
    public TimeSpan? ReadyAt;

    [DataField, AutoNetworkedField]
    public bool IsReady = true;
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

