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
| Lift with | Which thrusters lift the ship, named from the cockpit. **Up** = the thrusters that push the ship up (flames pointing down). **Auto**: on the ground (within 1 km of the highest ground), the side facing away from the planet; anywhere else, the side with the most thrust where the climb starts |
| Climb km | Empty: climb to where the planet's gravity ends (read from the world). A number: climb that many km from here |
| Speed m/s | Empty: the world's speed limit. Slower climbs burn more |
| Count off blocks | On: blocks that are off, stockpiling or recharging count as if you'll switch them on. Off: only what works now |
| Parachutes | Open the ship's parachutes when landing on a planet with air (see Parachutes below) |
| Planet | **Where I am now**: lift off from here. **Visit <planet>**: every planet and moon in the world, nearest first; checks landing there and climbing back out |

## Views

| View | Shows |
|---|---|
| Lift-off check | YES/NO with the reason; thrust-to-weight at the start; the heaviest the ship can be to lift off, and to reach space; landing burn (when visiting); climb distance and time; the weakest point on the way up; fuel, battery and reactor fuel used and left; power shortages |
| Thrust by direction | All six directions: thrusters, thrust at the start of the climb, thrust in space, thrust-to-weight at the start, what they burn |
| Thrusters | Each kind of thruster (modded too): count, direction, fuel, thrust each, effectiveness at the start of the climb, how many are off |
| Fuel & power | Gas tanks, batteries, reactors, engines, solar/wind: stored, output, used on the trip, left after |
| All planets | Every planet and moon: distance, surface gravity, thrust-to-weight on the ground, YES/NO, and mass to spare (or too much). Click a planet for its full answer in the summary under the table (air, where gravity ends, landing, climb, fuel used, limits). The planet you're sitting on (within 1 km of its highest ground) is checked from where you are, marked "(on it)"; every other planet, including one whose gravity you're flying in, as a visit. Long text shrinks to fit, and its tooltip has all of it |
| Planet names (setup) | Your names for the planets: name, game's name, distance, gravity, where the name came from. **Paste GPS list** names planets from GPS on the clipboard; **Use this GPS** gives a planet any GPS by hand; pick a row, type a name and **Set name**, or **Game's name** to undo |

## Planet names

Servers often name planets differently from the game (which only knows the planet type, such as `EarthLike` or `Moon`). In **Planet names (setup)**:

- **Paste GPS list**: copy the server's planet GPS from anywhere, in the game's usual `GPS:Name:X:Y:Z:` format (colour and anything after it are ignored, any number of GPS at once). It reads the clipboard the same way the game's GPS screen does and uses the game's own GPS pattern. Each GPS names the planet whose gravity it's inside, nearest that planet's surface first, so a moon's GPS goes to the moon even inside a planet's gravity. Coordinates only need to be roughly right (surface, orbit or centre). A GPS outside every planet's gravity is reported and skipped. If two GPS land on the same planet, the one nearer its centre wins.
- **Use this GPS**: for a GPS that didn't match (or matched the wrong planet). Pick the planet in the table, pick the GPS in the GPS list and press it. The list has every GPS from the last paste, then your own in-game GPS list, each labelled with the planet whose gravity it's in, or how far outside the nearest planet's gravity it is.
- **Set name / Game's name**: type a name for the planet picked in the table, or put the original back.

Names are stored per world in local storage (`FatAlbert_<world>_PlanetNames.txt`, by planet entity id) and show in the Planet dropdown, the All planets view and the answers. Planets of the same type that have no name of yours are numbered (`Moon 1`, `Moon 2`) in a fixed order.

## Visiting another planet

Picking a planet (or the All planets view) checks a round trip from space:

1. **Fall in**: free. The game caps a ship's speed, so it drops at the speed limit with the thrusters idle.
2. **Land**: full thrust from the speed limit to a stop at sea level, with sea-level gravity and air. If thrust can't beat weight there, the answer is NO (it would crash).
3. **Climb back out**: the normal climb from sea level, with whatever fuel and power the landing left.

Auto direction on a visit picks the side with the most thrust at that planet's sea level (that's the side you'll point up). Real terrain sits above or below sea level, so a landing on a mountain has a shorter climb.

## Parachutes

