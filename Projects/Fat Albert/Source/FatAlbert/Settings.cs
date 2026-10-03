using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sandbox.ModAPI;
using TimShared;
using VRage.Utils;
using VRageMath;

namespace FatAlbert;

/// <summary>The window's inputs, kept between sessions in the plugin's own local storage file (never in Custom Data).</summary>
internal static class Settings
{
	private const string FileName = "FatAlbert_Settings.txt";

	/// <summary>-1 picks the direction facing away from the planet; otherwise a Dir.</summary>
	public static int Direction = -1;

	/// <summary>Kilometres to climb as typed; empty means "to the edge of the gravity well".</summary>
	public static string DistanceKm = "";

	/// <summary>Climb speed in m/s as typed; empty means the world's speed limit.</summary>
	public static string Speed = "";

	/// <summary>Count thrusters, tanks and power blocks that are off (or stockpiling / recharging) as if they were on.</summary>
	public static bool CountOff = true;

	public static int View;

	/// <summary>Open the ship's parachutes when landing on a planet with air.</summary>
	public static bool UseChutes = true;

	/// <summary>Show the HUD overlay.</summary>
	public static bool Hud;

	/// <summary>Key that turns the HUD overlay on and off, as modifiers and a key name, e.g. "Ctrl+Alt+F".</summary>
	public static string HudKey = "Ctrl+Alt+F";

	/// <summary>Top-left corner of the HUD overlay in screen GUI coordinates (0..1 across, 0..1 down).</summary>
	public static float HudX = 0.02f;

	public static float HudY = 0.18f;

	/// <summary>How much Fat Albert talks in chat.</summary>
	public static Personality.Chattiness Chattiness = Personality.Chattiness.Normal;

	public static double? Distance
	{
		get
		{
			double? km = Parse(DistanceKm);
			return km.HasValue && km.Value > 0 ? km.Value * 1000.0 : (double?)null;
		}
	}

	public static double ClimbSpeed(double limit)
	{
		double? speed = Parse(Speed);
		return speed.HasValue && speed.Value > 0 ? Math.Min(speed.Value, limit) : limit;
	}

	public static double? Parse(string text)
	{
		string trimmed = (text ?? "").Trim().Replace(',', '.');
		return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null;
	}

	public static void Load()
	{
		try
		{
			if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(FileName, typeof(Settings)))
			{
				return;
			}
			Dictionary<string, string> values = new Dictionary<string, string>();
			using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(FileName, typeof(Settings)))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					int equals = line.IndexOf('=');
					if (equals > 0)
					{
						values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
					}
				}
			}
			if (values.TryGetValue("Direction", out string direction) && int.TryParse(direction, out int dir) && dir >= -1 && dir <= 5)
			{
				Direction = dir;
			}
			if (values.TryGetValue("DistanceKm", out string distance))
			{
				DistanceKm = distance;
			}
			if (values.TryGetValue("Speed", out string speed))
			{
				Speed = speed;
			}
			if (values.TryGetValue("CountOff", out string countOff))
			{
				CountOff = countOff != "0";
			}
			if (values.TryGetValue("UseChutes", out string chutes))
			{
				UseChutes = chutes != "0";
			}
			if (values.TryGetValue("Hud", out string hud))
			{
				Hud = hud == "1";
			}
			if (values.TryGetValue("HudKey", out string hudKey) && hudKey.Length > 0)
			{
				HudKey = hudKey;
			}
			if (values.TryGetValue("HudX", out string hudX) && Parse(hudX) is double x)
			{
				HudX = (float)MathHelper.Clamp(x, 0.0, 0.95);
			}
			if (values.TryGetValue("HudY", out string hudY) && Parse(hudY) is double y)
			{
				HudY = (float)MathHelper.Clamp(y, 0.0, 0.95);
			}
			if (values.TryGetValue("Personality", out string level) && Enum.TryParse(level, true, out Personality.Chattiness parsed) && Enum.IsDefined(typeof(Personality.Chattiness), parsed))
			{
				Chattiness = parsed;
			}
			if (values.TryGetValue("View", out string view) && int.TryParse(view, out int index))
			{
				View = index;
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] Could not read settings: {ex.Message}");
		}
	}

	public static void Save()
	{
		try
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(FileName, typeof(Settings)))
			{
				writer.WriteLine("Direction=" + Direction);
				writer.WriteLine("DistanceKm=" + DistanceKm);
				writer.WriteLine("Speed=" + Speed);
				writer.WriteLine("CountOff=" + (CountOff ? "1" : "0"));
				writer.WriteLine("View=" + View);
				writer.WriteLine("UseChutes=" + (UseChutes ? "1" : "0"));
				writer.WriteLine("Hud=" + (Hud ? "1" : "0"));
				writer.WriteLine("HudKey=" + HudKey);
				writer.WriteLine("HudX=" + HudX.ToString(CultureInfo.InvariantCulture));
				writer.WriteLine("HudY=" + HudY.ToString(CultureInfo.InvariantCulture));
				writer.WriteLine("Personality=" + Chattiness);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] Could not save settings: {ex.Message}");
		}
	}
}
