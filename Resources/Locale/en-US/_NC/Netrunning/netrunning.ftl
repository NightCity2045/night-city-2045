# Netrunning

# META / NET server console
netrunning-ui-tab-overview = Overview
netrunning-ui-tab-topology = Topology
netrunning-ui-label-load = Load: { $used }/{ $max }
netrunning-ui-label-modules = Modules: { $count }/{ $limit }
netrunning-ui-label-nodes = Nodes: { $count }
netrunning-ui-daemon-installed = Daemon: installed
netrunning-ui-daemon-empty = Daemon: empty
netrunning-ui-footer-admin-ready = Linked deck detected. Local admin session can be opened.
netrunning-ui-footer-admin-missing-deck = Link a deck to this server before opening an admin session.
netrunning-ui-admin-root-owned = Root already owned
netrunning-ui-admin-local-active = Local access active
netrunning-ui-admin-request = Acquire administrator rights
netrunning-ui-module-desc = [color=#7fd3ff]{ $name }[/color]\nLoad: { $load }\nPrice: { $price }\n\n{ $desc }
netrunning-ui-module-select-hint = [color=#768692]Select a room package and a free port to start construction.[/color]
netrunning-ui-port-occupied = [Occupied] Port { $dir }
netrunning-ui-port-free = Port { $dir }
netrunning-ui-topology-locked = Placement locked. Open a local admin session or acquire ROOT first.
netrunning-ui-topology-empty = This local network has no nodes available for manual placement yet.
netrunning-ui-topology-ready = Select a node from the list, then click a free tile on the topology map.
netrunning-ui-topology-none = [color=#768692]No node selected.[/color]
netrunning-ui-topology-access-ready = Placement available
netrunning-ui-topology-access-locked = Placement locked
netrunning-ui-topology-selected = [color=#ffd27a]Selected:[/color] { $name }\nClass: { $class }\nPosition: { $x }, { $y }\n{ $access }

# META / NET node console
netrunning-node-title = Network node
netrunning-node-title-name = NODE://{ $name }
netrunning-node-kind = TYPE: { $kind }
netrunning-node-kind-camera-group = TYPE: { $kind } / CAMERAS IN GROUP: { $count }
netrunning-node-viewport-camera = Live camera feed. Displays the active physical node.
netrunning-node-viewport-device = Live view of the physical device. Centered on the node, observation radius near 4 tiles.
netrunning-node-shard-kind-daemon = DAEMON
netrunning-node-shard-kind-script = SCRIPT
netrunning-node-shard-ready = Select a script from the deck and release it into the current node.
netrunning-node-shard-empty = No META scripts are available in the deck.
netrunning-node-shard-no-deck = No deck is linked to the avatar. Scripts are unavailable.
netrunning-node-kind-door = AIRLOCK
netrunning-node-kind-camera = CAMERA NODE
netrunning-node-kind-gate = DATA GATE
netrunning-node-kind-device = DEVICE

# META / NET server runtime
netrunning-server-provider-none = LCP: none
netrunning-server-provider = LCP: { $name }
netrunning-server-access-none = ACCESS: NO SESSION
netrunning-server-access-root = ACCESS: ROOT / PERSISTENT
netrunning-server-access-local = ACCESS: LOCAL ADMIN
netrunning-server-access-linked = ACCESS: DECK LINKED, SESSION CLOSED
netrunning-server-title = SERVER://{ $name }
netrunning-class-node = NODE
netrunning-class-cameras = CAMERAS
netrunning-class-ice = ICE
netrunning-class-door = DOOR
netrunning-class-camera = CAMERA
netrunning-class-light = LIGHT
netrunning-class-device = DEVICE
netrunning-class-power = POWER
netrunning-class-unknown = UNKNOWN
netrunning-popup-compile-error = COMPILE ERROR: { $error }
netrunning-popup-daemon-slot-occupied = Defensive daemon slot is already occupied.
netrunning-popup-daemon-installed = Defensive META shard installed into the server.
netrunning-verb-open-server-console = Open server console
netrunning-verb-eject-defensive-shard = Eject defensive shard
netrunning-popup-daemon-ejected = Defensive META shard ejected.
netrunning-popup-link-deck-first = Link your deck to this server first.
netrunning-popup-root-already-owned = Root access is already acquired and stored in the deck.
netrunning-popup-local-admin-active = Local admin session is already active.
netrunning-popup-local-admin-opened = Local admin session opened. Topology unlocked.
netrunning-popup-node-not-owned = ERROR: selected node no longer belongs to this local network.
netrunning-popup-no-deck = ERROR: netrunner deck not found.
netrunning-popup-shard-missing = ERROR: shard unavailable.
netrunning-popup-topology-admin-required = ERROR: topology rebuild requires local admin or ROOT.
netrunning-popup-topology-tile-outside = ERROR: selected tile is outside the deployed local network map.
netrunning-popup-topology-tile-occupied = ERROR: tile is already occupied by another node or defense.
netrunning-popup-no-digital-grid = ERROR: server has no initialized digital grid.
netrunning-popup-module-limit = ERROR: server module limit reached ({ $limit }).
netrunning-popup-server-overload = ERROR: server overload ({ $load }/{ $max }).
netrunning-popup-port-unavailable = ERROR: expansion port unavailable or already occupied.
netrunning-popup-module-attached = { $module } stitched to port.
netrunning-popup-scan-no-power-line = SCAN ERROR: server sees no direct logical power line.
netrunning-popup-scan-complete = SCAN COMPLETE: displayed nodes: { $count }.
netrunning-popup-scan-empty = SCAN COMPLETE: no network nodes found in this LCP segment.

# META / Cyberdeck terminal
netrunning-cyberdeck-server-offline = Server: offline
netrunning-cyberdeck-server-load = Server: { $used }/{ $max } load
netrunning-cyberdeck-connection-offline = Connection: OFFLINE (AR overlay required)
netrunning-cyberdeck-link-linked = Link: linked
netrunning-cyberdeck-link-ready = Link: ready
netrunning-cyberdeck-construction-moved = Module construction moved to the physical server console.
netrunning-cyberdeck-run-requires-ar = Requires AR glasses to execute remote scripts.
netrunning-cyberdeck-run-defensive-install = Defensive daemon shards must be installed in a protected node.
netrunning-server-console-title = Local network console
netrunning-server-console-subtitle = LOCAL NETWORK ADMINISTRATIVE CONSOLE
netrunning-server-console-unknown = SERVER://UNKNOWN
netrunning-server-devices-title = LCP SEGMENT DEVICES
netrunning-server-devices-hint = The local network only exposes nodes on the same direct logical power source.
netrunning-server-refresh = Refresh topology
netrunning-server-ports-title = TOPOLOGY PORTS
netrunning-server-ports-hint = Free ports accept new rooms and local network clusters.
netrunning-server-module-title = MODULE ASSEMBLY
netrunning-server-module-hint = Select a room package and stitch it into a compatible expansion port.
netrunning-server-construct = Build module
netrunning-server-placement-title = NODE PLACEMENT
netrunning-server-map-title = TOPOLOGY MAP
netrunning-server-map-hint = Clicking a free tile instantly moves the selected node. Requires local admin or ROOT.
netrunning-node-unknown-title = NODE://UNKNOWN
netrunning-node-unknown-kind = TYPE: UNKNOWN
netrunning-node-viewport-title = NODE VIEWPORT
netrunning-node-viewport-default = Direct visual channel through the selected node.
netrunning-node-scripts-title = DECK SCRIPTS
netrunning-node-scripts-default = Connect a deck to access combat scripts.
netrunning-node-execute = Run selected script
netrunning-node-control-title = NODE CONTROL
netrunning-node-toggle = Toggle
netrunning-node-rescan = Rescan

