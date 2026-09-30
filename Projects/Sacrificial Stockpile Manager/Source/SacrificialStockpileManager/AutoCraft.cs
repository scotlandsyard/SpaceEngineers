using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace SacrificialStockpileManager;

/// <summary>
/// Keeps a construct's items between their quota (minimum) and maximum. The shortfall is queued in the
/// assemblers; with Disassemble on, the surplus above a maximum is disassembled on one idle assembler, which is
/// switched back to assembly once its disassembly queue is done.
///
/// Queuing uses AddQueueItemRequest and mode changes RequestDisassembleEnabled, the same requests as the
/// production screen. On a client the server checks access and the local state only updates after its reply,
/// hence the cooldowns.
/// </summary>
internal static class AutoCraft
{
	/// <summary>Seconds before an item queued on a multiplayer client is queued again.</summary>
	private const double ClientCooldownSeconds = 15.0;

	private const double ServerCooldownSeconds = 2.0;

	/// <summary>Seconds to wait for a mode switch before deciding the assembler can't disassemble.</summary>
	private const double ModeSwitchTimeoutSeconds = 20.0;

	// Assemblers asked to switch to disassembly, and when. Not saved: after a restart the switch has either
	// happened (and the id is in Store.Disassemblers) or it's asked again.
	private static readonly Dictionary<long, double> s_switchRequested = new Dictionary<long, double>();

	private static readonly HashSet<long> s_cannotDisassemble = new HashSet<long>();

	// When the plugin last added to or removed from a disassembler's queue. Until the server's reply has had
	// time to arrive, the local queue may be out of date, so the assembler is left alone.
	private static readonly Dictionary<long, double> s_queueChanged = new Dictionary<long, double>();

	public static void Run(Construct construct, GridRules rules, double now)
	{
		Dictionary<string, string> notes = new Dictionary<string, string>();
		construct.QuotaNotes = notes;
		if (construct.Snapshot == null)
		{
			return;
		}
		List<MyAssembler> assemblers = construct.Blocks
			.Where(b => (b.Kind == BlockKind.Assembler || (rules != null && rules.UseSurvivalKits && b.Kind == BlockKind.SurvivalKit)) && b.Role != Effective.Manual && !b.Docked)
			.Select(b => b.Block as MyAssembler)
			.Where(b => b != null && !b.Closed)
			.ToList();
		bool isServer = MyAPIGateway.Multiplayer?.IsServer ?? true;
		double cooldown = isServer ? ServerCooldownSeconds : ClientCooldownSeconds;
		if (rules != null)
		{
			CheckQuotas(construct, rules, assemblers, notes, now, cooldown);
		}
		// Assemblers the plugin put into disassembly mode go back once they're done, whatever the settings are now.
		// This runs after the quotas so a freshly switched assembler gets its queue before it's judged empty.
		RestoreFinishedDisassemblers(construct, assemblers, rules, now, cooldown);
	}

	private static void CheckQuotas(Construct construct, GridRules rules, List<MyAssembler> assemblers, Dictionary<string, string> notes, double now, double cooldown)
	{
		List<MyAssembler> usable = assemblers.Where(a => a.IsFunctional && a.Enabled && a.IsWorking && ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly).ToList();
		Dictionary<string, double> totals = StationTotals(construct);
		foreach (ItemLimit quota in rules.Quotas)
		{
			string key = quota.Item;
			totals.TryGetValue(key, out double have);
			MyBlueprintDefinitionBase blueprint = Items.Blueprint(key);
			if (quota.HasMax && have > quota.Max)
			{
				notes[key] = Disassemble(construct, rules, assemblers, key, blueprint, have - quota.Max, now, cooldown);
				continue;
			}
			if (!quota.HasMin || quota.Min <= 0.0)
			{
				notes[key] = quota.HasMax ? "Within maximum" : "";
				continue;
			}
			if (blueprint == null)
			{
				notes[key] = have >= quota.Min ? "Stocked" : "No blueprint makes it";
				continue;
			}
			double queued = construct.QueuedAmount(key);
			if (have >= quota.Min)
			{
				notes[key] = "Stocked";
				continue;
			}
			if (have + queued >= quota.Min)
			{
				notes[key] = "Queued";
				continue;
			}
			if (!rules.Autocraft)
			{
				notes[key] = $"Short {Items.Amount(quota.Min - have - queued)} (autocraft off)";
				continue;
			}
			if (construct.CraftCooldown.TryGetValue(key, out double until) && now < until)
			{
				notes[key] = "Queued, waiting for the server";
				continue;
			}
			List<MyAssembler> candidates = usable.Where(a => a.CanUseBlueprint(blueprint)).ToList();
			if (candidates.Count == 0)
			{
				notes[key] = assemblers.Any(a => a.CanUseBlueprint(blueprint)) ? "Assemblers are off, disassembling or unpowered" : "No assembler here can make it";
				continue;
			}
			double perRun = Items.BlueprintYield(blueprint, key);
			int runs = (int)Math.Ceiling((quota.Min - have - queued) / perRun - 1e-6);
			if (runs <= 0)
			{
				continue;
			}
			Queue(candidates, blueprint, runs);
			construct.CraftCooldown[key] = now + cooldown;
			notes[key] = $"Queued {Items.Amount(runs * perRun)}";
			construct.AddLog($"Queued {Items.Amount(runs * perRun)} {Items.Name(key)} (quota {Items.Amount(quota.Min)})");
		}
	}

