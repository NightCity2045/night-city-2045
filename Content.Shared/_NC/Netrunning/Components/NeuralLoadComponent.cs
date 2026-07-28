using Robust.Shared.GameStates;

namespace Content.Shared._NC.Netrunning.Components;

/// <summary>
/// Temporary neural processing load accumulated by a human operator in HotSim.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NeuralLoadComponent : Component
{
    [DataField, AutoNetworkedField]
    public float CurrentLoad;

    [DataField, AutoNetworkedField]
    public float MaxLoad = 20f;

    [AutoNetworkedField]
    public bool Overloaded;

    [ViewVariables]
    public double RecoveryBlockedUntil;

    [ViewVariables]
    public bool WarningIssued;

    [ViewVariables]
    public bool CriticalIssued;
}
