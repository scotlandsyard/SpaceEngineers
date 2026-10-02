using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IngameChargeMode = Sandbox.ModAPI.Ingame.ChargeMode;

namespace FatAlbert;

/// <summary>
/// Reads ships on the game thread into plain snapshots. Only reads: nothing here changes the ship, so it works the
/// same on any server. Block lists are cached per ship and rebuilt every few seconds, so refreshing the window
/// once a second only reads values.
/// </summary>
internal static class ShipReader
{
	public class ShipEntry
	{
		public long Key;

		public string Name;

		public double Distance;
	}

	private class BlockCache
	{
		public double BuiltAt;

		public readonly List<IMyCubeGrid> Grids = new List<IMyCubeGrid>();

		public readonly List<MyThrust> Thrusters = new List<MyThrust>();

		public readonly List<MyGasTank> Tanks = new List<MyGasTank>();

		public readonly List<MyCubeBlock> Producers = new List<MyCubeBlock>();

		public readonly List<MyCubeBlock> Inventories = new List<MyCubeBlock>();

		public readonly List<IMyShipController> Controllers = new List<IMyShipController>();
	}

	private const double CacheSeconds = 10.0;

	private static readonly Dictionary<long, BlockCache> s_cache = new Dictionary<long, BlockCache>();

	private static Dictionary<string, int> s_priorities;

	public static void Clear()
	{
		s_cache.Clear();
		s_priorities = null;
	}

