# Shared

Code that more than one plugin in this repo uses. Each file here exists once, and every plugin that needs it links it into its own project, so changing it in one place changes it everywhere on the next build. Each plugin still compiles its own copy, so every plugin works on its own and nothing extra has to be installed.

A project links a file like this (in `Projects/<Name>/Source/<Name>.csproj`):

```xml
<ItemGroup>
  <Compile Include="../../../Shared/PluginSwitcher.cs" Link="Shared/PluginSwitcher.cs" />
</ItemGroup>
```

## PluginSwitcher.cs

Lets our plugins find each other in game:

- **Dropdown:** the top-left corner of each plugin's window shows a dropdown with the plugin's name. Picking another plugin closes the window and opens that plugin's. It only lists plugins that are loaded, and it's hidden when no other one is.
- **`/tim` in chat** opens the window you used last, or the first plugin in the list if you haven't opened one yet. The name is a nod to Tim "The Toolman" Taylor.

The plugins talk over a private mod message channel (`0x54494D5F53574954`, "TIM_SWIT"), so none of them depends on another being loaded. Every loaded plugin sees `/tim`; only the first one alphabetically acts on it.

| Plugin | Name in the dropdown | Window |
|---|---|---|
| BaR Maid | BaR Maid | Main window |
| Fat Albert | Fat Albert | Main window |
| OreScout | OreScout | Marker library |
| Sacrificial Stockpile Manager | Stockpile Manager | Main window |
| Script to Plugin | Script to Plugin | Main window |

To add a plugin:
1. Link the file into its project (above).
2. In its session's `BeforeStart`, call `PluginSwitcher.Register("Name", openWindowAction)`. In `UnloadData`, call `PluginSwitcher.Unregister()`.
3. In its window's `RecreateControls`, replace `AddCaption("…");` with `PluginSwitcher.AddSwitcher(this, AddCaption("…"));`.

## AssemblerModes.cs

One assembler mode that every plugin that queues work respects, stored on the block so it's set once for all of them:

```
[TIM]
Assembler=Coop
```

| Mode | Meaning |
|---|---|
| Unset | The default. Treated exactly like Manual, so no plugin uses an assembler until the player makes it Main or Co-op. |
| Main | Takes the orders. Kept out of the game's cooperative mode so co-op assemblers can help it. |
| Coop | Kept in cooperative mode. Never gets orders directly; takes a share of a Main assembler's work. |
| Manual | No plugin queues on it or changes it in any way. |

In the game, a cooperative assembler only takes work from a conveyor-connected assembler that is not cooperative and has a queue, so the orders have to go to a Main one.

How a plugin uses it:
1. Leave out `AssemblerModes.IsManual(block)` assemblers from everything: queuing, disassembly, mode switches. `IsManual` is true for Unset too.
2. Before queuing, call `AssemblerModes.PrepareForOrders(usable)` with the assemblers the plugin may use right now (on, working, in assembly mode, accessible). It skips Manual and Unset ones, makes one Main if none of the rest is (preferring one already out of cooperative mode, then the lowest entity id, so every plugin picks the same one), sets the game's cooperative switch to match, and returns the assemblers to queue into.
3. Queue each order into one of the returned assemblers (BaR Maid takes the shortest queue).
4. Count what's already queued on **every** assembler of the construct, not just the Main ones: co-op assemblers move work out of a Main assembler's queue into their own. Skip queues of assemblers in disassembly mode.
5. Let the player set a mode with `AssemblerModes.Set(assembler, mode)`. Since everything starts out Manual, every plugin that queues work needs a way to set it, so a player never has to switch plugins just to opt an assembler in.

Modes are only ever filled in, never swapped, so two plugins can't fight over an assembler.

## Personality.cs

Gives a plugin a voice: short in-character lines in chat when something happens. Lines only show on your own screen; nothing is sent to the server or other players.

How a plugin uses it:
1. Link the file, and embed the plugin's lines (kept next to its csproj):
   ```xml
   <EmbeddedResource Include="Personality.txt" LogicalName="Personality.txt" />
   ```
2. In `BeforeStart`, set `Personality.Level` from the plugin's saved settings, then call `Personality.Register("Name shown in chat")`. In `UnloadData`, call `Personality.Unregister()`.
3. Add a **Personality** setting to the plugin's settings page: Off, Quiet, Normal (the default) or Chatty. Save it with the plugin's other settings and set `Personality.Level` when it changes.
4. Call `Personality.Say("event_key")` on the game thread whenever something worth a comment happens. Pass name/value pairs to fill placeholders: `Say("starved", "item", "Steel Plate")` turns `{item}` into `Steel Plate`. It's cheap to call often, because the cooldowns decide whether anything is said.

`Personality.txt`:

```
# Comments and blank lines are skipped.
[starved]
Out of {item} again. I'm a maid, not a miracle worker.
- A leading "- " is dropped, so pasted bullet lists work.
[!grid_lost]
Lines under a key marked with ! are important: said even on Quiet.
```

When it talks:

| Level | What it says | Least time between its own lines |
|---|---|---|
| Off | Nothing | - |
| Quiet | Important events only | 2 minutes |
| Normal | Everything | 45 seconds |
| Chatty | Everything | 15 seconds |

On every level, the same event isn't commented on again for 3 minutes, the same line is never used twice in a row, and after any of our plugins speaks, the others wait 8 seconds.

### Banter (the director)

The plugins share a private message channel (`0x54494D5F50455253`, "TIM_PERS"). A banter plugin can act as the **director**: it announces itself, and from then on the plugins send it their events instead of talking themselves. The director decides who speaks. It can ask a plugin to say one of its own lines, or show a written exchange between several characters itself.

Messages are `object[]` arrays:

| Message | Sent by | Meaning |
|---|---|---|
| `{"hello", name}` | Plugin, on load | Asks whether a director is loaded. |
| `{"director", directorName}` | Director | Sent on load and in answer to "hello". Plugins send it their events from then on. |
| `{"bye", directorName}` | Director, on unload | Plugins go back to talking themselves. |
| `{"event", name, key, values}` | Plugin | Something happened. `values` is the name/value `string[]` passed to `Say`. Not sent when the plugin's Personality is Off. |
| `{"say", directorName, targetName, key, values}` | Director | The target says one of its own lines for that event now, ignoring cooldowns. |
| `{"spoke", name}` | Anyone who showed a line | Everyone holds back for 8 seconds. |

Every plugin keeps working on its own, with or without the director.

## Releasing a plugin that uses these files

A plugin that links a file from here needs this folder when it's built from the repo. If a plugin is published through the Pulsar plugin hub, its hub entry must include `Shared` in its source directories as well as the plugin's own folder, or the hub build won't find the file.
