# SpaceEngineers

Plugins for Space Engineers, loaded with the [Pulsar](https://github.com/SpaceGT/Pulsar) plugin manager. Each project lives in its own folder under [`Projects/`](Projects/) and has a README with the full details.

All of them run on the client: they need nothing installed on the server and work in multiplayer through the same requests the game's own terminal sends.

## Projects

| Plugin | What it does | Open it with |
|---|---|---|
| [BaR Maid](Projects/BaR%20Maid/) | Companion for the SKO Nanobot Build and Repair System. It queues the components the systems are missing into their assemblers. An in-game menu shows the targets, missing components and priority lists and changes the systems' settings, with separate groups per hangar or bay. A plugin version of the BaR companion script, using a menu instead of LCD panels. | `/barmaid`, or the **BaR Maid** toolbar action on a Build and Repair block |
| [Sacrificial Stockpile Manager](Projects/Sacrificial%20Stockpile%20Manager/) | Inventory management: shows every grid's inventory, sorts items into chosen storage, keeps blocks stocked between a minimum and maximum, and queues grid-wide quotas in assemblers. | `/ssm`, or the **Stockpile Manager** toolbar action |
| [Script to Plugin](Projects/Script%20to%20Plugin/) | Runs programmable block scripts in your own game instead of in a programmable block, each one acting like its own programmable block. | `/stp`, or the **Script to Plugin menu** toolbar action |
| [OreScout](Projects/OreScout/) | Server-friendly ore scanning from real ore detector blocks, limited to the detector's own range, with GPS markers and a marker library. | Ore detector toolbar actions, `/scout` for the marker library |

Each plugin has its own DLL name, action IDs, chat command and Custom Data section, so they can all be loaded together without clashing.

## Building

You need the .NET SDK and Space Engineers installed.

1. If the game isn't in the default Steam location, set `Bin64` in the project's `.csproj`, or pass it on the command line: `dotnet build -c Release -p:Bin64="D:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64"`.
2. Run `dotnet build -c Release` in the project's `Source` folder.
3. The plugin DLL is written to `Source/bin/Release/net481/`. Add it to Pulsar as a local plugin.

The projects target .NET Framework 4.8.1 (x64) and reference the game's DLLs straight from its `Bin64` folder, without copying them.