	/// <summary>The player's ships near them (not stations), nearest first. The ship they're flying always comes first.</summary>
	public static List<ShipEntry> FindShips(double range)
	{
		List<ShipEntry> result = new List<ShipEntry>();
		IMyPlayer player = MyAPIGateway.Session?.Player;
		if (player == null)
		{
			return result;
		}
		Vector3D position = player.GetPosition();
		long controlledKey = ControlledShipKey();
		HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
		MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);
		HashSet<long> seen = new HashSet<long>();
		foreach (IMyEntity entity in entities)
		{
			IMyCubeGrid grid = (IMyCubeGrid)entity;
			if (grid.Physics == null || grid.IsStatic || grid.MarkedForClose)
			{
				continue;
			}
			double distance = Vector3D.Distance(position, grid.WorldAABB.Center);
			if (distance > range)
			{
				continue;
			}
			IMyCubeGrid main = MainGrid(grid);
			if (!seen.Add(main.EntityId))
			{
				continue;
			}
			bool mine = main.EntityId == controlledKey || main.BigOwners.Contains(player.IdentityId);
			if (!mine || GroupHasStatic(main))
			{
				continue;
			}
			result.Add(new ShipEntry { Key = main.EntityId, Name = main.CustomName, Distance = Vector3D.Distance(position, main.WorldAABB.Center) });
		}
		return result.OrderBy(s => s.Key == controlledKey ? 0 : 1).ThenBy(s => s.Distance).ToList();
	}

	/// <summary>The ship the player is sitting in, or 0.</summary>
	public static long ControlledShipKey()
	{
		IMyEntity controlled = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity;
		return controlled is IMyCubeBlock block && block.CubeGrid != null ? MainGrid(block.CubeGrid).EntityId : 0;
	}

	/// <summary>The biggest grid joined by rotors, pistons and hinges; it names and keys the ship.</summary>
	public static IMyCubeGrid MainGrid(IMyCubeGrid grid)
	{
		List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, grids);
		return grids.Count == 0 ? grid : grids.OrderByDescending(g => ((MyCubeGrid)g).BlocksCount).ThenBy(g => g.EntityId).First();
	}

	private static bool GroupHasStatic(IMyCubeGrid grid)
	{
		List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
		MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, grids);
		return grids.Any(g => g.IsStatic);
	}

	public static ShipSnapshot Read(long key, bool countOff, bool rebuild)
	{
		if (!(MyAPIGateway.Entities.GetEntityById(key) is IMyCubeGrid main) || main.MarkedForClose)
		{
			s_cache.Remove(key);
			return null;
		}
		double now = FatAlbertSession.Now;
		if (rebuild || !s_cache.TryGetValue(key, out BlockCache cache) || now - cache.BuiltAt > CacheSeconds || cache.Grids.Any(g => g.MarkedForClose))
		{
			cache = Build(main, now);
			s_cache[key] = cache;
		}

		ShipSnapshot ship = new ShipSnapshot
		{
			Key = key,
			Name = main.CustomName,
			Grids = cache.Grids.Count,
			IsStatic = cache.Grids.Any(g => g.IsStatic),
			SpeedLimit = MyGridPhysics.GetShipMaxLinearVelocity(main.GridSizeEnum)
		};
		foreach (IMyCubeGrid grid in cache.Grids)
		{
			if (!grid.IsStatic && grid.Physics != null)
			{
				ship.Mass += grid.Physics.Mass;
			}
		}

		IMyShipController reference = PickReference(cache);
		ship.Reference = reference?.WorldMatrix ?? main.WorldMatrix;
		ship.ReferenceName = reference?.CustomName;
		ship.Position = main.Physics?.CenterOfMassWorld ?? main.WorldAABB.Center;
		ship.Planet = ReadPlanet(ship.Position);

		ReadThrusters(ship, cache, countOff);
		ReadTanks(ship, cache, countOff);
		ReadSources(ship, cache, countOff);
		return ship;
	}

	private static BlockCache Build(IMyCubeGrid main, double now)
	{
		BlockCache cache = new BlockCache { BuiltAt = now };
		MyAPIGateway.GridGroups.GetGroup(main, GridLinkTypeEnum.Mechanical, cache.Grids);
		if (cache.Grids.Count == 0)
		{
			cache.Grids.Add(main);
		}
		foreach (IMyCubeGrid grid in cache.Grids)
		{
			foreach (MyCubeBlock block in ((MyCubeGrid)grid).GetFatBlocks())
			{
				switch (block)
				{
				case MyThrust thrust:
					cache.Thrusters.Add(thrust);
					break;
				case MyGasTank tank:
					cache.Tanks.Add(tank);
					break;
				case IMyPowerProducer _:
					cache.Producers.Add(block);
					break;
				case IMyShipController controller:
					cache.Controllers.Add(controller);
					break;
				}
				if (block.InventoryCount > 0)
				{
					cache.Inventories.Add(block);
				}
			}
		}
		return cache;
	}

	/// <summary>The cockpit the player sits in, else the main cockpit, else any cockpit or remote control.</summary>
	private static IMyShipController PickReference(BlockCache cache)
	{
		IMyEntity controlled = MyAPIGateway.Session?.Player?.Controller?.ControlledEntity?.Entity;
		IMyShipController seat = cache.Controllers.FirstOrDefault(c => c == controlled && !c.MarkedForClose);
		return seat
			?? cache.Controllers.FirstOrDefault(c => c.IsMainCockpit && !c.MarkedForClose)
			?? cache.Controllers.FirstOrDefault(c => c.CanControlShip && !c.MarkedForClose)
			?? cache.Controllers.FirstOrDefault(c => !c.MarkedForClose);
	}

	private static PlanetInfo ReadPlanet(Vector3D position)
	{
		return ReadPlanet(MyGamePruningStructure.GetClosestPlanet(position));
	}

	/// <summary>Every planet and moon in the world, nearest to the player first. Planets are always loaded.</summary>
	public static List<PlanetInfo> ReadPlanets()
	{
		Vector3D position = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		List<PlanetInfo> planets = MyPlanets.GetPlanets()
			.Where(p => p != null && !p.MarkedForClose)
			.Select(ReadPlanet)
			.Where(p => p != null)
			.OrderBy(p => Vector3D.Distance(position, p.Center))
			.ToList();
		// Two planets of the same type (two moons, say) get a number so they can be told apart. Numbered by entity id
		// so the numbers don't swap as you fly around.
		foreach (IGrouping<string, PlanetInfo> same in planets.GroupBy(p => p.GameName).Where(g => g.Count() > 1))
		{
			int number = 1;
			foreach (PlanetInfo planet in same.OrderBy(p => p.Id))
			{
				planet.GameName += " " + number++;
			}
		}
		foreach (PlanetInfo planet in planets)
		{
			planet.Name = PlanetNames.Get(planet.Id) ?? planet.GameName;
		}
		return planets;
	}

	private static PlanetInfo ReadPlanet(MyPlanet planet)
	{
		if (planet == null || !(planet.Components.Get<MyGravityProviderComponent>() is MySphericalNaturalGravityComponent gravity))
		{
			return null;
		}
		IMySphericalNaturalGravityComponent sphere = gravity;
		return new PlanetInfo
		{
			Id = planet.EntityId,
			GameName = planet.Generator?.Id.SubtypeName ?? planet.StorageName ?? "Planet",
			Name = PlanetNames.Get(planet.EntityId) ?? planet.Generator?.Id.SubtypeName ?? planet.StorageName ?? "Planet",
			Center = planet.PositionComp.GetPosition(),
			MinRadius = sphere.MinRadius,
			MaxRadius = sphere.MaxRadius,
			Falloff = sphere.Falloff,
			Intensity = sphere.Intensity,
			GravityLimit = gravity.GravityLimit,
			AverageRadius = planet.AverageRadius,
			HasAtmosphere = planet.HasAtmosphere && planet.Generator != null && planet.Generator.HasAtmosphere,
			AtmosphereAltitude = planet.AtmosphereAltitude,
			AirDensity = planet.Generator?.Atmosphere.Density ?? 0f
		};
	}

	private static void ReadThrusters(ShipSnapshot ship, BlockCache cache, bool countOff)
	{
		foreach (MyThrust thrust in cache.Thrusters)
		{
			if (thrust.MarkedForClose || !thrust.IsFunctional || thrust.BlockDefinition == null)
			{
				continue;
			}
			bool on = thrust.Enabled;
			if (!on)
			{
				ship.BlocksOff++;
				if (!countOff)
				{
					continue;
				}
			}
			MyThrustDefinition definition = thrust.BlockDefinition;
			bool electric = thrust.FuelDefinition == null || thrust.FuelDefinition.Id == MyResourceDistributorComponent.ElectricityId;
			string fuelKey = null;
			double litresPerMWs = 0.0;
			if (!electric)
			{
				fuelKey = thrust.FuelDefinition.Id.ToString();
				ship.FuelNames[fuelKey] = thrust.FuelDefinition.Id.SubtypeName;
				double efficiency = thrust.FuelConverterDefinition.Efficiency;
				double density = thrust.FuelDefinition.EnergyDensity;
				litresPerMWs = efficiency > 0 && density > 0 ? 1.0 / (efficiency * density) : 0.0;
			}
			ship.Thrusters.Add(new ThrusterInfo
			{
				TypeName = definition.DisplayNameText ?? definition.Id.SubtypeName,
				// MyThrust.ThrustForce: the force points opposite the block's Forward (its flame).
				Push = -thrust.WorldMatrix.Forward,
				Force = definition.ForceMagnitude * ((IMyThrust)thrust).ThrustMultiplier,
				MaxPower = thrust.MaxPowerConsumption,
				Electric = electric,
				FuelKey = fuelKey,
				LitresPerMWs = litresPerMWs,
				MinInfluence = definition.MinPlanetaryInfluence,
				MaxInfluence = definition.MaxPlanetaryInfluence,
				EffectAtMin = definition.EffectivenessAtMinInfluence,
				EffectAtMax = definition.EffectivenessAtMaxInfluence,
				NeedsAtmosphere = definition.NeedsAtmosphereForInfluence,
				ConsumptionPerG = definition.ConsumptionFactorPerG,
				On = on
			});
		}
	}

	private static void ReadTanks(ShipSnapshot ship, BlockCache cache, bool countOff)
	{
		foreach (MyGasTank tank in cache.Tanks)
		{
			if (tank.MarkedForClose || !tank.IsFunctional || tank.BlockDefinition == null)
			{
				continue;
			}
			// A tank that's off or stockpiling gives nothing to the thrusters until it's changed.
			bool on = tank.Enabled && !((IMyGasTank)tank).Stockpile;
			if (!on)
			{
				ship.BlocksOff++;
				if (!countOff)
				{
					continue;
				}
			}
			MyDefinitionId gasId = tank.BlockDefinition.StoredGasId;
			string key = gasId.ToString();
			ship.FuelNames[key] = gasId.SubtypeName;
			if (!ship.Gas.TryGetValue(key, out GasPool pool))
			{
				pool = new GasPool { Key = key, Name = gasId.SubtypeName };
				ship.Gas[key] = pool;
			}
			pool.Tanks++;
			if (!on)
			{
				pool.TanksOff++;
			}
			pool.Capacity += tank.BlockDefinition.Capacity;
			pool.Litres += tank.FilledRatio * tank.BlockDefinition.Capacity;
		}
	}

	private static void ReadSources(ShipSnapshot ship, BlockCache cache, bool countOff)
	{
		HashSet<MyDefinitionId> reactorFuels = new HashSet<MyDefinitionId>();
		foreach (MyCubeBlock block in cache.Producers)
		{
			if (block.MarkedForClose || !block.IsFunctional || !(block is IMyPowerProducer producer))
			{
				continue;
			}
			bool on = !(block is IMyFunctionalBlock functional) || functional.Enabled;
			PowerSource source = new PowerSource { TypeName = block.BlockDefinition.DisplayNameText ?? block.BlockDefinition.Id.SubtypeName };
			MyPowerProducerDefinition definition = block.BlockDefinition as MyPowerProducerDefinition;
			// Only reactors have an output multiplier (IMyReactor.PowerOutputMultiplier); mods use it for upgrades.
			double multiplier = block is IMyReactor upgraded ? upgraded.PowerOutputMultiplier : 1.0;
			switch (block)
			{
			case IMyBatteryBlock battery:
				source.Kind = SourceKind.Battery;
				source.MaxOutput = (definition?.MaxPowerOutput ?? producer.MaxOutput) * multiplier;
				source.StoredMWh = battery.CurrentStoredPower;
				// A battery set to recharge gives no power until it's switched back.
				on = on && battery.ChargeMode != IngameChargeMode.Recharge;
				break;
			case MyReactor reactor when reactor.BlockDefinition.FuelInfos != null && reactor.BlockDefinition.FuelInfos.Length > 0:
			{
				MyReactorDefinition.FuelInfo fuel = reactor.BlockDefinition.FuelInfos[0];
				source.Kind = SourceKind.Reactor;
				source.MaxOutput = reactor.BlockDefinition.MaxPowerOutput * multiplier;
				source.FuelKey = fuel.FuelId.ToString();
				// MyReactor burns ConsumptionPerSecond_Items kg/s at full output, whatever the output multiplier.
				source.EnergyPerUnit = fuel.ConsumptionPerSecond_Items > 0 ? reactor.BlockDefinition.MaxPowerOutput / fuel.ConsumptionPerSecond_Items : 0.0;
				reactorFuels.Add(fuel.FuelId);
				ship.FuelNames[source.FuelKey] = MyDefinitionManager.Static.GetPhysicalItemDefinition(fuel.FuelId)?.DisplayNameText ?? fuel.FuelId.SubtypeName;
				break;
			}
			default:
				if (block.BlockDefinition is MyGasFueledPowerProducerDefinition engine)
				{
					// Hydrogen engines: MyGasFueledPowerProducer drains output / FuelProductionToCapacityMultiplier litres a second.
					source.Kind = SourceKind.Engine;
					source.MaxOutput = engine.MaxPowerOutput * multiplier;
					source.FuelKey = engine.Fuel.FuelId.ToString();
					source.EnergyPerUnit = engine.FuelProductionToCapacityMultiplier;
					ship.FuelNames[source.FuelKey] = engine.Fuel.FuelId.SubtypeName;
				}
				else
				{
					// Solar, wind and anything modded we don't know: what it gives right now, for the whole climb.
					source.Kind = SourceKind.Other;
					source.MaxOutput = producer.MaxOutput;
				}
				break;
			}
			source.Priority = Priority(definition, source.Kind);
			source.On = on;
			if (!on)
			{
				ship.BlocksOff++;
				if (!countOff)
				{
					continue;
				}
			}
			ship.Sources.Add(source);
		}

		if (reactorFuels.Count == 0)
		{
			return;
		}
		foreach (MyCubeBlock block in cache.Inventories)
		{
			if (block.MarkedForClose)
			{
				continue;
			}
			for (int i = 0; i < block.InventoryCount; i++)
			{
				MyInventory inventory = block.GetInventory(i);
				if (inventory == null)
				{
					continue;
				}
				foreach (MyDefinitionId fuel in reactorFuels)
				{
					double amount = (double)inventory.GetItemAmount(fuel);
					if (amount > 0)
					{
						string key = fuel.ToString();
						ship.ItemFuel[key] = (ship.ItemFuel.TryGetValue(key, out double total) ? total : 0.0) + amount;
					}
				}
			}
		}
	}

	/// <summary>The source's place in the game's power order, from its resource distribution group.</summary>
	private static int Priority(MyPowerProducerDefinition definition, SourceKind kind)
	{
		if (s_priorities == null)
		{
			s_priorities = new Dictionary<string, int>();
			foreach (MyResourceDistributionGroupDefinition group in MyDefinitionManager.Static.GetDefinitionsOfType<MyResourceDistributionGroupDefinition>())
			{
				if (group.IsSource)
				{
					s_priorities[group.Id.SubtypeName] = group.Priority;
				}
			}
		}
		if (definition != null && s_priorities.TryGetValue(definition.ResourceSourceGroup.String, out int priority))
		{
			return priority;
		}
		switch (kind)
		{
		case SourceKind.Other:
			return 1;
		case SourceKind.Battery:
			return 2;
		default:
			return 3;
		}
	}
}
