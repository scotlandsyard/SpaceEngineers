# Sacrificial Stockpile Manager

A client-side Pulsar plugin for inventory management and auto-restocking. It takes ideas from inventory scripts such as Isy's Inventory Manager, Mamba Inventory Manager, GOAT Sorter and iBex, but everything is set up in an in-game menu instead of block names or Custom Data:

- Shows the inventory of every ship and station you own, including ones that aren't loaded right now (as they were when last in range).
- Sorts items into the storage blocks you choose (by category or by single item).
- Keeps any block between a minimum and a maximum of an item: lockers, reactors, generators, turrets, refineries.
- Keeps grid-wide quotas by queuing the shortfall in assemblers (autocraft).
- Shows status pages on LCD screens.

## Opening the menu

- Type `/ssm` in chat, or
- Drag any block with an inventory or a screen onto a toolbar and pick **Stockpile Manager**. The menu opens on that block.

The same blocks also get the toolbar actions **Stockpile Manager: Sort now** and **Stockpile Manager: Unload docked ships**, and the chat has `/ssm sort` and `/ssm unload` (they act on the grid you're on or nearest to).

| Control | What it does |
|---|---|
| Grid | Your ships and stations: loaded ones first, then the rest with when they were last seen |
| View | All grids, Overview, Items & quotas, Blocks, Block settings, Block type limits, Production, Production details, Refinery priority, Displays (LCD), Log, Help |

The window closes with the X in its corner or with Esc. Tables update every half second. Click a column header to sort.

## Views

| View | What it shows | Buttons |
|---|---|---|
| All grids | Every grid: last sync (Live when loaded), distance, storage used, storage size, fill, kinds of item; totals under the table | Open grid, GPS marker, Remove from list, Help |
| Overview | Grid settings (double-click to switch), status, storage used / size / free, item totals, warnings, recent actions | Change setting, Sort now, Unload docked ships, GPS marker |
| Items & quotas | Every item on the grid with its quota, maximum, queued amount and autocraft / disassembly state | Set quota, Set maximum, Clear quota, Autocraft on/off; Find box |
| Blocks | Every block with an inventory: role, fill %, used / size in litres, settings | Edit block, Turn on / off, Rescan, Help |
| Block settings | One block: role, fill priority (0-9), Accepts checkboxes, and per item what's there and its min/max (including limits from its type) | Set minimum, Set maximum, Clear limits, Back; Accept item |
| Block type limits | Minimums and maximums for every block of a type on the grid: reactors, O2/H2 generators, turrets and guns, cockpits and seats, ship tools, gas tanks, refineries, assemblers. Shows how many blocks it covers and how many are below or above | Set minimum, Set maximum, Clear limits, Help |
| Production | Assemblers, refineries, reactors, generators: assembler mode, state, and queue or contents | Details, Edit block, Turn on / off, Assembler mode |
| Production details | One assembler: its queue (with progress) and every material the queue needs, how much the grid has and how much is missing, and whether it blocks the current item. One refinery: input in refining order, and output | Remove from queue, Turn on / off, Edit block, Back |
| Refinery priority | Every refinable ore: its priority, amount in storage and in refineries, and which refineries are working on it | Raise priority, Lower priority, No priority, Help |
| Displays (LCD) | Every screen on the grid and the page it shows | Show page, Stop showing, Rescan, Help |
| Log | What the plugin did on this grid, newest first | Clear log, Rescan, Help |

**Storage** in All grids, the Overview and the LCD Overview page means blocks acting as Storage or Stock (cargo containers unless you change their role), not counting docked ships. Machines, cockpits and tools are left out so they don't inflate the totals.

**Remove from list** is for grids that were deleted or destroyed. It removes the grid and its settings (block settings, quotas, LCD assignments; screens elsewhere that showed it go back to their own grid). A loaded grid can't be removed. A removed grid that still exists comes back, with default settings, the next time it's in range.

In Items & quotas and Block settings, pick an item by selecting its row or from the list under the table, type an amount (`500`, `2,500`, `2.5k`, `1.2M`) and press the button. A blank amount clears a minimum or maximum.

## Grid settings

Set in the Overview. All are off for a new grid except Clean production blocks, and nothing moves until Automation is on.

| Setting | What it does |
|---|---|
| Automation | Apply minimums, maximums, sorting and intake draining. While off, nothing is moved and the menu only shows what would need doing. |
| Autocraft | Queue the shortfall of quotas in the grid's assemblers |
| Clean production blocks | **On by default.** Move what refineries and assemblers made into storage, and take anything out of an assembler's input that its queue doesn't need (for example components a modded blueprint pulled in, which can fill the input until nothing assembles). While an assembler disassembles, the parts are moved out and the items waiting to be taken apart are left alone. Cooperating assemblers keep what any queue on the grid needs |
| Survival kits autocraft | Let autocraft use survival kits too |
| Disassemble surplus | Disassemble what's above an item's maximum (Items & quotas) |
| Keep bottles filled | Take bottles that aren't full to a gas tank to refill, and put them back when full (needs Automation) |
| Blocks shared with me | Off: only blocks you own are managed. On: blocks shared with your faction or everyone, and unowned blocks, are managed too |

**Whose blocks.** The game lets a player use blocks shared with their faction, blocks shared with everyone, and blocks nobody owns. By default the plugin only manages blocks the player owns. It never takes from, puts into or queues on anyone else's block, and never writes to their screens. The server checks every move and queue request against the player's access either way, so the plugin can't do more than the player could by hand.

**Sort now** runs everything Automation would do, once, until a pass has nothing to move (at most 2 minutes), even while Automation is off. **Unload docked ships** moves the cargo of the player's ships docked to the grid (cargo containers, connectors, collectors; not Stock or Manual blocks, and above any minimum) into the grid's storage. A grid's main grid is a station if it has one, so unloading always goes ship to station.

**Priority** (0-9, Block settings): among storage blocks that accept an item equally well, the higher number fills first.

**Assembler modes.** Shared with our other plugins through `Shared/AssemblerModes.cs` and stored in the assembler's Custom Data as `[TIM] Assembler=Main|Coop|Manual`. Main takes the orders. Co-op (the default) stays in the game's cooperative mode and helps a Main assembler, because in the game a cooperative assembler only takes work from a connected assembler that isn't cooperative. Manual means no plugin queues on it or changes it. Each quota shortfall is queued as one order on a Main assembler (the shortest queue if there are several). What's already queued is counted on every assembler, Main or Co-op. If none of the usable assemblers is Main, one is made Main the first time something is queued. Set a mode with the Production view's Assembler mode button. Setting an assembler's role to Manual in Block settings also sets its shared mode to Manual.

**Disassembly.** The plugin picks one idle assembler (on, powered, nothing queued, not cooperating, not Manual; normally the Main one), switches it to disassembly with the same request as the terminal's mode switch, and queues the surplus. Once that queue is done, it switches the assembler back to assembly. The game keeps the assembly queue aside meanwhile. It only switches back assemblers it switched itself, and it remembers them across restarts. The assembler pulls the items itself through conveyors, from any connected inventory. If the surplus disappears first, the plugin takes those entries out of the queue.

**Refinery priority.** Refineries refine their input top to bottom. With Automation on, the plugin moves the highest-priority ore a refinery holds to the front, using the same request as dragging a stack inside an inventory. If the refinery holds none of a better ore that's in storage, it brings some in, sending back its lowest-priority stack first if it's full.

**Bottles.** Bottles below 100% in storage or intakes go to a tank of the same gas with Auto-Refill on, power and gas in it. The tank fills them as they arrive, and full bottles go back to storage. Gas generators aren't used, because they refill bottles only on the server and a client can't see when a bottle in one is full. Stock blocks with bottle minimums get the fullest bottles first.

## Block roles

| Role | Auto for | Behaviour |
|---|---|---|
| Storage | Cargo containers | Stores items; a source for minimums and a destination for sorting |
| Intake | Connectors, collectors | Emptied into storage |
| Stock | (never Auto) | Holds only the items it has limits for, between min and max; everything else is moved out |
| Machine | Everything else | Left alone except for its own limits (and output draining) |
| Manual | (never Auto) | Never touched |

**Accepts** (Block settings): a storage block can accept categories (ore, ingots, components, ammo, tools, bottles, food, other) and single items. Once any storage block accepts something, items move to the best match: exact item, then category, then general storage (storage that accepts nothing in particular). Items a block doesn't accept are moved out of it.

**Limits** work on any block. A minimum brings the item in from intakes, production output, storage and other blocks' surplus. A maximum sends the excess to storage. On a machine, limits count its input inventory.

**Docked ships.** In Space Engineers a ship docked by connector shares the station's terminal, so it shows up as part of the station while docked, and the station's settings apply. Its Auto blocks count as machines, so its cargo isn't sorted into the station. To restock a docked ship, give its lockers the Stock role with minimums.

Quotas stay with the grid they were set on. Each mechanical group in a terminal system (the station itself, each docked ship) is handled on its own: its settings are looked up only among its own grids, and its quotas count only its own stock and queue only on its own assemblers. A docked ship's quotas keep working while it's docked, but they never drive the station's assemblers, and the station's quotas never use the ship's.

## LCD pages

Overview, Ores, Ingots, Components, Ammo, Tools & bottles, All items, Containers (each storage block's fill), Stock limits, Quotas, Warnings, Log. A screen can show the grid it's on or any other known grid. When a page is first assigned, the screen is switched to text mode with a monospace font. After that, font and size are left as you set them.

## Where settings are stored

Everything is stored on your computer in the plugin's local storage, one pair of files per world (`SacrificialStockpileManager_<world>_Settings.xml` and `..._Grids.xml`, keyed by the world's name). Nothing is written to Custom Data or block names, so other players don't see your settings. LCD text is visible to everyone.

## Multiplayer

Everything runs on your client. Nothing is needed on the server.

- **Moving items** sends the same request as dragging an item in the terminal's inventory screen (`MyInventory.TransferByUser`). The server checks your access to both inventories. The server doesn't check conveyors for this request, so the plugin checks them itself, the same way a programmable block does, and only moves items between conveyor-connected inventories.
- **Queuing** sends the same request as clicking a blueprint in the production screen.
- **Pacing:** each grid gets at most 10 moves every 3 seconds, and the work is spread over frames, one grid at a time.
- **Server delay:** the server takes a moment to confirm a move. Moves still on their way are counted for up to 6 seconds, so they aren't sent twice. An autocraft item isn't queued again for 15 seconds.
- **Only while you're around.** Unlike a programmable block, the plugin only works while you're online and the grid is loaded near you. Grids out of range show their last known contents.
- **Other players with the plugin.** If two players manage the same grid, both move items.

## Switching between our plugins

If any of our other plugins are loaded too (BaR Maid, OreScout, Sacrificial Stockpile Manager, Script to Plugin), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Building

The project also builds `Shared/PluginSwitcher.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/SacrificialStockpileManager.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/SacrificialStockpileManager.dll`.

The plugin uses its own assembly name (`SacrificialStockpileManager`), action ID (`SacrificialStockpileManager_OpenMenu`), chat command (`/ssm`), storage files (`SacrificialStockpileManager_*`) and GPS key line (`SSM:grid:`), so it can't clash with other plugins or mods.
