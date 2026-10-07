# Findings: "Tiered Hydrogen Bottles - Like Vanilla Tools" (workshop 3566839484)

This mod was written to replace that one on a server that ran it in August 2026 and saw problems with it. Before writing it, we studied the original mod and checked it against the game's own code (decompiled) and the client game logs from that time. This file records what we found. It's reference for the repo and the server admin, and isn't part of the published mod.

## Summary

- **We found nothing in the original that could cause the middle-mouse-button tool problem.** It has no input, toolbar or hand-tool code. The most likely cause is another mod on the server (details below).
- One real flaw: its refill recipes produce stone ore, while the vanilla ones produce gravel. This gives stone ore a "recipe" in one of the game's internal lookups. We found no harm from it in play, but it's untidy.
- Its HUD script works, but it uses the game's internal classes rather than the mod API, so it's more likely to break after a game update.
- It was hydrogen-only and had no settings: changing a capacity or recipe meant editing the mod.

## What the original contains

- Three new hydrogen bottles: Enhanced 600 L, Proficient 1000 L, Elite 1800 L. The vanilla bottle (400 L) is left unchanged; the vanilla definition in the mod is commented out.
- Assembly recipes laid out like the vanilla Welder tiers. The basic assembler makes tier 2 and the assembler makes tiers 2 to 4. Each tier adds a rarer metal.
- Refill recipes, so H2/O2 generators and hydrogen tanks accept and fill the new bottles.
- One script: a HUD counter for the bottle pips next to the jetpack fuel bar. It replaces the game's own counter, which only counts the vanilla bottle, and reads the player's inventory every 1.5 seconds.

The game logs from 31 July and 1 August 2026 show the script compiled and loaded with no errors from this mod.

## 1. The middle-mouse-button tool problem

**What was reported:** pressing the middle mouse button would remove a tool, and it seemed connected to this mod.

**What the original can and can't do:** nothing in the mod touches input, the toolbar, or the tool in your hands:
- The script only reads the local player's inventory to count bottles.
- The definitions only add items, recipes and recipe-list entries.

We found no route from either to tool switching.

**What the middle mouse button does in the game:** it is bound to two vanilla controls, Build Planner and Cube Colour Change. When you look at a container or other block with an inventory:

| Keys | What happens |
|---|---|
| Middle-click | Withdraws the components in your build planner into your inventory |
| Shift + middle-click | Adds the build planner's components to an assembler's queue |
| Ctrl + middle-click, Ctrl + Alt + middle-click | Other build planner withdraw variants |
| **Alt + middle-click** | **Deposit All**: moves every item from your inventory into the container, except items flagged to stay with you |

Vanilla tools and bottles are flagged to stay with you, and the original's bottles were flagged the same way. A modded hand tool without that flag would be moved into the container by Alt + middle-click. To a player, that looks like "middle mouse removed my tool."

**Other mods that use the middle button:** we checked those on the August list that were available locally. Paint Gun, WeaponCore and Defense Shields read the middle button, and none of them removes tools.

**What we couldn't check:** about 50 of the server's 111 mods weren't available to us. That includes mods that add hand tools or change controls, such as The Destiny Universe Tools By Jared, Binoculars, [QoL] Mechanical Keybinds, Server Mod Pack v4 and Server Addons V1.

**Conclusion:** most likely a different mod, possibly combined with Alt + middle-click Deposit All. This isn't proven. The server's mod list has changed since, so we've assumed the problem won't return.

**If it comes back with the new mod:** please note which tool was in hand, what you were looking at, whether Alt, Ctrl or Shift was held, and whether it still happens with Tiered Gas Bottles removed. Tiered Gas Bottles has no input, toolbar or tool code at all, so that test separates it cleanly from other mods.

## 2. Refill recipes produce stone ore instead of gravel

