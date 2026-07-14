# NC — CitiNet Radio Channel Localization (EN)
chat-radio-ncpd = NCPD
chat-radio-traumateam = Trauma Team
chat-radio-maxtac = MAX-TAC
chat-radio-militech = Militech
chat-radio-biotechnica = Biotechnica

# CitiNet BBS
citinet-bbs-channel-public = City Net
citinet-bbs-channel-afterlife = Afterlife
citinet-bbs-channel-maelstrom = Maelstrom
citinet-bbs-channel-ncpd-dispatch = NCPD | Dispatch
citinet-bbs-channel-ncpd-detectives = NCPD | Detectives
citinet-bbs-channel-ncpd-command = NCPD | Command
citinet-bbs-channel-maxtac-tactical = MaxTac | Tactical
citinet-bbs-channel-maxtac-command = MaxTac | Command
citinet-bbs-channel-biotech-general = Biotechnica | General
citinet-bbs-channel-biotech-operatives = Biotech | Operatives
citinet-bbs-channel-biotech-command = Biotech | Command
citinet-bbs-channel-trauma-general = Trauma Team
citinet-bbs-channel-trauma-operatives = Field Operatives
citinet-bbs-channel-trauma-comms = Corporate Comms

# CitiNet Cartridge UI
citinet-cartridge-name = CitiNet
citinet-cartridge-description = Night City communication network terminal.

citinet-delivery-cartridge-name = "QuickPath Navi"

citinet-tab-calls = Calls
citinet-tab-group = Tactical
citinet-tab-bbs = BBS

citinet-call-initiate = Call
citinet-call-accept = Accept
citinet-call-decline = Decline
citinet-call-hangup = Hang up
citinet-call-incoming = Incoming call from {$caller}...
citinet-call-ringing = Calling {$target}...
citinet-call-flatline = FLATLINE — {$name} is down
citinet-flatline-dead = FLATLINE — {$name} KIA
citinet-flatline-critical = CRITICAL — {$name} is down
citinet-call-active = Connected — {$target}
citinet-call-no-relay = [color=red]No signal — CitiNet Relay offline[/color]
citinet-call-ping-location = {$sender} sent location: {$coords}

citinet-group-create = Create bridge
citinet-group-invite = Invite
citinet-group-leave = Leave
citinet-group-participants = Participants: {$count}/{$max}
citinet-group-flatline = [color=red][SYSTEM]: Agent {$name} disconnected. Status: CRITICAL (FLATLINE)[/color]

citinet-bbs-join = Join
citinet-bbs-leave = Leave
citinet-bbs-send = Send
citinet-bbs-password-required = Password required
citinet-bbs-enter-password = Enter access code:
citinet-bbs-wrong-password = Access denied — wrong code
citinet-bbs-no-relay = [color=red]Channel unavailable — CitiNet offline[/color]
citinet-bbs-anonymous = Anonymous
citinet-bbs-invite-received = >> You have been granted access to {$channel} by {$inviter}
citinet-bbs-invite-sent = >> Agent {$target} has been invited to {$channel}
citinet-p2p-game-chat = [CitiNet/Direct] {$sender}: {$message}
citinet-group-game-chat = [CitiNet/Bridge] {$sender}: {$message}
citinet-bbs-game-chat = [CitiNet/{$channel}] {$sender}: {$message}

# BurnerChip
citinet-burner-chip-name = Burner chip
citinet-burner-chip-description = A cheap black market chip. Provides a temporary anonymous ID that can't be traced by NCPD databases.
citinet-burner-chip-inserted = Burner chip activated. Temporary ID: {$id}
citinet-burner-chip-removed = Burner chip deactivated. Original ID restored.
citinet-burner-chip-used = This chip has already been used.
citinet-burner-chip-destroyed = Burner chip destroyed.

# CitiNet Relay
citinet-relay-name = CitiNet Relay
citinet-relay-description = A local city network relay server. Routes civilian communications — calls and BBS channels. Requires power.

citinet-sender-system = SYSTEM
citinet-sender-flatline = FLATLINE
citinet-call-busy = >> TARGET LINE BUSY. TRY AGAIN LATER.
citinet-call-connection-lost = >> CONNECTION LOST. RELAY SIGNAL DROPPED.

