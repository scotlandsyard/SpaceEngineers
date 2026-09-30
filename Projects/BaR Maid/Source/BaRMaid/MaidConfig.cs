using System;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace BaRMaid;

/// <summary>
/// Reads and writes the [BaR Maid] section of a block's Custom Data. It only ever touches its own section,
/// and it doesn't need the rest of the Custom Data to be valid INI (other scripts use all sorts of formats).
/// </summary>
internal static class MaidConfig
{
	public const string Section = "BaR Maid";

	/// <summary>The section name from before the plugin was renamed. Still read; renamed on the next write.</summary>
	private const string LegacySection = "Needy BOB";

	public const string GroupKey = "Group";

	public const string AutoQueueKey = "AutoQueue";

	public const string DefaultGroup = "Default";

	/// <summary>Group name that takes a block out of every group.</summary>
	public const string NoGroup = "None";

	private static readonly char[] LineBreaks = { '\n' };

	/// <summary>The group a block is assigned to, or null when it has none (Build and Repair blocks then join Default).</summary>
	public static string GetGroup(IMyTerminalBlock block)
	{
		string value = Get(block, GroupKey);
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	public static bool GetAutoQueue(IMyTerminalBlock block)
	{
		string value = Get(block, AutoQueueKey);
		return value != null && (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");
	}

	public static bool IsNoGroup(string name)
	{
		return name != null && name.Equals(NoGroup, StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsDefaultGroup(string name)
	{
		return name == null || name.Equals(DefaultGroup, StringComparison.OrdinalIgnoreCase);
	}

	public static string Get(IMyTerminalBlock block, string key)
	{
		string[] lines = (block.CustomData ?? "").Split(LineBreaks);
		FindSection(lines, out int start, out int end);
		for (int i = start + 1; start >= 0 && i < end; i++)
		{
			if (TrySplit(lines[i], out string k, out string v) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
			{
				return v;
			}
		}
		return null;
	}

	/// <summary>Sets a key in the section (null removes it). Drops the section when it ends up empty.</summary>
	public static void Set(IMyTerminalBlock block, string key, string value)
	{
		string original = block.CustomData ?? "";
		List<string> lines = new List<string>(original.Replace("\r", "").Split(LineBreaks));
		if (lines.Count == 1 && lines[0].Length == 0)
		{
			lines.Clear();
		}
		FindSection(lines.ToArray(), out int start, out int end);
		if (start >= 0)
		{
			// Brings an old [Needy BOB] header up to date.
			lines[start] = "[" + Section + "]";
		}
		if (start < 0)
		{
			if (value == null)
			{
				return;
			}
			// Add the section before MyIni's end-of-data marker if there is one, otherwise at the end.
			int insertAt = lines.FindIndex(l => l.Trim() == "---");
			if (insertAt < 0)
			{
				insertAt = lines.Count;
			}
			List<string> section = new List<string> { "[" + Section + "]", key + "=" + value };
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
				if (TrySplit(lines[i], out string k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
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
				lines[found] = key + "=" + value;
			}
			else if (value != null)
			{
				// Insert after the last non-blank line of the section so a blank separator line stays at its end.
				int insertAt = end;
				while (insertAt > start + 1 && lines[insertAt - 1].Trim().Length == 0)
				{
					insertAt--;
				}
				lines.Insert(insertAt, key + "=" + value);
				end++;
			}
			bool empty = true;
			for (int i = start + 1; i < end; i++)
			{
				if (lines[i].Trim().Length > 0)
				{
					empty = false;
					break;
				}
			}
			if (empty)
			{
				lines.RemoveRange(start, end - start);
				// Remove the blank line that separated the section from what came before it.
				if (start > 0 && start <= lines.Count && lines[start - 1].Trim().Length == 0 && (start == lines.Count || lines[start].Trim().Length == 0))
				{
					lines.RemoveAt(start - 1);
				}
			}
		}
		string updated = string.Join("\n", lines).TrimEnd('\n');
		if (updated != original.Replace("\r", "").TrimEnd('\n'))
		{
			block.CustomData = updated;
		}
	}

	/// <summary>Finds the section header line and the index just past the section's last line (-1 when missing).</summary>
	private static void FindSection(string[] lines, out int start, out int end)
	{
		start = -1;
		end = lines.Length;
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (start < 0)
			{
				if (line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase) || line.Equals("[" + LegacySection + "]", StringComparison.OrdinalIgnoreCase))
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
		if (trimmed.Length == 0 || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
		{
			return false;
		}
		int eq = trimmed.IndexOf('=');
		if (eq <= 0)
		{
			return false;
		}
		key = trimmed.Substring(0, eq).Trim();
		value = trimmed.Substring(eq + 1).Trim();
		return true;
	}
}
