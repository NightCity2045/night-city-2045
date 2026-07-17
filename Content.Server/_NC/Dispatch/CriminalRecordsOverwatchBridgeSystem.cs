using Content.Shared._NC.Ncpd;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Security;

namespace Content.Server._NC.Dispatch;

/// <summary>
/// Mirrors criminal-record Wanted status into Overwatch alerts for the city-tracking workflow.
/// </summary>
public sealed class CriminalRecordsOverwatchBridgeSystem : EntitySystem
{
    [Dependency] private readonly OverwatchSystem _overwatch = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCCriminalRecordStatusChangedEvent>(OnCriminalRecordStatusChanged);
    }

    private void OnCriminalRecordStatusChanged(NCCriminalRecordStatusChangedEvent ev)
    {
        var query = EntityQueryEnumerator<IdentityComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!Identity.Name(uid, EntityManager).Equals(ev.Name))
                continue;

            var sourceId = GetSourceId(uid);
            if (ev.Status != SecurityStatus.Wanted)
            {
                _overwatch.RemoveEntityAlert(sourceId);
                continue;
            }

            var description = string.IsNullOrWhiteSpace(ev.Reason)
                ? Loc.GetString("nc-overwatch-wanted-description", ("name", ev.Name))
                : Loc.GetString("nc-overwatch-wanted-description-with-reason", ("name", ev.Name), ("reason", ev.Reason));

            _overwatch.AddEntityAlert(
                uid,
                Loc.GetString("nc-overwatch-wanted-title"),
                description,
                sourceId);
        }
    }

    private string GetSourceId(EntityUid uid)
    {
        return $"criminal_record_{GetNetEntity(uid)}";
    }
}
