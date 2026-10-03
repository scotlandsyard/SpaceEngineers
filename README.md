# SpaceEngineers

Plugins for Space Engineers, loaded with the [Pulsar](https://github.com/SpaceGT/Pulsar) plugin manager. Each project lives in its own folder under [`Projects/`](Projects/) and has a README with the full details.

All of them run on the client: they need nothing installed on the server and work in multiplayer through the same requests the game's own terminal sends.

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

## Building

You need the .NET SDK and Space Engineers installed.

1. If the game isn't in the default Steam location, set `Bin64` in the project's `.csproj`, or pass it on the command line: `dotnet build -c Release -p:Bin64="D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"`.
2. Run `dotnet build -c Release` in the project's `Source` folder.
3. The plugin DLL is written to `Source/bin/Release/net481/`. Add it to Pulsar as a local plugin.

The projects target .NET Framework 4.8.1 (x64) and reference the game's DLLs straight from its `Bin64` folder, without copying them.

Code used by more than one project lives once in [`Shared/`](Shared/) and is linked into each project that uses it.
