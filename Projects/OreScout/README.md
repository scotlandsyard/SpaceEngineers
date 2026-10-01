# OreScout

A server-friendly ore scanner for the Pulsar plugin manager. It keeps the ore scans and the marker library, but every scan starts at a real ore detector block and reaches only as far as that detector's own range. It shows nothing the vanilla ore detector couldn't already reveal; it just turns what's in range into GPS markers you can keep.


OreScout uses its own DLL name, action IDs, Custom Data sections, marker tag (`OS:`) and library file.

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

## Marker library

**Export from GPS** moves Scout Ore markers out of the GPS list into the library (deposit markers stay in GPS), **Import to GPS** or a double-click puts one back, and **Delete** removes one. Click a column header to sort. The library is saved per world name as `OreScout_Markers_<world>.xml` in `%AppData%\SpaceEngineers\Storage\`.

## Switching between our plugins

If any of our other plugins are loaded too (BaR Maid, OreScout, Sacrificial Stockpile Manager, Script to Plugin), the top-left corner of the window has a dropdown with this plugin's name. Pick another plugin there to close this window and open that plugin's. Type `/tim` in chat to open whichever of these windows you used last. See [Shared/README.md](../../Shared/README.md).

## Building

The project also builds `Shared/PluginSwitcher.cs` from the repo's `Shared` folder, so build from a full clone of the repo. Set `Bin64` in `Source/OreScout.csproj` if Space Engineers isn't in the default Steam location, then run `dotnet build -c Release` in `Source`. The plugin is written to `Source/bin/Release/net481/OreScout.dll`.

