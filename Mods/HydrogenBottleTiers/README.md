# Hydrogen Bottle Tiers

A Space Engineers workshop mod that adds three bigger hydrogen bottles, tiered like the vanilla hand tools. The vanilla bottle is not changed.

| Bottle | Capacity | Mass | Made in | Recipe (ingots) | Time |
|---|---|---|---|---|---|
| Hydrogen Bottle (vanilla) | 400 L | 30 kg | Basic assembler, assembler | Iron 80, Silicon 10, Nickel 30 | 10 s |
| Enhanced Hydrogen Bottle | 600 L | 35 kg | Basic assembler, assembler | Iron 105, Silicon 25, Nickel 40, Cobalt 5 | 15 s |
| Proficient Hydrogen Bottle | 1000 L | 50 kg | Assembler | Iron 240, Silicon 40, Nickel 65, Cobalt 15, Silver 5, Magnesium 0.5 | 30 s |
| Elite Hydrogen Bottle | 1800 L | 75 kg | Assembler | Iron 400, Silicon 80, Nickel 100, Cobalt 22.5, Silver 12.5, Magnesium 2.5, Gold 5, Platinum 2 | 60 s |

- The new bottles work everywhere the vanilla one does: they refill your jetpack automatically, and H2/O2 generators and hydrogen tanks fill them, plus modded tanks that take bottles the vanilla way (through the HydrogenBottles blueprint class).
- The icons are the vanilla bottle with one, two or three chevrons, like the vanilla tool tiers.
- The bottle count on the HUD, next to the jetpack fuel bar, counts every non-empty hydrogen bottle of any tier. The vanilla count only includes the vanilla bottle.

## For server admins

Capacity, mass, build time and recipe can be changed per bottle. On its first load the mod writes a settings file into the world's folder:

```
<world folder>\Storage\<workshop id>.sbm_HydrogenBottleTiers\HydrogenBottleTiers.xml
```

Edit it while the server is stopped, or restart after editing. Clients don't need to do anything: when a player joins, the server sends its settings, so everyone sees the same capacities and recipes.

```xml
<Bottle Subtype="HydrogenBottleTier4">
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

- A recipe can use any item type: `Ingot`, `Ore`, `Component`, or items from other mods.
- Invalid values fall back to the mod's defaults rather than breaking anything. A whole recipe falls back if any of its lines is wrong, so a typo can't make a bottle free. Each problem is written to the server log in a line starting with `[HydrogenBottleTiers]`.
- Delete the file to get the defaults back. When a new version of the mod adds settings, they're added to your file and your values are kept.
- Changing a recipe doesn't change bottles that already exist. Changing capacity applies to all bottles, including full ones, because the game stores a bottle's fill as a percentage.

## Server friendliness

- No input handling, no toolbar or tool code, and nothing that runs every tick on the server. The script runs once at world load, then only answers one small message per joining player (at most one every 5 seconds per player).
- The HUD counter runs only on players' PCs, once a second, and only reads that player's own inventory.
- If a game update ever breaks the script, the bottles keep working with their default values: they're plain definitions. Only the settings file and the HUD count would stop working until the mod is updated.

## Development

- `Data\*.sbc`: the definitions; their values are the defaults and must match `TierConfig.CreateDefault()` in `Data\Scripts\HydrogenBottleTiers\Config.cs`.
- `Data\Scripts\HydrogenBottleTiers\`: `Session.cs` (settings file, sync, applying values), `HydrogenBottleHudStat.cs` (HUD count).
- `Source\HydrogenBottleTiers.csproj` only checks that the scripts compile (`dotnet build -c Release`). It doesn't check the mod script whitelist, so only an in-game load proves the scripts are allowed.
- `Stage-Mod.ps1` copies the game files into the local Mods folder for testing and publishing, without the build output.
