# Fat Albert

A client-side Pulsar plugin that answers one question: **can this ship lift off and get out of the planet's gravity well, and does it have the fuel and power to do it?**

It reads the ship (thrusters, mass, tanks, batteries, reactors, engines) and the planet (gravity, atmosphere, where gravity ends), then flies the ship straight up on paper, step by step, burning fuel and power the way the game does. Nothing on the ship is changed, so it works on any server.

## Opening it

- Type `/fat` (or `/fatalbert`) in chat, or
- Put **Fat Albert: lift-off check** from a cockpit or remote control onto a toolbar. It opens on that ship, or
- Pick **Fat Albert** in the plugin list at the top left of any of our other plugins' windows (`/tim` opens the one used last).

## Inputs

| Control | What it does |
|---|---|
| Ship | Your ships within 5 km; the one you're sitting in comes first |
| View | Lift-off check, Thrust by direction, Thrusters, Fuel & power, Help |
| Lift with | Which thrusters lift the ship, named from the cockpit. **Up** = the thrusters that push the ship up (flames pointing down). **Auto** picks the side facing away from the planet right now |
| Climb km | Empty: climb to where the planet's gravity ends (read from the world). A number: climb that many km from here |
| Speed m/s | Empty: the world's speed limit. Slower climbs burn more |
| Count blocks that are switched off... | On: blocks that are off, stockpiling or recharging count as if you'll switch them on. Off: only what works now |

## Views

| View | Shows |
|---|---|
| Lift-off check | YES/NO with the reason; thrust-to-weight here; the heaviest the ship can be to lift off, and to reach space; climb distance and time; the weakest point on the way up; fuel, battery and reactor fuel used and left; power shortages |
| Thrust by direction | All six directions: thrusters, thrust here, thrust in space, thrust-to-weight here, what they burn |
| Thrusters | Each kind of thruster (modded too): count, direction, fuel, thrust each, effectiveness at this altitude, how many are off |
| Fuel & power | Gas tanks, batteries, reactors, engines, solar/wind: stored, output, used to climb, left after |

## How the climb is worked out

All formulas are copied from the game's code:

- **Gravity** (`MySphericalNaturalGravityComponent`): constant up to the planet's hill tops, then falls off with the planet's falloff exponent; it stops where it drops to 0.05 g (the gravity limit). That's the default end of the climb.
- **Air density** (`MyPlanet.GetAirDensity`): falls linearly from sea level to the top of the atmosphere.
- **Thrust** (`MyThrusterBlockThrustComponent`): each thruster's definition force × its thrust multiplier, scaled by its effectiveness at the current air density (atmospheric thrusters fade out as the air thins; ions get stronger). Modded thrusters are read the same way.
- **Fuel** (`MyEntityThrustComponent`): power at full thrust = definition power × power multiplier × atmosphere effectiveness × gravity factor; gas thrusters burn power / (efficiency × gas energy density) litres a second. Every thruster facing the same way fires at the same percentage, as in the game.
- **Power**: electric thrusters draw from solar/wind first, then batteries, then reactors and hydrogen engines (the game's resource group priorities). If they want more power than the ship can make, they push less.
- **Flight**: full thrust until the speed limit (or the speed you set), then just enough thrust to hold that speed. Half-second steps; gives up after 4 hours.
- **Heaviest mass to reach space**: a search over the ship's mass with the same climb.

Not counted: power used by the rest of the ship, ice in O2/H2 generators, ships docked by connector, conveyor connections (all tanks and thrusters are assumed connected), other planets' or moons' gravity. Solar and wind count their current output for the whole climb. Gas has no mass in the game, so the ship's mass stays the same.

## Names used by this plugin

| What | Name |
|---|---|
| Assembly / namespace | `FatAlbert` |
| Chat commands | `/fat`, `/fatalbert` |
| Toolbar action | `FatAlbert_OpenMenu` |
| Settings file (local storage) | `FatAlbert_Settings.txt` |
| Custom Data, GPS markers | none |

## Building

```
cd "Projects/Fat Albert/Source"
dotnet build -c Release
```

The DLL is `Source/bin/Release/net481/FatAlbert.dll`. The project links `Shared/PluginSwitcher.cs`, so a Pulsar hub release must include `Shared` in its source directories.
