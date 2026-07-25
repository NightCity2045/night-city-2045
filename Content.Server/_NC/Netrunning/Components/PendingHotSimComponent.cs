namespace Content.Server._NC.Netrunning.Components;

/// <summary>
/// Holds an immersion request while the entry device executes its defensive META chain.
/// </summary>
[RegisterComponent]
public sealed partial class PendingHotSimComponent : Component
{
    [ViewVariables]
    public EntityUid User;

    [ViewVariables]
    public EntityUid EntryTarget;

    [ViewVariables]
    public EntityUid NetworkServer;

    [ViewVariables]
    public EntityUid TransactionServer;

    [ViewVariables]
    public int TransactionId;
}
