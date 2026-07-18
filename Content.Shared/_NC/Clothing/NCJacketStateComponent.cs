using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Clothing;

/// <summary>
/// The four visual combinations supported by an NC jacket.
/// </summary>
[Serializable, NetSerializable]
public enum NCJacketVisualState : byte
{
    ClosedSleevesDown,
    OpenSleevesDown,
    ClosedSleevesRolled,
    OpenSleevesRolled,
}

/// <summary>
/// Appearance key used by the jacket's data-driven sprite visualizer.
/// </summary>
[Serializable, NetSerializable]
public enum NCJacketVisuals : byte
{
    State,
}

/// <summary>
/// Mapped sprite layer changed by the jacket appearance visualizer.
/// </summary>
[Serializable, NetSerializable]
public enum NCJacketVisualLayers : byte
{
    Base,
}

/// <summary>
/// Data-only state for independently opening a jacket and rolling its sleeves.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class NCJacketStateComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool IsOpen;

    [DataField, AutoNetworkedField]
    public bool SleevesRolled;

    [DataField, AutoNetworkedField]
    public bool CanRollSleeves;

    [DataField(required: true)]
    public string OpenPrefix = string.Empty;

    [DataField(required: true)]
    public string RolledPrefix = string.Empty;

    [DataField(required: true)]
    public string OpenRolledPrefix = string.Empty;

    [DataField(required: true)]
    public LocId OpenVerbText;

    [DataField(required: true)]
    public LocId CloseVerbText;

    [DataField(required: true)]
    public LocId RollSleevesVerbText;

    [DataField(required: true)]
    public LocId LowerSleevesVerbText;
}
