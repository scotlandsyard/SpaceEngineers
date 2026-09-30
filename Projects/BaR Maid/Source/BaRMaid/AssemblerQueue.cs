using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;

namespace NeedyBOB;

/// <summary>
/// Puts components into assembler queues the same way the Build and Repair mod's EnsureQueued does: count
/// what is already queued or sitting in the assemblers' output, then top up the shortest queues. On a
/// client, AddQueueItemRequest sends the same request as the terminal's production screen, and the server
/// checks the player's access before adding it.
/// </summary>
internal static class AssemblerQueue
{
	public static MyBlueprintDefinitionBase BlueprintFor(MyDefinitionId component)
	{
		try
		{
			return MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(component);
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Makes sure <paramref name="amount"/> of the component is queued or already built in the assemblers.
	/// Returns how many were added to queues; <paramref name="problem"/> says why nothing could be queued.
	/// </summary>
	public static int Ensure(List<IMyAssembler> assemblers, MyDefinitionId component, int amount, out string problem)
	{
		problem = null;
		if (amount <= 0)
		{
			return 0;
		}
		MyBlueprintDefinitionBase blueprint = BlueprintFor(component);
		if (blueprint == null)
		{
			problem = "No blueprint makes it";
			return 0;
		}
		List<KeyValuePair<MyProductionBlock, int>> candidates = new List<KeyValuePair<MyProductionBlock, int>>();
		int totalQueue = 0;
		foreach (IMyAssembler assembler in assemblers)
		{
			if (!(assembler is MyProductionBlock block))
			{
				continue;
			}
			amount -= AvailableAmount(block, component, blueprint, out int queueSize);
			if (block.CanUseBlueprint(blueprint))
			{
				candidates.Add(new KeyValuePair<MyProductionBlock, int>(block, queueSize));
				totalQueue += queueSize;
			}
		}
		if (amount <= 0)
		{
			return 0;
		}
		if (candidates.Count == 0)
		{
			problem = "No assembler in the group can build it";
			return 0;
		}
		int wanted = amount;
		candidates.Sort((a, b) => a.Value.CompareTo(b.Value));
		// First bring the shorter queues up to the average, then spread what's left evenly.
		int average = totalQueue / candidates.Count;
		if (average > 0)
		{
			foreach (KeyValuePair<MyProductionBlock, int> candidate in candidates)
			{
				int space = Math.Min(average - candidate.Value, amount);
				if (space > 0)
				{
					candidate.Key.AddQueueItemRequest(blueprint, space);
					amount -= space;
					if (amount <= 0)
					{
						return wanted;
					}
				}
			}
		}
		int perBlock = (int)Math.Ceiling((double)amount / candidates.Count);
		foreach (KeyValuePair<MyProductionBlock, int> candidate in candidates)
		{
			int space = Math.Min(perBlock, amount);
			candidate.Key.AddQueueItemRequest(blueprint, space);
			amount -= space;
			if (amount <= 0)
			{
				break;
			}
		}
		return wanted;
	}

	/// <summary>How many of the component are queued in the assemblers.</summary>
	public static int QueuedAmount(List<IMyAssembler> assemblers, MyDefinitionId component)
	{
		MyBlueprintDefinitionBase blueprint = BlueprintFor(component);
		int total = 0;
		if (blueprint == null)
		{
			return 0;
		}
		foreach (IMyAssembler assembler in assemblers)
		{
			if (assembler is MyProductionBlock block && !block.Closed)
			{
				foreach (MyProductionBlock.QueueItem item in block.Queue)
				{
					if (item.Blueprint != null && item.Blueprint.Id == blueprint.Id)
					{
						total += (int)item.Amount;
					}
				}
			}
		}
		return total;
	}

	/// <summary>How many of the component are sitting finished in the assemblers' output inventories.</summary>
	public static int OutputAmount(List<IMyAssembler> assemblers, MyDefinitionId component)
	{
		int total = 0;
		foreach (IMyAssembler assembler in assemblers)
		{
			if (!assembler.Closed && assembler.OutputInventory is VRage.Game.ModAPI.IMyInventory inventory)
			{
				total += (int)inventory.GetItemAmount(component);
			}
		}
		return total;
	}

	private static int AvailableAmount(MyProductionBlock block, MyDefinitionId component, MyBlueprintDefinitionBase blueprint, out int queueSize)
	{
		int amount = 0;
		queueSize = 0;
		if (((IMyAssembler)block).OutputInventory is VRage.Game.ModAPI.IMyInventory inventory)
		{
			amount += (int)inventory.GetItemAmount(component);
		}
		foreach (MyProductionBlock.QueueItem item in block.Queue)
		{
			queueSize += (int)item.Amount;
			if (item.Blueprint != null && item.Blueprint.Id == blueprint.Id)
			{
				amount += (int)item.Amount;
			}
		}
		return amount;
	}
}
