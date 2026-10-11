using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Sandbox.Game.World;
using VRage.Utils;

namespace SeamlessUnloadFix;

/// <summary>
/// Clean-up for the Nexus Seamless Switcher plugin, found by its type names because Pulsar compiles the GitHub
/// build under a random assembly name and other builds use their own. Each fix looks up what it needs
/// once, and turns itself off with one log line if a name is missing, so a renamed Seamless only loses that fix.
///
/// Mod list: Seamless's ModAPI keeps a list of mod session components to tell about a switch and never empties it,
///           so after a reload it also calls the dead components of earlier worlds. We call its own ClearCache when a
///        world unloads (finalizer on MySession.Unload: it runs even when the unload throws).
/// Switch state: Seamless's "switch in progress" flags are only cleared by a join result. A switch that dies earlier
///        leaves them on, and the next normal join's JoinResult is then taken for the end of a switch. Seamless
///        keeps the session loaded during a switch, so with no session at all no switch can be running: Update
///        resets the flags then. (Not in MySession.Unload: when a switch is refused, the game unloads inside
///        OnUserJoined and Seamless's own postfix still reads the flag right after.)
/// </summary>
internal static class SeamlessFixes
{
	private const string Tag = "[SeamlessUnloadFix] ";

	private static readonly string[] SwitcherTypes = { "SeamlessClient.Components.ServerSwitcherV1", "SeamlessClient.Components.SectorTransferEngine" };

	private static bool s_looked;

	// Mod list fix, one entry per Seamless build found: its ClearCache and the list it empties
	private static readonly List<(MethodInfo Clear, FieldInfo List)> s_modLists = new List<(MethodInfo, FieldInfo)>();

	// Switch state fix, one entry per switcher class found
	private static readonly List<Switcher> s_switchers = new List<Switcher>();

	private sealed class Switcher
	{
		public string Name;

		public PropertyInfo Switching;

		public PropertyInfo Waiting;

		public PropertyInfo TargetServer;

		public PropertyInfo TargetWorld;

		public FieldInfo Watchdog;
	}

	internal static void Apply(Harmony harmony)
	{
		try
		{
			harmony.Patch(AccessTools.Method(typeof(MySession), nameof(MySession.Unload)),
				finalizer: new HarmonyMethod(typeof(SeamlessFixes), nameof(UnloadFinalizer)));
		}
		catch (Exception e)
		{
			Log("Seamless mod list fix failed to patch MySession.Unload:\n" + e);
		}
	}

	/// <summary>Every frame. Finds Seamless on the first call: by then every plugin has run its Init.</summary>
	internal static void Update()
	{
		if (!s_looked)
		{
			s_looked = true;
			Find();
		}
		if (MySession.Static == null)
		{
			ResetSwitchState();
		}
	}

	private static void Find()
	{
		var seamless = new List<Assembly>();
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type type;
			try
			{
				type = assembly.GetType("SeamlessClient.Seamless", throwOnError: false);
			}
			catch
			{
				continue;
			}
			if (type != null)
			{
				seamless.Add(assembly);
			}
		}
		if (seamless.Count == 0)
		{
			Log("Seamless Switcher isn't loaded; its fixes are off");
			return;
		}
		if (seamless.Count > 1)
		{
			Log($"Warning: {seamless.Count} Seamless Switcher builds are loaded at once; fixing all of them, but only one should be enabled");
		}
		foreach (Assembly assembly in seamless)
		{
			string name = assembly.GetName().Name + " " + assembly.GetName().Version;

			Type modApi = assembly.GetType("SeamlessClient.Components.ModAPI", throwOnError: false);
			MethodInfo clearCache = modApi == null ? null : AccessTools.Method(modApi, "ClearCache", Type.EmptyTypes);
			if (clearCache != null && clearCache.IsStatic)
			{
				s_modLists.Add((clearCache, AccessTools.Field(modApi, "LoadedMods")));
				Log($"Seamless ({name}): mod list fix on");
			}
			else
			{
				Log($"Seamless ({name}): ModAPI.ClearCache not found; mod list fix off");
			}

			bool found = false;
			foreach (string typeName in SwitcherTypes)
			{
				Type type = assembly.GetType(typeName, throwOnError: false);
				if (type == null)
				{
					continue;
				}
				var switcher = new Switcher
				{
					Name = type.Name,
					Switching = AccessTools.Property(type, "isSeamlessSwitching"),
					Waiting = AccessTools.Property(type, "WaitingForClientCheck"),
					TargetServer = AccessTools.Property(type, "TargetServer"),
					TargetWorld = AccessTools.Property(type, "TargetWorld"),
					Watchdog = AccessTools.Field(type, "ConnectionWatchdog")
				};
				if (switcher.Switching?.GetMethod?.IsStatic == true && switcher.Switching.SetMethod != null)
				{
					s_switchers.Add(switcher);
					found = true;
					Log($"Seamless ({name}): switch state fix on ({type.Name})");
				}
			}
			if (!found)
			{
				Log($"Seamless ({name}): no known switcher class; switch state fix off");
			}
		}
	}

	private static Exception UnloadFinalizer(Exception __exception)
	{
		try
		{
			ClearModList();
		}
		catch (Exception e)
		{
			Log("Clearing Seamless's mod list failed:\n" + e);
		}
		return __exception;
	}

	private static void ClearModList()
	{
		foreach ((MethodInfo clear, FieldInfo list) in s_modLists)
		{
			int count = (list?.GetValue(null) as ICollection)?.Count ?? -1;
			clear.Invoke(null, null);
			if (count > 0)
			{
				Log($"Cleared Seamless's list of {count} mod(s) to notify on a switch; the next world registers its own");
			}
		}
	}

	private static void ResetSwitchState()
	{
		foreach (Switcher s in s_switchers)
		{
			try
			{
				bool switching = (bool)s.Switching.GetValue(null);
				bool waiting = s.Waiting != null && (bool)s.Waiting.GetValue(null);
				if (!switching && !waiting)
				{
					continue;
				}
				s.Switching.SetValue(null, false);
				if (s.Waiting?.SetMethod != null)
				{
					s.Waiting.SetValue(null, false);
				}
				if (s.TargetServer?.SetMethod != null)
				{
					s.TargetServer.SetValue(null, null);
				}
				if (s.TargetWorld?.SetMethod != null)
				{
					s.TargetWorld.SetValue(null, null);
				}
				try
				{
					// Official master only: its reconnect watchdog would otherwise switch back to the old target.
					(s.Watchdog?.GetValue(null) as CancellationTokenSource)?.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
				Log($"Seamless {s.Name} still thought a switch was in progress with no world loaded; reset it");
			}
			catch (Exception e)
			{
				Log($"Resetting Seamless {s.Name} failed; this fix is off from now on:\n" + e);
				s_switchers.Remove(s);
				return;
			}
		}
	}

	private static void Log(string message)
	{
		MyLog.Default?.WriteLineAndConsole(Tag + message);
	}
}
