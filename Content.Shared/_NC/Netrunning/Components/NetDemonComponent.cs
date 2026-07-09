using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
///     Active NET hunter. Movement/HTN can be layered later; this component
///     defines the combat pulse that META scripts can spawn and tune.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NetDemonComponent : Component
{
    [DataField("damage"), AutoNetworkedField]
    public int Damage = 5;

    [DataField("pulseInterval"), AutoNetworkedField]
    public float PulseInterval = 1.5f;

    [DataField("range"), AutoNetworkedField]
    public float Range = 1.5f;

    [ViewVariables]
    public float Accumulator;
}
