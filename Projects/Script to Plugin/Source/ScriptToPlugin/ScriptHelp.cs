using Sandbox.Graphics.GUI;
using VRageMath;

namespace ScriptToPlugin;

/// <summary>The text of the Help page.</summary>
internal static class ScriptHelp
{
	private static readonly string[] Lines =
	{
		"# What this does",
		"Script to Plugin runs programmable block scripts in your own game instead of in a programmable block. Each script in the list works like its own programmable block. You can have as many as you like, and they keep running while you're near the grid.",
		"",
		"# Getting started",
		"- New script: pick the host block, then paste or write the code in the code editor. Browse Scripts in the editor loads your local and workshop scripts, just like in a programmable block.",
		"- If the host block you pick is a programmable block with a script, you're asked whether to copy its script and Custom Data. Switch the real programmable block off afterwards, or the script runs twice.",
		"- The script starts as soon as it compiles. The list shows what it's doing and the right side shows its output (Echo) and any errors.",
		"",
		"# The host block",
		"A script needs a block to stand in for its programmable block. The script sees that block as Me:",
		"- Me.CubeGrid, position and orientation are the host block's. GridTerminalSystem is the host's grid and everything connected to it, as for a programmable block.",
		"- Me.GetSurface uses the host's screens. Pick a block with screens (a programmable block, cockpit or LCD) if the script draws on Me.",
		"- Me.CustomName, Me.CustomData, Me.Enabled and the detailed info belong to the script, not the host. The script can't rename or switch off the host block. Me has no terminal actions of its own.",
		"- Any block you have access to works. Scripts on a grid that isn't loaded wait and carry on when it's back.",
		"",
		"# Buttons",
		"- Edit code: opens the game's code editor. OK saves and restarts the script. Check Code works as usual.",
		"- Custom Data: the script's own Custom Data (Me.CustomData). Saving it doesn't restart the script, just as on a programmable block. Recompile if the script only reads it at start.",
		"- Set host block: moves the script to another block.",
		"- On / Off: switches the script on or off. Off calls the script's Save() first, like switching a programmable block off. On starts it again.",
		"- Recompile: saves, compiles and restarts the script, like the programmable block's Recompile button.",
		"- Run: runs the script once with the argument in the box. The box is also the default argument for the toolbar.",
		"",
		"# Toolbar",
		"The host block gets actions for each of its scripts: '<script>: Run' (asks for an argument, like a programmable block), '<script>: Run (default argument)', '<script>: On/Off', and 'Script to Plugin menu'.",
		"Use them from your own toolbar (cockpit, remote control, and so on). Button panels, timers, sensors and event controllers run their actions on the server, where this plugin isn't running, so they can't start these scripts. Use a toolbar, or have the script check the blocks itself every few ticks.",
		"",
		"# Multiplayer",
		"- Everything a script changes goes to the server in the same way as when you change it in the terminal. The server checks your access, so a script can only do what you could do yourself.",
		"- Scripts only run while you're online and near the grid. Nobody else sees them running.",
		"- Text a script writes to an LCD is sent to the server when it changes, so everyone sees it. Sprites a script draws are only shown on your own screen.",
		"- IGC: the scripts in this plugin can message each other. They can't reach real programmable blocks, because those run on the server. Antenna range isn't checked between the plugin's scripts.",
		"- Timers and sensors can't run these scripts (see Toolbar).",
		"",
		"# Limits",
		"The same limits as a programmable block: 50,000 instructions per run, and a script stops if it throws an error. The list shows how long each script takes per tick, so you can see what it costs your frame rate.",
		"",
		"# Chat personality",
		"Script to Plugin comments in chat now and then, in electric cyan: when a script compiles, starts, stops, fails to compile or crashes. Only you see it. Choose how much it talks with the dropdown above the list: Off, Quiet (only compile errors and crashes), Normal or Chatty. Chat as sets the name its lines show under (press Enter; clear it for Script to Plugin), or type /stp name <name>. Both settings are yours and the same in every world.",
		"Our plugins take turns: after any of them speaks, the next ordinary line waits 2 minutes on Chatty, 5 on Normal and 15 on Quiet. Compile errors and crashes can come sooner, but never within 30 seconds of the last line. The same kind of event is commented on at most every 10 minutes.",
		"",
		"# Where scripts are kept",
		"Scripts, Custom Data and Storage are saved on your computer, in the plugin's storage for this world. They're saved every minute, when the world is saved, and when you leave."
	};

	public static void Write(MyGuiControlMultilineText text)
	{
		Vector4 heading = Color.White.ToVector4();
		Vector4 body = new Color(200, 215, 230).ToVector4();
		foreach (string line in Lines)
		{
			if (line.StartsWith("# "))
			{
				text.AppendText(line.Substring(2), "White", 0.8f, heading);
			}
			else
			{
				text.AppendText(line, "Blue", 0.7f, body);
			}
			text.AppendLine();
		}
	}
}
