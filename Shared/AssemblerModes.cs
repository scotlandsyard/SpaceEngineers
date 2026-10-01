using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace TimShared;

/// <summary>How our plugins may use an assembler. Stored on the block, so every plugin sees the same value.</summary>
internal enum AssemblerMode
{
	/// <summary>Not set yet: becomes Coop (or Main) the first time a plugin queues work on this construct.</summary>
	Unset,

	/// <summary>Takes the plugins' orders. Kept out of cooperative mode so the co-op assemblers can help it.</summary>
	Main,

	/// <summary>Kept in cooperative mode: helps a Main assembler and never gets orders directly.</summary>
	Coop,

	/// <summary>Left completely alone by every plugin: no orders, no mode changes.</summary>
	Manual
}

/// <summary>
/// The assembler modes shared by our plugins (Main, Co-op, Manual). The mode lives in the block's Custom Data:
/// <code>
/// [TIM]
/// Assembler=Coop
/// </code>
/// In the game, a cooperative assembler only takes work from a conveyor-connected assembler that is not in
/// cooperative mode and has a queue. So orders go to Main assemblers and the Co-op ones share them out. A plugin
/// that queues work calls <see cref="PrepareForOrders"/> first: it gives unset assemblers their default mode,
/// makes sure one is Main, and sets the game's cooperative switch to match. Only the plugin's own assemblers are
/// ever changed, and modes are only ever filled in, never swapped, so two plugins can't fight over one.
/// This one file is linked into every plugin that needs it (see Shared/README.md).
/// </summary>
internal static class AssemblerModes
{
	public const string Section = "TIM";

	public const string Key = "Assembler";

	public static AssemblerMode Get(IMyTerminalBlock block)
	{
		string value = ReadValue(block?.CustomData);
		return value != null && Enum.TryParse(value, true, out AssemblerMode mode) ? mode : AssemblerMode.Unset;
	}

	public static bool IsManual(IMyTerminalBlock block)
	{
		return Get(block) == AssemblerMode.Manual;
	}

	public static string Describe(AssemblerMode mode)
	{
		switch (mode)
		{
		case AssemblerMode.Main:
			return "Main";
		case AssemblerMode.Coop:
			return "Co-op";
		case AssemblerMode.Manual:
			return "Manual";
		default:
			return "Co-op (default)";
		}
	}

	/// <summary>Stores the mode (Unset removes it) and sets the game's cooperative switch to match.</summary>
	public static void Set(IMyAssembler assembler, AssemblerMode mode)
	{
		WriteValue(assembler, mode == AssemblerMode.Unset ? null : mode.ToString());
		ApplySwitch(assembler, mode);
	}

	/// <summary>
	/// Readies a plugin's assemblers for new orders and returns the ones to queue into. <paramref name="usable"/>
	/// should be the assemblers the plugin may use right now (on, working, in assembly mode, accessible); Manual
	/// ones are skipped here. Unset assemblers become Co-op, and if none of the usable ones is Main one is made
	/// Main: preferably one already out of cooperative mode, else the one with the lowest entity id, so every
	/// plugin picks the same one.
	/// </summary>
	public static List<IMyAssembler> PrepareForOrders(List<IMyAssembler> usable)
	{
		List<IMyAssembler> managed = usable.Where(a => a != null && !a.Closed && Get(a) != AssemblerMode.Manual).ToList();
		if (managed.Count == 0)
		{
			return managed;
		}
		if (!managed.Any(a => Get(a) == AssemblerMode.Main))
		{
			IMyAssembler pick = managed.Where(a => Get(a) == AssemblerMode.Unset && !a.CooperativeMode).OrderBy(a => a.EntityId).FirstOrDefault()
				?? managed.OrderBy(a => a.EntityId).First();
			WriteValue(pick, AssemblerMode.Main.ToString());
		}
		List<IMyAssembler> receivers = new List<IMyAssembler>();
		foreach (IMyAssembler assembler in managed)
		{
			AssemblerMode mode = Get(assembler);
			if (mode == AssemblerMode.Unset)
			{
				mode = AssemblerMode.Coop;
				WriteValue(assembler, mode.ToString());
			}
			ApplySwitch(assembler, mode);
			// An assembler that can't be cooperative (the game doesn't allow it for every type) would sit idle as Co-op,
			// so it takes orders like a Main one.
			if (mode == AssemblerMode.Main || !SupportsCoop(assembler))
			{
				receivers.Add(assembler);
			}
		}
		return receivers;
	}

