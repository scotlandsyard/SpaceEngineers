# Tiered Gas Bottles (O2 & H2)

A Space Engineers workshop mod that adds three bigger oxygen bottles and three bigger hydrogen bottles, tiered like the vanilla hand tools. The vanilla bottles are not changed.

| Tier | Oxygen | Hydrogen | Mass | Made in | Recipe (ingots) | Time |
|---|---|---|---|---|---|---|
| Vanilla | 40 L | 400 L | 30 kg | Basic assembler, assembler | Iron 80, Silicon 10, Nickel 30 | 10 s |
| Enhanced | 60 L | 600 L | 35 kg | Basic assembler, assembler | Iron 105, Silicon 25, Nickel 40, Cobalt 5 | 15 s |
| Proficient | 100 L | 1000 L | 50 kg | Assembler | Iron 240, Silicon 40, Nickel 65, Cobalt 15, Silver 5, Magnesium 0.5 | 30 s |
| Elite | 180 L | 1800 L | 75 kg | Assembler | Iron 400, Silicon 80, Nickel 100, Cobalt 22.5, Silver 12.5, Magnesium 2.5, Gold 5, Platinum 2 | 60 s |

- The new bottles work everywhere the vanilla ones do: they refill your suit automatically, and H2/O2 generators and gas tanks fill them. That includes modded tanks that take bottles the vanilla way, through the OxygenBottles / HydrogenBottles blueprint classes.
- The icons are the vanilla bottles with one, two or three chevrons, like the vanilla tool tiers.
- The bottle counts on the HUD, next to the suit's oxygen and fuel bars, include every non-empty bottle of that gas, of any tier. The vanilla counts only include the vanilla bottles.

## For server admins

Each bottle can be enabled or disabled, and its capacity, mass, build time and recipe changed. On its first load the mod writes a settings file into the world's folder:

```
<world folder>\Storage\<workshop id>.sbm_TieredGasBottles\TieredGasBottles.xml
```

Edit it while the server is stopped, or restart after editing. Clients don't need to do anything: when a player joins, the server sends its settings, so everyone sees the same bottles, capacities and recipes.

```xml
<Bottle Subtype="HydrogenBottleTier4">
  <Enabled>true</Enabled>
  <CapacityLitres>1800</CapacityLitres>
  <MassKg>75</MassKg>
  <BuildTimeSeconds>60</BuildTimeSeconds>
  <Recipe>
    <Item Type="Ingot" Subtype="Iron" Amount="400" />
    <Item Type="Ingot" Subtype="Platinum" Amount="2" />
    <Item Type="Component" Subtype="SteelPlate" Amount="10" />
  </Recipe>
</Bottle>
```

- The bottles are `OxygenBottleTier2`/`3`/`4` and `HydrogenBottleTier2`/`3`/`4`.
- `Enabled` set to `false` takes the bottle out of every assembler. Bottles that already exist keep working: they can still be refilled, used and traded.
- A recipe can use any item type: `Ingot`, `Ore`, `Component`, or items from other mods.
- Invalid values fall back to the mod's defaults rather than breaking anything. A whole recipe falls back if any of its lines is wrong, so a typo can't make a bottle free. Each problem is written to the server log in a line starting with `[TieredGasBottles]`.
- Delete the file to get the defaults back. When a new version of the mod adds settings, they're added to your file and your values are kept.
- Changing a recipe doesn't change bottles that already exist. Changing capacity applies to all bottles, including full ones, because the game stores a bottle's fill as a percentage.

## Server friendliness

- No input handling, no toolbar or tool code, and nothing that runs every tick on the server. The script runs once at world load, then only answers one small message per joining player (at most one every 5 seconds per player).
- The HUD counters run only on players' PCs, once a second, and only read that player's own inventory.
- If a game update ever breaks the script, the bottles keep working with their default values: they're plain definitions. Only the settings file and the HUD counts would stop working until the mod is updated.

## Development

- `Data\*.sbc`: the definitions; their values are the defaults and must match `TierConfig.CreateDefault()` in `Config.cs`, and the assembler classes must match `BottleInfo` in `Session.cs`.
- `Data\Scripts\TieredGasBottles\`: `Session.cs` (settings file, sync, applying values), `BottleHudStat.cs` (HUD counts).
- `Source\TieredGasBottles.csproj` only checks that the scripts compile (`dotnet build -c Release`). It doesn't check the mod script whitelist, so only an in-game load proves the scripts are allowed.
- `Stage-Mod.ps1` copies the game files (`Data`, `Textures`, `thumb.jpg`, `modinfo.sbmi`) into the local Mods folder for testing and publishing. Nothing else is published: not the build output, this README, or `ORIGINAL-MOD-FINDINGS.md`.
- `ORIGINAL-MOD-FINDINGS.md`: what we found in the mod this one replaces, including the middle-mouse-button tool problem.
