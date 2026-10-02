using System;
using System.Collections.Generic;
using VRageMath;

namespace FatAlbert;

/// <summary>The six ways a ship can push, named from the cockpit's point of view.</summary>
public enum Dir
{
	Up,
	Down,
	Forward,
	Backward,
	Left,
	Right
}

/// <summary>
/// One thruster as read from the game, with everything the climb needs. Plain data, so the climb can run on a
/// background thread. Every number comes from the block's own definition and multipliers, so modded thrusters
/// work the same way as vanilla ones.
/// </summary>
public class ThrusterInfo
{
	public string TypeName;

	/// <summary>World direction the thruster pushes the ship (opposite to its flame).</summary>
	public Vector3D Push;

	/// <summary>Full thrust in newtons with no atmosphere penalty (definition force × thrust multiplier).</summary>
	public double Force;

	/// <summary>Megawatts at full thrust (definition × power multiplier), before atmosphere and gravity factors.</summary>
	public double MaxPower;

	public bool Electric;

	/// <summary>Gas the thruster burns (hydrogen in vanilla); null when electric.</summary>
	public string FuelKey;

	/// <summary>Litres of fuel per megawatt-second: 1 / (converter efficiency × gas energy density).</summary>
	public double LitresPerMWs;

	public float MinInfluence;

	public float MaxInfluence;

	public float EffectAtMin;

	public float EffectAtMax;

	public bool NeedsAtmosphere;

	public float ConsumptionPerG;

	public bool On;

	/// <summary>
	/// Share of full thrust left at this air density (0..1+). Copied from MyThrusterBlockThrustComponent, where
	/// "planetary influence" is the air density of the nearest planet.
	/// </summary>
	public double Effectiveness(double airDensity, bool planetHasAtmosphere)
	{
		if (NeedsAtmosphere && !planetHasAtmosphere)
		{
			return EffectAtMin;
		}
		if (MaxInfluence != MinInfluence)
		{
			double t = MathHelper.Clamp((airDensity - MinInfluence) / (MaxInfluence - MinInfluence), 0.0, 1.0);
			return EffectAtMin + (EffectAtMax - EffectAtMin) * t;
		}
		return 1.0;
	}

	/// <summary>
	/// Extra power use in gravity, as the game has it. The game passes gravity in g and divides by 9.81 again, so
	/// this is tiny; it's copied as is so the numbers match what the ship really burns.
	/// </summary>
	public double ConsumptionMultiplier(double gravityG)
	{
		return 1.0 + ConsumptionPerG * (gravityG / 9.81);
	}
}

public class GasPool
{
	public string Key;

	public string Name;

	public double Litres;

	public double Capacity;

	public int Tanks;

	/// <summary>Tanks counted only because "count switched-off blocks" is on (off or stockpiling).</summary>
	public int TanksOff;
}

public enum SourceKind
{
	Battery,
	Reactor,
	Engine,
	Other
}

public class PowerSource
{
	public SourceKind Kind;

	public string TypeName;

	public double MaxOutput;

	/// <summary>Lower runs first, as in the game's resource distribution groups (solar 1, battery 2, reactors 3).</summary>
	public int Priority;

	/// <summary>Batteries: megawatt-hours stored.</summary>
	public double StoredMWh;

	/// <summary>Engines: the gas they burn. Reactors: the fuel item.</summary>
	public string FuelKey;

	/// <summary>Engines: MW·s per litre. Reactors: MW·s per kg of fuel.</summary>
	public double EnergyPerUnit;

	public bool On;
}

/// <summary>A planet's gravity and atmosphere, with the game's own formulas.</summary>
public class PlanetInfo
{
	public string Name;

	public Vector3D Center;

	public double MinRadius;

	public double MaxRadius;

	public double Falloff;

	/// <summary>Surface gravity in g.</summary>
	public double Intensity;

	/// <summary>Distance from the centre where gravity drops to 0.05 g and then stops.</summary>
	public double GravityLimit;

	public double AverageRadius;

	public bool HasAtmosphere;

	public double AtmosphereAltitude;

	public double AirDensity;

	/// <summary>Gravity in g at this distance from the centre (MySphericalNaturalGravityComponent.GetGravityMultiplier).</summary>
	public double GravityAt(double radius)
	{
		if (radius > GravityLimit)
		{
			return 0.0;
		}
		double multiplier = 1.0;
		if (radius > MaxRadius)
		{
			multiplier = Math.Pow(radius / MaxRadius, -Falloff);
		}
		else if (radius < MinRadius)
		{
			multiplier = Math.Max(0.01, radius / MinRadius);
		}
		return multiplier * Intensity;
	}

	/// <summary>Air density at this distance from the centre (MyPlanet.GetAirDensity).</summary>
	public double AirAt(double radius)
	{
		if (!HasAtmosphere || AtmosphereAltitude <= 0)
		{
			return 0.0;
		}
		return MathHelper.Clamp(1.0 - (radius - AverageRadius) / AtmosphereAltitude, 0.0, 1.0) * AirDensity;
	}

	public double AtmosphereTop => AverageRadius + (HasAtmosphere ? AtmosphereAltitude : 0.0);
}

/// <summary>Everything about one ship that the climb needs, read on the game thread.</summary>
public class ShipSnapshot
{
	public long Key;

	public string Name;

	public bool IsStatic;

	public int Grids;

	public double Mass;

	/// <summary>Where the directions are measured from: the cockpit, else the main grid.</summary>
	public MatrixD Reference;

	public string ReferenceName;

	public Vector3D Position;

	public double SpeedLimit;

	public PlanetInfo Planet;

	public readonly List<ThrusterInfo> Thrusters = new List<ThrusterInfo>();

	public readonly Dictionary<string, GasPool> Gas = new Dictionary<string, GasPool>();

	public readonly List<PowerSource> Sources = new List<PowerSource>();

	/// <summary>Reactor fuel items on the ship, in kg, by item id.</summary>
	public readonly Dictionary<string, double> ItemFuel = new Dictionary<string, double>();

	public readonly Dictionary<string, string> FuelNames = new Dictionary<string, string>();

	public int BlocksOff;

	public Vector3D Axis(Dir dir)
	{
		switch (dir)
		{
		case Dir.Up:
			return Reference.Up;
		case Dir.Down:
			return Reference.Down;
		case Dir.Forward:
			return Reference.Forward;
		case Dir.Backward:
			return Reference.Backward;
		case Dir.Left:
			return Reference.Left;
		default:
			return Reference.Right;
		}
	}

	public double Radius => Planet == null ? 0.0 : Vector3D.Distance(Position, Planet.Center);

	public string FuelName(string key)
	{
		return key != null && FuelNames.TryGetValue(key, out string name) ? name : key ?? "Electricity";
	}
}
