using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace SeamlessUnloadFix;

/// <summary>
/// Two patches that keep a world from staying half loaded after a failed unload.
///
/// 1. MySession.UnloadDataComponents unloads the session components newest first, and the first exception stops
///    the loop. MySession.Unload then skips everything after it too (the other mods, scripts, multiplayer, terminal
///    controls, definitions) while the session still counts as gone, so the leftovers break every later join until
///    the game restarts. Our prefix does the same loop first, each component in its own try/catch; a component that
///    throws is logged and marked unloaded. The game's own loop then finds nothing left to unload and carries on.
///
/// 2. A clean unload always unloads the definition manager before the next LoadData (MySession.Unload and
///    LoadMultiplayerWorld both do). If LoadData starts while the last load's definitions are still there, the
///    previous unload didn't finish: unload them first, the way MySession.Unload does, or InitBlockGroups crashes
///    on block variant groups it already resolved.
///
/// Both run on the game thread.
/// </summary>
internal static class Patches
{
	private const string HarmonyId = "SeamlessUnloadFix";

	private const string Tag = "[SeamlessUnloadFix] ";

	private static Harmony s_harmony;

	private static AccessTools.FieldRef<MySession, List<MySessionComponentBase>> s_loadOrder;

	private static Action<MySessionComponentBase, bool> s_setLoaded;

	/// <summary>True from the start of MyDefinitionManager.LoadData until its next UnloadData.</summary>
	private static bool s_definitionsLoaded;

	private static readonly List<string> s_crashedMods = new List<string>();

	private static bool s_clearedDefinitions;

	internal static bool HasNotice => s_crashedMods.Count > 0 || s_clearedDefinitions;

	internal static void Apply()
	{
		if (s_harmony != null)
		{
			return;
		}
		s_harmony = new Harmony(HarmonyId);
		try
		{
			s_loadOrder = AccessTools.FieldRefAccess<MySession, List<MySessionComponentBase>>("m_loadOrder");
			s_setLoaded = AccessTools.MethodDelegate<Action<MySessionComponentBase, bool>>(AccessTools.PropertySetter(typeof(MySessionComponentBase), nameof(MySessionComponentBase.Loaded)));
			s_harmony.Patch(AccessTools.Method(typeof(MySession), nameof(MySession.UnloadDataComponents), new[] { typeof(bool) }),
				prefix: new HarmonyMethod(typeof(Patches), nameof(UnloadDataComponentsPrefix)));
			Log("Safe unload patch applied");
		}
		catch (Exception e)
		{
			Log("Safe unload patch failed, the game's own unload is unchanged:\n" + e);
		}
		try
		{
			s_harmony.Patch(AccessTools.Method(typeof(MyDefinitionManager), nameof(MyDefinitionManager.LoadData), new[] { typeof(List<MyObjectBuilder_Checkpoint.ModItem>) }),
				prefix: new HarmonyMethod(typeof(Patches), nameof(LoadDataPrefix)));
			s_harmony.Patch(AccessTools.Method(typeof(MyDefinitionManager), nameof(MyDefinitionManager.UnloadData), new[] { typeof(bool) }),
				postfix: new HarmonyMethod(typeof(Patches), nameof(UnloadDataPostfix)));
			Log("Leftover definitions patch applied");
		}
		catch (Exception e)
		{
			Log("Leftover definitions patch failed:\n" + e);
		}
		SeamlessFixes.Apply(s_harmony);
	}

	internal static void Remove()
	{
		s_harmony?.UnpatchAll(HarmonyId);
		s_harmony = null;
	}

	internal static string TakeNotice()
	{
		var parts = new List<string>();
		if (s_crashedMods.Count > 0)
		{
			parts.Add($"Leaving the last world, {string.Join(", ", s_crashedMods.Distinct())} crashed while unloading. The rest of the world was unloaded anyway.");
		}
		if (s_clearedDefinitions)
		{
			parts.Add("The last world hadn't fully unloaded; its leftover definitions were cleared before this load.");
		}
		s_crashedMods.Clear();
		s_clearedDefinitions = false;
		return string.Join(" ", parts) + " Details are in the game log.";
	}

	private static void UnloadDataComponentsPrefix(MySession __instance)
	{
		try
		{
			List<MySessionComponentBase> order = s_loadOrder(__instance);
			for (int i = order.Count - 1; i >= 0; i--)
			{
				MySessionComponentBase component = order[i];
				if (!component.Loaded)
				{
					continue;
				}
				try
				{
					component.UnloadDataConditional();
				}
				catch (Exception e)
				{
					s_setLoaded(component, false);
					string mod = component.ModContext?.ModName;
					string name = string.IsNullOrEmpty(mod) ? component.GetType().FullName : mod;
					s_crashedMods.Add(name);
					Log($"{component.GetType().FullName} ({mod ?? "game"}) crashed in UnloadData; skipped it and carried on unloading:\n{e}");
				}
			}
		}
		catch (Exception e)
		{
			// Leave whatever is left to the game's own loop.
			Log("Safe unload loop failed, the game's own unload continues:\n" + e);
		}
	}

	private static void LoadDataPrefix(MyDefinitionManager __instance)
	{
		if (s_definitionsLoaded)
		{
			Log("Definitions from the last world are still loaded (its unload didn't finish); unloading them before this load");
			try
			{
				__instance.UnloadData();
				__instance.PreloadDefinitions();
				s_clearedDefinitions = true;
			}
			catch (Exception e)
			{
				Log("Clearing the leftover definitions failed:\n" + e);
			}
		}
		s_definitionsLoaded = true;
	}

	private static void UnloadDataPostfix()
	{
		s_definitionsLoaded = false;
	}

	private static void Log(string message)
	{
		MyLog.Default?.WriteLineAndConsole(Tag + message);
	}
}
