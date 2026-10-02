# BaR Maid

A client-side Pulsar plugin version of the [BaR companion script](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905) (v2.5.4) for the SKO Nanobot Build and Repair System. It does what the programmable block script does, but shows everything in an in-game menu instead of on LCD panels:

- Queues the components a Build and Repair system is missing into the assemblers of its group.
- Shows live status: current weld and grind targets, target lists, floating items, missing components and the priority lists.
- Handles any number of independent groups (for example one per hangar bay).
- Finds Build and Repair systems by itself. With no setup at all, every system on a construct forms one group, called Default.

It needs the **SKO maintained** Nanobot Build and Repair mod (workshop 2111073562), just like the script. It reads the same scripting properties the script uses.

## Opening the menu

- Type `/barmaid` in chat, or
- Drag a Build and Repair block onto a toolbar and pick the **BaR Maid** action.

The menu opens on the group of the block you used. From chat, it opens on a group on the grid you're controlling, or failing that the nearest one.

| Control | What it does |
|---|---|
| Group | Every group you can access that is loaded near you, shown as `Grid: Group (n systems)` |
| View | Status, Weld targets, Grind targets, Collect targets, Missing components, BaR settings, Weld priority, Grind priority, Setup: groups, Help |
| Auto-queue | Turns auto-queuing on or off for the group |
| Queue now | Queues what's missing once, whether auto-queue is on or not |
| Rescan | Looks for systems and assemblers again straight away (this also happens every 5 seconds by itself) |
| Help | Opens the in-game help page; Back returns to the view you came from |

The window closes with the X in its corner or with Esc. Tables update every half second. Click a column header to sort. Until you do, targets are listed in the mod's own order.

**BaR settings** lists the Build and Repair settings the server's mod settings allow: search, work and weld mode, projections, colours, janitor options, grind order, push options, work area size and offset, sound and effects, and script control. Select one and press **Lower / Previous** or **Raise / Next** (hold Shift for 10x steps), or double-click it to step it forward. The change is made on every system in the group through the mod's own terminal properties, so it syncs exactly like a terminal change, and settings the server has locked don't move. **Weld priority** and **Grind priority** are read-only: the mod only saves order changes made in its own terminal list.

### Toolbar actions

Build and Repair blocks get four actions:

| Action | What it does |
|---|---|
| BaR Maid | Opens the menu on this block's group |
| BaR Maid Auto-queue On/Off | Flips auto-queue for this block's group; the slot shows On or Off |
| BaR Maid Auto-queue On / Off | Switches it on or off |

They work from your own toolbar (cockpit or character). Button panels, timers and event controllers run their actions on the server, where the plugin isn't loaded.

## Groups

Groups belong to one construct: the grids joined by rotors, pistons and hinges. A ship docked by connector is a separate construct, so its assemblers are never used for the station's systems (its cargo still counts as stock, because the systems can pull from it). The setting is stored in each block's Custom Data:

```
[BaR Maid]
Group=Hangar 1
AutoQueue=true
```

You don't have to type this yourself. In **Setup: groups**, select a block, type a group name and press **Assign to group**. **Turn on / off** switches the selected block.

There are no predefined group names: any name you type creates that group once a Build and Repair system is in it. The script's group settings (`BuildAndRepairGroup1`, `AssemblerGroup1`) and terminal block groups are not used.

- A Build and Repair block with no group joins **Default**.
- Assemblers are opt-in: one with no group is never used and shows as **(not used)**. Assign it to a group to use it, Default included (that writes `Group=Default`), and set it to Main or Co-op (see Assembler modes below). Survival kits are never used.
- An assembler assigned to a group only works for that group.
- **Remove from groups** writes `Group=None` on a system and removes the group from an assembler. Either way the block is out of every group.
- `AutoQueue` is stored on the group's Build and Repair blocks. It's off until you switch it on. A system moved into another group takes on that group's setting.

Everything else in the block's Custom Data is left alone. Because the settings live on the blocks, they are saved with the world and synced to the server, so they are the same for everyone.

### Assembler modes

Every assembler has a mode, shared with our other plugins through [`Shared/AssemblerModes.cs`](../../Shared/AssemblerModes.cs), so it's set once for all of them. It shows in the Mode column of **Setup: groups** and is set with the **Assembler: Main / Co-op / Manual** buttons there. It's stored in the block's Custom Data:

```
[TIM]
Assembler=Coop
```

