using Sandbox.Graphics.GUI;
using VRageMath;

namespace SacrificialStockpileManager;

/// <summary>The text of the Help view.</summary>
internal static class SsmHelp
{
	private static readonly string[] Lines =
	{
		"# What it does",
		"Sacrificial Stockpile Manager keeps track of the inventory on all your ships and stations and keeps them stocked. It sorts items into the containers you choose, keeps blocks between a minimum and maximum of each item, and queues items in your assemblers to keep a quota. Everything is set up in this window; nothing is stored in Custom Data.",
		"It runs in your own game. It works on a grid while that grid is loaded near you, and remembers what each grid held when it was last in range.",
		"",
		"# Opening this window",
		"- Type /ssm in chat, or",
		"- Put any block with an inventory or a screen on a toolbar and pick 'Stockpile Manager'. The window opens on that block.",
		"",
		"# Grids",
		"The Grid list shows every ship and station you own a part of: loaded ones first, then the ones you've seen before with how long ago. A grid that isn't loaded shows what it held then. You can still change its settings; they apply when it's back in range.",
		"- All grids (the first view) lists every grid at once: when it was last synced (Live if it's loaded now), distance, storage used, storage size, fill and how many kinds of item it holds. The line under the table adds them up. Double-click a grid, or select it and press Open grid, to go to its Overview.",
		"- Storage means blocks acting as Storage or Stock (cargo containers unless you change their role), not counting docked ships. Machines, cockpits and tools are left out so they don't inflate the totals. The Blocks view shows every block's used and total size.",
		"- GPS marker adds a marker where the grid was last seen (it moves the existing one if you press it again).",
		"- Remove from list is for grids that were deleted or destroyed. It removes the grid and its settings (block settings, quotas, displays). A grid that is loaded can't be removed. If a removed grid still exists, it comes back with default settings the next time it's in range.",
		"",
		"# Automation (Overview)",
		"Automation is off on every grid until you switch it on. While it's off, nothing is moved; the window only shows what is where and what would need doing. Double-click a setting in the Overview to switch it:",
		"- Automation: apply minimums, maximums, sorting and intake draining.",
		"- Autocraft: queue the shortfall of quotas in the assemblers.",
		"- Empty production output: move what refineries and assemblers made into storage. For an assembler that's disassembling, that's the parts that came out (the game puts them in its input), and the items waiting to be taken apart are left where they are.",
		"- Survival kits autocraft: let autocraft use survival kits too.",
		"- Disassemble surplus: disassemble whatever is above an item's maximum (see Quotas below).",
		"- Keep bottles filled: take oxygen and hydrogen bottles that aren't full to a gas tank to refill, and put them back once full (needs Automation).",
		"",
		"# Block roles",
		"Every block with an inventory has a role. Auto picks one from the block type.",
		"- Storage (cargo containers): items are stored here and taken from here.",
		"- Intake (connectors, collectors): emptied into storage.",
		"- Stock: a locker or loadout. It holds only the items it has limits for, kept between their minimum and maximum. Everything else is moved out.",
		"- Machine (everything else when Auto: reactors, generators, refineries, assemblers, cockpits, turrets, tools...): left alone, except for its own limits.",
		"- Manual: player only. The plugin never takes from it or puts into it.",
		"Docked ships: a ship docked by connector shows up as part of the station it's docked to, and the station's settings apply. Its Auto blocks count as machines, so its cargo isn't sorted into the station. To restock a docked ship, give its lockers the Stock role with minimums: they fill from the station's storage.",
		"",
		"# Sorting (Accepts)",
		"In Block settings, tick the categories a storage block accepts: ore, ingots, components, ammo, tools, bottles, food or other. You can also accept single items: pick the item and press Accept item.",
		"Sorting starts once at least one storage block accepts something. Items then move into the block that accepts them best: an exact item beats its category, which beats general storage (a storage block that accepts nothing in particular). Items a block doesn't accept are moved out of it.",
		"",
		"# Limits",
		"In Block settings, pick an item (select a row, or choose it in the list below the table), type an amount and press Set minimum or Set maximum. A blank amount clears it. Amounts can be written as 500, 2,500, 2.5k or 1.2M.",
		"Find: type part of a name (or a category, like 'ingot') in the Find box. The item list and the table only show matching items, and the first match is picked.",
		"- Minimum: the plugin brings the item in from storage, intakes, production output and other blocks' surplus, until the block holds that much.",
		"- Maximum: anything above it is moved to storage.",
		"This works on any block: keep uranium in reactors, ice in generators, ammo in turrets, or ore in a refinery. Limits on a machine count its input inventory.",
		"",
		"# Quotas and autocraft (Items & quotas)",
		"The Items view lists everything on the grid. Select an item, type an amount and press Set quota. With Autocraft on, the plugin counts what the whole grid holds plus what's already queued, and queues the difference in the assemblers (shortest queue first). It only uses assemblers that are on, powered, in assembly mode and not Manual.",
		"Set maximum sets the most you want of an item. With Disassemble surplus on, anything above it is disassembled: the plugin picks one idle assembler (on, powered, nothing queued, not Manual), switches it to disassembly, queues the surplus, and switches it back to assembly once that queue is done. Its assembly queue is kept aside meanwhile, as the game always does. The assembler pulls the items itself through the conveyors, from any connected inventory. If the items are used up before they're disassembled, their entries are taken out of the queue.",
		"A maximum below the quota isn't allowed: setting one moves the other to match.",
		"",
		"# Production details",
		"Double-click an assembler or refinery in the Production view (or select it and press Details). For an assembler you see the queue (the first item shows how far along it is), then every material the whole queue needs: how much, how much the grid has, and how much is missing. 'Blocks the current item' means the assembler is stuck on it right now; 'Runs out partway' means the queue will stop later. Amounts allow for the world's assembler efficiency. Remove from queue takes the selected queue entry out.",
		"For a refinery you see its input in the order it refines it, and its output.",
		"",
		"# Refinery priority",
		"The Refinery priority view lists every ore that can be refined. Select an ore and press Raise priority (or double-click it) to put it on the list; raise and lower to order it. With Automation on, each refinery works on the highest-priority ore it can get: the plugin moves that ore to the front of the refinery's input (refineries refine top to bottom), and if the refinery doesn't have any, brings some in from storage, sending back its lowest-priority ore if it's full. Ores without a priority are refined as they arrive.",
		"",
		"# Keeping bottles filled",
		"With Keep bottles filled and Automation on, oxygen and hydrogen bottles in storage that aren't full are taken to a tank of the same gas that has Auto-Refill switched on, power and gas in it. The tank fills them straight away, and full bottles go back to storage. Gas generators aren't used: they refill bottles on the server only, so your game can't see when a bottle in one is full. When a bottle can't be served, the Overview warns about it. Stock blocks with bottle minimums get the fullest bottles first.",
		"",
		"# Displays (LCD)",
		"Select a screen on the grid, pick a page and what it shows, then press Show page. Pages: Overview, Ores, Ingots, Components, Ammo, Tools & bottles, All items, Stock limits, Quotas, Warnings and Log. A screen can show another grid's last known stock too. The screen is switched to text mode with a monospace font once; after that you can change the font size as you like.",
		"",
		"# Good to know",
		"- Moving items uses the same request as dragging them in the terminal, and queuing uses the same request as clicking a blueprint, so the server checks your access. Items only move between inventories connected by conveyors, just like a programmable block.",
		"- At most 10 moves are sent every 3 seconds per grid, so large backlogs take a little while.",
		"- In multiplayer the server takes a moment to confirm each move. The plugin allows for moves still on their way, so it doesn't send them twice.",
		"- Only while you're around: unlike a programmable block, the plugin works only while you're online and the grid is loaded.",
		"- Settings are stored on your computer, per world. Other players don't see them. LCD text is visible to everyone.",
		"- If two players with the plugin manage the same grid, both will move items."
	};

	public static void Write(MyGuiControlMultilineText text)
	{
		Vector4 heading = Color.White.ToVector4();
		Vector4 body = new Color(200, 215, 230).ToVector4();
		foreach (string line in Lines)
		{
			if (line.StartsWith("# "))
			{
				text.AppendText(line.Substring(2), "White", 0.85f, heading);
			}
			else
			{
				text.AppendText(line, "Blue", 0.75f, body);
			}
			text.AppendLine();
		}
	}
}
