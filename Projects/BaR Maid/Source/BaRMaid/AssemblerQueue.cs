using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;

namespace BaRMaid;

/// <summary>
/// Puts components into assembler queues, based on the Build and Repair mod's EnsureQueued: count
/// what is already queued or sitting in the assemblers' output, then queue the rest in the shortest queue. On a
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
	/// Makes sure <paramref name="amount"/> of the component is queued or already built. What's already queued or
	/// finished is counted in <paramref name="countIn"/>: every assembler on the construct, because co-op
	/// assemblers move work out of the main one's queue into their own. New work goes to
	/// <paramref name="receivers"/> (the group's Main assemblers), or to <paramref name="fallback"/> when no
	/// receiver can build the component. Returns how many were added to queues; <paramref name="problem"/> says
	/// why nothing could be queued.
	/// </summary>
	public static int Ensure(List<IMyAssembler> countIn, List<IMyAssembler> receivers, List<IMyAssembler> fallback, MyDefinitionId component, int amount, out string problem)
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
		foreach (IMyAssembler assembler in countIn)
		{
			if (assembler is MyProductionBlock block && !block.Closed)
			{
				amount -= AvailableAmount(block, component, blueprint, out _);
			}
		}
		if (amount <= 0)
		{
			return 0;
		}
		List<KeyValuePair<MyProductionBlock, int>> candidates = Candidates(receivers, blueprint);
		if (candidates.Count == 0)
		{
			candidates = Candidates(fallback, blueprint);
		}
		if (candidates.Count == 0)
		{
			problem = "No assembler in the group can build it";
			return 0;
		}
		// The whole order goes to one assembler, the one with the shortest queue, rather than a slice to every
		// assembler (which the mod's EnsureQueued does, filling every queue with the same item).
		KeyValuePair<MyProductionBlock, int> shortest = candidates[0];
		foreach (KeyValuePair<MyProductionBlock, int> candidate in candidates)
		{
			if (candidate.Value < shortest.Value)
			{
				shortest = candidate;
			}
		}
		shortest.Key.AddQueueItemRequest(blueprint, amount);
		return amount;
	}

	/// <summary>The assemblers that can build the blueprint, with their queue sizes.</summary>
	private static List<KeyValuePair<MyProductionBlock, int>> Candidates(List<IMyAssembler> assemblers, MyBlueprintDefinitionBase blueprint)
	{
		List<KeyValuePair<MyProductionBlock, int>> candidates = new List<KeyValuePair<MyProductionBlock, int>>();
		foreach (IMyAssembler assembler in assemblers)
		{
			if (assembler is MyProductionBlock block && !block.Closed && block.CanUseBlueprint(blueprint))
			{
				int queueSize = 0;
				foreach (MyProductionBlock.QueueItem item in block.Queue)
				{
					queueSize += (int)item.Amount;
				}
				candidates.Add(new KeyValuePair<MyProductionBlock, int>(block, queueSize));
			}
		}
		return candidates;
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
			if (assembler is MyProductionBlock block && !block.Closed && assembler.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly)
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
		// A disassembly queue takes items apart, so it doesn't count toward what's coming.
		if (((IMyAssembler)block).Mode != Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly)
		{
			return amount;
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
