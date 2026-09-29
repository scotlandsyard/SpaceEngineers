# Needy BOB

A client-side Pulsar plugin version of the [BaR companion script](https://steamcommunity.com/sharedfiles/filedetails/?id=3472701905) (v2.5.4) for the SKO Nanobot Build and Repair System. It does what the programmable block script does, but shows everything in an in-game menu instead of on LCD panels:

- Queues the components a Build and Repair system is missing into the assemblers of its group.
- Shows live status: current weld and grind targets, target lists, floating items, missing components and the priority lists.
- Handles any number of independent groups (for example one per hangar bay).
- Finds Build and Repair systems by itself. With no setup at all, every system on a construct forms one group, called Default.

It needs the **SKO maintained** Nanobot Build and Repair mod (workshop 2111073562), just like the script. It reads the same scripting properties the script uses.

## Opening the menu

- Type `/bob` in chat, or
- Drag a Build and Repair block onto a toolbar and pick the **Needy BOB** action.

The menu opens on the group of the block you used. From chat, it opens on a group on the grid you're controlling, or failing that the nearest one.

| Control | What it does |
|---|---|
| Group | Every group you can access that is loaded near you, shown as `Grid: Group (n systems)` |
| View | Status, Weld targets, Grind targets, Collect targets, Missing components, Weld priority, Grind priority, Setup: groups |
| Auto-queue | Turns auto-queuing on or off for the group |
| Queue now | Queues what's missing once, whether auto-queue is on or not |
| Rescan | Looks for systems and assemblers again straight away (this also happens every 5 seconds by itself) |

Tables update every half second. Click a column header to sort. Until you do, targets are listed in the mod's own order.

## Groups

Groups belong to one construct (all the grids that share a terminal system). The setting is stored in each block's Custom Data:

```
[Needy BOB]
Group=Hangar 1
AutoQueue=true
```

You don't have to type this yourself. In **Setup: groups**, select a block, type a group name and press **Assign to group**.

- A Build and Repair block with no group joins **Default**.
- An assembler with no group works for **Default**. Every unassigned assembler on the construct counts, so simple setups need nothing. Survival kits are never used.
- An assembler assigned to a name only works for that group.
- **Remove from groups** writes `Group=None`. That takes a system or an assembler out of every group.
- `AutoQueue` is stored on the group's Build and Repair blocks. It's off until you switch it on. A system moved into another group takes on that group's setting.

Everything else in the block's Custom Data is left alone. Because the settings live on the blocks, they are saved with the world and synced to the server, so they are the same for everyone.

## Auto-queuing

While auto-queue is on, each group is checked every 3 seconds. This works the same way as the mod's own EnsureQueued, which the script calls:

1. It takes the missing components across the group's systems. Overlapping systems report the same shortfall, so each component uses the largest amount any one system reports, not the sum.
2. It counts what's already queued or finished in the group's assemblers, and queues only the difference.
3. It spreads the new work over the assemblers that can build the component, starting with the shortest queues.

It only uses assemblers that are switched on, intact, in assembly mode and accessible to you. **Missing components** shows, for each component, the amount missing, the amount queued, the amount already built, and whether it was queued or why it couldn't be.

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

## Building

Set `Bin64` in `Source/NeedyBOB.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/NeedyBOB.dll`.

The plugin uses its own assembly name (`NeedyBOB`), action ID (`NeedyBOB_OpenMenu`), chat command (`/bob`) and Custom Data section (`[Needy BOB]`), so it can't clash with the other plugins in this repo.
