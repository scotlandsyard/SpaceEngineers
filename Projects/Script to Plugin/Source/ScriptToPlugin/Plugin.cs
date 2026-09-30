using System;
using VRage.Plugins;

namespace ScriptToPlugin;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Script to Plugin";

	public static Plugin Instance { get; private set; }

	public void Init(object gameInstance)
	{
		Instance = this;
	}

	public void Update()
	{
		try
		{
			ScriptEditors.Pump();
		}
		catch (Exception ex)
		{
			VRage.Utils.MyLog.Default.WriteLineAndConsole($"[ScriptToPlugin] Plugin.Update: {ex}");
		}
	}

	public void Dispose()
	{
		ScriptEditors.Clear();
		Instance = null;
	}
}
