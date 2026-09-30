# Sacrificial Stockpile Manager

A client-side Pulsar plugin for inventory management and auto-restocking. It takes ideas from inventory scripts such as Isy's Inventory Manager, Mamba Inventory Manager, GOAT Sorter, iBex and SHEPHERD, but everything is set up in an in-game menu instead of block names or Custom Data:

- Shows the inventory of every ship and station you own, including ones that aren't loaded right now (as they were when last in range).
- Sorts items into the storage blocks you choose (by category or by single item).
- Keeps any block between a minimum and a maximum of an item: lockers, reactors, generators, turrets, refineries.
- Keeps grid-wide quotas by queuing the shortfall in assemblers (autocraft).
- Shows status pages on LCD screens.

## Opening the menu

- Type `/ssm` in chat, or
- Drag any block with an inventory or a screen onto a toolbar and pick **Stockpile Manager**. The menu opens on that block.

| Control | What it does |
|---|---|
| Grid | Your ships and stations: loaded ones first, then the rest with when they were last seen |
| View | Overview, Items & quotas, Blocks, Block settings, Production, Displays (LCD), Log, Help |

The window closes with the X in its corner or with Esc. Tables update every half second. Click a column header to sort.

## Views

| View | What it shows | Buttons |
|---|---|---|
| Overview | Grid settings (double-click to switch), status, cargo fill, item totals, warnings, recent actions | Change setting, GPS marker, Forget grid, Help |
| Items & quotas | Every item on the grid with its quota, queued amount and autocraft state | Set quota, Clear quota, Autocraft on/off, Help |
| Blocks | Every block with an inventory: role, fill, settings | Edit block, Turn on / off, Rescan, Help |
| Block settings | One block: role, Accepts checkboxes, and per item what's there and its min/max | Set minimum, Set maximum, Clear limits, Back; Accept item |
| Production | Assemblers, refineries, reactors, generators: state and queue or contents | Edit block, Turn on / off, Rescan, Help |
| Displays (LCD) | Every screen on the grid and the page it shows | Show page, Stop showing, Rescan, Help |
| Log | What the plugin did on this grid, newest first | Clear log, Rescan, Help |

In Items & quotas and Block settings, pick an item by selecting its row or from the list under the table, type an amount (`500`, `2,500`, `2.5k`, `1.2M`) and press the button. A blank amount clears a minimum or maximum.

## Grid settings

Set in the Overview; all are off for a new grid.

| Setting | What it does |
|---|---|
| Automation | Apply minimums, maximums, sorting and intake draining. While off, nothing is moved and the menu only shows what would need doing. |
| Autocraft | Queue the shortfall of quotas in the grid's assemblers |
| Empty production output | Move what refineries and assemblers made into storage |
| Survival kits autocraft | Let autocraft use survival kits too |

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

**Docked ships.** In Space Engineers a ship docked by connector shares the station's terminal, so it shows up as part of the station while docked, and the station's settings apply. Its Auto blocks count as machines, so its cargo isn't sorted into the station and its assemblers aren't used for the station's quotas. To restock a docked ship, give its lockers the Stock role with minimums.

## LCD pages

Overview, Ores, Ingots, Components, Ammo, Tools & bottles, All items, Stock limits, Quotas, Warnings, Log. A screen can show the grid it's on or any other known grid. When a page is first assigned, the screen is switched to text mode with a monospace font. After that, font and size are left as you set them.

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

## Building

Set `Bin64` in `Source/SacrificialStockpileManager.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/SacrificialStockpileManager.dll`.

The plugin uses its own assembly name (`SacrificialStockpileManager`), action ID (`SacrificialStockpileManager_OpenMenu`), chat command (`/ssm`), storage files (`SacrificialStockpileManager_*`) and GPS key line (`SSM:grid:`), so it can't clash with the other plugins in this repo.