# Store Categories
citinet-store-category-tools = Tools
citinet-store-category-medical = Medical Supplies
citinet-store-category-equipment = Equipment
citinet-store-category-style = Style & Fashion
citinet-store-category-botany = Botany
citinet-store-category-workwear = Workwear
citinet-store-category-weapons = Weapons
citinet-store-category-armor = Protection
citinet-store-category-ammo = Ammunition
citinet-store-category-cyberware = Cyberware
citinet-store-category-industrial = Industrial Equipment
citinet-store-category-chemistry = Chemistry
citinet-store-category-seeds = Seeds

citinet-store-cart-empty = Your cart is empty.
citinet-store-cart-added = Added { $amount } item(s) to cart.
citinet-store-cart-invalid = The cart contains unavailable listings. Clear it and try again.
citinet-store-stock-insufficient = City stock is insufficient for that cart.
citinet-store-corporate-account-unavailable = Corporate account route is unavailable.
citinet-store-corporate-data-insufficient = Not enough corporate data on the faction account.
citinet-store-corporate-funds-insufficient = Not enough eddies on the corporate account.
citinet-store-personal-funds-insufficient = Not enough eddies on your account.
citinet-store-delivery-failed-refunded = Delivery failed: { $reason } Funds and data were refunded.

nc-delivery-map-marker = Package ready: { $location }
nc-delivery-map-marker-pending = Package: { $location } ({ $seconds }s)
citinet-delivery-no-drop-points = No delivery points are available. Try again later.
citinet-delivery-no-corporate-zones = No corporate pickup zones are available. Try again later.
citinet-delivery-packaging-error = Packaging failed. Contact technical support.
citinet-delivery-corporate-dropbox-ready = Cargo crate ({ $count } item(s)) delivered to { $location }. PIN: { $pin }. Storage expires in 15 minutes. Navigation chip issued.
citinet-delivery-dead-drop-ready = Order ({ $count } item(s)) was left at { $location }. Navigation chip issued.
citinet-delivery-corporate-zone-scheduled = Corporate order ({ $count } item(s)) authorized. Pickup zone: { $location }. ETA: { $minutes } minute(s). Navigation chip issued.
citinet-delivery-keypad-unlocked = Code accepted. Lock opened.
citinet-delivery-keypad-access-granted = Access granted. Retrieve your cargo.
citinet-delivery-keypad-wrong-pin = Incorrect code!
citinet-delivery-chip-examine-ready = Pickup ready: { $location }.
citinet-delivery-chip-examine-pending = Pickup route: { $location }. ETA: { $seconds } seconds.

citinet-store-ui-balance = BALANCE: { $balance } ED
citinet-store-ui-corp-funds = CORP FUNDS: { $balance } ED
citinet-store-ui-corp-data = DATA: { $data }
citinet-store-ui-corp-status-no-account = LINK STATUS: NO CORPORATE ACCOUNT ROUTE
citinet-store-ui-corp-status-dropbox = LINK STATUS: SECURE DROPBOX ROUTE
citinet-store-ui-corp-status-zone = LINK STATUS: OFF-CITY PICKUP ZONE ROUTE
citinet-store-ui-stock = STOCK: { $count }
citinet-store-ui-stock-depleted = STOCK: DEPLETED
citinet-store-ui-sold-out = SOLD OUT
citinet-store-ui-add-to-cart = ADD
citinet-store-ui-price = { $price } ED + { $data } DATA
citinet-store-ui-price-money = { $price } ED
citinet-store-ui-cart-summary = CART: { $price } ED + { $data } DATA
citinet-store-ui-cart-summary-money = CART: { $price } ED
citinet-store-ui-cart-clear = CLEAR
citinet-store-ui-cart-checkout = CHECKOUT
citinet-store-ui-cart-empty = Cart is empty.
citinet-store-ui-cart-line = { $name } x{ $amount }
citinet-store-ui-cart-line-price = { $price } ED + { $data } DATA
citinet-store-ui-cart-line-price-money = { $price } ED

# Store Items
citinet-pill-canister-desc = 10 pills, 10 units each.

# NetSites
citinet-site-name-home = CitiNet Home
citinet-site-name-comm = CitiNet Comm
citinet-site-name-flatline = Flatline Database
citinet-site-name-astrozon = Astrozon
citinet-site-name-night-market = Night Market
citinet-site-name-ncpd-records = NCPD Central Database
citinet-site-name-trauma-monitor = Trauma Care Monitor

# Map
citinet-map-beacon-default = New POI
citinet-map-sector-default = New Sector
ent-CitiNetMapCartridge = CitiNet Map cartridge
ent-CitiNetMapCartridge-desc = A program for layered tactical map visualization.
map-program-name = City Map
