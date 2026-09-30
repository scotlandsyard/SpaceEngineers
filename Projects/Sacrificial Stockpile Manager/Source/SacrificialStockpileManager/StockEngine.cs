using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Ingame;

namespace SacrificialStockpileManager;

/// <summary>
/// One pass over a loaded construct: tops blocks up to their minimums, trims them to their maximums, clears
/// stock blocks and intakes, sorts storage by the Accepts settings, and optionally empties production output.
///
/// Every move is the same request the terminal's inventory screen sends (MyInventory.TransferByUser), so the
/// server checks the player's access. The server doesn't check conveyors, so this checks them itself, the
/// same way a programmable block's TransferItemTo does. A pass sends at most <see cref="MaxTransfersPerPass"/>
/// requests.
/// </summary>
internal class StockEngine
{
	public const int MaxTransfersPerPass = 10;

	private const int MaxConnectionChecks = 150;

	/// <summary>How long a sent transfer is assumed to be on its way when the server hasn't answered yet.</summary>
	private const double PendingSeconds = 6.0;

	private const double Epsilon = 1e-4;

	private struct Pending
	{
		public double Delta;

		/// <summary>The amount the inventory held when the first transfer was sent.</summary>
		public double Base;

		public double Expires;
	}

	private class Stack
	{
		public uint ItemId;

		public string Key;

		public double Amount;
	}

	private class Inv
	{
		public LiveBlock Block;

		public MyInventory Inventory;

		public bool IsOutput;

		public int Index;

		/// <summary>Per item, including transfers still on their way.</summary>
		public Dictionary<string, double> Amounts;

		public List<Stack> Stacks;

		/// <summary>Volume (m³) of items sent here this pass or still on their way.</summary>
		public double AddedVolume;

		public string Label => IsOutput ? Block.Name + " (output)" : Block.Name;
	}

	// Transfers sent to the server whose result hasn't shown up locally yet, keyed "owner:index:item".
	// Only used on multiplayer clients; on a server (single player) transfers apply at once.
	private static readonly Dictionary<string, Pending> s_pending = new Dictionary<string, Pending>();

	private readonly Construct _construct;

	private readonly bool _move;

	private readonly double _now;

	private readonly bool _isServer;

	private readonly List<Inv> _inventories = new List<Inv>();

	private readonly List<Inv> _storage = new List<Inv>();

	private readonly Dictionary<string, bool> _connections = new Dictionary<string, bool>();

	private int _transfers;

	private int _checks;

	public readonly List<string> Warnings = new List<string>();

	public int Transfers => _transfers;

	private bool Budget => _transfers < MaxTransfersPerPass;

	private StockEngine(Construct construct, bool move, double now)
	{
		_construct = construct;
		_move = move;
		_now = now;
		_isServer = MyAPIGateway.Multiplayer?.IsServer ?? true;
		foreach (LiveBlock block in construct.Blocks)
		{
			if (block.Role == Effective.Manual || block.Block.Closed)
			{
				continue;
			}
			if (block.Input != null)
			{
				Inv input = Read(block, block.Input, 0, isOutput: false);
				_inventories.Add(input);
				if (block.Role == Effective.Storage)
				{
					_storage.Add(input);
				}
			}
			if (block.Output != null)
			{
				_inventories.Add(Read(block, block.Output, 1, isOutput: true));
			}
		}
	}

	/// <summary>Runs one pass. With <paramref name="move"/> false it only works out the warnings.</summary>
	public static StockEngine Run(Construct construct, GridRules rules, double now)
	{
		ExpirePending(now);
		StockEngine engine = new StockEngine(construct, rules != null && rules.Automation, now);
		engine.FillMinimums();
		engine.TrimMaximums();
		engine.ClearStockBlocks();
		engine.DrainIntakes();
		engine.SortStorage();
		if (rules != null && rules.DrainOutputs)
		{
			engine.DrainOutputs();
		}
		return engine;
	}

	private static void ExpirePending(double now)
	{
		if (s_pending.Count == 0)
		{
			return;
		}
		foreach (string key in s_pending.Where(p => p.Value.Expires < now).Select(p => p.Key).ToList())
		{
			s_pending.Remove(key);
		}
	}

