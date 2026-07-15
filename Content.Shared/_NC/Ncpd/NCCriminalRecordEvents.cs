using Content.Shared.Security;

namespace Content.Shared._NC.Ncpd;

/// <summary>
/// Raised when a station criminal record status changes so NC dispatch systems can mirror it.
/// </summary>
public sealed class NCCriminalRecordStatusChangedEvent(string name, SecurityStatus status, string? reason) : EntityEventArgs
{
    public readonly string Name = name;
    public readonly SecurityStatus Status = status;
    public readonly string? Reason = reason;
}