The vanilla refill recipes (`OxygenBottlesRefill`, `HydrogenBottlesRefill`) "produce" 0.9 gravel (`Ingot/Stone`). That is just a placeholder the generator never actually outputs. The original's three refill recipes produce stone ore (`Ore/Stone`) instead.

The game keeps a lookup from each item to the recipe that makes it. Vanilla already points gravel at one of its own refill recipes, so a mod whose refills produce gravel changes nothing that matters. In vanilla, nothing produces stone ore, so the original's refills gave stone ore a "recipe" for the first time: its Elite refill recipe, because each refill overwrites the last. The log shows the overwriting on every load:

```
Overriding non-primary blueprint "...HydrogenBottle2}->{0.9x MyObjectBuilder_Ore/Stone}" with non-primary blueprint "...HydrogenBottle3}->..."
Overriding non-primary blueprint "...HydrogenBottle3}->{0.9x MyObjectBuilder_Ore/Stone}" with non-primary blueprint "...HydrogenBottle4}->..."
```

These "Overriding" lines are harmless on their own. Tiered Gas Bottles logs the same kind of line for its gravel refills, and so would any mod with more than one refill recipe. The problem is only which item ends up mapped. The lookup is used by:
- assembler "disassemble all"
- the build planner's "add to production"
- the economy's price calculation

We checked whether this could be exploited, for example by disassembling stone ore into Elite bottles. Assemblers refuse it, because the refill recipe isn't in any assembler's recipe list, so we found no harm in play. It's still a needless side effect on a base-game item.

**In Tiered Gas Bottles:** the refill recipes copy vanilla exactly, gravel result included, so stone ore is left alone.

## 3. HUD script uses the game's internal classes

The counter replaces the game's `player_hydrogen_bottles` HUD stat, using the same mechanism the vanilla counter uses. That part is fine. However:

- **Internal classes:** it relies on the game's internal classes (`MyResourceDistributorComponent`, `MyOxygenContainerDefinition` through `Sandbox.Game` namespaces), not the mod API. Internal classes change more often with game updates, and a mod script that fails to compile loses its HUD counter.
- **Allocation:** it reads the inventory with a call the game marks as obsolete because it creates a new list on every call.
- **Log line:** it writes a line to the log each time it's created, which is twice per world load.

None of this caused visible problems in the August logs. It's simply less robust.

**In Tiered Gas Bottles:**
- The counters use only the mod API, read the inventory without creating lists, and log nothing unless something fails.
- If a counter ever hits an error, it stops quietly instead of affecting the HUD.

## 4. Other differences in Tiered Gas Bottles

| | Original | Tiered Gas Bottles |
|---|---|---|
| Gases | Hydrogen only | Oxygen and hydrogen |
| Item IDs | `HydrogenBottle2/3/4` | `OxygenBottleTier2/3/4`, `HydrogenBottleTier2/3/4` (own IDs, no clash with the original) |
| Server settings | None; editing the mod was the only way | Settings file in the world folder: enable or disable each bottle, set capacity, mass, build time and recipe. Validated, logged, sent to clients automatically |
| Elite bottle mass | 100 kg | 75 kg (configurable) |
| Icons | Custom images | Vanilla bottle icons with tier chevrons layered on top |
| After a game update | The definitions keep working; the HUD script may break | The definitions keep working with the defaults; only the settings file and the HUD counts would need an update |
| Server load | HUD script only (client side) | Runs once at world load; one small message per joining player; HUD counts client side only |

## How this was checked

- **The original mod:** its definitions and scripts, from the workshop download.
- **Game code:** decompiled from the current game build. Classes: `MyHudStatManager`, `MyStatPlayerHydrogenBottles`, `MySandboxGame` (default key bindings), `MyGuiScreenGamePlay` and `MyTerminalInventoryController` (middle-mouse build planner actions), `MyDefinitionManager` (recipe lookup), `MyProductionBlock` (assembler recipe check).
- **Logs:** client game logs from the server on 31 July and 1 August 2026, and the server's mod list from them.
