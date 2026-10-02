using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace FatAlbert;

public enum AscentOutcome
{
	Made,
	NoGravity,
	NoThrusters,
	TooHeavy,
	Stalled,
	OutOfFuel,
	TooSlow
}

public class AscentResult
{
	public AscentOutcome Outcome;

	public Dir Direction;

	public double Mass;

	public double StartRadius;

	public double TargetRadius;

	/// <summary>Thrust pushing the chosen way at the start, in newtons.</summary>
	public double StartThrust;

	public double StartGravity;

	public double StartTwr;

	/// <summary>Lowest thrust-to-weight on the way up, and the height above the start where it happened.</summary>
	public double MinTwr = double.MaxValue;

	public double MinTwrHeight;

	/// <summary>Height above the start that the ship reached (the whole way when it made it).</summary>
	public double Height;

	public double Seconds;

	public double TopSpeed;

	/// <summary>What ran out first, and the height above the start where it did.</summary>
	public string RanOut;

	public double RanOutHeight;

	/// <summary>True when electric thrusters wanted more power than the ship could give at some point.</summary>
	public bool PowerShort;

	public double PowerShortHeight;

	public readonly Dictionary<string, double> GasUsed = new Dictionary<string, double>();

	public readonly Dictionary<string, double> ItemUsed = new Dictionary<string, double>();

	public double BatteryUsed;

	/// <summary>Heaviest the ship could be and still make it, in kg (filled in by Ascent.Solve).</summary>
	public double MaxMass;

	/// <summary>Heaviest the ship could be and still lift off from where it is, in kg.</summary>
	public double MaxLiftoffMass;

	public bool Success => Outcome == AscentOutcome.Made;
}

/// <summary>
/// Flies the ship straight up from where it is, one half-second step at a time: full thrust until the speed limit,
/// then just enough to hold that speed, as the dampeners would. Each step works out gravity and air density at the
/// ship's height, what every thruster can push there, and burns gas, battery charge and reactor fuel in the game's
/// own order. Plain maths on a snapshot, so it runs on a background thread.
/// </summary>
internal static class Ascent
{
	private const double Step = 0.5;

	private const double MaxSeconds = 4 * 3600;

	/// <summary>Thrusters at more than about 87° off the lift direction add nothing worth counting.</summary>
	private const double MinShare = 0.05;

	/// <summary>Thrusters of one kind pushing the chosen way, added together so big ships climb as fast as small ones.</summary>
	private class Active
	{
		public ThrusterInfo Thruster;

		/// <summary>Sum of full thrust × how much of it points the chosen way.</summary>
		public double Force;

		/// <summary>Sum of full-thrust power; a thruster at an angle still burns its full power.</summary>
		public double Power;
	}

	/// <summary>The direction that points most away from the planet right now, else the strongest.</summary>
	public static Dir AutoDirection(ShipSnapshot ship)
	{
		Dir[] dirs = (Dir[])Enum.GetValues(typeof(Dir));
		if (ship.Planet != null && ship.Radius > 1.0)
		{
			Vector3D up = Vector3D.Normalize(ship.Position - ship.Planet.Center);
			return dirs.OrderByDescending(d => Vector3D.Dot(ship.Axis(d), up)).First();
		}
		return dirs.OrderByDescending(d => VacuumThrust(ship, d)).First();
	}

	/// <summary>Thrust pushing this way with no atmosphere penalty, in newtons.</summary>
	public static double VacuumThrust(ShipSnapshot ship, Dir dir)
	{
		Vector3D axis = ship.Axis(dir);
		return ship.Thrusters.Sum(t => Math.Max(0.0, Vector3D.Dot(t.Push, axis)) * t.Force);
	}

	/// <summary>Thrust pushing this way at the ship's position, before fuel and power limits, in newtons.</summary>
	public static double ThrustHere(ShipSnapshot ship, Dir dir)
	{
		Vector3D axis = ship.Axis(dir);
		double air = ship.Planet?.AirAt(ship.Radius) ?? 0.0;
		bool atmosphere = ship.Planet?.HasAtmosphere ?? false;
		return ship.Thrusters.Sum(t => Math.Max(0.0, Vector3D.Dot(t.Push, axis)) * t.Force * t.Effectiveness(air, atmosphere));
	}

	/// <summary>Where the climb ends: the gravity limit, or the player's own distance.</summary>
	public static double TargetRadius(ShipSnapshot ship, double? distance)
	{
		if (ship.Planet == null)
		{
			return 0.0;
		}
		return distance.HasValue ? ship.Radius + distance.Value : ship.Planet.GravityLimit;
	}

