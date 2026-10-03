# OreScout

A server-friendly ore scouting plugin for the Pulsar plugin manager. It turns what your ore detector can see into GPS markers you can keep, sort and come back to.

Every scan starts at a real ore detector block and reaches only as far as that detector's own range, and the detector has to be working. OreScout shows nothing the vanilla ore detector couldn't already reveal; it just makes it easier to keep track of.

Its markers carry a hidden `OS:` tag, so OreScout only ever updates or removes markers it made itself. Your own GPS points are never touched.

## Using it in game

Drag any ore detector onto a toolbar. Its action list offers:

| Action | What it does |
|---|---|
| Scout Ore | One marker per ore per asteroid (or planet) in range, merged across nearby asteroids |
| Scout Deposits | One marker per separate deposit in range |
| Clear Deposit Markers | Removes every marker Scout Deposits made |
| Marker Library | Opens the marker library window (also `/scout` in chat) |

The detector must be intact, switched on and powered. The scan radius is the detector's current range setting (its terminal slider), capped at 3 km for modded detectors with very long ranges.

## Settings

The detector's Custom Data gets two sections, `OreScout` and `OreScout Deposits`, each with:

- `Ores`: comma-separated list; blank means every ore in the world. Stone is never reported.
- `MinDepositVoxels`: smallest deposit to report, in m³ (defaults 8 and 64).
- `YieldBonusPercent`: extra yield for modded drills, e.g. `50`.
- `CreateGps`, `ShowChat`.

`OreScout` also has `MergeRadius` (default 1500 m; 0 turns merging off). `OreScout Deposits` has `DepositSpacing` (default 8 m): ore voxels up to that far apart count as one deposit.

Amounts are what a vanilla ship drill would collect, times the world's harvest multiplier and `YieldBonusPercent`.

Rescanning updates markers in place. Scout Ore markers are kept when their ore is mined out, because asteroids get reset; one is only removed when a marker for the same ore within `MergeRadius` replaces it. Scout Deposits markers in range whose deposit is gone are removed.

## Marker library

A window for keeping ore markers without cluttering your GPS list. Open it with the Marker Library action or `/scout` in chat. **Export from GPS** moves Scout Ore markers out of the GPS list into the library (deposit markers stay in GPS), and **Delete** removes the selected one. **Show** (top right) filters to one ore, **From you** gives each marker's distance from you, and **Measure From Row** fills the **From marker** column (renamed after that marker, e.g. **From Ice**) with distances from the selected marker (**Measure From Me** clears it). Tick markers with **Mark / Unmark** or a double-click, then **Import Marked**, or use **Import All Shown** to import everything the filter shows. Click a column header to sort. The library is saved per world name as `OreScout_Markers_<world>.xml` in `%AppData%\SpaceEngineers\Storage\`.

## Chat personality

OreScout is a relentlessly enthusiastic frontier prospector. It says hello shortly after you load in, and comments in chat when you start a scan, when a scan finds ore (or nothing), when you open the marker library, and when you export markers into it. Each scan gets one comment, about the best find: rare ores (platinum, uranium, gold, silver) beat common ones, and nearer beats farther.

Set how much it talks with **Chat** at the top of the marker library window: **Off**, **Quiet** (important lines only), **Normal** (the default) or **Chatty**. The setting is yours alone and applies in every world. It's saved in `OreScout_Settings.txt` in `%AppData%\SpaceEngineers\Storage\`. Lines only appear on your own screen; nothing is sent to other players.

## Switching between our plugins

If any of our other plugins are loaded too (BaR Maid, Fat Albert, OreScout, Sacrificial Stockpile Manager, Script to Plugin), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Building

The project also builds `Shared/PluginSwitcher.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/OreScout.csproj` if Space Engineers isn't in the default Steam location, then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/OreScout.dll`.
