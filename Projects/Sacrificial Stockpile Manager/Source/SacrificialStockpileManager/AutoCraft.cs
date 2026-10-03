using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using TimShared;
using VRage;
using MyAssemblerMode = Sandbox.ModAPI.Ingame.MyAssemblerMode;

namespace SacrificialStockpileManager;

/// <summary>
/// Keeps items between their quota (minimum) and maximum. The shortfall is queued in the assemblers; with
/// Disassemble on, the surplus above a maximum is disassembled on one idle assembler, which is switched back to
/// assembly once its disassembly queue is done.
///
/// Quotas belong to the grid they were set on. A construct can hold several units (the grid itself and ships
/// docked to it by connector), and each unit's quotas count only that unit's stock and use only its assemblers,
/// so docking never applies a ship's quotas to a station or the other way round.
///
/// Orders follow the assembler modes shared with our other plugins (Shared/AssemblerModes.cs): each order goes to
/// one Main assembler and the Co-op ones share it out; Manual assemblers, and ones with no mode yet, are never used.
///
/// When BaR Maid (our repair plugin) is loaded too, its orders go first: quotas are only queued while the unit's
/// Main and Co-op assemblers are all idle, and only about a minute of work at a time. Anything BaR Maid queues then
/// waits at most for that batch, and no more stockpile work is added until its queue is done.
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

	/// <summary>With BaR Maid loaded: about how long one batch of quota work keeps the assemblers busy.</summary>
	private const double YieldBatchSeconds = 60.0;

	private static bool? s_barMaidLoaded;

	// What this run did that's worth a chat line (Shared/Personality.cs); at most one is said per run.

	private static string s_quotaMet;

	private static string s_craftItem;

	private static double s_craftAmount;

	private static string s_disassembleItem;

	private static double s_disassembleAmount;

	/// <summary>True when the BaR Maid plugin is loaded in this game, so its repair orders go before quotas.</summary>
	public static bool YieldsToBaRMaid => s_barMaidLoaded ??= AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BaRMaid");

	/// <summary>Seconds to wait for a mode switch before deciding the assembler can't disassemble.</summary>
	private const double ModeSwitchTimeoutSeconds = 20.0;

	// Assemblers asked to switch to disassembly, and when. Not saved: after a restart the switch has either
	// happened (and the id is in Store.Disassemblers) or it's asked again.
	private static readonly Dictionary<long, double> s_switchRequested = new Dictionary<long, double>();

	private static readonly HashSet<long> s_cannotDisassemble = new HashSet<long>();

	// When the plugin last added to or removed from a disassembler's queue. Until the server's reply has had
	// time to arrive, the local queue may be out of date, so the assembler is left alone.
	private static readonly Dictionary<long, double> s_queueChanged = new Dictionary<long, double>();

	/// <summary>One unit's quotas for this pass.</summary>
	private class Unit
	{
		public long Id;

		public GridRules Rules;

		/// <summary>The unit's assemblers the plugin may use (Main or Co-op, not someone else's).</summary>
		public List<MyAssembler> Assemblers;

		public Dictionary<string, double> Totals;

		public Dictionary<string, string> Notes;

		/// <summary>The assemblers orders go to, worked out the first time something needs queuing.</summary>
		public List<MyAssembler> Receivers;

		/// <summary>A batch was queued this pass (the local queues only show it after the server's reply).</summary>
		public bool QueuedThisPass;
	}

	public static void Run(Construct construct, double now)
	{
		construct.QuotaNotes = new Dictionary<string, string>();
		construct.UnitNotes = new Dictionary<long, Dictionary<string, string>>();
		if (construct.Snapshot == null)
		{
			return;
		}
		bool isServer = MyAPIGateway.Multiplayer?.IsServer ?? true;
		double cooldown = isServer ? ServerCooldownSeconds : ClientCooldownSeconds;
		s_quotaMet = null;
		s_craftItem = null;
		s_craftAmount = 0.0;
		s_disassembleItem = null;
		s_disassembleAmount = 0.0;
		foreach (KeyValuePair<long, List<long>> entry in construct.Units.ToList())
		{
			bool core = entry.Key == construct.CoreUnit;
			// The unit's own settings only: a docked ship's are found by its own grids, never the station's.
			GridRules rules = core ? Store.GridRules(construct.Snapshot) : Store.GridRules(entry.Key, entry.Value);
			Unit unit = new Unit
			{
				Id = entry.Key,
				Rules = rules,
				Assemblers = construct.Blocks
					.Where(b => b.Unit == entry.Key && (b.Kind == BlockKind.Assembler || (rules != null && rules.UseSurvivalKits && b.Kind == BlockKind.SurvivalKit)) && b.Role != Effective.Manual)
					.Select(b => b.Block as MyAssembler)
					// Assemblers are Manual until the player makes them Main or Co-op.
					.Where(b => b != null && !b.Closed && !AssemblerModes.IsManual(b))
					.ToList(),
				// A docked ship's notes show when it's picked in the Grid list.
				Notes = core ? construct.QuotaNotes : construct.UnitNotes[entry.Key] = new Dictionary<string, string>()
			};
			if (rules != null && rules.Quotas.Count > 0)
			{
				unit.Totals = Construct.StationTotals(construct.Snapshot, unit.Id);
				CheckQuotas(construct, unit, now, cooldown);
			}
			// Assemblers the plugin put into disassembly mode go back once they're done, whatever the settings are now.
			// This runs after the quotas so a freshly switched assembler gets its queue before it's judged empty.
			RestoreFinishedDisassemblers(construct, unit, now, cooldown);
		}
		if (s_quotaMet != null)
		{
			Personality.Say("quota_met", "item", s_quotaMet);
		}
		else if (s_craftItem != null)
		{
			Personality.Say("quota_crafting", "count", Items.Amount(s_craftAmount), "item", s_craftItem);
		}
		else if (s_disassembleItem != null)
		{
			Personality.Say("disassembling", "item", s_disassembleItem);
		}
	}

	private static void CheckQuotas(Construct construct, Unit unit, double now, double cooldown)
	{
		GridRules rules = unit.Rules;
		List<MyAssembler> usable = unit.Assemblers.Where(a => a.IsFunctional && a.Enabled && a.IsWorking && ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly).ToList();
		foreach (ItemLimit quota in rules.Quotas)
		{
			string key = quota.Item;
			unit.Totals.TryGetValue(key, out double have);
			if (quota.HasMin && quota.Min > 0.0)
			{
				// quota_met is for a quota that was short and is now met; the first look only sets the baseline.
				string shortKey = unit.Id + ":" + key;
				bool isShort = have < quota.Min - 1e-6;
				if (!isShort && construct.QuotaShort.TryGetValue(shortKey, out bool wasShort) && wasShort)
				{
					s_quotaMet ??= Items.Name(key);
				}
				construct.QuotaShort[shortKey] = isShort;
			}
			MyBlueprintDefinitionBase blueprint = Items.Blueprint(key);
			if (quota.HasMax && have > quota.Max)
			{
				unit.Notes[key] = Disassemble(construct, unit, key, blueprint, have - quota.Max, now, cooldown);
				continue;
			}
			if (!quota.HasMin || quota.Min <= 0.0)
			{
				unit.Notes[key] = quota.HasMax ? "Within maximum" : "";
				continue;
			}
			if (blueprint == null)
			{
				unit.Notes[key] = have >= quota.Min ? "Stocked" : "No blueprint makes it";
				continue;
			}
			// Everything already queued on the unit's assemblers, Main or Co-op.
			double queued = construct.QueuedAmount(key, unit.Id);
			if (have >= quota.Min)
			{
				unit.Notes[key] = "Stocked";
				continue;
			}
			if (have + queued >= quota.Min)
			{
				unit.Notes[key] = "Queued";
				continue;
			}
			if (!rules.Autocraft)
			{
				unit.Notes[key] = $"Short {Items.Amount(quota.Min - have - queued)} (autocraft off)";
				continue;
			}
			string cooldownKey = unit.Id + ":" + key;
			if (construct.CraftCooldown.TryGetValue(cooldownKey, out double until) && now < until)
			{
				unit.Notes[key] = "Queued, waiting for the server";
				continue;
			}
			if (usable.Count == 0)
			{
				unit.Notes[key] = unit.Assemblers.Count == 0 ? "No Main or Co-op assembler: set one in Production" : "Assemblers are off, disassembling or unpowered";
				continue;
			}
			// BaR Maid's repair orders go first: only queue on idle assemblers, one batch at a time.
			string batchKey = unit.Id + ":batch";
			bool batchPending = unit.QueuedThisPass || (construct.CraftCooldown.TryGetValue(batchKey, out double batchUntil) && now < batchUntil);
			if (YieldsToBaRMaid && (batchPending || unit.Assemblers.Any(a => ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly && !a.IsQueueEmpty)))
			{
				unit.Notes[key] = $"Short {Items.Amount(quota.Min - have - queued)}, waits for free assemblers (BaR Maid first)";
				continue;
			}
			// Makes sure one is Main and turns the game's cooperative switch to match.
			unit.Receivers ??= AssemblerModes.PrepareForOrders(usable.Cast<IMyAssembler>().ToList()).OfType<MyAssembler>().ToList();
			MyAssembler receiver = unit.Receivers.Where(a => a.CanUseBlueprint(blueprint)).OrderBy(a => a.Queue.Sum(q => (double)q.Amount)).FirstOrDefault();
			if (receiver == null)
			{
				unit.Notes[key] = usable.Any(a => a.CanUseBlueprint(blueprint)) ? "The Main assembler can't make it: make one that can Main" : "No assembler here can make it";
				continue;
			}
			double perRun = Items.BlueprintYield(blueprint, key);
			int runs = (int)Math.Ceiling((quota.Min - have - queued) / perRun - 1e-6);
			if (runs <= 0)
			{
				continue;
			}
			string batch = "";
			if (YieldsToBaRMaid)
			{
				int batchRuns = BatchRuns(blueprint, usable);
				if (batchRuns < runs)
				{
					runs = batchRuns;
					batch = " (a batch; BaR Maid first)";
				}
				unit.QueuedThisPass = true;
				// Until the server's reply shows the batch in the queue, it would look idle.
				construct.CraftCooldown[batchKey] = now + (MyAPIGateway.Multiplayer?.IsServer ?? true ? 0.0 : 5.0);
			}
			// One order on one Main assembler; its Co-op assemblers take their share from its queue.
			receiver.AddQueueItemRequest(blueprint, (MyFixedPoint)runs);
			construct.CraftCooldown[cooldownKey] = now + cooldown;
			unit.Notes[key] = $"Queued {Items.Amount(runs * perRun)}{batch}";
			if (runs * perRun > s_craftAmount)
			{
				s_craftAmount = runs * perRun;
				s_craftItem = Items.Name(key);
			}
			construct.AddLog($"Queued {Items.Amount(runs * perRun)} {Items.Name(key)} on {receiver.CustomName} (quota {Items.Amount(quota.Min)})");
		}
	}

	/// <summary>
	/// How many runs of the blueprint the usable assemblers get through in about <see cref="YieldBatchSeconds"/>, at
	/// the game's speed (world assembler speed, block speed and speed modules), at least one.
	/// </summary>
	private static int BatchRuns(MyBlueprintDefinitionBase blueprint, List<MyAssembler> usable)
	{
		float world = Sandbox.Game.World.MySession.Static?.AssemblerSpeedMultiplier ?? 1f;
		double runsPerSecond = 0.0;
		foreach (MyAssembler assembler in usable)
		{
			float speed = assembler.BlockDefinition is MyAssemblerDefinition definition ? definition.AssemblySpeed : 1f;
			if (assembler.UpgradeValues.TryGetValue("Productivity", out float productivity))
			{
				speed += productivity;
			}
			runsPerSecond += world * speed / Math.Max(0.01f, blueprint.BaseProductionTimeInSeconds);
		}
		return Math.Max(1, (int)Math.Floor(runsPerSecond * YieldBatchSeconds));
	}

	/// <summary>Queues the surplus for disassembly. Returns the note shown in the Items view.</summary>
	private static string Disassemble(Construct construct, Unit unit, string key, MyBlueprintDefinitionBase blueprint, double surplus, double now, double cooldown)
	{
		string over = $"{Items.Amount(surplus)} over maximum";
		if (!unit.Rules.Disassemble)
		{
			return over + " (disassembly off)";
		}
		if (blueprint == null)
		{
			return over + ", no blueprint to disassemble it";
		}
		double perRun = Items.BlueprintYield(blueprint, key);
		double queued = DisassemblyQueued(unit.Assemblers, blueprint) * perRun;
		surplus -= queued;
		if (surplus < perRun)
		{
			return queued > 0.0 ? $"Disassembling {Items.Amount(queued)}" : over;
		}
		string cooldownKey = unit.Id + ":d:" + key;
		if (construct.CraftCooldown.TryGetValue(cooldownKey, out double until) && now < until)
		{
			return "Disassembly queued, waiting for the server";
		}
		MyAssembler disassembler = unit.Assemblers.FirstOrDefault(a => ((IMyAssembler)a).Mode == MyAssemblerMode.Disassembly && Store.Disassemblers.Contains(a.EntityId) && a.IsWorking);
		if (disassembler == null)
		{
			return StartDisassembler(construct, unit, now) ?? over + ", switching an assembler to disassembly";
		}
		if (!disassembler.CanUseBlueprint(blueprint))
		{
			return over + $", {disassembler.CustomName} can't disassemble it";
		}
		int runs = (int)Math.Floor(surplus / perRun + 1e-6);
		disassembler.AddQueueItemRequest(blueprint, (MyFixedPoint)runs);
		construct.CraftCooldown[cooldownKey] = now + cooldown;
		s_queueChanged[disassembler.EntityId] = now;
		construct.AddLog($"Queued {Items.Amount(runs * perRun)} {Items.Name(key)} for disassembly on {disassembler.CustomName}");
		if (runs * perRun > s_disassembleAmount)
		{
			s_disassembleAmount = runs * perRun;
			s_disassembleItem = Items.Name(key);
		}
		return $"Disassembling {Items.Amount(runs * perRun + queued)}";
	}

	/// <summary>
	/// Asks an idle assembler of the unit (assembly mode, nothing queued, not cooperating, not Manual) to switch to
	/// disassembly. Only one at a time. Returns a note when none can be used, else null.
	/// </summary>
	private static string StartDisassembler(Construct construct, Unit unit, double now)
	{
		foreach (MyAssembler assembler in unit.Assemblers)
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
		// Co-op assemblers are in the game's cooperative mode (IsSlave) and stay that way, so this is normally an idle
		// Main assembler.
		MyAssembler idle = unit.Assemblers.FirstOrDefault(a => a.IsWorking && ((IMyAssembler)a).Mode == MyAssemblerMode.Assembly && a.IsQueueEmpty && !a.IsSlave && !s_cannotDisassemble.Contains(a.EntityId) && construct.Find(a.EntityId)?.Kind == BlockKind.Assembler && !AssemblerModes.IsManual(a));
		if (idle == null)
		{
			return "waiting for an idle Main assembler to disassemble with";
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
	private static void RestoreFinishedDisassemblers(Construct construct, Unit unit, double now, double cooldown)
	{
		bool changed = false;
		foreach (MyAssembler assembler in unit.Assemblers)
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
				if (RemoveStaleEntries(construct, unit, assembler))
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
	private static bool RemoveStaleEntries(Construct construct, Unit unit, MyAssembler assembler)
	{
		bool removed = false;
		Dictionary<string, double> totals = unit.Totals ?? Construct.StationTotals(construct.Snapshot, unit.Id);
		List<MyProductionBlock.QueueItem> queue = assembler.Queue.ToList();
		for (int i = queue.Count - 1; i >= 0; i--)
		{
			MyBlueprintDefinitionBase blueprint = queue[i].Blueprint;
			if (blueprint?.Results == null || blueprint.Results.Length != 1)
			{
				continue;
			}
			string key = Items.Key(blueprint.Results[0].Id);
			ItemLimit quota = unit.Rules?.Quota(key);
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
}