	/// <summary>Runs the climb for the ship as it is, then searches for the heaviest mass that still makes it.</summary>
	public static AscentResult Solve(ShipSnapshot ship, Dir dir, double? distance, double speed)
	{
		AscentResult result = Run(ship, dir, ship.Mass, distance, speed);
		if (result.Outcome == AscentOutcome.NoGravity || result.Outcome == AscentOutcome.NoThrusters || result.StartGravity <= 0)
		{
			return result;
		}
		result.MaxLiftoffMass = result.StartThrust / result.StartGravity;
		// Nothing heavier than the lift-off limit can make it, so search below that.
		double low = 0.0;
		double high = result.MaxLiftoffMass;
		for (int i = 0; i < 24 && high - low > Math.Max(1.0, high * 0.001); i++)
		{
			double mid = (low + high) / 2.0;
			if (Run(ship, dir, mid, distance, speed).Success)
			{
				low = mid;
			}
			else
			{
				high = mid;
			}
		}
		result.MaxMass = low;
		return result;
	}

	public static AscentResult Run(ShipSnapshot ship, Dir dir, double mass, double? distance, double speed)
	{
		AscentResult result = new AscentResult { Direction = dir, Mass = mass };
		PlanetInfo planet = ship.Planet;
		double r0 = ship.Radius;
		result.StartRadius = r0;
		if (planet == null || planet.GravityAt(r0) <= 0.0)
		{
			result.Outcome = AscentOutcome.NoGravity;
			return result;
		}
		double target = TargetRadius(ship, distance);
		result.TargetRadius = target;

		Vector3D axis = ship.Axis(dir);
		Dictionary<string, Active> kinds = new Dictionary<string, Active>();
		foreach (ThrusterInfo thruster in ship.Thrusters)
		{
			double share = Vector3D.Dot(thruster.Push, axis);
			if (share < MinShare)
			{
				continue;
			}
			string kind = $"{thruster.FuelKey}|{thruster.LitresPerMWs}|{thruster.MinInfluence}|{thruster.MaxInfluence}|{thruster.EffectAtMin}|{thruster.EffectAtMax}|{thruster.NeedsAtmosphere}|{thruster.ConsumptionPerG}";
			if (!kinds.TryGetValue(kind, out Active entry))
			{
				entry = new Active { Thruster = thruster };
				kinds[kind] = entry;
			}
			entry.Force += thruster.Force * share;
			entry.Power += thruster.MaxPower;
		}
		List<Active> active = kinds.Values.ToList();
		if (active.Count == 0)
		{
			result.Outcome = AscentOutcome.NoThrusters;
			return result;
		}

		// Working copies of everything the climb burns.
		Dictionary<string, double> gas = ship.Gas.ToDictionary(p => p.Key, p => p.Value.Litres);
		Dictionary<string, double> items = new Dictionary<string, double>(ship.ItemFuel);
		List<PowerSource> sources = ship.Sources.Where(s => s.MaxOutput > 0).OrderBy(s => s.Priority).ToList();
		double[] charge = sources.Select(s => s.StoredMWh).ToArray();
		Dictionary<string, double> gasRate = new Dictionary<string, double>();

		double height = 0.0;
		double velocity = 0.0;
		double seconds = 0.0;
		bool first = true;
		while (true)
		{
			double radius = r0 + height;
			if (radius >= target)
			{
				result.Outcome = AscentOutcome.Made;
				break;
			}
			if (seconds > MaxSeconds)
			{
				result.Outcome = AscentOutcome.TooSlow;
				break;
			}
			double gravityG = planet.GravityAt(radius);
			double gravity = gravityG * 9.81;
			double air = planet.AirAt(radius);

			double gasThrust = 0.0;
			double electricThrust = 0.0;
			double demand = 0.0;
			gasRate.Clear();
			foreach (Active entry in active)
			{
				ThrusterInfo thruster = entry.Thruster;
				if (!thruster.Electric && (!gas.TryGetValue(thruster.FuelKey, out double left) || left <= 0.0))
				{
					continue;
				}
				double effect = thruster.Effectiveness(air, planet.HasAtmosphere);
				double force = entry.Force * effect;
				// MyEntityThrustComponent: full-thrust power scales with the atmosphere factor and the gravity factor.
				double power = entry.Power * effect * thruster.ConsumptionMultiplier(gravityG);
				if (thruster.Electric)
				{
					electricThrust += force;
					demand += power;
				}
				else
				{
					gasThrust += force;
					gasRate[thruster.FuelKey] = (gasRate.TryGetValue(thruster.FuelKey, out double rate) ? rate : 0.0) + power * thruster.LitresPerMWs;
				}
			}

			double supply = 0.0;
			for (int i = 0; i < sources.Count; i++)
			{
				if (HasFuel(sources[i], charge[i], gas, items))
				{
					supply += sources[i].MaxOutput;
				}
			}
			double powerShare = demand > 0.0 ? Math.Min(1.0, supply / demand) : 0.0;
			if (demand > 0.0 && powerShare < 0.999 && !result.PowerShort)
			{
				result.PowerShort = true;
				result.PowerShortHeight = height;
			}

			double thrust = gasThrust + electricThrust * powerShare;
			double weight = mass * gravity;
			double twr = weight > 0.0 ? thrust / weight : double.MaxValue;
			if (first)
			{
				result.StartThrust = thrust;
				result.StartGravity = gravity;
				result.StartTwr = twr;
			}
			if (twr < result.MinTwr)
			{
				result.MinTwr = twr;
				result.MinTwrHeight = height;
			}

			// Full thrust up to the speed limit, then just enough to hold it.
			double throttle = velocity < speed - 1e-6 ? 1.0 : (thrust > 0.0 ? Math.Min(1.0, weight / thrust) : 0.0);
			double acceleration = mass > 0.0 ? (throttle * thrust - weight) / mass : 0.0;
			double newVelocity = Math.Min(speed, velocity + acceleration * Step);
			if (newVelocity <= 0.0)
			{
				if (first)
				{
					result.Outcome = AscentOutcome.TooHeavy;
				}
				else
				{
					result.Outcome = result.RanOut != null ? AscentOutcome.OutOfFuel : AscentOutcome.Stalled;
				}
				break;
			}
			first = false;
			height += (velocity + newVelocity) / 2.0 * Step;
			velocity = newVelocity;
			result.TopSpeed = Math.Max(result.TopSpeed, velocity);
			seconds += Step;

			foreach (KeyValuePair<string, double> rate in gasRate)
			{
				Burn(gas, rate.Key, throttle * rate.Value * Step, result.GasUsed, result, height);
			}
			double draw = throttle * powerShare * demand;
			for (int i = 0; i < sources.Count && draw > 0.0; i++)
			{
				PowerSource source = sources[i];
				if (!HasFuel(source, charge[i], gas, items))
				{
					continue;
				}
				double take = Math.Min(draw, source.MaxOutput);
				draw -= take;
				switch (source.Kind)
				{
				case SourceKind.Battery:
				{
					double used = Math.Min(charge[i], take * Step / 3600.0);
					charge[i] -= used;
					result.BatteryUsed += used;
					if (charge[i] <= 0.0 && sources.Where((s, j) => s.Kind == SourceKind.Battery && charge[j] > 0.0).Count() == 0)
					{
						NoteRanOut(result, "Batteries", height);
					}
					break;
				}
				case SourceKind.Reactor:
					if (source.EnergyPerUnit > 0.0)
					{
						Burn(items, source.FuelKey, take * Step / source.EnergyPerUnit, result.ItemUsed, result, height);
					}
					break;
				case SourceKind.Engine:
					if (source.EnergyPerUnit > 0.0)
					{
						Burn(gas, source.FuelKey, take * Step / source.EnergyPerUnit, result.GasUsed, result, height);
					}
					break;
				}
			}
		}
		result.Height = height;
		result.Seconds = seconds;
		if (result.MinTwr == double.MaxValue)
		{
			result.MinTwr = result.StartTwr;
		}
		// The names in RanOut are fuel keys until here.
		if (result.RanOut != null && result.RanOut != "Batteries")
		{
			result.RanOut = ship.FuelName(result.RanOut);
		}
		return result;
	}

	private static bool HasFuel(PowerSource source, double charge, Dictionary<string, double> gas, Dictionary<string, double> items)
	{
		switch (source.Kind)
		{
		case SourceKind.Battery:
			return charge > 0.0;
		case SourceKind.Reactor:
			return items.TryGetValue(source.FuelKey, out double kg) && kg > 0.0;
		case SourceKind.Engine:
			return gas.TryGetValue(source.FuelKey, out double litres) && litres > 0.0;
		default:
			return true;
		}
	}

	private static void Burn(Dictionary<string, double> pool, string key, double amount, Dictionary<string, double> used, AscentResult result, double height)
	{
		if (key == null || amount <= 0.0 || !pool.TryGetValue(key, out double left) || left <= 0.0)
		{
			return;
		}
		double take = Math.Min(left, amount);
		pool[key] = left - take;
		used[key] = (used.TryGetValue(key, out double total) ? total : 0.0) + take;
		if (left - take <= 0.0)
		{
			NoteRanOut(result, key, height);
		}
	}

	private static void NoteRanOut(AscentResult result, string what, double height)
	{
		if (result.RanOut == null)
		{
			result.RanOut = what;
			result.RanOutHeight = height;
		}
	}
}
