using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Cargo.Systems;
using Content.Server.Emp;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Throwing;
using Content.Shared.UserInterface;
using Content.Shared.VendingMachines;
using Content.Shared.Wall;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Content.Shared.Inventory;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Server._NC.Bank;
using Content.Server._NC.VendingMachines;  // NC edit: Profitable vending
using Content.Shared._NC.VendingMachines;  // NC edit: Profitable vending
using Content.Shared.Storage;  // NC edit: Partial vending machine restock


namespace Content.Server.VendingMachines
{
    public sealed class VendingMachineSystem : SharedVendingMachineSystem
    {
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly PricingSystem _pricing = default!;
        [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
        [Dependency] private readonly Content.Shared.Inventory.InventorySystem _inventory = default!;
        [Dependency] private readonly Content.Shared.Stacks.SharedStackSystem _stack = default!;
        [Dependency] private readonly BankSystem _bank = default!;
        [Dependency] private readonly MoneyCollectorSystem _moneyCollector = default!;

        private const float WallVendEjectDistanceFromWall = 1f;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<VendingMachineComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<VendingMachineComponent, BreakageEventArgs>(OnBreak);
            SubscribeLocalEvent<VendingMachineComponent, DamageChangedEvent>(OnDamageChanged);
            SubscribeLocalEvent<VendingMachineComponent, PriceCalculationEvent>(OnVendingPrice);

            SubscribeLocalEvent<VendingMachineComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);

            SubscribeLocalEvent<VendingMachineComponent, VendingMachineSelfDispenseEvent>(OnSelfDispense);

            SubscribeLocalEvent<VendingMachineComponent, RestockDoAfterEvent>(OnDoAfter);

            SubscribeLocalEvent<VendingMachineRestockComponent, PriceCalculationEvent>(OnPriceCalculation);
        }

        private void OnVendingPrice(EntityUid uid, VendingMachineComponent component, ref PriceCalculationEvent args)
        {
            var price = 0.0;

            foreach (var entry in component.Inventory.Values)
            {
                if (!PrototypeManager.TryIndex<EntityPrototype>(entry.ID, out var proto))
                {
                    Log.Error($"Unable to find entity prototype {entry.ID} on {ToPrettyString(uid)} vending.");
                    continue;
                }

                price += entry.Amount * _pricing.GetEstimatedPrice(proto);
            }

            args.Price += price;
        }

        protected override void OnMapInit(EntityUid uid, VendingMachineComponent component, MapInitEvent args)
        {
            base.OnMapInit(uid, component, args);

            if (HasComp<ApcPowerReceiverComponent>(uid))
            {
                TryUpdateVisualState((uid, component));
            }
        }

        private void OnActivatableUIOpenAttempt(EntityUid uid, VendingMachineComponent component, ActivatableUIOpenAttemptEvent args)
        {
            if (component.Broken)
                args.Cancel();
        }

        private void OnPowerChanged(EntityUid uid, VendingMachineComponent component, ref PowerChangedEvent args)
        {
            TryUpdateVisualState((uid, component));
        }

        private void OnBreak(EntityUid uid, VendingMachineComponent vendComponent, BreakageEventArgs eventArgs)
        {
            vendComponent.Broken = true;
            TryUpdateVisualState((uid, vendComponent));
        }

        private void OnDamageChanged(EntityUid uid, VendingMachineComponent component, DamageChangedEvent args)
        {
            if (!args.DamageIncreased && component.Broken)
            {
                component.Broken = false;
                TryUpdateVisualState((uid, component));
                return;
            }

            if (component.Broken || component.DispenseOnHitCoolingDown ||
                component.DispenseOnHitChance == null || args.DamageDelta == null)
                return;

            if (args.DamageIncreased && args.DamageDelta.GetTotal() >= component.DispenseOnHitThreshold &&
                _random.Prob(component.DispenseOnHitChance.Value))
            {
                if (component.DispenseOnHitCooldown != null)
                {
                    component.DispenseOnHitEnd = Timing.CurTime + component.DispenseOnHitCooldown.Value;
                }

                EjectRandom(uid, throwItem: true, forceEject: true, component);
            }
        }