With **Parachutes** ticked, a visit to a planet with air opens every parachute that has its canopy material on board (canvas in vanilla; each opening uses `MaterialDeployCost` items, shared from what's on the ship). Drag is `MyParachute`'s formula, `2.5 × (air × 1.225) × v² × πr² × DragCoefficient`, with the fully open canopy's radius from `ReefAtmosphereLevel` and `RadiusMultiplier`, worked out at sea level. A parachute needs air of at least its `MinimumAtmosphereLevel` (0.2 vanilla).

- The ship comes down at the parachutes' terminal speed (or the speed limit, if lower), so the braking burn starts slower and the parachutes keep pulling while it brakes.
- If thrust can't hold the ship up but the parachutes get it down to 5 m/s or less, it counts as landed (the landing line says so, in amber).
- Modded parachutes are read from their own definitions.

## HUD overlay

Three lines at the left of the screen: the ship and trip, YES/NO with the reason, and thrust/weight, spare mass and what's left in the tanks and batteries. It uses the ship you're sitting in (else the one picked last in the window) and the trip picked under **Planet**, rechecked every two seconds on a background thread.

- Toggle: **Ctrl+Alt+F**, `/fat hud`, or the **HUD** button in the window. Remembered between sessions.
- Move: **Move HUD** in the window (the window closes first). The HUD follows the mouse; left click keeps it there (saved as `HudX`/`HudY`), Esc or right click puts it back. **Reset inputs** moved to the Help page to make room.
- Other hotkey: set `HudKey` in `FatAlbert_Settings.txt` (local storage) while the game is closed, e.g. `HudKey=Ctrl+Shift+H` (key names are `VRage.Input.MyKeys`).
- It's a draw-only screen set up like the game's own HUD screen: it never takes focus or input, and it hides when the game's HUD is hidden.

## Chat personality

Fat Albert, a loud launchmaster, comments in chat through `Shared/Personality.cs`. His lines are in `Source/Personality.txt`, compiled into the DLL. Lines show only on your own screen.

- Setting: the **Chat** dropdown at the top right of the window (Off, Quiet, Normal, Chatty), or `/fat chat off|quiet|normal|chatty`. Saved as `Personality=` in `FatAlbert_Settings.txt`. Default Normal.
- Chat name: `/fat name <new name>` (up to `Personality.MaxNameLength`, 24 characters); `/fat name` alone goes back to "Fat Albert". Saved as `DisplayName=`, and saved again on `Personality.Changed`, so a rename from Wilson's window sticks too. His name shows in bright orange.
- Timing comes from `Personality.cs`: one turn per cycle across all our plugins (after anyone speaks, the next ordinary line waits 2 min on Chatty, 5 on Normal, 15 on Quiet), important lines no sooner than 30 s after anyone's last line, the same event at most every 10 minutes.
- No greeting: Wilson's roll call replaces it, using the `[rollcall]` lines (no code here).
- `menu_opened` when the window opens.
- One line per answer (`Chatter.cs`), for the window's main answer and the HUD's. A line is only considered when the answer for that ship and trip changes, so rechecks of the same answer stay quiet:
  - Made it: `barely_made_it` if the ship couldn't be 10% heavier and still make it, `overpowered` if it could still make it at 3 times its mass, otherwise `check_pass`.
  - `too_heavy` (with `{mass}`; marked important, so it's said even on Quiet), `out_of_fuel` (a gas ran out), `out_of_power` (batteries flat, reactors out of uranium, or a stall while power was short), `thin_air` (a stall with power to spare: the atmospheric thrusters ran out of air).
  - No line for no gravity, mass not read, no thrusters, can't land, or too slow.

## How the climb is worked out

All formulas are copied from the game's code:

- **Gravity** (`MySphericalNaturalGravityComponent`): constant up to the planet's hill tops, then falls off with the planet's falloff exponent; it stops where it drops to 0.05 g (the gravity limit). That's the default end of the climb.
- **Air density** (`MyPlanet.GetAirDensity`): falls linearly from sea level to the top of the atmosphere.
- **Thrust** (`MyThrusterBlockThrustComponent`): each thruster's definition force × its thrust multiplier, scaled by its effectiveness at the current air density (atmospheric thrusters fade out as the air thins; ions get stronger). Modded thrusters are read the same way.
- **Fuel** (`MyEntityThrustComponent`): power at full thrust = definition power × power multiplier × atmosphere effectiveness × gravity factor; gas thrusters burn power / (efficiency × gas energy density) litres a second. Every thruster facing the same way fires at the same percentage, as in the game.
- **Power**: electric thrusters draw from solar/wind first, then batteries, then reactors and hydrogen engines (the game's resource group priorities). If they want more power than the ship can make, they push less.
- **Flight**: full thrust until the speed limit (or the speed you set), then just enough thrust to hold that speed. Half-second steps; gives up after 4 hours.
- **Heaviest mass to reach space**: a search over the ship's mass with the same climb.
- **Ship mass**: `MyCubeGrid.GetCurrentMass` (physical mass, as the cockpit shows it, from the grids' collision shapes), over the grids joined by rotors, pistons and hinges. Not `Physics.Mass`, which reads 0 on multiplayer clients. If that comes back 0 too, the blocks and their cargo are added up instead; if the mass is still 0 the answer says so rather than guessing.

Not counted: power used by the rest of the ship, ice in O2/H2 generators, ships docked by connector, conveyor connections (all tanks and thrusters are assumed connected), other planets' or moons' gravity. Solar and wind count their current output for the whole climb. Gas has no mass in the game, so the ship's mass stays the same.

## Names used by this plugin

| What | Name |
|---|---|
| Assembly / namespace | `FatAlbert` |
| Chat commands | `/fat`, `/fatalbert`, `/fat hud`, `/fat chat <level>`, `/fat name <name>` |
| HUD hotkey | Ctrl+Alt+F (`HudKey` setting) |
| Toolbar action | `FatAlbert_OpenMenu` |
| Settings file (local storage) | `FatAlbert_Settings.txt` |
| Planet names (local storage) | `FatAlbert_<world>_PlanetNames.txt` |
| Custom Data, GPS markers | none |

## Building

```
cd "Projects/Fat Albert/Source"
dotnet build -c Release
```

The DLL is `Source/bin/Release/net481/FatAlbert.dll`. The project links `Shared/PluginSwitcher.cs`, so a Pulsar hub release must include `Shared` in its source directories.
