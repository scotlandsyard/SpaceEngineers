# Script to Plugin

A client-side Pulsar plugin that runs programmable block scripts in your own game instead of in a programmable block. Every script in its menu works like its own programmable block, so one plugin can stand in for any number of them.

- Paste any in-game script, or load one with **Browse Scripts** in the game's own code editor (local and workshop scripts).
- Scripts compile with the game's own script compiler and whitelist, and have the programmable block's limits (50,000 instructions per run).
- `Me`, `GridTerminalSystem`, `Runtime`, `Storage`, `Echo`, `Save()`, `UpdateFrequency` and run arguments work as in a programmable block. `IGC` works between the plugin's own scripts.
- Works on multiplayer servers without anything installed on the server.

## Opening the menu

- Type `/stp` in chat (`/stp name <name>` renames the plugin's chat voice instead), or
- Use the **Script to Plugin menu** toolbar action of a block that hosts a script.

| Control | What it does |
|---|---|
| Script list | Every script in this world, with its host block and state. Double-click one to edit its code |
| Right panel | The selected script's state, host, run time, and its output (Echo) and errors, like a programmable block's detailed info |
| New script | Pick a host block, then write or paste the code |
| Edit code | Opens the game's code editor. OK saves and restarts the script |
| Custom Data | The script's own Custom Data (`Me.CustomData`) |
| Set host block | Moves the script to another block |
| Switch on / off | Off calls the script's `Save()` and stops it. On starts it again |
| Recompile | Saves, compiles and restarts the script |
| Rename / Delete | Rename keeps its toolbar slots working. Delete asks first |
| Argument + Run | Runs the script once with that argument. The box is also the default argument |
| Help | In-game help. Back returns to the output |
| Chat as + dropdown | The name the plugin's chat lines show under, and how much it talks (see Chat personality) |

The window closes with the X in its corner or with Esc. The list and output refresh twice a second.

## The host block

A script needs a block to stand in for its programmable block. You pick it when you create the script, from the loaded grids near you, and can change it later. The script sees it as `Me`:

- **From the host:** `Me.CubeGrid`, position, orientation, owner, `IsFunctional`/`IsWorking`, inventory and screens (`Me.GetSurface`). `GridTerminalSystem` is the host's grid and everything connected to it, just as for a programmable block on that grid.
- **The script's own:** `Me.CustomName` (the script's name), `Me.CustomData`, `Me.Enabled`, `Me.DetailedInfo` and `Me.TerminalRunArgument`. A script can't rename, overwrite or switch off its host by accident.
- `Me` has no terminal actions or properties (`Me.GetActionWithName` returns null). Handing out the host's actions would let a script turn the host off when it meant to turn itself off.
- Any block you have access to works. If the script draws on `Me`'s screen, pick a block with screens: a programmable block, cockpit or LCD. The block picker lists those first.

If you pick a programmable block that already has a script, you're asked whether to copy its code, Custom Data and default argument. Switch the real programmable block off afterwards, or the script runs twice.

## Toolbar actions

Every block that hosts a script gets these actions, one set per script:

| Action | What it does |
|---|---|
| `<script>: Run` | Asks for an argument and a slot label, like the programmable block's Run action |
| `<script>: Run (default argument)` | Runs with the argument from the menu |
| `<script>: On/Off` | Switches the script on or off; the slot shows On or Off |
| Script to Plugin menu | Opens the menu on this block's script |

They work from your own toolbar (cockpit, remote control, and so on). Button panels, timers, sensors and event controllers run their actions on the server, where the plugin isn't loaded, so they can't run these scripts.

## Multiplayer

Everything runs on your client. Nothing is needed on the server.

- **Changes go through the server.** When a script switches a block, changes a setting or writes to Custom Data, the game sends the same request as a change made in the terminal. The server checks your access, so a script can't do anything you couldn't do by hand. `GridTerminalSystem` only lists blocks you have access to, as a programmable block you own would.
- **LCDs.** Text a script writes (`WriteText`) is sent to the server when it changes, so everyone sees it. Sprites a script draws (`DrawFrame`) are shown only on your own screen.
- **Only while you're around.** Scripts run while you're online and the host's grid is loaded near you. When the grid unloads, the script stops and keeps its Storage. It starts again when the grid is back.
- **IGC.** The game's IGC runs on the server and only knows real programmable blocks. The plugin's scripts talk to each other through the plugin's own IGC, with the game's rules (next-tick delivery, 25 waiting messages per listener, message callbacks). Reach depends on how the host grids are connected; antenna range isn't checked, so anything farther counts as in antenna range. Scripts can't message real programmable blocks, and real programmable blocks can't message them.
- **Worlds with scripts switched off.** If the server has in-game scripts switched off, the plugin still runs your scripts in your game, and the menu shows a note about it.
- **Other players** don't see the scripts, their output or their sprites.

## Differences from a programmable block

- There is no block: the host block stands in for it (see above), and output shows in the menu instead of the terminal.
- `Me` has no terminal actions or properties, and `Me.TryRun` on itself returns false (as it does in a running programmable block).
- Timers, sensors, button panels and event controllers can't run the scripts (see Toolbar actions).
- IGC only reaches the plugin's own scripts.
- Scripts start again after a world load like a programmable block: the constructor runs with the saved Storage.

## Chat personality

Script to Plugin has a voice: a script engine that broke out of the programmable block and is rather proud of it. Its name shows in electric cyan. It comments in chat when a script compiles, starts, stops, fails to compile or crashes. Scripts compiling and starting in the first 10 seconds after the world loads aren't announced; errors are. It doesn't greet you: if Wilson is loaded, it answers his roll call instead. Lines show only on your own screen.

Pick how much it talks with the dropdown above the script list. Our plugins take turns: after any of them speaks, the next ordinary line waits a while.

| Setting | What it says | Wait after anyone's last line |
|---|---|---|
| Off | Nothing | - |
| Quiet | Only compile errors and crashes | 15 minutes |
| Normal (default) | Everything | 5 minutes |
| Chatty | Everything | 2 minutes |

Compile errors and crashes can come sooner, but never within 30 seconds of the last line. The same kind of event is commented on at most every 10 minutes.

**Chat as** sets the name its lines show under, up to 24 characters: type it and press Enter, or type `/stp name <name>` in chat. Clear it (or type `/stp name` alone) to go back to Script to Plugin. A rename from Wilson's window is kept too.

Both settings are yours, the same in every world, and saved in `ScriptToPlugin_Settings.txt` in the plugin's local storage. The lines come from `Source/Personality.txt`, and the shared code is `Shared/Personality.cs` (see [Shared/README.md](../../Shared/README.md)).

## Where scripts are kept

Code, Custom Data, Storage, the default argument and the on/off state are saved in the plugin's local storage (under `%AppData%\SpaceEngineers\Storage`), one file per world, named after the world. They're saved a second after a change, every minute while scripts run, when you save the world, and when you leave. Nothing is stored in the world itself, so the scripts are yours and aren't shared with other players.

## Switching between our plugins

If any of our other plugins are loaded too (BaR Maid, Fat Albert, OreScout, Sacrificial Stockpile Manager), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Building

The project also builds `Shared/PluginSwitcher.cs` and `Shared/Personality.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/ScriptToPlugin.csproj` if Space Engineers isn't in the default Steam location. Then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/ScriptToPlugin.dll`.

The plugin uses its own assembly name (`ScriptToPlugin`), namespace, action IDs (`ScriptToPlugin_*`), chat command (`/stp`) and storage file (`ScriptToPlugin_<world>.xml`), so it can't clash with the other plugins in this repo. It has no Custom Data section or GPS markers.