	private static Dictionary<string, double> StationTotals(Construct construct)
	{
		return Construct.StationTotals(construct.Snapshot);
	}

	/// <summary>Queues the surplus for disassembly. Returns the note shown in the Items view.</summary>
	private static string Disassemble(Construct construct, GridRules rules, List<MyAssembler> assemblers, string key, MyBlueprintDefinitionBase blueprint, double surplus, double now, double cooldown)
	{
		string over = $"{Items.Amount(surplus)} over maximum";
		if (!rules.Disassemble)
		{
			return over + " (disassembly off)";
		}
		if (blueprint == null)
		{
			return over + ", no blueprint to disassemble it";
		}
		double perRun = Items.BlueprintYield(blueprint, key);
		double queued = DisassemblyQueued(assemblers, blueprint) * perRun;
		surplus -= queued;
		if (surplus < perRun)
		{
			return queued > 0.0 ? $"Disassembling {Items.Amount(queued)}" : over;
		}
		if (construct.CraftCooldown.TryGetValue("d:" + key, out double until) && now < until)
		{
			return "Disassembly queued, waiting for the server";
		}
		MyAssembler disassembler = assemblers.FirstOrDefault(a => ((IMyAssembler)a).Mode == MyAssemblerMode.Disassembly && Store.Disassemblers.Contains(a.EntityId) && a.IsWorking);
		if (disassembler == null)
		{
			return StartDisassembler(construct, assemblers, now) ?? over + ", switching an assembler to disassembly";
		}
		if (!disassembler.CanUseBlueprint(blueprint))
		{
			return over + $", {disassembler.CustomName} can't disassemble it";
		}
		int runs = (int)Math.Floor(surplus / perRun + 1e-6);
		disassembler.AddQueueItemRequest(blueprint, (MyFixedPoint)runs);
		construct.CraftCooldown["d:" + key] = now + cooldown;
		s_queueChanged[disassembler.EntityId] = now;
		construct.AddLog($"Queued {Items.Amount(runs * perRun)} {Items.Name(key)} for disassembly on {disassembler.CustomName}");
		return $"Disassembling {Items.Amount(runs * perRun + queued)}";
	}

	/// <summary>
	/// Asks an idle assembler (assembly mode, nothing queued, not cooperating) to switch to disassembly. Only one
	/// per construct. Returns a note when none can be used, else null.
	/// </summary>
	private static string StartDisassembler(Construct construct, List<MyAssembler> assemblers, double now)
	{
		foreach (MyAssembler assembler in assemblers)
		{
			if (s_switchRequested.TryGetValue(assembler.EntityId, out double asked))
			{
				if (now - asked < ModeSwitchTimeoutSeconds)
				{
					return null;
				}
				// The server never switched it: this assembler can't disassemble (survival kits, some modded blocks).
				s_switchRequested.Remove(assembler.EntityId);
				s_cannotDisassemble.Add(assembler.EntityId);
			}
		}
		MyAssembler idle = assemblers.FirstOrDefault(a => a.IsWorking && ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly && a.IsQueueEmpty && !a.IsSlave && !s_cannotDisassemble.Contains(a.EntityId) && construct.Find(a.EntityId)?.Kind == BlockKind.Assembler);
		if (idle == null)
		{
			return "waiting for an idle assembler to disassemble with";
		}
		idle.RequestDisassembleEnabled(newDisassembleEnabled: true);
		s_switchRequested[idle.EntityId] = now;
		if (!Store.Disassemblers.Contains(idle.EntityId))
		{
			Store.Disassemblers.Add(idle.EntityId);
			Store.SaveSettings();
		}
		construct.AddLog($"Switched {idle.CustomName} to disassembly");
		return null;
	}

