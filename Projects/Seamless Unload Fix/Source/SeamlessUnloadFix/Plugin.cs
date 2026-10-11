using System;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Plugins;

namespace SeamlessUnloadFix;

public class Plugin : IPlugin, IDisposable
{
	public const string Name = "Seamless Unload Fix";

	public void Init(object gameInstance)
	{
		Patches.Apply();
	}

	public void Update()
	{
		SeamlessFixes.Update();

		// What the patches caught while the last world was unloading, shown once the next world has a player.
		if (Patches.HasNotice && MySession.Static?.LocalHumanPlayer != null && MyAPIGateway.Utilities != null)
		{
			MyAPIGateway.Utilities.ShowMessage(Name, Patches.TakeNotice());
		}
	}

	public void Dispose()
	{
		Patches.Remove();
	}
}
