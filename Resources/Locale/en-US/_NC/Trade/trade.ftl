ent-Trademachine = trade terminal
ent-Trademachine-desc = An automated terminal for urban trade operations.
ent-TrademachineCity = City buyback terminal
ent-TrademachineCity-desc = This terminal only accepts approved items from citizens and pays eurodollars for them. Nothing can be purchased here.
ent-TrademachineTickets = caravan trade terminal
ent-TrademachineTickets-desc = A trade terminal that accepts caravan vouchers in exchange for equipment.

nc-store-category-city-melee = Melee
nc-store-category-city-medical = Medicine
nc-store-category-city-apparel = Gear
nc-store-category-city-clothing = Clothing
nc-store-category-city-equipment = Equipment
nc-store-category-city-techstyles = Style and fashion
nc-store-category-city-weapons-sell = Weapon buyback
nc-store-category-city-ammo-sell = Ammunition buyback
nc-store-category-city-crafting-sell = Parts buyback
nc-store-category-city-clothing-extra-sell = Specialized gear buyback
nc-store-category-city-chemistry-sell = Chemical buyback
nc-store-category-city-devices-sell = Device buyback

ent-NCPrizeTicket = caravan voucher
ent-NCPrizeTicket-desc = A voucher used at a special caravan trade terminal. It can be exchanged for powerful equipment if you have enough vouchers.
ent-NCPrizeTicket1 = { ent-NCPrizeTicket }
ent-NCPrizeTicket1-suffix = 1
ent-NCPrizeTicket1-desc = { ent-NCPrizeTicket-desc }
ent-NCPrizeTicket10 = { ent-NCPrizeTicket }
ent-NCPrizeTicket10-suffix = 10
ent-NCPrizeTicket10-desc = { ent-NCPrizeTicket-desc }
ent-NCPrizeTicket30 = { ent-NCPrizeTicket }
ent-NCPrizeTicket30-suffix = 30
ent-NCPrizeTicket30-desc = { ent-NCPrizeTicket-desc }
ent-NCPrizeTicket60 = { ent-NCPrizeTicket }
ent-NCPrizeTicket60-suffix = 60
ent-NCPrizeTicket60-desc = { ent-NCPrizeTicket-desc }

nc-store-window-title = Trade Terminal
nc-store-select-category = Select a category
nc-store-search-placeholder = Search items...
nc-store-footer-balance = Balance:
nc-store-tab-buy = Buy
nc-store-tab-sell = Sell
nc-store-tab-contracts = Contracts
nc-store-cat-ready-short = Ready
nc-store-cat-crate-short = In crate
nc-store-cat-ready-full = Ready to sell
nc-store-cat-crate-full = Ready to sell (in crate)
nc-store-category-fallback = Miscellaneous
nc-store-mass-sell-button = Sell crate contents
nc-store-mass-sell-tooltip = Quickly sell everything inside a crate.
    Requirements:
    • The crate must be closed
    • You must be pulling the crate
nc-store-mass-sell-tooltip-with-reward = { nc-store-mass-sell-tooltip }

    Estimated value: { $reward }
nc-store-only-mass-sell = This item can only be sold in bulk through a closed crate.
nc-store-show-more = Show more ({ $count })
nc-store-prompt-select-category = Please select a category on the left.
nc-store-empty-search = No items match your search.
nc-store-empty-category-search = No items in this category match your search.
nc-store-search-results-buy = Search results (Buy): { $count }
nc-store-search-results-sell = Search results (Sell): { $count }
nc-store-no-stock = Out of stock
nc-store-buying-finished = Purchase limit reached
nc-store-remaining = Remaining: { $count }
nc-store-will-buy = Wanted: { $count }
nc-store-owned = You have: { $count }
nc-store-no-access = Access denied
nc-store-contracts-empty = There are no active contracts. Check back later.
nc-store-difficulty-easy = Easy
nc-store-difficulty-medium = Medium
nc-store-difficulty-hard = Hard
nc-store-contract-title = Contract ({ $difficulty })
nc-store-contract-badge-single = One-time
nc-store-contract-badge-single-tooltip =
    This contract can only be completed once per shift.
    It disappears from the list after completion.
nc-store-contract-goals-header = Order objectives:
nc-store-contract-reward-header = Reward:
nc-store-contract-items-header = Items:
nc-store-contract-action-claim = Complete contract
nc-store-contract-action-claim-progress = Submit partial delivery ({ $progress }/{ $required })
nc-store-contract-action-can-claim = Ready to submit
nc-store-contract-action-not-done = Incomplete
nc-store-contract-claim-tooltip-single = Complete this one-time contract and receive the full reward.
nc-store-contract-claim-tooltip-repeatable = Submit the current contract progress and receive the reward.
nc-store-contract-claim-tooltip-not-done = The contract requirements have not been met. Not enough items.
nc-store-contract-completed = Contract completed successfully!
nc-store-contract-goal-line = { $item }: { $count }
nc-store-contract-progress-line = Progress: { $progress } of { $required }
nc-store-currency-format = { $amount } { $currency }
nc-store-contract-title-pretty = Contract: { $difficulty } — { $goal }
nc-store-contract-title-pretty-nogoal = Contract: { $difficulty }

nc-store-contract-desc-default = Fulfill the contract requirements and collect the reward.
nc-store-contract-desc-generated = Required: { $goals }

nc-store-contract-goal-inline = { $item } ×{ $count }

nc-store-unknown-item = ???

nc-store-proto-tooltip-name-only = { $name }
nc-store-proto-tooltip = { $name }
    { $desc }

nc-store-contract-reward-none = No reward specified
nc-store-contract-reward-item-line = { $item } ×{ $count }

nc-store-contract-badge-completed = COMPLETED
nc-store-contract-badge-completed-tooltip = The contract is complete and the reward can be claimed.
