using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Voxels;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace OreScout;

public enum VoxelScanKind
{
	Asteroids,
	Planets,
	SurfaceRocks,
	/// <summary>Every voxel map: asteroids, planets and boulders, like the vanilla ore detector.</summary>
	All
}

/// <summary>A voxel map to scan, captured on the game thread so the background scan never touches entities.</summary>
public class VoxelTarget
{
	public long EntityId;

	public IMyStorage Storage;

	public Vector3D Center;

	public MatrixD Rotation;

	public Vector3 SizeInMetresHalf;

	public Vector3I StorageMin;

	public Vector3I StorageSize;

	public bool IsPlanet;

	public double PlanetMinimumRadius;
}

/// <summary>One ore on one voxel map: total amount found and the centre of its largest (or closest, on planets) cluster.</summary>
public class OreDeposit
{
	public long VoxelEntityId;

	public string OreName;

	public Vector3D Position;

	public double VolumeM3;

	public double MassKg;

	public bool IsPlanet;
}

/// <summary>A voxel material that yields a scanned ore, and how much ore a ship drill gets from one full cubic metre of it.</summary>
public class OreMaterial
{
	public string Ore;

	public double KgPerM3;
}

/// <summary>
/// Scans voxel maps for ore on a background thread, reading the storage the same way the vanilla
/// ore detector does (content first to skip empty cells, then material with PreciseOrePositions).
/// </summary>
public static class VoxelOreScanner
{
	/// <summary>Every asteroid and planet is mostly stone, so it is never reported.</summary>
	public const string StoneOre = "Stone";

	private const int CellSizeInLodVoxels = 8;

	private const double ClusterRadiusMeters = 80.0;

	// Planet ore never sits deeper than this below the lowest surface point, so the core can be skipped.
	private const double PlanetOreMaxDepthMeters = 500.0;

	private class Cluster
	{
		public Vector3D Sum;

		public int Samples;

		public double Kg;

		public Vector3D Centroid => Sum / Samples;
	}

	// Ship drills harvest this fraction of each mined voxel's volume as ore (MyDrillBase.VoxelHarvestRatio).
	private const double DrillHarvestRatio = 0.009;