        private void OnSelfDispense(EntityUid uid, VendingMachineComponent component, VendingMachineSelfDispenseEvent args)
        {
            if (args.Handled)
                return;

            args.Handled = true;
            EjectRandom(uid, throwItem: true, forceEject: false, component);
        }

        private void OnDoAfter(EntityUid uid, VendingMachineComponent component, DoAfterEvent args)
        {
            if (args.Handled || args.Cancelled || args.Args.Used == null)
                return;

            // NC edit start: Partial vending machine restock

            // Handle regular restock
            if (TryComp<VendingMachineRestockComponent>(args.Args.Used, out var restockComponent))
            {
                TryRestockInventory(uid, component);

                Popup.PopupEntity(Loc.GetString("vending-machine-restock-done", ("this", args.Args.Used), ("user", args.Args.User), ("target", uid)), args.Args.User, PopupType.Medium);

                Audio.PlayPvs(restockComponent.SoundRestockDone, uid, AudioParams.Default.WithVolume(-2f).WithVariation(0.2f));

                Del(args.Args.Used.Value);

                args.Handled = true;
                return;
            }

            // Handle partial restock
            if (TryComp<VendingMachineRestockPartialComponent>(args.Args.Used, out var partialRestockComponent))
            {
                TryPartialRestockInventory(uid, partialRestockComponent, component);

                Popup.PopupEntity(Loc.GetString("vending-machine-restock-done", ("this", args.Args.Used), ("user", args.Args.User), ("target", uid)), args.Args.User, PopupType.Medium);

                Audio.PlayPvs(partialRestockComponent.SoundRestockDone, uid, AudioParams.Default.WithVolume(-2f).WithVariation(0.2f));

                Del(args.Args.Used.Value);

                args.Handled = true;
                return;
            }

            Log.Error($"{ToPrettyString(args.Args.User)} tried to restock {ToPrettyString(uid)} with {ToPrettyString(args.Args.Used.Value)} which did not have a VendingMachineRestockComponent or VendingMachineRestockPartialComponent.");

            // NC edit end: Partial vending machine restock
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.CanShoot"/> property of the vending machine.
        /// </summary>
        public void SetShooting(EntityUid uid, bool canShoot, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.CanShoot = canShoot;
        }

        /// <summary>
        /// Sets the <see cref="VendingMachineComponent.Contraband"/> property of the vending machine.
        /// </summary>
        public void SetContraband(EntityUid uid, bool contraband, VendingMachineComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            component.Contraband = contraband;
            Dirty(uid, component);
        }

        /// <summary>
        /// Ejects a random item from the available stock. Will do nothing if the vending machine is empty.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="throwItem">Whether to throw the item in a random direction after dispensing it.</param>
        /// <param name="forceEject">Whether to skip the regular ejection checks and immediately dispense the item without animation.</param>
        /// <param name="vendComponent"></param>
        public void EjectRandom(EntityUid uid, bool throwItem, bool forceEject = false, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            var availableItems = GetAvailableInventory(uid, vendComponent);
            if (availableItems.Count <= 0)
                return;

            var item = _random.Pick(availableItems);

            if (forceEject)
            {
                vendComponent.NextItemToEject = item.ID;
                vendComponent.ThrowNextItem = throwItem;
                var entry = GetEntry(uid, item.ID, item.Type, vendComponent);
                if (entry != null)
                    entry.Amount--;
                EjectItem(uid, vendComponent, forceEject);
            }
            else
            {
                TryEjectVendorItem(uid, item.Type, item.ID, throwItem, user: null, vendComponent: vendComponent);
            }
        }

        protected override void EjectItem(EntityUid uid, VendingMachineComponent? vendComponent = null, bool forceEject = false)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            // No need to update the visual state because we never changed it during a forced eject
            if (!forceEject)
                TryUpdateVisualState((uid, vendComponent));

            if (string.IsNullOrEmpty(vendComponent.NextItemToEject))
            {
                vendComponent.ThrowNextItem = false;
                return;
            }

            // Default spawn coordinates
            var spawnCoordinates = Transform(uid).Coordinates;

            //Make sure the wallvends spawn outside of the wall.

            if (TryComp<WallMountComponent>(uid, out var wallMountComponent))
            {

                var offset = wallMountComponent.Direction.ToWorldVec() * WallVendEjectDistanceFromWall;
                spawnCoordinates = spawnCoordinates.Offset(offset);
            }

            var ent = Spawn(vendComponent.NextItemToEject, spawnCoordinates);

            if (vendComponent.ThrowNextItem)
            {
                var range = vendComponent.NonLimitedEjectRange;
                var direction = new Vector2(_random.NextFloat(-range, range), _random.NextFloat(-range, range));
                _throwingSystem.TryThrow(ent, direction, vendComponent.NonLimitedEjectForce);
            }

            vendComponent.NextItemToEject = null;
            vendComponent.ThrowNextItem = false;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var disabled = EntityQueryEnumerator<EmpDisabledComponent, VendingMachineComponent>();
            while (disabled.MoveNext(out var uid, out _, out var comp))
            {
                if (comp.NextEmpEject < _timing.CurTime)
                {
                    EjectRandom(uid, true, false, comp);
                    comp.NextEmpEject += (5 * comp.EjectDelay);
                }
            }
        }

