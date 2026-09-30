using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Input;
using VRage.Plugins;
using VRage.Utils;

namespace ScriptToPlugin;

/// <summary>
/// Some keyboards and keyboard/mouse software make Windows report key code 255 (or 0) as held down all the time.
/// Neither is a real key, but the game counts it as "a key is held", and the code editor's text box then claims
/// every click, so the editor's buttons (OK, Check Code, Browse Scripts, X) never respond. That happens in a
/// programmable block's own editor too. This clears those two codes each frame, after the game has read the
/// keyboard and before any screen handles input.
/// </summary>
internal static class PhantomKeys
{
	// Reused every frame: the arguments for SetKey(code, false) for key codes 0 and 255.
	private static readonly object[][] ClearArguments = { new object[] { (MyKeys)0, false }, new object[] { (MyKeys)255, false } };

	private static FieldInfo s_stateField;

	private static MethodInfo s_setKey;

	private static bool s_failed;

	/// <summary>Adds the plugin to the game's list of plugins that get HandleInput, which Pulsar doesn't do itself.</summary>
	public static void Register(IHandleInputPlugin plugin)
	{
		try
		{
			if (HandleInputPlugins() is List<IHandleInputPlugin> list && !list.Contains(plugin))
			{
				list.Add(plugin);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Could not register for input: {ex.Message}");
		}
	}

	public static void Unregister(IHandleInputPlugin plugin)
	{
		try
		{
			(HandleInputPlugins() as List<IHandleInputPlugin>)?.Remove(plugin);
		}
		catch
		{
		}
	}

	private static object HandleInputPlugins()
	{
		return typeof(MyPlugins).GetField("m_handleInputPlugins", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
	}

	/// <summary>Called every frame between the keyboard being read and the screens handling input.</summary>
	public static void Clear()
	{
		if (s_failed || MyInput.Static == null)
		{
			return;
		}
		try
		{
			if (s_setKey == null)
			{
				s_stateField = FindField(MyInput.Static.GetType(), "m_keyboardState");
				s_setKey = s_stateField?.FieldType.GetMethod("SetKey", new[] { typeof(MyKeys), typeof(bool) });
				if (s_setKey == null)
				{
					s_failed = true;
					MyLog.Default.WriteLineAndConsole("[ScriptToPlugin] Could not find the keyboard state; phantom keys are left alone.");
					return;
				}
			}
			object state = s_stateField.GetValue(MyInput.Static);
			if (state == null)
			{
				return;
			}
			foreach (object[] arguments in ClearArguments)
			{
				s_setKey.Invoke(state, arguments);
			}
		}
		catch (Exception ex)
		{
			s_failed = true;
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Clearing phantom keys failed, stopped: {ex}");
		}
	}

	private static FieldInfo FindField(Type type, string name)
	{
		for (; type != null; type = type.BaseType)
		{
			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (field != null)
			{
				return field;
			}
		}
		return null;
	}
}
