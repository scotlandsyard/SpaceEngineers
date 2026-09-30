using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using IngameEntity = VRage.Game.ModAPI.Ingame.IMyEntity;
using IngameSlimBlock = VRage.Game.ModAPI.Ingame.IMySlimBlock;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace BaRMaid;

/// <summary>
/// One group of Build and Repair systems and the assemblers that build for them, on one construct
/// (everything sharing a terminal system).
/// </summary>
internal class MaidGroup
{
	/// <summary>After queuing a component, wait this long before queuing it again, so the server has time to
	/// send the updated assembler queues back to this client.</summary>
	private const double RequeueDelaySeconds = 15.0;

	/// <summary>After finding that nothing can build a component, wait this long before trying again.</summary>
	private const double ProblemRetrySeconds = 30.0;

	private const int MaxLogLines = 6;

	public string Key;

	public string Name;

	public string GridName;

	public bool IsDefault;

	public IMyGridTerminalSystem TerminalSystem;

	public readonly List<IMyShipWelder> Systems = new List<IMyShipWelder>();

	public readonly List<IMyAssembler> Assemblers = new List<IMyAssembler>();

	/// <summary>Every Build and Repair system and assembler on the construct, for the setup view.</summary>
	public List<IMyTerminalBlock> ConstructBlocks = new List<IMyTerminalBlock>();

	public bool AutoQueue;

	public double NextQueueCheck;

	public readonly List<string> Log = new List<string>();

	/// <summary>What happened the last time each component was queued (or why it couldn't be).</summary>
	public readonly Dictionary<MyDefinitionId, string> Notes = new Dictionary<MyDefinitionId, string>();

	private readonly Dictionary<MyDefinitionId, double> _lastAttempt = new Dictionary<MyDefinitionId, double>();

	public string Label => $"{GridName}: {Name}";

	public IEnumerable<IMyShipWelder> LiveSystems => Systems.Where(s => !s.Closed);

	public IMyShipWelder FirstSystem => LiveSystems.FirstOrDefault();

	/// <summary>Assemblers that can take new work right now.</summary>
	public List<IMyAssembler> UsableAssemblers()
	{
		return Assemblers.Where(a => !a.Closed && a.IsFunctional && a.Enabled && a.Mode == MyAssemblerMode.Assembly && a.HasLocalPlayerAccess()).ToList();
	}

	/// <summary>
	/// Missing components across the group. Systems that overlap report the same shortfall, so each
	/// component takes the largest amount any one system reports rather than the sum.
	/// </summary>
	public Dictionary<MyDefinitionId, int> MissingComponents()
	{
		Dictionary<MyDefinitionId, int> result = new Dictionary<MyDefinitionId, int>();
		foreach (IMyShipWelder system in LiveSystems)
		{
			Dictionary<MyDefinitionId, int> missing = BarApi.MissingComponents(system);
			if (missing == null)
			{
				continue;
			}
			foreach (KeyValuePair<MyDefinitionId, int> item in missing)
			{
				if (!result.TryGetValue(item.Key, out int existing) || item.Value > existing)
				{
					result[item.Key] = item.Value;
				}
			}
		}
		return result;
	}

	public List<IngameSlimBlock> WeldTargets()
	{
		return Union(BarApi.WeldTargets);
	}

	public List<IngameSlimBlock> GrindTargets()
	{
		return Union(BarApi.GrindTargets);
	}

	public List<IngameEntity> CollectTargets()
	{
		return Union(BarApi.CollectTargets);
	}

	/// <summary>Merges each system's list, keeping the mod's order and dropping duplicates.</summary>
	private List<T> Union<T>(Func<IMyTerminalBlock, List<T>> read) where T : class
	{
		List<T> result = new List<T>();
		HashSet<T> seen = new HashSet<T>();
		foreach (IMyShipWelder system in LiveSystems)
		{
			List<T> list = read(system);
			if (list == null)
			{
				continue;
			}
			foreach (T item in list)
			{
				if (item != null && seen.Add(item))
				{
					result.Add(item);
				}
			}
		}
		return result;
	}

	/// <summary>
	/// Queues whatever the group is missing into its assemblers. Returns how many component kinds were queued.
	/// </summary>
	public int QueueMissing(double now)
	{
		List<IMyAssembler> assemblers = UsableAssemblers();
		if (assemblers.Count == 0)
		{
			return 0;
		}
		int kinds = 0;
		foreach (KeyValuePair<MyDefinitionId, int> item in MissingComponents())
		{
			if (item.Value <= 0 || (_lastAttempt.TryGetValue(item.Key, out double last) && now < last))
			{
				continue;
			}
			int queued = AssemblerQueue.Ensure(assemblers, item.Key, item.Value, out string problem);
			if (queued > 0)
			{
				_lastAttempt[item.Key] = now + RequeueDelaySeconds;
				Notes[item.Key] = $"Queued {queued} at {DateTime.Now:HH:mm:ss}";
				AddLog($"Queued {queued} x {ComponentName(item.Key)}");
				kinds++;
			}
			else if (problem != null)
			{
				_lastAttempt[item.Key] = now + ProblemRetrySeconds;
				Notes[item.Key] = problem;
			}
			else
			{
				Notes.Remove(item.Key);
			}
		}
		return kinds;
	}

	/// <summary>Seconds until the component may be queued again, or 0.</summary>
	public double WaitSeconds(MyDefinitionId component, double now)
	{
		return _lastAttempt.TryGetValue(component, out double until) && until > now ? until - now : 0.0;
	}

	public void AddLog(string line)
	{
		Log.Insert(0, $"{DateTime.Now:HH:mm:ss} {line}");
		if (Log.Count > MaxLogLines)
		{
			Log.RemoveRange(MaxLogLines, Log.Count - MaxLogLines);
		}
	}

	public static string ComponentName(MyDefinitionId id)
	{
		try
		{
			if (MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id, out MyPhysicalItemDefinition definition) && !string.IsNullOrEmpty(definition.DisplayNameText))
			{
				return definition.DisplayNameText;
			}
		}
		catch (Exception)
		{
		}
		return id.SubtypeName;
	}
}
