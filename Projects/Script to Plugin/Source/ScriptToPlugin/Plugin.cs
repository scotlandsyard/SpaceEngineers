using System;
using VRage.Plugins;
using VRage.Utils;

namespace ScriptToPlugin;

public class Plugin : IHandleInputPlugin
{
	public const string Name = "Script to Plugin";

	public static Plugin Instance { get; private set; }

	public void Init(object gameInstance)
	{
		Instance = this;
		PhantomKeys.Register(this);
	}

	/// <summary>Runs every frame after the keyboard is read and before screens handle input.</summary>
	public void HandleInput()
	{
		PhantomKeys.Clear();
	}

	public void Update()
	{
		try
		{
			ScriptEditors.Pump();
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Plugin.Update: {ex}");
		}
	}

	public void Dispose()
	{
		PhantomKeys.Unregister(this);
		ScriptEditors.Clear();
		Instance = null;
	}
}
