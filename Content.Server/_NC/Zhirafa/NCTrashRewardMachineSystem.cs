using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Power.Components;
using Content.Server._NC.Trade;
using Content.Shared._NC.Zhirafa.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;

namespace Content.Server._NC.Zhirafa;

/// <summary>
/// Processes hand-fed trash and trash-bag contents for the Zhirafa janitor economy loop.
/// </summary>
public sealed class NCTrashRewardMachineSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly NcStoreCurrencySystem _currency = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCTrashRewardMachineComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<NCTrashRewardMachineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp<ApcPowerReceiverComponent>(ent, out var power) && !power.Powered)
        {
            _popup.PopupEntity(Loc.GetString("nc-trash-reward-machine-no-power"), ent, args.User);
            return;
        }

        if (TryComp<AccessReaderComponent>(ent, out var reader) && !_access.IsAllowed(args.User, ent, reader))
        {
            _popup.PopupEntity(Loc.GetString("nc-trash-reward-machine-access-denied"), ent, args.User);
            return;
        }

        if (_whitelist.IsWhitelistPass(ent.Comp.TrashContainerWhitelist, args.Used) &&
            TryComp<StorageComponent>(args.Used, out var storage))
        {
            ProcessTrashContainer(ent, args.User, args.Used, storage);
            return;
        }

        if (!_whitelist.IsWhitelistPass(ent.Comp.TrashWhitelist, args.Used))
        {
            _popup.PopupEntity(Loc.GetString("nc-trash-reward-machine-invalid-item"), ent, args.User);
            return;
        }

        ProcessItems(ent, args.User, args.Used, new[] { args.Used });
    }

    private void ProcessTrashContainer(
        Entity<NCTrashRewardMachineComponent> machine,
        EntityUid user,
        EntityUid container,
        StorageComponent storage)
    {
        // Copy the list before deleting entities because container membership changes during deletion.
        var trash = storage.Container.ContainedEntities
            .Where(item => _whitelist.IsWhitelistPass(machine.Comp.TrashWhitelist, item))
            .ToArray();

        if (trash.Length == 0)
        {
            _popup.PopupEntity(Loc.GetString("nc-trash-reward-machine-empty-container"), machine, user);
            return;
        }

        ProcessItems(machine, user, container, trash);
    }

    private void ProcessItems(
        Entity<NCTrashRewardMachineComponent> machine,
        EntityUid user,
        EntityUid source,
        IReadOnlyCollection<EntityUid> trash)
    {
        var reward = Math.Max(0, machine.Comp.RewardPerItem) * trash.Count;
        if (reward <= 0)
            return;

        foreach (var item in trash)
            QueueDel(item);

        _currency.GiveCurrency(user, machine.Comp.Currency, reward);
        _audio.PlayPvs(machine.Comp.ProcessSound, machine);
        _popup.PopupEntity(
            Loc.GetString("nc-trash-reward-machine-success", ("count", trash.Count), ("amount", reward)),
            machine,
            user,
            PopupType.Medium);

        _adminLog.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(user):player} recycled {trash.Count} trash entities from {ToPrettyString(source)} at {ToPrettyString(machine)} and received {reward} {machine.Comp.Currency}.");
    }
}