| Mode | What happens |
|---|---|
| Manual (default) | Left completely alone: no plugin queues on it or changes it, whatever its group. An assembler with no mode set counts as Manual. |
| Main | Gets the orders, and is kept out of cooperative mode so co-op assemblers can help it. |
| Co-op | Kept in the game's cooperative mode. Never gets orders itself; it takes a share of a Main assembler's work. |

So an assembler is only used once it's both assigned to a group and set to Main or Co-op. When a group can't queue, the Status and Missing components views say why (no assemblers assigned, all of them Manual, or the rest off or damaged).

This follows how the game works: a cooperative assembler only takes work from a conveyor-connected assembler that isn't cooperative and has a queue, so the work has to go to a Main one. When something needs queuing and the group has Co-op assemblers but no usable Main one, BaR Maid makes one of them Main: preferably one that's already out of cooperative mode, otherwise the one with the lowest entity id. Modes are only ever filled in, never swapped, so two plugins never fight over an assembler. An assembler type the game won't let be cooperative takes orders itself.

The plugin was called Needy BOB at first. A `[Needy BOB]` section from then is still read, and is renamed to `[BaR Maid]` the next time the plugin writes to that block. Toolbar slots holding the old Needy BOB actions have to be set up again.

## Auto-queuing

While auto-queue is on, each group is checked every 3 seconds. This is based on the mod's own EnsureQueued, which the script calls, with two differences, in steps 2 and 3:

1. It takes the missing components across the group's systems. Overlapping systems report the same shortfall, so each component uses the largest amount any one system reports, not the sum.
2. It subtracts what's already on the construct (cargo, connectors, the systems themselves, finished output in assemblers) and what's already queued on any assembler of the construct, and queues only the difference.
3. It puts the whole order in one of the group's **Main** assemblers, the one with the shortest queue that can build the component, and the **Co-op** assemblers share it out (see below). The mod's EnsureQueued splits each order across every assembler, which leaves the same item queued everywhere.

Step 2 matters because the Build and Repair mod looks through at most 16 inventories each time it fetches components, carrying on from where it stopped the time before. On a base with many inventories, it reports a component as missing whenever the inventories it just checked didn't have it, even with plenty in a cargo container it hasn't reached yet. Inventory sorters make this happen more often, because they keep moving items and emptying the Build and Repair block. The original script queued on every one of those reports. BaR Maid only crafts what the construct really doesn't have.

It only uses assemblers that are switched on, intact, in assembly mode and accessible to you. **Missing components** shows, for each component, the amount missing, the amount in stock, the amount queued, and whether it was queued or why it couldn't be. "In stock, not crafted" means the system just hasn't found it yet.

**Inventory sorters:** a sorter that empties every block with items it isn't asking for will keep pulling components back out of the Build and Repair blocks. For the GV Inventory Sorter, put `Locked` in the Build and Repair block's name (its other ignore words are `Hidden` and `!manual`) and the sorter leaves it alone.

## Multiplayer

Everything runs on your client. Nothing is needed on the server.

- **Queuing** sends the same request as clicking a blueprint in the assembler's production screen. The server checks your access to the assembler, so the plugin can't do anything you couldn't do by hand.
- **Status** comes from the state the Build and Repair mod already sends to every player. In multiplayer the mod sends only the first 24 entries of each list (targets, missing components). Single player has no limit.
- **Queue timing:** the server takes a moment to confirm new queue items. So after queuing a component, the plugin waits 15 seconds before queuing that component again.
- **Only while you're around.** Unlike a programmable block script, the plugin only works while you're online and the grid is loaded near you.
- **Other players with the plugin.** If two players with the plugin are both near the same group with auto-queue on, both queue for it. The cooldown means an item is normally queued only once, but in rare cases it can be queued twice.

## Differences from the script

- An in-game menu replaces the LCD and cockpit screens. The `[BaR:group]` name tags and `@BaR` Custom Data blocks for LCDs are not used.
- The script's "info-only" argument becomes the Auto-queue switch.
- Target lists are merged from every system in the group. The script showed only the first system's lists.
- The Status view also lists each system with what it's doing, plus the last few queue actions.
- The script's empty "script controlled grinding" hook is left out.

## Switching between our plugins

If any of our other plugins are loaded too (BaR Maid, OreScout, Sacrificial Stockpile Manager, Script to Plugin), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Building

The project also builds `Shared/PluginSwitcher.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/BaRMaid.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/BaRMaid.dll`.

The plugin uses its own assembly name (`BaRMaid`), action IDs (`BaRMaid_*`), chat command (`/barmaid`) and Custom Data section (`[BaR Maid]`), so it can't clash with the other plugins in this repo.
