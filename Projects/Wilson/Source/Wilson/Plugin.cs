using System;
using VRage.Plugins;

namespace Wilson;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Wilson";

	public static Plugin Instance { get; private set; }

	public void Init(object gameInstance)
	{
		Instance = this;
	}

	public void Update()
	{
	}

	public void Dispose()
	{
		Instance = null;
	}
}
