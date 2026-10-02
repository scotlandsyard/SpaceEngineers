using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace FatAlbert;

public enum AscentOutcome
{
	Made,
	NoGravity,
	NoMass,
	NoThrusters,
	CantLand,
	TooHeavy,
	Stalled,
	OutOfFuel,
	TooSlow
}

/// <summary>
/// What to fly: from where the ship is now, or a visit to another planet (fall in, brake to land at sea level,
/// then climb back out on what's left).
/// </summary>
public class AscentPlan
{
	public PlanetInfo Planet;

	public double StartRadius;

	public bool Land;

	public Dir Dir;

	/// <summary>Metres to climb; null climbs to the planet's gravity limit.</summary>
	public double? Distance;

	public double Speed;

	/// <summary>Speed the ship falls in at before braking: the world's speed limit, which costs no fuel to reach.</summary>
	public double FallSpeed;

	/// <summary>Parachutes to open for the landing (ones with canopy material), or null to land on thrusters alone.</summary>
	public List<ParachuteInfo> Chutes;

	public double TargetRadius => Distance.HasValue ? StartRadius + Distance.Value : Planet?.GravityLimit ?? 0.0;

	public static AscentPlan Here(ShipSnapshot ship, Dir dir, double? distance, double speed)
	{
		return new AscentPlan { Planet = ship.Planet, StartRadius = ship.Radius, Dir = dir, Distance = distance, Speed = speed, FallSpeed = ship.SpeedLimit };
	}

	public static AscentPlan Visit(ShipSnapshot ship, PlanetInfo planet, Dir dir, double? distance, double speed, bool useChutes)
	{
		return new AscentPlan { Planet = planet, StartRadius = planet.AverageRadius, Land = true, Dir = dir, Distance = distance, Speed = speed, FallSpeed = ship.SpeedLimit, Chutes = useChutes ? ship.UsableParachutes() : null };
	}
}

public class AscentResult
{
	public AscentOutcome Outcome;

	public Dir Direction;

	public double Mass;

	public double StartRadius;

	public double TargetRadius;

	/// <summary>Thrust pushing the chosen way at the start of the climb, in newtons.</summary>
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

	/// <summary>Totals for the whole trip, landing included.</summary>
	public readonly Dictionary<string, double> GasUsed = new Dictionary<string, double>();

	public readonly Dictionary<string, double> ItemUsed = new Dictionary<string, double>();

	public double BatteryUsed;

	/// <summary>Landing only: braking time, how high braking starts, and what it burns.</summary>
	public bool Landed;

	public double LandingSeconds;

	public double LandingHeight;

	public readonly Dictionary<string, double> LandingGas = new Dictionary<string, double>();

	public readonly Dictionary<string, double> LandingItems = new Dictionary<string, double>();

	public double LandingBattery;

	/// <summary>Parachutes that opened, the speed they brought the ship down to, and the speed it touched down at.</summary>
	public int ChuteCount;

	public double ChuteSpeed;

	public double TouchdownSpeed;

	/// <summary>Heaviest the ship could be and still make it, in kg (filled in by Ascent.Solve).</summary>
	public double MaxMass;

	/// <summary>Heaviest the ship could be and still lift off from the start, in kg.</summary>
	public double MaxLiftoffMass;

	public bool Success => Outcome == AscentOutcome.Made;
}

/// <summary>
/// Flies the ship straight up, one half-second step at a time: full thrust until the speed limit, then just enough
/// to hold that speed, as the dampeners would. Each step works out gravity and air density at the ship's height,
/// what every thruster can push there, and burns gas, battery charge and reactor fuel in the game's own order.
/// A visit to another planet first brakes from the fall speed to land at sea level, at full thrust. Plain maths on
/// a snapshot, so it runs on a background thread.
/// </summary>
internal static class Ascent
{
	private const double Step = 0.5;

	private const double MaxSeconds = 4 * 3600;

	/// <summary>An hour of half-second braking steps, plus a little for the final short one.</summary>
	private const int MaxLandingSteps = 7300;

	/// <summary>Touching down on parachutes alone counts as a landing at or below this speed (m/s).</summary>
	public const double SafeTouchdown = 5.0;

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

	/// <summary>The fuel and power left on board during one flight, and what the thrusters can do at a given height.</summary>
	private class Flight
	{
		private readonly ShipSnapshot _ship;

		private readonly PlanetInfo _planet;

		private readonly List<Active> _active;

		private readonly AscentResult _result;

		private readonly Dictionary<string, double> _gas;

		private readonly Dictionary<string, double> _items;

		private readonly List<PowerSource> _sources;

