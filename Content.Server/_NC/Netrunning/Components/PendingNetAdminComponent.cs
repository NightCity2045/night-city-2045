namespace Content.Server._NC.Netrunning.Components;

/// <summary>
/// Holds a local administrator request while server defense programs execute.
/// </summary>
[RegisterComponent]
public sealed partial class PendingNetAdminComponent : Component
{
    [ViewVariables]
    public EntityUid User;

    [ViewVariables]
    public EntityUid Server;

    [ViewVariables]
    public EntityUid TransactionServer;

    [ViewVariables]
    public int TransactionId;
}
