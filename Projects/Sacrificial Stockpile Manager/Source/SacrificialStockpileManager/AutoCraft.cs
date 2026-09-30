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
/// Keeps a construct's items at their quotas by queuing the shortfall in its assemblers. Queuing uses
/// AddQueueItemRequest, the same request as clicking a blueprint in the production screen; on a client the
/// server checks access and the local queue only updates after its reply, hence the cooldown per item.
/// </summary>
internal static class AutoCraft
{
	/// <summary>Seconds before an item queued on a multiplayer client is queued again.</summary>
	private const double ClientCooldownSeconds = 15.0;

	private const double ServerCooldownSeconds = 2.0;

	public static void Run(Construct construct, GridRules rules, double now)
	{
		Dictionary<string, string> notes = new Dictionary<string, string>();
		construct.QuotaNotes = notes;
		if (rules == null || rules.Quotas.Count == 0 || construct.Snapshot == null)
		{
			return;
		}
		List<MyProductionBlock> assemblers = construct.Blocks
			.Where(b => (b.Kind == BlockKind.Assembler || (rules.UseSurvivalKits && b.Kind == BlockKind.SurvivalKit)) && b.Role != Effective.Manual && !b.Docked)
			.Select(b => b.Block as MyProductionBlock)
			.Where(b => b != null && !b.Closed)
			.ToList();
		List<MyProductionBlock> usable = assemblers.Where(a => a.IsFunctional && a.Enabled && a.IsWorking && ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly).ToList();
		// A docked ship's cargo isn't the station's stock.
		Dictionary<string, double> totals = new Dictionary<string, double>();
		foreach (BlockSnapshot block in construct.Snapshot.Blocks.Where(b => !b.Docked))
		{
			foreach (KeyValuePair<string, double> item in block.ItemAmounts.Concat(block.OutputAmounts))
			{
				totals.TryGetValue(item.Key, out double existing);
				totals[item.Key] = existing + item.Value;
			}
		}
		bool isServer = MyAPIGateway.Multiplayer?.IsServer ?? true;
		foreach (ItemLimit quota in rules.Quotas)
		{
			if (!quota.HasMin || quota.Min <= 0.0)
			{
				continue;
			}
			string key = quota.Item;
			totals.TryGetValue(key, out double have);
			MyBlueprintDefinitionBase blueprint = Items.Blueprint(key);
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
			List<MyProductionBlock> candidates = usable.Where(a => a.CanUseBlueprint(blueprint)).ToList();
			if (candidates.Count == 0)
			{
				notes[key] = assemblers.Any(a => a.CanUseBlueprint(blueprint)) ? "Assemblers are off, busy disassembling or unpowered" : "No assembler here can make it";
				continue;
			}
			double perRun = Items.BlueprintYield(blueprint, key);
			int runs = (int)Math.Ceiling((quota.Min - have - queued) / perRun - 1e-6);
			if (runs <= 0)
			{
				continue;
			}
			Queue(candidates, blueprint, runs);
			construct.CraftCooldown[key] = now + (isServer ? ServerCooldownSeconds : ClientCooldownSeconds);
			notes[key] = $"Queued {Items.Amount(runs * perRun)}";
			construct.AddLog($"Queued {Items.Amount(runs * perRun)} {Items.Name(key)} (quota {Items.Amount(quota.Min)})");
		}
	}

	/// <summary>Spreads the runs over the assemblers, shortest queue first.</summary>
	private static void Queue(List<MyProductionBlock> candidates, MyBlueprintDefinitionBase blueprint, int runs)
	{
		List<KeyValuePair<MyProductionBlock, double>> byQueue = candidates
			.Select(a => new KeyValuePair<MyProductionBlock, double>(a, a.Queue.Sum(q => (double)q.Amount)))
			.OrderBy(a => a.Value)
			.ToList();
		int perBlock = (int)Math.Ceiling((double)runs / byQueue.Count);
		foreach (KeyValuePair<MyProductionBlock, double> candidate in byQueue)
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