		private readonly double[] _charge;

		private readonly Dictionary<string, double> _gasRate = new Dictionary<string, double>();

		private double _demand;

		/// <summary>Full thrust at the last Measure, after fuel and power limits.</summary>
		public double Thrust;

		/// <summary>Share of the electric thrusters' full-thrust power the ship can make (1 = enough).</summary>
		public double PowerShare;

		public Flight(ShipSnapshot ship, PlanetInfo planet, List<Active> active, AscentResult result)
		{
			_ship = ship;
			_planet = planet;
			_active = active;
			_result = result;
			_gas = ship.Gas.ToDictionary(p => p.Key, p => p.Value.Litres);
			_items = new Dictionary<string, double>(ship.ItemFuel);
			_sources = ship.Sources.Where(s => s.MaxOutput > 0).OrderBy(s => s.Priority).ToList();
			_charge = _sources.Select(s => s.StoredMWh).ToArray();
		}

		public void Measure(double radius, double gravityG)
		{
			double air = _planet.AirAt(radius);
			double gasThrust = 0.0;
			double electricThrust = 0.0;
			_demand = 0.0;
			_gasRate.Clear();
			foreach (Active entry in _active)
			{
				ThrusterInfo thruster = entry.Thruster;
				if (!thruster.Electric && (!_gas.TryGetValue(thruster.FuelKey, out double left) || left <= 0.0))
				{
					continue;
				}
				double effect = thruster.Effectiveness(air, _planet.HasAtmosphere);
				double force = entry.Force * effect;
				// MyEntityThrustComponent: full-thrust power scales with the atmosphere factor and the gravity factor.
				double power = entry.Power * effect * thruster.ConsumptionMultiplier(gravityG);
				if (thruster.Electric)
				{
					electricThrust += force;
					_demand += power;
				}
				else
				{
					gasThrust += force;
					_gasRate[thruster.FuelKey] = (_gasRate.TryGetValue(thruster.FuelKey, out double rate) ? rate : 0.0) + power * thruster.LitresPerMWs;
				}
			}

			double supply = 0.0;
			for (int i = 0; i < _sources.Count; i++)
			{
				if (HasFuel(i))
				{
					supply += _sources[i].MaxOutput;
				}
			}
			PowerShare = _demand > 0.0 ? Math.Min(1.0, supply / _demand) : 0.0;
			Thrust = gasThrust + electricThrust * PowerShare;
		}

		public bool PowerShort => _demand > 0.0 && PowerShare < 0.999;

		/// <summary>Burns what the thrusters used at this throttle for this long, as measured last.</summary>
		public void Consume(double throttle, double seconds, double height)
		{
			foreach (KeyValuePair<string, double> rate in _gasRate)
			{
				Burn(_gas, rate.Key, throttle * rate.Value * seconds, _result.GasUsed, height);
			}
			double draw = throttle * PowerShare * _demand;
			for (int i = 0; i < _sources.Count && draw > 0.0; i++)
			{
				PowerSource source = _sources[i];
				if (!HasFuel(i))
				{
					continue;
				}
				double take = Math.Min(draw, source.MaxOutput);
				draw -= take;
				switch (source.Kind)
				{
				case SourceKind.Battery:
				{
					double used = Math.Min(_charge[i], take * seconds / 3600.0);
					_charge[i] -= used;
					_result.BatteryUsed += used;
					if (_charge[i] <= 0.0 && !_sources.Where((s, j) => s.Kind == SourceKind.Battery && _charge[j] > 0.0).Any())
					{
						NoteRanOut("Batteries", height);
					}
					break;
				}
				case SourceKind.Reactor:
					if (source.EnergyPerUnit > 0.0)
					{
						Burn(_items, source.FuelKey, take * seconds / source.EnergyPerUnit, _result.ItemUsed, height);
					}
					break;
				case SourceKind.Engine:
					if (source.EnergyPerUnit > 0.0)
					{
						Burn(_gas, source.FuelKey, take * seconds / source.EnergyPerUnit, _result.GasUsed, height);
					}
					break;
				}
			}
		}

		private bool HasFuel(int index)
		{
			PowerSource source = _sources[index];
			switch (source.Kind)
			{
			case SourceKind.Battery:
				return _charge[index] > 0.0;
			case SourceKind.Reactor:
				return _items.TryGetValue(source.FuelKey, out double kg) && kg > 0.0;
			case SourceKind.Engine:
				return _gas.TryGetValue(source.FuelKey, out double litres) && litres > 0.0;
			default:
				return true;
			}
		}

