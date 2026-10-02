using Sandbox.Graphics.GUI;
using VRageMath;

namespace BaRMaid;

/// <summary>The text of the Help view.</summary>
internal static class MaidHelp
{
	private static readonly string[] Lines =
	{
		"# What BaR Maid does",
		"BaR Maid watches your Nanobot Build and Repair systems (BaR). When they can't weld because components are missing, it queues those components in your assemblers. It also shows what the systems are doing and lets you change their settings. It runs in your own game, so it only works while you are online and near the grid.",
		"",
		"# Opening this window",
		"- Type /barmaid in chat, or put a Build and Repair block on a toolbar and pick 'BaR Maid'.",
		"- The same block also has toolbar actions 'BaR Maid Auto-queue On/Off', '... On' and '... Off'. They switch auto-queue for that block's group without opening the window, and the slot shows On or Off. Use them from your own toolbar: button panels and timers run on the server, where BaR Maid isn't loaded.",
		"",
		"# Groups",
		"A group is a set of Build and Repair systems plus the assemblers that build for them, on one ship or station (everything connected to the same terminal). The Group list at the top shows them as 'Grid: Group'.",
		"- There are no set group names. You make them up: in 'Setup: groups', type any name (for example Hangar 1), select a block and press Assign to group. A group exists as long as at least one Build and Repair system is in it.",
		"- Default: every Build and Repair system you haven't assigned. With no setup at all, all systems on a grid are the Default group.",
		"- Assemblers are never used until you assign them to a group AND give them a mode (see Assembler modes below). Select one in Setup: groups and assign it to a group (leave the name blank, or type Default, for the Default group). It then builds only for that group. Unassigned assemblers show as (not used).",
		"- Only assemblers on the same ship or station count: grids joined by rotors, pistons and hinges. Ships docked by connector are separate, so their assemblers are never used. Cargo on a docked ship does count as stock, because the systems can pull from it through the connector.",
		"- Remove from groups takes a block out of every group; it then shows as (none) or (not used). Assign it again to bring it back.",
		"- The group names from the original script's settings (like BuildAndRepairGroup1 and AssemblerGroup1) are not used, and neither are terminal block groups.",
		"- The group is stored in a [BaR Maid] section of the block's Custom Data. It is saved with the world and the same for every player, and the rest of the Custom Data is left alone.",
		"",
		"# Assembler modes: Main, Co-op, Manual",
		"Every assembler has a mode, shown in Setup: groups and set with the Assembler buttons there. The same mode is used by our other plugins (such as Stockpile Manager), so you set it once.",
		"- Manual (the default): left completely alone. No plugin queues on it or changes its settings, whatever group it's in. Every assembler starts out Manual, so set the ones you want used to Main or Co-op.",
		"- Main: gets the orders and is kept out of cooperative mode so the co-op assemblers can help it. If a group has Co-op assemblers but no usable Main one when something needs queuing, BaR Maid makes one of them Main.",
		"- Co-op: kept in the game's cooperative mode. It never gets orders itself; it takes a share of the work from a Main assembler.",
		"- An assembler type the game won't let be cooperative takes orders itself, like a Main one.",
		"Each order goes to one Main assembler, the one with the shortest queue, and the co-op ones share it out. Queued amounts are counted on every assembler of the ship or station, so nothing is ordered twice when a co-op assembler takes part of a queue.",
		"Quick setup for one group: assign your assemblers to the group, set one to Main and the rest to Co-op, then switch Auto-queue on.",
		"",
		"# Views",
		"- Status: whether the systems are working, what they are welding or grinding, how much is waiting, their main settings, each system on its own, and the last things queued.",
		"- Weld targets / Grind targets: blocks waiting to be welded or ground, with integrity and distance from the first system. Green rows are being worked on right now.",
		"- Collect targets: floating items the systems will pick up.",
		"- Missing components: what the systems need but can't find. In stock = how many are already on this ship or station (cargo, connectors, finished in assemblers and so on). Queued = already in the group's assembler queues. The last column says what BaR Maid did about it, or why it couldn't.",
		"- The Build and Repair mod only searches a few inventories at a time, so on a big base it can list something as missing that is sitting in cargo. BaR Maid never crafts what is already in stock; those rows say 'In stock, not crafted' until the system finds it.",
		"- BaR settings: the Build and Repair settings of every system in the group (see below).",
		"- Weld priority / Grind priority: which kinds of block are handled first. Read only here; change the order in the block's terminal.",
		"- Setup: groups: every Build and Repair system and assembler on this grid, with its group and state. Green rows belong to the group picked at the top.",
		"Click a column header to sort a list; click it again to reverse. Everything refreshes by itself.",
		"",
		"# Buttons",
		"- Auto-queue: switches auto-queuing on or off for the group. While it's on, BaR Maid checks every 3 seconds and queues what is missing, minus what is already in stock on the ship or station or queued.",
		"- Queue now: queues what is missing once, even while auto-queue is off.",
		"- Rescan: looks for systems and assemblers again straight away (it also does this every 5 seconds).",
		"- Setup: Assign to group / Remove from groups as above. Turn on / off switches the selected block on or off. Assembler: Main / Co-op / Manual sets the selected assembler's mode.",
		"- BaR settings: select a setting, then press Lower / Previous or Raise / Next. Hold Shift for steps 10 times bigger. Double-click a setting to step it forward. The change goes to every system in the group, just as if you had changed each one in its terminal. Settings the server has locked stay as they are, and the status line says so. A value shown as '(systems differ)' means the systems in the group don't all have the same value.",
		"",
		"# Good to know",
		"- Only assemblers that are on, intact, in assembly mode and accessible to you are used.",
		"- After queuing a component, BaR Maid waits 15 seconds before queuing it again, so the server has time to confirm the first order.",
		"- In multiplayer the mod only sends the first 24 targets and missing components to players, so long lists are cut off.",
		"- If two players with BaR Maid have auto-queue on for the same group, an item can now and then be queued twice.",
		"- Needs the SKO maintained Nanobot Build and Repair mod."
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