	private Inv Read(LiveBlock block, MyInventory inventory, int index, bool isOutput)
	{
		Inv inv = new Inv
		{
			Block = block,
			Inventory = inventory,
			IsOutput = isOutput,
			Index = index,
			Amounts = new Dictionary<string, double>(),
			Stacks = new List<Stack>()
		};
		foreach (MyPhysicalInventoryItem item in inventory.GetItems())
		{
			string key = Items.Key(item.Content.GetId());
			double amount = (double)item.Amount;
			inv.Stacks.Add(new Stack { ItemId = item.ItemId, Key = key, Amount = amount });
			inv.Amounts.TryGetValue(key, out double existing);
			inv.Amounts[key] = existing + amount;
		}
		if (!_isServer && s_pending.Count > 0)
		{
			string prefix = PendingPrefix(inv);
			foreach (KeyValuePair<string, Pending> entry in s_pending.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
			{
				string key = entry.Key.Substring(prefix.Length);
				inv.Amounts.TryGetValue(key, out double actual);
				// Once the amount has moved at least halfway toward the expected result, the server has answered.
				if ((actual - entry.Value.Base) * Math.Sign(entry.Value.Delta) >= Math.Abs(entry.Value.Delta) / 2.0)
				{
					s_pending.Remove(entry.Key);
					continue;
				}
				inv.Amounts[key] = Math.Max(0.0, actual + entry.Value.Delta);
				if (entry.Value.Delta > 0.0)
				{
					inv.AddedVolume += entry.Value.Delta * ItemVolume(key);
				}
			}
		}
		return inv;
	}

	private static string PendingPrefix(Inv inv)
	{
		return inv.Block.Block.EntityId + ":" + inv.Index + ":";
	}

	private static double Have(Inv inv, string key)
	{
		return inv.Amounts.TryGetValue(key, out double amount) ? amount : 0.0;
	}

	private static double ItemVolume(string key)
	{
		return Items.Definition(key)?.Volume ?? 0.0;
	}

	/// <summary>
	/// How good a destination a block is for an item: 3 = accepts this exact item, 2 = accepts its category,
	/// 1 = general storage (accepts nothing in particular), 0 = not a destination.
	/// </summary>
	private static int Tier(LiveBlock block, string key)
	{
		if (block.Role != Effective.Storage)
		{
			return 0;
		}
		List<string> accept = block.Rules?.Accept;
		if (accept == null || accept.Count == 0)
		{
			return 1;
		}
		if (accept.Contains(key))
		{
			return 3;
		}
		return accept.Contains(Items.Category(key).ToString()) ? 2 : 0;
	}

	/// <summary>How much of an item may be taken out of an inventory to fill someone else's minimum.</summary>
	private static double Available(Inv inv, string key)
	{
		double have = Have(inv, key);
		if (have <= 0.0)
		{
			return 0.0;
		}
		if (inv.IsOutput)
		{
			return inv.Block.Docked ? 0.0 : have;
		}
		ItemLimit limit = inv.Block.Rules?.Limit(key);
		switch (inv.Block.Role)
		{
		case Effective.Intake:
			return have;
		case Effective.Storage:
			return Math.Max(0.0, have - (limit != null && limit.HasMin ? limit.Min : 0.0));
		case Effective.Stock:
			// A stock block gives up items it isn't meant to hold, and anything above its maximum.
			return limit == null ? have : limit.HasMax ? Math.Max(0.0, have - limit.Max) : 0.0;
		case Effective.Machine:
			return limit != null && limit.HasMax ? Math.Max(0.0, have - limit.Max) : 0.0;
		default:
			return 0.0;
		}
	}

	/// <summary>Order to take from sources: things that should leave anyway first, other blocks' surplus last.</summary>
	private static int SourceRank(Inv inv, string key)
	{
		if (inv.IsOutput || inv.Block.Role == Effective.Intake || (inv.Block.Role == Effective.Stock && inv.Block.Rules?.Limit(key) == null))
		{
			return 0;
		}
		if (inv.Block.Role == Effective.Storage)
		{
			return Tier(inv.Block, key) <= 1 ? 1 : 2;
		}
		return 3;
	}

	private bool Connected(Inv source, Inv destination, MyDefinitionId id, string key)
	{
		string cacheKey = source.Block.Block.EntityId + ":" + source.Index + ">" + destination.Block.Block.EntityId + ":" + destination.Index + ":" + key;
		if (_connections.TryGetValue(cacheKey, out bool connected))
		{
			return connected;
		}
		if (_checks >= MaxConnectionChecks)
		{
			return false;
		}
		_checks++;
		connected = ((IMyInventory)source.Inventory).CanTransferItemTo(destination.Inventory, id);
		_connections[cacheKey] = connected;
		return connected;
	}

	/// <summary>How much more of the item fits, allowing for what was already sent there.</summary>
	private static double Fits(Inv inv, string key, MyDefinitionId id)
	{
		double fit = (double)inv.Inventory.ComputeAmountThatFits(id);
		double volume = ItemVolume(key);
		if (volume > 0.0)
		{
			fit -= inv.AddedVolume / volume;
		}
		if (Items.IsIntegral(key))
		{
			fit = Math.Floor(fit + Epsilon);
		}
		return Math.Max(0.0, fit);
	}

	private static double Free(Inv inv)
	{
		return (double)(inv.Inventory.MaxVolume - inv.Inventory.CurrentVolume) - inv.AddedVolume;
	}

	private IEnumerable<ItemLimit> LimitsOf(Inv inv)
	{
		return inv.Block.Rules?.Limits ?? Enumerable.Empty<ItemLimit>();
	}

	// ---- Passes ----

	private void FillMinimums()
	{
		foreach (Inv destination in _inventories)
		{
			if (destination.IsOutput)
			{
				continue;
			}
			foreach (ItemLimit limit in LimitsOf(destination))
			{
				if (!limit.HasMin || Have(destination, limit.Item) >= limit.Min - Epsilon)
				{
					continue;
				}
				string problem = Fill(destination, limit.Item, limit.Min - Have(destination, limit.Item));
				double have = Have(destination, limit.Item);
				if (have < limit.Min - Epsilon)
				{
					Warnings.Add($"{destination.Block.Name}: {Items.Name(limit.Item)} {Items.Amount(have)} of {Items.Amount(limit.Min)} - {problem ?? "not enough available"}");
				}
			}
		}
	}

	/// <summary>Brings items into a block. Returns why it fell short, or null.</summary>
	private string Fill(Inv destination, string key, double need)
	{
		if (!_move)
		{
			return "automation is off";
		}
		if (!Budget)
		{
			return "busy, continues shortly";
		}
		if (!Items.TryParse(key, out MyDefinitionId id))
		{
			return "unknown item";
		}
		if (!destination.Inventory.CheckConstraint(id))
		{
			return "this block can't hold it";
		}
		if (Items.IsIntegral(key))
		{
			need = Math.Ceiling(need - Epsilon);
		}
		List<Inv> sources = _inventories.Where(s => s.Block != destination.Block && Available(s, key) > Epsilon).OrderBy(s => SourceRank(s, key)).ToList();
		if (sources.Count == 0)
		{
			return "none available";
		}
		bool unconnected = false;
		foreach (Inv source in sources)
		{
			if (!Budget)
			{
				return "busy, continues shortly";
			}
			if (!Connected(source, destination, id, key))
			{
				unconnected = true;
				continue;
			}
			double fit = Fits(destination, key, id);
			if (fit <= Epsilon)
			{
				return "no room in the block";
			}
			need -= Transfer(source, destination, key, Math.Min(need, Math.Min(Available(source, key), fit)));
			if (need <= Epsilon)
			{
				return null;
			}
		}
		return unconnected ? "not connected by conveyor to where it is" : "not enough available";
	}

	private void TrimMaximums()
	{
		foreach (Inv source in _inventories)
		{
			if (source.IsOutput)
			{
				continue;
			}
			foreach (ItemLimit limit in LimitsOf(source))
			{
				if (!limit.HasMax)
				{
					continue;
				}
				double excess = Have(source, limit.Item) - limit.Max;
				if (excess <= Epsilon)
				{
					continue;
				}
				Put(source, limit.Item, excess, 1);
				double left = Have(source, limit.Item) - limit.Max;
				if (left > Epsilon && (!_move || Budget))
				{
					Warnings.Add($"{source.Block.Name}: {Items.Name(limit.Item)} {Items.Amount(Have(source, limit.Item))}, above maximum {Items.Amount(limit.Max)} - {(_move ? "no connected storage has room" : "automation is off")}");
				}
			}
		}
	}

	private void ClearStockBlocks()
	{
		foreach (Inv source in _inventories)
		{
			if (!source.IsOutput && source.Block.Role == Effective.Stock)
			{
				foreach (string key in source.Amounts.Keys.ToList())
				{
					if (source.Block.Rules?.Limit(key) == null)
					{
						Put(source, key, Have(source, key), 1);
					}
				}
			}
		}
	}

	private void DrainIntakes()
	{
		foreach (Inv source in _inventories)
		{
			if (!source.IsOutput && source.Block.Role == Effective.Intake)
			{
				foreach (string key in source.Amounts.Keys.ToList())
				{
					Put(source, key, Available(source, key), 1);
				}
			}
		}
	}

	/// <summary>Moves items into the storage that accepts them best. Does nothing until some storage has Accepts set.</summary>
	private void SortStorage()
	{
		if (!_storage.Any(s => s.Block.Rules != null && s.Block.Rules.Accept.Count > 0))
		{
			return;
		}
		foreach (Inv source in _storage)
		{
			foreach (string key in source.Amounts.Keys.ToList())
			{
				int tier = Tier(source.Block, key);
				if (tier >= 3)
				{
					continue;
				}
				// Items that don't belong here can go to any storage; items that do only move to a better match.
				Put(source, key, Available(source, key), tier == 0 ? 1 : tier + 1);
			}
		}
	}

	private void DrainOutputs()
	{
		foreach (Inv source in _inventories)
		{
			// A docked ship's production output belongs to the ship.
			if (source.IsOutput && !source.Block.Docked)
			{
				foreach (string key in source.Amounts.Keys.ToList())
				{
					Put(source, key, Have(source, key), 1);
				}
			}
		}
	}

	/// <summary>Moves up to <paramref name="amount"/> into storage of at least <paramref name="minTier"/>. Returns how much was sent.</summary>
	private double Put(Inv source, string key, double amount, int minTier)
	{
		if (!_move || amount <= Epsilon || !Budget || !Items.TryParse(key, out MyDefinitionId id))
		{
			return 0.0;
		}
		double sent = 0.0;
		IEnumerable<Inv> destinations = _storage
			.Where(d => d.Block != source.Block)
			.Select(d => new KeyValuePair<Inv, int>(d, Tier(d.Block, key)))
			.Where(d => d.Value >= minTier)
			.OrderByDescending(d => d.Value)
			.ThenByDescending(d => Have(d.Key, key) > 0.0)
			.ThenByDescending(d => Free(d.Key))
			.Select(d => d.Key)
			.ToList();
		foreach (Inv destination in destinations)
		{
			if (!Budget)
			{
				break;
			}
			if (!destination.Inventory.CheckConstraint(id))
			{
				continue;
			}
			double room = amount - sent;
			ItemLimit limit = destination.Block.Rules?.Limit(key);
			if (limit != null && limit.HasMax)
			{
				room = Math.Min(room, limit.Max - Have(destination, key));
			}
			if (room <= Epsilon)
			{
				continue;
			}
			double fit = Fits(destination, key, id);
			if (fit <= Epsilon || !Connected(source, destination, id, key))
			{
				continue;
			}
			sent += Transfer(source, destination, key, Math.Min(room, fit));
			if (sent >= amount - Epsilon)
			{
				break;
			}
		}
		return sent;
	}

	/// <summary>Sends the transfer requests, stack by stack. Returns the amount sent.</summary>
	private double Transfer(Inv source, Inv destination, string key, double amount)
	{
		bool integral = Items.IsIntegral(key);
		double moved = 0.0;
		foreach (Stack stack in source.Stacks)
		{
			if (stack.Key != key || stack.Amount <= Epsilon)
			{
				continue;
			}
			if (!Budget)
			{
				break;
			}
			double take = Math.Min(amount - moved, Math.Min(stack.Amount, Have(source, key)));
			if (integral)
			{
				take = Math.Floor(take + Epsilon);
			}
			if (take <= Epsilon)
			{
				break;
			}
			MyInventory.TransferByUser(source.Inventory, destination.Inventory, stack.ItemId, -1, (MyFixedPoint)take);
			_transfers++;
			stack.Amount -= take;
			moved += take;
			Adjust(source, key, -take);
			Adjust(destination, key, take);
			destination.AddedVolume += take * ItemVolume(key);
			if (moved >= amount - Epsilon)
			{
				break;
			}
		}
		if (moved > 0.0)
		{
			_construct.AddLog($"Moved {Items.Amount(moved)} {Items.Name(key)}: {source.Label} > {destination.Label}");
		}
		return moved;
	}

	private void Adjust(Inv inv, string key, double delta)
	{
		double before = Have(inv, key);
		inv.Amounts[key] = Math.Max(0.0, before + delta);
		if (_isServer)
		{
			return;
		}
		string pendingKey = PendingPrefix(inv) + key;
		if (s_pending.TryGetValue(pendingKey, out Pending pending))
		{
			pending.Delta += delta;
		}
		else
		{
			// "before" already includes nothing pending for this key, so it's the real amount right now.
			pending = new Pending { Delta = delta, Base = before };
		}
		pending.Expires = _now + PendingSeconds;
		s_pending[pendingKey] = pending;
	}
}
