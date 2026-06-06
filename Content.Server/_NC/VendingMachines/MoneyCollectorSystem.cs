using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Server._NC.Bank;
using Content.Shared._NC.Bank;
using Content.Shared._NC.Bank.Components;
using Content.Shared._NC.VendingMachines;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;

namespace Content.Server._NC.VendingMachines
{
    public sealed class MoneyCollectorSystem : EntitySystem
    {
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;
        [Dependency] private readonly PopupSystem _popup = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly BankSystem _bank = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<MoneyCollectorComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<MoneyCollectorComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        }

        private void OnInteractUsing(EntityUid uid, MoneyCollectorComponent component, InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            // Return if held item doesn't have a PDA component
            if (!HasComp<PdaComponent>(args.Used))
                return;

            // Check if user has required access
            if (!HasRequiredAccess(args.User, component.Access))
            {
                _popup.PopupEntity(Loc.GetString("money-collector-insufficient-access"), uid, args.User);
                return;
            }

            // Check if there's money to extract
            if (component.CollectedMoney <= 0)
            {
                _popup.PopupEntity(Loc.GetString("money-collector-no-money"), uid, args.User);
                return;
            }

            // Extract money to bank account
            TryExtractMoney(uid, args.User, component);
            args.Handled = true;
        }

        private void OnGetVerbs(EntityUid uid, MoneyCollectorComponent component, GetVerbsEvent<Verb> args)
        {
            if (args.User == uid || !HasRequiredAccess(args.User, component.Access))
                return;

            // Return if held item doesn't have a PDA component
            if (!HasComp<PdaComponent>(args.Using))
                return;

            // Add extract funds verb
            args.Verbs.Add(new Verb()
            {
                Text = Loc.GetString("money-collector-extract-verb"),
                Category = VerbCategory.Interaction,
                Act = () =>
                {
                    TryExtractMoney(uid, args.User, component);
                }
            });
        }

        public void CollectMoney(EntityUid uid, MoneyCollectorComponent component, uint amount)
        {
            var collectAmount = 0u;
            var depositAmount = amount;

            // Calculate collection and deposit amounts based on percentage if present.
            if (component.CollectPercentage > 0.0f)
            {
                collectAmount = (uint) (amount * component.CollectPercentage);
                depositAmount = amount - collectAmount;
            }
            else if (component.BankAccount == SectorBankAccount.Invalid)
            {
                collectAmount = amount;
            }

            // If bank account is set, deposit money straight to it.
            if (depositAmount > 0 && component.BankAccount != SectorBankAccount.Invalid)
            {
                var station = GetStation(uid);
                if (station == null) return;
                _bank.TryFactionDeposit(station.Value, component.BankAccount, (int) depositAmount);
            }

            // If we need to collect money, collect it.
            if (collectAmount > 0)
            {
                component.CollectedMoney += collectAmount;
                Dirty(uid, component);
            }
        }

        private bool HasRequiredAccess(EntityUid user, List<List<string>> requiredAccess)
        {
            // Get user's access tags
            var userAccessTags = _accessReader.FindAccessTags(user);
            if (userAccessTags == null)
                return false;

            // Check if user has any of the required access tags
            foreach (var accessList in requiredAccess)
            {
                if (accessList.Any(access => userAccessTags.Contains(access)))
                    return true;
            }

            return false;
        }

        private async void TryExtractMoney(EntityUid uid, EntityUid user, MoneyCollectorComponent component)
        {
            var amountToExtract = component.CollectedMoney;

            if (await _bank.TryBankDeposit(user, (int) amountToExtract))
            {
                // Update collected money
                component.CollectedMoney = 0;
                Dirty(uid, component);

                // Play sound and show popup
                _audio.PlayPvs(component.ExtractSound, uid);
                _popup.PopupEntity(Loc.GetString("money-collector-extracted", ("amount", amountToExtract)), uid, user);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("money-collector-error"), uid, user);
            }
        }

        private EntityUid? GetStation(EntityUid console)
        {
            var station = _stationSystem.GetOwningStation(console);
            if (station == null)
                station = _stationSystem.GetStationsSet().FirstOrDefault();

            if (station == null)
            {
                var queryBank = EntityQueryEnumerator<StationBankComponent>();
                if (queryBank.MoveNext(out var bankUid, out _))
                {
                    station = bankUid;
                }
            }

            return station;
        }
    }
}
