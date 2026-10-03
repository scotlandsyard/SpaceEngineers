using System.Linq;
using Sandbox.ModAPI;
using TimShared;

namespace FatAlbert;

/// <summary>
/// Fat Albert's chat lines (Shared/Personality.cs, lines in Personality.txt). One comment per answer: a line is only
/// considered when the answer for a ship and trip changes, so rechecks of the same answer (the window's refresh, the
/// HUD every two seconds) stay quiet. Call on the game thread.
/// </summary>
internal static class Chatter
{
	/// <summary>Made it, but the ship can't get more than this much heavier (as a share of its mass) and still make it.</summary>
	private const double BarelySpare = 0.10;

	/// <summary>Made it, and could still make it at this many times its mass.</summary>
	private const double OverpoweredTimes = 3.0;

	private static string s_lastSaid;

	/// <summary>Sets how much Fat Albert talks, and saves it.</summary>
	public static void SetLevel(Personality.Chattiness level)
	{
		Personality.Level = level;
		SaveDisplayName();
	}

	/// <summary>Renames him in chat; null or blank goes back to his own name.</summary>
	public static void SetDisplayName(string name)
	{
		Personality.DisplayName = name;
		// Changed only fires on a real change, so save here too.
		SaveDisplayName();
		MyAPIGateway.Utilities.ShowMessage(FatAlbertSession.ChatSender, $"Chat name: {Personality.DisplayName}. /fat name on its own puts it back.");
	}

	/// <summary>Saves the chat name and level; runs whenever either changes (the window's Chat dropdown, /fat chat, Wilson).</summary>
	public static void SaveDisplayName()
	{
		string name = Personality.DisplayName;
		Settings.DisplayName = name == null || name == FatAlbertSession.ChatSender ? "" : name;
		Settings.Chattiness = Personality.Level;
		Settings.Save();
	}

	public static string LevelHint(Personality.Chattiness level)
	{
		switch (level)
		{
		case Personality.Chattiness.Off:
			return "Fat Albert stays silent.";
		case Personality.Chattiness.Quiet:
			return "Only speaks up when the ship is too heavy to lift off.";
		case Personality.Chattiness.Chatty:
			return "Comments on every answer.";
		default:
			return "Comments on the answer now and then.";
		}
	}

	public static void Reset()
	{
		s_lastSaid = null;
	}

	/// <summary>Comments on an answer if it differs from the last one commented on.</summary>
	public static void Comment(ShipSnapshot ship, AscentPlan plan, AscentResult result)
	{
		if (ship == null || plan == null || result == null)
		{
			return;
		}
		string key = EventFor(ship, result);
		string said = key == null ? null : $"{ship.Key}|{plan.Planet?.Id}|{plan.Land}|{key}";
		if (said == null || said == s_lastSaid)
		{
			return;
		}
		s_lastSaid = said;
		switch (key)
		{
		case "check_pass":
			Personality.Say("check_pass", "ship", ship.Name);
			break;
		case "barely_made_it":
			Personality.Say("barely_made_it", "ship", ship.Name);
			break;
		case "overpowered":
			Personality.Say("overpowered", "ship", ship.Name);
			break;
		case "too_heavy":
			Personality.Say("too_heavy", "ship", ship.Name, "mass", Format.Mass(ship.Mass));
			break;
		case "out_of_fuel":
			Personality.Say("out_of_fuel", "ship", ship.Name);
			break;
		case "out_of_power":
			Personality.Say("out_of_power", "ship", ship.Name);
			break;
		case "thin_air":
			Personality.Say("thin_air", "ship", ship.Name);
			break;
		}
	}

	/// <summary>Which event an answer is, or null for answers with nothing to say (no gravity, no mass read, can't land...).</summary>
	private static string EventFor(ShipSnapshot ship, AscentResult result)
	{
		switch (result.Outcome)
		{
		case AscentOutcome.Made:
			if (result.MaxMass < ship.Mass * (1.0 + BarelySpare))
			{
				return "barely_made_it";
			}
			return result.MaxMass >= ship.Mass * OverpoweredTimes ? "overpowered" : "check_pass";
		case AscentOutcome.TooHeavy:
			return "too_heavy";
		case AscentOutcome.OutOfFuel:
			// Flat batteries or reactors out of uranium leave the ship without power; a gas running dry is fuel.
			return result.RanOut == "Batteries" || ship.ItemFuel.Keys.Any(k => ship.FuelName(k) == result.RanOut) ? "out_of_power" : "out_of_fuel";
		case AscentOutcome.Stalled:
			// Gravity only weakens on the way up, so a stall is either the power giving out or the air thinning.
			return result.PowerShort ? "out_of_power" : "thin_air";
		default:
			return null;
		}
	}
}