	public static bool SupportsCoop(IMyAssembler assembler)
	{
		return assembler is MyCubeBlock block && block.BlockDefinition is MyAssemblerDefinition definition && definition.EnableCooperativeMode;
	}

	private static void ApplySwitch(IMyAssembler assembler, AssemblerMode mode)
	{
		if (mode != AssemblerMode.Main && mode != AssemblerMode.Coop)
		{
			return;
		}
		bool coop = mode == AssemblerMode.Coop && SupportsCoop(assembler);
		if (assembler.CooperativeMode != coop)
		{
			assembler.CooperativeMode = coop;
		}
	}

	// A small section reader and writer, so the rest of the Custom Data never has to be valid INI.

	private static string ReadValue(string customData)
	{
		if (string.IsNullOrEmpty(customData))
		{
			return null;
		}
		string[] lines = customData.Replace("\r", "").Split('\n');
		FindSection(lines, out int start, out int end);
		for (int i = start + 1; start >= 0 && i < end; i++)
		{
			if (TrySplit(lines[i], out string key, out string value) && key.Equals(Key, StringComparison.OrdinalIgnoreCase))
			{
				return value;
			}
		}
		return null;
	}

	private static void WriteValue(IMyTerminalBlock block, string value)
	{
		string original = (block.CustomData ?? "").Replace("\r", "");
		List<string> lines = original.Length == 0 ? new List<string>() : new List<string>(original.Split('\n'));
		FindSection(lines.ToArray(), out int start, out int end);
		if (start < 0)
		{
			if (value == null)
			{
				return;
			}
			int insertAt = lines.FindIndex(l => l.Trim() == "---");
			if (insertAt < 0)
			{
				insertAt = lines.Count;
			}
			List<string> section = new List<string> { "[" + Section + "]", Key + "=" + value };
			if (insertAt > 0 && lines[insertAt - 1].Trim().Length > 0)
			{
				section.Insert(0, "");
			}
			lines.InsertRange(insertAt, section);
		}
		else
		{
			int found = -1;
			for (int i = start + 1; i < end; i++)
			{
				if (TrySplit(lines[i], out string key, out _) && key.Equals(Key, StringComparison.OrdinalIgnoreCase))
				{
					found = i;
					break;
				}
			}
			if (found >= 0 && value == null)
			{
				lines.RemoveAt(found);
				end--;
			}
			else if (found >= 0)
			{
				lines[found] = Key + "=" + value;
			}
			else if (value != null)
			{
				lines.Insert(start + 1, Key + "=" + value);
				end++;
			}
			if (!lines.Skip(start + 1).Take(end - start - 1).Any(l => l.Trim().Length > 0))
			{
				lines.RemoveRange(start, end - start);
				if (start > 0 && lines[start - 1].Trim().Length == 0 && (start == lines.Count || lines[start].Trim().Length == 0))
				{
					lines.RemoveAt(start - 1);
				}
			}
		}
		string updated = string.Join("\n", lines).TrimEnd('\n');
		if (updated != original.TrimEnd('\n'))
		{
			block.CustomData = updated;
		}
	}

	private static void FindSection(string[] lines, out int start, out int end)
	{
		start = -1;
		end = lines.Length;
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (start < 0)
			{
				if (line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase))
				{
					start = i;
				}
			}
			else if ((line.StartsWith("[") && line.EndsWith("]")) || line == "---")
			{
				end = i;
				return;
			}
		}
	}

	private static bool TrySplit(string line, out string key, out string value)
	{
		key = null;
		value = null;
		string trimmed = line.Trim();
		int eq = trimmed.IndexOf('=');
		if (trimmed.Length == 0 || trimmed.StartsWith(";") || trimmed.StartsWith("#") || eq <= 0)
		{
			return false;
		}
		key = trimmed.Substring(0, eq).Trim();
		value = trimmed.Substring(eq + 1).Trim();
		return true;
	}
}