        public void TryRestockInventory(EntityUid uid, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            RestockInventoryFromPrototype(uid, vendComponent);

            Dirty(uid, vendComponent);
            TryUpdateVisualState((uid, vendComponent));
        }

        // NC edit start: Partial vending machine restock

        /// <summary>
        /// Restocks a vending machine with the contents from a partial restock component.
        /// Adds items to existing inventory and adds new items if they weren't present before.
        /// Uses pricing from restock component for new items, existing items keep their price.
        /// </summary>
        public void TryPartialRestockInventory(EntityUid uid, VendingMachineRestockPartialComponent restockComponent, VendingMachineComponent? vendComponent = null)
        {
            if (!Resolve(uid, ref vendComponent))
                return;

            // Process each entry in the restock component
            foreach (var entry in restockComponent.Contents)
            {
                if (entry.PrototypeId == null)
                    continue;

                var itemId = entry.PrototypeId.Value;
                var amount = (int) entry.GetAmount(_random);

                // Add to regular inventory (partial restocks only affect regular inventory)
                if (vendComponent.Inventory.TryGetValue(itemId, out var existingEntry))
                {
                    // Item already exists, add to its amount, keep existing price
                    existingEntry.Amount += (uint) amount;
                }
                else
                {
                    // Item doesn't exist, add it as new entry with price from restock component
                    vendComponent.Inventory.Add(itemId, new VendingMachineInventoryEntry(InventoryType.Regular, itemId, (uint) amount, entry.Price));
                }
            }

            Dirty(uid, vendComponent);
            TryUpdateVisualState((uid, vendComponent));
        }
        // NC edit end: Partial vending machine restock

        private void OnPriceCalculation(EntityUid uid, VendingMachineRestockComponent component, ref PriceCalculationEvent args)
        {
            List<double> priceSets = new();

            // Find the most expensive inventory and use that as the highest price.
            foreach (var vendingInventory in component.CanRestock)
            {
                double total = 0;

                if (PrototypeManager.TryIndex(vendingInventory, out VendingMachineInventoryPrototype? inventoryPrototype))
                {
                    foreach (var (item, amount) in inventoryPrototype.StartingInventory)
                    {
                        if (PrototypeManager.TryIndex(item, out EntityPrototype? entity))
                            total += _pricing.GetEstimatedPrice(entity) * amount;
                    }
                }

                priceSets.Add(total);
            }

            args.Price += priceSets.Max();
        }
        public override async void AuthorizedVend(EntityUid uid, EntityUid sender, InventoryType type, string itemId, VendingMachineComponent component)
        {
            var entry = GetEntry(uid, itemId, type, component);
            if (entry == null)
                return;

            if (entry.Price > 0)
            {
                if (!await _bank.TryBankWithdraw(sender, (int) entry.Price))
                {
                    Popup.PopupEntity(Loc.GetString("vending-machine-component-try-eject-insufficient-funds"), uid, sender, PopupType.Medium);
                    Deny((uid, component), sender);
                    return;
                }

                // NC edit start: Profitable vending - collect money if the component exists
                if (TryComp<MoneyCollectorComponent>(uid, out var collector))
                {
                    _moneyCollector.CollectMoney(uid, collector, entry.Price);
                }
                // NC edit end: Profitable vending
            }

            base.AuthorizedVend(uid, sender, type, itemId, component);
        }
    }
}

