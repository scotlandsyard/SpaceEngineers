using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;

namespace NeedyBOB;

internal enum SettingKind
{
	Toggle,
	Choice,
	Number
}

/// <summary>
/// One Build and Repair setting that the menu can change. It goes through the mod's own terminal property,
/// so a change made on a client is sent to the server exactly like a change in the block's terminal, and
/// settings the server has locked stay as they are.
/// </summary>
internal class BarSetting
{
	private const string Prefix = "BuildAndRepair.";

	public string Id;

	public string Label;

	public SettingKind Kind;

	public long[] Keys;

	public string[] Names;

	public float Step = 1f;

	public string Unit = "";

	// Keys and names follow the mod's own dropdowns (WorkMode 4 is deprecated and no longer offered).
	public static readonly List<BarSetting> All = new List<BarSetting>
	{
		Choice("Mode", "Search mode", new long[] { 1, 2 }, "Walk mode", "Fly mode"),
		Choice("WorkMode", "Work mode", new long[] { 1, 2, 8, 16 }, "Weld before grind", "Grind before weld", "Welding only", "Grinding only"),
		Choice("WeldMode", "Weld mode", new long[] { 0, 1, 2 }, "Weld fully", "Weld to functional", "Skeleton only"),
		Toggle("AllowBuild", "Build projected blocks"),
		Toggle("UseIgnoreColor", "Skip blocks painted the ignore color"),
		Toggle("UseGrindColor", "Grind blocks painted the grind color"),
		Toggle("GrindJanitorEnemies", "Janitor: grind enemy blocks"),
		Toggle("GrindJanitorNotOwned", "Janitor: grind unowned blocks"),
		Toggle("GrindJanitorNeutrals", "Janitor: grind neutral blocks"),
		Toggle("GrindJanitorOptionDisableOnly", "Janitor: only grind until disabled"),
		Toggle("GrindJanitorOptionHackOnly", "Janitor: only grind until hackable"),
		Toggle("GrindIgnorePriorityOrder", "Grind: ignore priority order"),
		Toggle("GrindNearFirst", "Grind: nearest first"),
		Toggle("GrindFarFirst", "Grind: farthest first"),
		Toggle("GrindSmallestGridFirst", "Grind: smallest grid first"),
		Toggle("CollectIfIdle", "Collect floating items only when idle"),
		Toggle("PushIngotOreImmediately", "Push ingots and ore out immediately"),
		Toggle("PushItemsImmediately", "Push items out immediately"),
		Toggle("PushComponentImmediately", "Push components out immediately"),
		Toggle("ShowArea", "Show work area"),
		Number("AreaWidth", "Area width", 1f, "m"),
		Number("AreaHeight", "Area height", 1f, "m"),
		Number("AreaDepth", "Area depth", 1f, "m"),
		Number("AreaOffsetLeftRight", "Area offset left/right", 1f, "m"),
		Number("AreaOffsetUpDown", "Area offset up/down", 1f, "m"),
		Number("AreaOffsetFrontBack", "Area offset front/back", 1f, "m"),
		Number("SoundVolume", "Sound volume", 5f, "%"),
		Toggle("DisableTickingSound", "Turn off ticking sound"),
		Toggle("DisableParticleEffects", "Turn off particle effects"),
		Toggle("ScriptControlled", "Script controlled (a script picks targets)")
	};

	private static BarSetting Toggle(string id, string label)
	{
		return new BarSetting { Id = id, Label = label, Kind = SettingKind.Toggle };
	}

	private static BarSetting Choice(string id, string label, long[] keys, params string[] names)
	{
		return new BarSetting { Id = id, Label = label, Kind = SettingKind.Choice, Keys = keys, Names = names };
	}

	private static BarSetting Number(string id, string label, float step, string unit)
	{
		return new BarSetting { Id = id, Label = label, Kind = SettingKind.Number, Step = step, Unit = unit };
	}

	/// <summary>False when the server's mod settings leave this option out of the terminal altogether.</summary>
	public bool Exists(IMyTerminalBlock block)
	{
		return block.GetProperty(Prefix + Id) != null;
	}

	/// <summary>The value as text, or null when it can't be read.</summary>
	public string Format(IMyTerminalBlock block)
	{
		try
		{
			switch (Kind)
			{
			case SettingKind.Toggle:
				return (block.GetProperty(Prefix + Id) as ITerminalProperty<bool>)?.GetValue(block) == true ? "On" : "Off";
			case SettingKind.Choice:
			{
				long? value = (block.GetProperty(Prefix + Id) as ITerminalProperty<long>)?.GetValue(block);
				int index = value.HasValue ? Array.IndexOf(Keys, value.Value) : -1;
				return index >= 0 ? Names[index] : value?.ToString();
			}
			default:
			{
				float? value = (block.GetProperty(Prefix + Id) as ITerminalProperty<float>)?.GetValue(block);
				return value.HasValue ? $"{value.Value:0.#} {Unit}" : null;
			}
			}
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Steps the setting on every system: direction +1 = next / higher, -1 = previous / lower (a toggle just
	/// flips). All systems get the first system's new value. Returns an error message, or null on success.
	/// </summary>
	public string Change(List<IMyShipWelder> systems, int direction, bool bigStep)
	{
		IMyShipWelder first = systems.FirstOrDefault();
		if (first == null)
		{
			return "No working Build and Repair system in this group.";
		}
		try
		{
			string before = Format(first);
			switch (Kind)
			{
			case SettingKind.Toggle:
			{
				ITerminalProperty<bool> property = first.GetProperty(Prefix + Id) as ITerminalProperty<bool>;
				bool value = !property.GetValue(first);
				foreach (IMyShipWelder system in systems)
				{
					property.SetValue(system, value);
				}
				break;
			}
			case SettingKind.Choice:
			{
				ITerminalProperty<long> property = first.GetProperty(Prefix + Id) as ITerminalProperty<long>;
				int index = Math.Max(0, Array.IndexOf(Keys, property.GetValue(first)));
				// The server can forbid some modes; the mod then ignores them, so move on to the next one.
				for (int tries = 1; tries < Keys.Length && Format(first) == before; tries++)
				{
					index = (index + direction + Keys.Length) % Keys.Length;
					foreach (IMyShipWelder system in systems)
					{
						property.SetValue(system, Keys[index]);
					}
				}
				break;
			}
			default:
			{
				ITerminalProperty<float> property = first.GetProperty(Prefix + Id) as ITerminalProperty<float>;
				// The mod clamps to the block's own limits.
				float value = (float)Math.Round(property.GetValue(first) + direction * Step * (bigStep ? 10f : 1f));
				foreach (IMyShipWelder system in systems)
				{
					property.SetValue(system, value);
				}
				break;
			}
			}
			if (Format(first) == before)
			{
				return Kind == SettingKind.Number
					? $"{Label} is at its limit, or the server doesn't allow changing it."
					: $"The server doesn't allow changing {Label.ToLowerInvariant()} to that.";
			}
			return null;
		}
		catch (Exception)
		{
			// The mod registers locked settings without a setter.
			return $"The server doesn't allow changing {Label.ToLowerInvariant()}.";
		}
	}
}
