using System;

namespace FatAlbert;

/// <summary>Numbers as the game shows them: kN/MN, t, L, MWh, km.</summary>
internal static class Format
{
	public static string Force(double newtons)
	{
		return newtons >= 1e6 ? $"{newtons / 1e6:0.00} MN" : $"{newtons / 1e3:0.#} kN";
	}

	/// <summary>Always in kg, as the game's terminal shows ship mass.</summary>
	public static string Mass(double kg)
	{
		return $"{kg:#,0} kg";
	}

	public static string Litres(double litres)
	{
		return $"{litres:#,0} L";
	}

	public static string Energy(double mwh)
	{
		return mwh >= 1.0 ? $"{mwh:#,0.00} MWh" : $"{mwh * 1000.0:0.#} kWh";
	}

	public static string Power(double mw)
	{
		return mw >= 1.0 ? $"{mw:#,0.00} MW" : $"{mw * 1000.0:0.#} kW";
	}

	public static string Distance(double metres)
	{
		return Math.Abs(metres) >= 1000.0 ? $"{metres / 1000.0:#,0.0} km" : $"{metres:0} m";
	}

	public static string Gravity(double ms2)
	{
		return $"{ms2 / 9.81:0.00} g";
	}

	public static string Time(double seconds)
	{
		TimeSpan span = TimeSpan.FromSeconds(Math.Round(seconds));
		return span.TotalHours >= 1.0 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
	}

	public static string Ratio(double ratio)
	{
		return double.IsNaN(ratio) ? "?" : double.IsInfinity(ratio) || ratio > 999 ? "over 999" : $"{ratio:0.00}";
	}

	public static string Percent(double part, double whole)
	{
		return whole > 0 ? $"{100.0 * part / whole:0}%" : "-";
	}
}
