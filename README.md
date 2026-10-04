# SpaceEngineers

Plugins for Space Engineers, loaded with the [Pulsar](https://github.com/SpaceGT/Pulsar) plugin manager. Each project lives in its own folder under [`Projects/`](Projects/) and has a README with the full details.

All of them run on the client: they need nothing installed on the server and work in multiplayer through the same requests the game's own terminal sends.

## Install

There are two ways to get a plugin. Either way, you end up with a DLL to drop into Pulsar.

**Download it (easiest)**

1. Open [Releases](../../releases) and download the plugin's `.zip`. Each plugin has its own release, and its README is inside the zip.
2. Windows blocks files downloaded from the internet, and Pulsar can't load a blocked DLL (its log shows `0x80131515`). Before unzipping, right-click the `.zip` > **Properties**, tick **Unblock** at the bottom and press **OK**.
3. Close the game. Copy the `.dll` from the zip into the `Legacy\Local` folder of your Pulsar install: `%AppData%\Pulsar\Legacy\Local` unless you installed Pulsar somewhere else.
4. Start the game with Pulsar, tick the plugin in the plugin list and restart when asked.

If a plugin you've already copied in doesn't load, unblock the DLL itself the same way, or run this in PowerShell to unblock everything in the folder (change the path if Pulsar is elsewhere): `Get-ChildItem "$env:AppData\Pulsar\Legacy\Local" | Unblock-File`

**Build it yourself (if you'd rather not run a DLL from someone else)**

Every line of code is in this repo, and the build uses only the .NET SDK and your own game files.

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (the releases are built with .NET SDK 10) and have Space Engineers installed through Steam.
2. Get the source: **Code > Download ZIP** above and unzip it, or `git clone` the repo.
3. Open PowerShell in that folder and run `.\Build.ps1` to build every plugin, or `.\Build.ps1 "BaR Maid"` for one. If Windows blocks the script, run `powershell -ExecutionPolicy Bypass -File .\Build.ps1` instead.
4. The DLLs land in the `Build` folder. Install them the same way as step 3 above. DLLs you build yourself aren't blocked.

`Build.ps1` finds the game through Steam. If it can't, pass the folder: `.\Build.ps1 -Bin64 "D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"`.

**Checking a download against the source:** the builds are deterministic. Building the release's version of the source with the same game version gives a byte-for-byte identical DLL. `Build.ps1` prints each DLL's SHA256, and each release lists the SHA256 of its DLL, so you can compare them. A different .NET SDK version can produce a different hash with the same code. In that case, compare by building with the SDK version named in the release notes.

## Projects

| Plugin | What it does | Open it with |
|---|---|---|
| [BaR Maid](Projects/BaR%20Maid/) | Companion for the SKO Nanobot Build and Repair System. It queues the components the systems are missing into their assemblers. An in-game menu shows the targets, missing components and priority lists and changes the systems' settings, with separate groups per hangar or bay. A plugin version of the BaR companion script, using a menu instead of LCD panels. | `/barmaid`, or the **BaR Maid** toolbar action on a Build and Repair block |
| [Sacrificial Stockpile Manager](Projects/Sacrificial%20Stockpile%20Manager/) | Inventory management: shows every grid's inventory, sorts items into chosen storage, keeps blocks stocked between a minimum and maximum, and queues grid-wide quotas in assemblers. | `/ssm`, or the **Stockpile Manager** toolbar action |
| [Fat Albert](Projects/Fat%20Albert/) | Lift-off check: works out whether a ship can take off and climb out of a planet's gravity well, and how much fuel and power it uses on the way. Shows thrust-to-weight in every direction and how much more weight the ship can carry. | `/fat`, or the **Fat Albert: lift-off check** toolbar action on a cockpit or remote control |
| [Script to Plugin](Projects/Script%20to%20Plugin/) | Runs programmable block scripts in your own game instead of in a programmable block, each one acting like its own programmable block. | `/stp`, or the **Script to Plugin menu** toolbar action |
| [OreScout](Projects/OreScout/) | Server-friendly ore scanning from real ore detector blocks, limited to the detector's own range, with GPS markers and a marker library. | Ore detector toolbar actions, `/scout` for the marker library |
| [Wilson](Projects/Wilson/) | The neighbour over the fence: directs the family's chat personalities. Decides who speaks when something happens, stages short exchanges between the characters, greets once for everyone on world load, and has a few proverbs of his own. Off makes every plugin talk on its own again. | `/wilson`, or **Wilson** in the plugin switcher dropdown |

Each plugin has its own DLL name, action IDs, chat command and Custom Data section, so they can all be loaded together without clashing.

## Working together

With more than one of these plugins loaded:

- **Switching windows:** the top-left corner of each plugin's window has a dropdown that jumps to another loaded plugin's window. Type `/tim` in chat to reopen the window you used last.
- **Chat personalities:** each plugin has a character who comments in chat when something happens, with an Off / Quiet / Normal / Chatty setting in that plugin. With Wilson loaded too, the characters talk to each other. Chat lines only show on your own screen.
- **Assembler modes:** BaR Maid and Sacrificial Stockpile Manager share one mode per assembler: **Main** (takes the orders), **Co-op** (helps a Main one) or **Manual** (left alone). New assemblers start as Manual, so neither plugin touches an assembler until you opt it in. You can set the mode from either plugin.

Each plugin still works on its own; none of them needs another to be loaded. See [`Shared/`](Shared/) for how this works.

## Building by hand

`Build.ps1` (see [Install](#install)) is the simple way. To build one project yourself:

1. Run `dotnet build -c Release` in the project's `Source` folder. If the game isn't in the default Steam location, add `-p:Bin64="D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"`.
2. The plugin DLL is written to `Source/bin/Release/net481/`.

The projects target .NET Framework 4.8.1 (x64) and reference the game's DLLs straight from its `Bin64` folder, without copying them. Release builds are deterministic and record no paths from the computer they were built on. `.gitattributes` fixes the line endings, so every checkout builds the same bytes.

Code used by more than one project lives once in [`Shared/`](Shared/) and is linked into each project that uses it.