		private void Burn(Dictionary<string, double> pool, string key, double amount, Dictionary<string, double> used, double height)
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
				NoteRanOut(key, height);
			}
		}

		private void NoteRanOut(string what, double height)
		{
			if (_result.RanOut == null)
			{
				_result.RanOut = what == "Batteries" ? what : _ship.FuelName(what);
				_result.RanOutHeight = height;
			}
		}
	}

	/// <summary>Within this height above a planet's highest ground, a ship counts as sitting on it (can't turn over).</summary>
	public const double GroundHeight = 1000.0;

	/// <summary>True when the ship is on or near the ground of the planet it's on, so it lifts with the side facing up.</summary>
	public static bool OnGround(ShipSnapshot ship)
	{
		return ship.Planet != null && ship.Planet.GravityAt(ship.Radius) > 0.0 && ship.Radius - ship.Planet.MaxRadius < GroundHeight;
	}

	/// <summary>
	/// Auto direction. On the ground: the side facing away from the planet, since it can't turn over. Anywhere else
	/// (flying, in orbit, or visiting a planet): the side with the most thrust where the climb starts, since you'll
	/// point that one up.
	/// </summary>
	public static Dir AutoDirection(ShipSnapshot ship, PlanetInfo visiting)
	{
		Dir[] dirs = (Dir[])Enum.GetValues(typeof(Dir));
		if (visiting == null && OnGround(ship))
		{
			Vector3D up = Vector3D.Normalize(ship.Position - ship.Planet.Center);
			return dirs.OrderByDescending(d => Vector3D.Dot(ship.Axis(d), up)).First();
		}
		PlanetInfo planet = visiting ?? ship.Planet;
		double radius = visiting != null || planet == null ? planet?.AverageRadius ?? 0.0 : ship.Radius;
		return dirs.OrderByDescending(d => planet == null ? VacuumThrust(ship, d) : ThrustAt(ship, d, planet, radius)).First();
	}

	/// <summary>Thrust pushing this way with no atmosphere penalty, in newtons.</summary>
	public static double VacuumThrust(ShipSnapshot ship, Dir dir)
	{
		Vector3D axis = ship.Axis(dir);
		return ship.Thrusters.Sum(t => Math.Max(0.0, Vector3D.Dot(t.Push, axis)) * t.Force);
	}

	/// <summary>Thrust pushing this way at this distance from a planet's centre, before fuel and power limits, in newtons.</summary>
	public static double ThrustAt(ShipSnapshot ship, Dir dir, PlanetInfo planet, double radius)
	{
		Vector3D axis = ship.Axis(dir);
		double air = planet?.AirAt(radius) ?? 0.0;
		bool atmosphere = planet?.HasAtmosphere ?? false;
		return ship.Thrusters.Sum(t => Math.Max(0.0, Vector3D.Dot(t.Push, axis)) * t.Force * t.Effectiveness(air, atmosphere));
	}

	/// <summary>Runs the trip for the ship as it is, then searches for the heaviest mass that still makes it.</summary>
	public static AscentResult Solve(ShipSnapshot ship, AscentPlan plan)
	{
		AscentResult result = Run(ship, plan, ship.Mass);
		if (result.Outcome == AscentOutcome.NoGravity || result.Outcome == AscentOutcome.NoMass || result.Outcome == AscentOutcome.NoThrusters || result.StartGravity <= 0)
		{
			return result;
		}
		result.MaxLiftoffMass = result.StartThrust / result.StartGravity;
		// Nothing heavier than the lift-off limit with full tanks can make it, so search below that.
		double low = 0.0;
		double high = Run(ship, plan, 1.0).MaxLiftoffLimit();
		for (int i = 0; i < 24 && high - low > Math.Max(1.0, high * 0.001); i++)
		{
			double mid = (low + high) / 2.0;
			if (Run(ship, plan, mid).Success)
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

	private static double MaxLiftoffLimit(this AscentResult result)
	{
		return result.StartGravity > 0 ? result.StartThrust / result.StartGravity : 0.0;
	}

	public static AscentResult Run(ShipSnapshot ship, AscentPlan plan, double mass)
	{
		AscentResult result = new AscentResult { Direction = plan.Dir, Mass = mass };
		PlanetInfo planet = plan.Planet;
		double r0 = plan.StartRadius;
		result.StartRadius = r0;
		if (mass <= 0.0)
		{
			// A mass of 0 means the ship couldn't be read properly; every number after this would be nonsense.
			result.Outcome = AscentOutcome.NoMass;
			return result;
		}
		if (planet == null || planet.GravityAt(r0) <= 0.0)
		{
			result.Outcome = AscentOutcome.NoGravity;
			return result;
		}
		double target = plan.TargetRadius;
		result.TargetRadius = target;

		Vector3D axis = ship.Axis(plan.Dir);
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
		if (kinds.Count == 0)
		{
			result.Outcome = AscentOutcome.NoThrusters;
			return result;
		}
		Flight flight = new Flight(ship, planet, kinds.Values.ToList(), result);

		if (plan.Land && !Land(flight, plan, mass, result))
		{
			return result;
		}

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
			flight.Measure(radius, gravityG);
			if (flight.PowerShort && !result.PowerShort)
			{
				result.PowerShort = true;
				result.PowerShortHeight = height;
			}

			double thrust = flight.Thrust;
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
			double throttle = velocity < plan.Speed - 1e-6 ? 1.0 : (thrust > 0.0 ? Math.Min(1.0, weight / thrust) : 0.0);
			double acceleration = mass > 0.0 ? (throttle * thrust - weight) / mass : 0.0;
			double newVelocity = Math.Min(plan.Speed, velocity + acceleration * Step);
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
			flight.Consume(throttle, Step, height);
		}
		result.Height = height;
		result.Seconds = seconds;
		if (result.MinTwr == double.MaxValue)
		{
			result.MinTwr = result.StartTwr;
		}
		return result;
	}

	/// <summary>
	/// Falling in costs nothing: the game caps the ship's speed, so it drops at the speed limit with the thrusters
	/// idle. With parachutes (and air thick enough to open them) it comes down at their terminal speed instead.
	/// Landing is the braking at the bottom: full thrust (plus the parachutes' drag) from that speed to a stop,
	/// worked out with sea level's gravity and air. If the parachutes alone get it down to SafeTouchdown, it lands
	/// even when the thrusters couldn't hold it up. False (and CantLand) when it can't slow down enough.
	/// </summary>
	private static bool Land(Flight flight, AscentPlan plan, double mass, AscentResult result)
	{
		PlanetInfo planet = plan.Planet;
		double gravityG = planet.GravityAt(plan.StartRadius);
		double weight = mass * gravityG * 9.81;
		double velocity = plan.FallSpeed;

		// Drag of every parachute that has canopy material and air to open in, per (m/s)².
		double drag = 0.0;
		if (plan.Chutes != null && planet.HasAtmosphere)
		{
			double air = planet.AirAt(plan.StartRadius);
			foreach (ParachuteInfo chute in plan.Chutes)
			{
				double factor = chute.DragFactor(air);
				if (factor > 0.0)
				{
					drag += factor;
					result.ChuteCount++;
				}
			}
		}
		if (drag > 0.0)
		{
			velocity = Math.Min(velocity, Math.Sqrt(weight / drag));
			result.ChuteSpeed = velocity;
		}

		double seconds = 0.0;
		double distance = 0.0;
		bool first = true;
		// The step cap is a backstop: the last step always ends exactly at a stop (below).
		for (int steps = 0; velocity > 0.0; steps++)
		{
			flight.Measure(plan.StartRadius, gravityG);
			if (first)
			{
				// Shown as the lift-off numbers when it can't even land.
				result.StartThrust = flight.Thrust;
				result.StartGravity = gravityG * 9.81;
				result.StartTwr = weight > 0.0 ? flight.Thrust / weight : double.MaxValue;
				first = false;
			}
			double deceleration = (flight.Thrust - weight + drag * velocity * velocity) / mass;
			if (deceleration <= 0.0 || steps > MaxLandingSteps)
			{
				if (drag > 0.0 && velocity <= SafeTouchdown)
				{
					// Can't slow down any more, but the parachutes have it slow enough to touch down.
					break;
				}
				result.Outcome = AscentOutcome.CantLand;
				return false;
			}
			double dt = Math.Min(Step, velocity / deceleration);
			distance += (velocity - deceleration * dt / 2.0) * dt;
			// Rounding can leave a sliver of speed after the final step, which would loop forever; that step stops it.
			velocity = dt < Step ? 0.0 : velocity - deceleration * dt;
			seconds += dt;
			flight.Consume(1.0, dt, 0.0);
		}
		result.Landed = true;
		result.TouchdownSpeed = velocity;
		result.LandingSeconds = seconds;
		result.LandingHeight = distance;
		result.LandingBattery = result.BatteryUsed;
		foreach (KeyValuePair<string, double> used in result.GasUsed)
		{
			result.LandingGas[used.Key] = used.Value;
		}
		foreach (KeyValuePair<string, double> used in result.ItemUsed)
		{
			result.LandingItems[used.Key] = used.Value;
		}
		return true;
	}
}