	/// <summary>Switches the plugin's disassemblers back to assembly once their disassembly queue is empty.</summary>
	private static void RestoreFinishedDisassemblers(Construct construct, List<MyAssembler> assemblers, GridRules rules, double now, double cooldown)
	{
		bool changed = false;
		foreach (MyAssembler assembler in assemblers)
		{
			long id = assembler.EntityId;
			if (!Store.Disassemblers.Contains(id))
			{
				continue;
			}
			if (((IMyAssembler)assembler).Mode == MyAssemblerMode.Disassembly)
			{
				s_switchRequested.Remove(id);
				if (s_queueChanged.TryGetValue(id, out double changedAt) && now - changedAt < cooldown)
				{
					continue;
				}
				// Drop queue entries whose surplus has gone (someone used the items), so the assembler isn't held up.
				if (RemoveStaleEntries(construct, assembler, rules))
				{
					s_queueChanged[id] = now;
					continue;
				}
				if (assembler.IsQueueEmpty)
				{
					assembler.RequestDisassembleEnabled(newDisassembleEnabled: false);
					Store.Disassemblers.Remove(id);
					changed = true;
					construct.AddLog($"Switched {assembler.CustomName} back to assembly");
				}
			}
			else if (!s_switchRequested.ContainsKey(id))
			{
				// Back in assembly mode, by us or by the player.
				Store.Disassemblers.Remove(id);
				changed = true;
			}
		}
		if (changed)
		{
			Store.SaveSettings();
		}
	}

	/// <summary>Removes the queue entries whose surplus has gone. Returns true if it asked for any removal.</summary>
	private static bool RemoveStaleEntries(Construct construct, MyAssembler assembler, GridRules rules)
	{
		bool removed = false;
		Dictionary<string, double> totals = null;
		List<MyProductionBlock.QueueItem> queue = assembler.Queue.ToList();
		for (int i = queue.Count - 1; i >= 0; i--)
		{
			MyBlueprintDefinitionBase blueprint = queue[i].Blueprint;
			if (blueprint?.Results == null || blueprint.Results.Length != 1)
			{
				continue;
			}
			string key = Items.Key(blueprint.Results[0].Id);
			ItemLimit quota = rules?.Quota(key);
			totals ??= StationTotals(construct);
			totals.TryGetValue(key, out double have);
			// Items already pulled into the assembler count as have, so this only fires when they're really gone.
			bool stillSurplus = quota != null && quota.HasMax && have > quota.Max;
			if (!stillSurplus && (double)assembler.OutputInventory.GetItemAmount(blueprint.Results[0].Id) <= 0.0)
			{
				assembler.RemoveQueueItemRequest(i, queue[i].Amount);
				construct.AddLog($"Removed {Items.Name(key)} from {assembler.CustomName}'s disassembly queue: no surplus left");
				removed = true;
			}
		}
		return removed;
	}

	private static double DisassemblyQueued(List<MyAssembler> assemblers, MyBlueprintDefinitionBase blueprint)
	{
		double runs = 0.0;
		foreach (MyAssembler assembler in assemblers)
		{
			if (((IMyAssembler)assembler).Mode != MyAssemblerMode.Disassembly)
			{
				continue;
			}
			foreach (MyProductionBlock.QueueItem item in assembler.Queue)
			{
				if (item.Blueprint != null && item.Blueprint.Id == blueprint.Id)
				{
					runs += (double)item.Amount;
				}
			}
		}
		return runs;
	}

	/// <summary>Spreads the runs over the assemblers, shortest queue first.</summary>
	private static void Queue(List<MyAssembler> candidates, MyBlueprintDefinitionBase blueprint, int runs)
	{
		List<KeyValuePair<MyAssembler, double>> byQueue = candidates
			.Select(a => new KeyValuePair<MyAssembler, double>(a, a.Queue.Sum(q => (double)q.Amount)))
			.OrderBy(a => a.Value)
			.ToList();
		int perBlock = (int)Math.Ceiling((double)runs / byQueue.Count);
		foreach (KeyValuePair<MyAssembler, double> candidate in byQueue)
		{
			int amount = Math.Min(perBlock, runs);
			if (amount <= 0)
			{
				break;
			}
			candidate.Key.AddQueueItemRequest(blueprint, (MyFixedPoint)amount);
			runs -= amount;
		}
	}
}