	/// <param name="yieldMultiplier">Applied on top of the vanilla drill yield, e.g. world harvest multiplier × modded drill bonus.</param>
	public static Dictionary<byte, OreMaterial> BuildMaterialMap(HashSet<string> ores, double yieldMultiplier)
	{
		Dictionary<byte, OreMaterial> map = new Dictionary<byte, OreMaterial>();
		foreach (MyVoxelMaterialDefinition def in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
		{
			if (def != null && !string.IsNullOrWhiteSpace(def.MinedOre) && ores.Contains(def.MinedOre) && !def.MinedOre.Trim().Equals(StoneOre, StringComparison.OrdinalIgnoreCase) && !map.ContainsKey(def.Index))
			{
				map[def.Index] = new OreMaterial
				{
					Ore = def.MinedOre,
					KgPerM3 = KgPerCubicMetre(def) * yieldMultiplier
				};
			}
		}
		return map;
	}

	/// <summary>Same maths as MyDrillBase.TryHarvestOreMaterial: ore volume mined, converted to item count, then to kg.</summary>
	private static double KgPerCubicMetre(MyVoxelMaterialDefinition material)
	{
		try
		{
			MyPhysicalItemDefinition ore = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), material.MinedOre));
			if (ore != null && ore.Volume > 0f)
			{
				return DrillHarvestRatio * material.MinedOreRatio / ore.Volume * ore.Mass;
			}
		}
		catch
		{
		}
		return 0.0;
	}

	public static List<VoxelTarget> FindTargets(Vector3D center, double radius, VoxelScanKind kind)
	{
		List<VoxelTarget> targets = new List<VoxelTarget>();
		List<MyVoxelBase> voxels = new List<MyVoxelBase>();
		BoundingSphereD sphere = new BoundingSphereD(center, radius);
		MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, voxels);
		HashSet<long> seen = new HashSet<long>();
		foreach (MyVoxelBase found in voxels)
		{
			MyVoxelBase voxel = found.RootVoxel ?? found;
			if (voxel.Storage == null || voxel.MarkedForClose || !seen.Add(voxel.EntityId))
			{
				continue;
			}
			MyPlanet planet = voxel as MyPlanet;
			bool boulder = planet == null && IsSmallBoulder(voxel);
			bool wanted = kind switch
			{
				VoxelScanKind.All => true,
				VoxelScanKind.Planets => planet != null,
				VoxelScanKind.SurfaceRocks => boulder && IsNearPlanetSurface(voxel.PositionComp.GetPosition()),
				_ => planet == null,
			};
			if (!wanted)
			{
				continue;
			}
			MatrixD rotation = voxel.WorldMatrix;
			rotation.Translation = Vector3D.Zero;
			targets.Add(new VoxelTarget
			{
				EntityId = voxel.EntityId,
				Storage = voxel.Storage,
				Center = voxel.PositionComp.GetPosition(),
				Rotation = rotation,
				SizeInMetresHalf = voxel.SizeInMetresHalf,
				StorageMin = voxel.StorageMin,
				StorageSize = voxel.Storage.Size,
				IsPlanet = planet != null,
				PlanetMinimumRadius = planet?.MinimumRadius ?? 0.0
			});
		}
		return targets;
	}

	/// <summary>Runs on a background thread. Returns one deposit per ore per voxel map.</summary>
	public static List<OreDeposit> Scan(List<VoxelTarget> targets, Vector3D center, double radius, Dictionary<byte, OreMaterial> materialToOre, Func<bool> cancelled)
	{
		List<OreDeposit> deposits = new List<OreDeposit>();
		MyStorageData cache = new MyStorageData();
		cache.Resize(new Vector3I(CellSizeInLodVoxels));
		foreach (VoxelTarget target in targets)
		{
			if (cancelled())
			{
				break;
			}
			// Planets are huge, so sample them more coarsely (8 m) than asteroids (4 m).
			int lod = target.IsPlanet ? 3 : 2;
			Dictionary<string, List<Cluster>> clusters;
			try
			{
				clusters = ScanTarget(target, center, radius, materialToOre, lod, cache, cancelled);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[OreScout] Skipped voxel map {target.EntityId}: {e.Message}");
				continue;
			}
			double sampleVolume = Math.Pow(1 << lod, 3);
			foreach (KeyValuePair<string, List<Cluster>> ore in clusters)
			{
				Cluster best = null;
				int totalSamples = 0;
				double totalKg = 0.0;
				foreach (Cluster cluster in ore.Value)
				{
					totalSamples += cluster.Samples;
					totalKg += cluster.Kg;
					if (best == null)
					{
						best = cluster;
					}
					else if (target.IsPlanet)
					{
						if (Vector3D.DistanceSquared(cluster.Centroid, center) < Vector3D.DistanceSquared(best.Centroid, center))
						{
							best = cluster;
						}
					}
					else if (cluster.Samples > best.Samples)
					{
						best = cluster;
					}
				}
				if (best != null)
				{
					deposits.Add(new OreDeposit
					{
						VoxelEntityId = target.EntityId,
						IsPlanet = target.IsPlanet,
						OreName = ore.Key,
						Position = best.Centroid,
						// On planets the marker points at one patch, so report that patch rather than everything in range.
						VolumeM3 = (target.IsPlanet ? best.Samples : totalSamples) * sampleVolume,
						MassKg = target.IsPlanet ? best.Kg : totalKg
					});
				}
			}
		}
		return deposits;
	}

	private static Dictionary<string, List<Cluster>> ScanTarget(VoxelTarget target, Vector3D center, double radius, Dictionary<byte, OreMaterial> materialToOre, int lod, MyStorageData cache, Func<bool> cancelled)
	{
		Dictionary<string, List<Cluster>> clusters = new Dictionary<string, List<Cluster>>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Vector3D> cellSums = new Dictionary<string, Vector3D>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, int> cellCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, double> cellKg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		ReadOreSamples(target, center, radius, materialToOre, lod, cache, cancelled, (Vector3I voxel, OreMaterial material, double kg) =>
		{
			string ore = material.Ore;
			cellSums.TryGetValue(ore, out Vector3D sum);
			cellSums[ore] = sum + SampleLocalPosition(voxel, lod);
			cellCounts.TryGetValue(ore, out int count);
			cellCounts[ore] = count + 1;
			cellKg.TryGetValue(ore, out double total);
			cellKg[ore] = total + kg;
		}, () =>
		{
			foreach (KeyValuePair<string, int> ore in cellCounts)
			{
				Vector3D world = ToWorld(target, cellSums[ore.Key] / ore.Value);
				if (Vector3D.DistanceSquared(world, center) <= radius * radius)
				{
					AddToClusters(clusters, ore.Key, world, ore.Value, cellKg[ore.Key]);
				}
			}
			cellSums.Clear();
			cellCounts.Clear();
			cellKg.Clear();
		});
		return clusters;
	}

	/// <summary>
	/// Runs on a background thread. Returns every separate deposit on the given asteroids: each connected
	/// blob of one ore, where samples up to <paramref name="spacingMeters"/> apart count as connected.
	/// Each deposit's position is the ore sample closest to its centre, so the marker lands on ore.
	/// </summary>
	public static List<OreDeposit> ScanDeposits(List<VoxelTarget> targets, Vector3D center, double radius, Dictionary<byte, OreMaterial> materialToOre, double spacingMeters, Func<bool> cancelled)
	{
		const int lod = 2;
		const int sampleMeters = 1 << lod;
		List<OreDeposit> deposits = new List<OreDeposit>();
		MyStorageData cache = new MyStorageData();
		cache.Resize(new Vector3I(CellSizeInLodVoxels));
		// Touching samples (including diagonals) are always connected; beyond that, anything within the spacing.
		int reach = Math.Max(1, (int)Math.Floor(spacingMeters / sampleMeters));
		double spacingInSamples = spacingMeters / sampleMeters;
		List<Vector3I> neighbourOffsets = new List<Vector3I>();
		for (int z = -reach; z <= reach; z++)
		{
			for (int y = -reach; y <= reach; y++)
			{
				for (int x = -reach; x <= reach; x++)
				{
					bool touching = Math.Abs(x) <= 1 && Math.Abs(y) <= 1 && Math.Abs(z) <= 1;
					if ((x != 0 || y != 0 || z != 0) && (touching || x * x + y * y + z * z <= spacingInSamples * spacingInSamples))
					{
						neighbourOffsets.Add(new Vector3I(x, y, z));
					}
				}
			}
		}
		foreach (VoxelTarget target in targets)
		{
			if (cancelled())
			{
				break;
			}
			Dictionary<string, Dictionary<Vector3I, double>> samplesByOre = new Dictionary<string, Dictionary<Vector3I, double>>(StringComparer.OrdinalIgnoreCase);
			try
			{
				ReadOreSamples(target, center, radius, materialToOre, lod, cache, cancelled, (Vector3I voxel, OreMaterial material, double kg) =>
				{
					if (!samplesByOre.TryGetValue(material.Ore, out Dictionary<Vector3I, double> samples))
					{
						samples = new Dictionary<Vector3I, double>();
						samplesByOre[material.Ore] = samples;
					}
					samples[voxel] = kg;
				}, null);
			}
			catch (Exception e)
			{
				MyLog.Default.WriteLineAndConsole($"[OreScout] Skipped voxel map {target.EntityId}: {e.Message}");
				continue;
			}
			double sampleVolume = Math.Pow(1 << lod, 3);
			foreach (KeyValuePair<string, Dictionary<Vector3I, double>> ore in samplesByOre)
			{
				Dictionary<Vector3I, double> unvisited = ore.Value;
				List<Vector3I> blob = new List<Vector3I>();
				Queue<Vector3I> queue = new Queue<Vector3I>();
				foreach (Vector3I start in unvisited.Keys.ToList())
				{
					if (!unvisited.TryGetValue(start, out double kg))
					{
						continue;
					}
					if (cancelled())
					{
						return deposits;
					}
					unvisited.Remove(start);
					blob.Clear();
					blob.Add(start);
					queue.Enqueue(start);
					while (queue.Count > 0)
					{
						Vector3I current = queue.Dequeue();
						foreach (Vector3I offset in neighbourOffsets)
						{
							Vector3I next = current + offset;
							if (unvisited.TryGetValue(next, out double nextKg))
							{
								unvisited.Remove(next);
								kg += nextKg;
								blob.Add(next);
								queue.Enqueue(next);
							}
						}
					}
					Vector3D centroid = Vector3D.Zero;
					foreach (Vector3I voxel in blob)
					{
						centroid += SampleLocalPosition(voxel, lod);
					}
					centroid /= blob.Count;
					Vector3D nearest = SampleLocalPosition(blob[0], lod);
					foreach (Vector3I voxel in blob)
					{
						Vector3D local = SampleLocalPosition(voxel, lod);
						if (Vector3D.DistanceSquared(local, centroid) < Vector3D.DistanceSquared(nearest, centroid))
						{
							nearest = local;
						}
					}
					deposits.Add(new OreDeposit
					{
						VoxelEntityId = target.EntityId,
						IsPlanet = target.IsPlanet,
						OreName = ore.Key,
						Position = ToWorld(target, nearest),
						VolumeM3 = blob.Count * sampleVolume,
						MassKg = kg
					});
				}
			}
		}
		return deposits;
	}

	/// <summary>
	/// Reads the part of a voxel map inside the scan sphere in 8×8×8 blocks of LOD voxels, calling
	/// <paramref name="onSample"/> for each solid voxel of a scanned ore (with its drill yield in kg)
	/// and <paramref name="onCellDone"/> after each block that had any content.
	/// </summary>
	private static void ReadOreSamples(VoxelTarget target, Vector3D center, double radius, Dictionary<byte, OreMaterial> materialToOre, int lod, MyStorageData cache, Func<bool> cancelled, Action<Vector3I, OreMaterial, double> onSample, Action onCellDone)
	{
		MatrixD inverseRotation = MatrixD.Transpose(target.Rotation);
		Vector3D localCenter = Vector3D.TransformNormal(center - target.Center + (Vector3D)target.StorageMin, inverseRotation) + (Vector3D)target.SizeInMetresHalf;
		Vector3D planetLocalCenter = (Vector3D)target.SizeInMetresHalf + (Vector3D)target.StorageMin;

		int lodVoxelMeters = 1 << lod;
		int cellMeters = CellSizeInLodVoxels * lodVoxelMeters;
		double cellHalfDiagonal = cellMeters * 0.8660254;
		Vector3I storageMax = target.StorageSize - 1;
		Vector3I minCell = ToCell(Vector3D.Max(localCenter - radius, Vector3D.Zero), cellMeters);
		Vector3I maxCell = ToCell(Vector3D.Min(localCenter + radius, (Vector3D)storageMax), cellMeters);
		if (minCell.X > maxCell.X || minCell.Y > maxCell.Y || minCell.Z > maxCell.Z)
		{
			return;
		}
		double sampleVolume = Math.Pow(lodVoxelMeters, 3);
		MyVoxelRequestFlags requestFlags = MyVoxelRequestFlags.PreciseOrePositions;

		using StoragePin pin = target.Storage.Pin();
		if (!pin.Valid)
		{
			return;
		}
		Vector3I cell = default;
		for (cell.Z = minCell.Z; cell.Z <= maxCell.Z; cell.Z++)
		{
			for (cell.Y = minCell.Y; cell.Y <= maxCell.Y; cell.Y++)
			{
				if (cancelled())
				{
					return;
				}
				for (cell.X = minCell.X; cell.X <= maxCell.X; cell.X++)
				{
					Vector3D cellCenter = (Vector3D)(cell * cellMeters) + cellMeters * 0.5;
					if (Vector3D.Distance(cellCenter, localCenter) > radius + cellHalfDiagonal)
					{
						continue;
					}
					if (target.IsPlanet && Vector3D.Distance(cellCenter, planetLocalCenter) + cellHalfDiagonal < target.PlanetMinimumRadius - PlanetOreMaxDepthMeters)
					{
						continue;
					}
					Vector3I lodMin = cell * CellSizeInLodVoxels;
					Vector3I lodMax = lodMin + (CellSizeInLodVoxels - 1);
					target.Storage.ReadRange(cache, MyStorageDataTypeFlags.Content, lod, lodMin, lodMax);
					if (!cache.ContainsVoxelsAboveIsoLevel())
					{
						continue;
					}
					target.Storage.ReadRange(cache, MyStorageDataTypeFlags.Material, lod, lodMin, lodMax, ref requestFlags);
					Vector3I p = default;
					for (p.Z = 0; p.Z < CellSizeInLodVoxels; p.Z++)
					{
						for (p.Y = 0; p.Y < CellSizeInLodVoxels; p.Y++)
						{
							for (p.X = 0; p.X < CellSizeInLodVoxels; p.X++)
							{
								int index = cache.ComputeLinear(ref p);
								byte content = cache.Content(index);
								if (content <= 127 || !materialToOre.TryGetValue(cache.Material(index), out OreMaterial material))
								{
									continue;
								}
								// Partly filled voxels yield proportionally less, as with the drill.
								onSample(p + lodMin, material, content / 255.0 * sampleVolume * material.KgPerM3);
							}
						}
					}
					onCellDone?.Invoke();
				}
			}
		}
	}

	/// <summary>Centre of a LOD voxel in storage-local metres.</summary>
	private static Vector3D SampleLocalPosition(Vector3I lodVoxel, int lod)
	{
		int size = 1 << lod;
		return (Vector3D)(lodVoxel * size) + size * 0.5;
	}

	private static Vector3D ToWorld(VoxelTarget target, Vector3D localMeters)
	{
		return target.Center - (Vector3D)target.StorageMin + Vector3D.TransformNormal(localMeters - (Vector3D)target.SizeInMetresHalf, target.Rotation);
	}

	private static void AddToClusters(Dictionary<string, List<Cluster>> clusters, string ore, Vector3D position, int samples, double kg)
	{
		if (!clusters.TryGetValue(ore, out List<Cluster> list))
		{
			list = new List<Cluster>();
			clusters[ore] = list;
		}
		foreach (Cluster cluster in list)
		{
			if (Vector3D.DistanceSquared(cluster.Centroid, position) <= ClusterRadiusMeters * ClusterRadiusMeters)
			{
				cluster.Sum += position * samples;
				cluster.Samples += samples;
				cluster.Kg += kg;
				return;
			}
		}
		list.Add(new Cluster
		{
			Sum = position * samples,
			Samples = samples,
			Kg = kg
		});
	}

	private static Vector3I ToCell(Vector3D localMeters, int cellMeters)
	{
		return new Vector3I((int)Math.Floor(localMeters.X / cellMeters), (int)Math.Floor(localMeters.Y / cellMeters), (int)Math.Floor(localMeters.Z / cellMeters));
	}

	private static bool IsSmallBoulder(MyVoxelBase voxel)
	{
		Vector3I size = voxel.Storage.Size;
		return (long)size.X * size.Y * size.Z < 262144;
	}

	private static bool IsNearPlanetSurface(Vector3D position)
	{
		MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(position);
		if (planet == null)
		{
			return false;
		}
		Vector3D surface = planet.GetClosestSurfacePointGlobal(ref position);
		return Vector3D.Distance(position, surface) < 2000.0;
	}
}
