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
| Wilson | Wilson | Main window |

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

Gives a plugin a voice: short in-character lines in chat when something happens, under the character's name in its own colour. Lines only show on your own screen; nothing is sent to the server or other players.

How a plugin uses it:
1. Link the file, and compile in the plugin's lines (`Personality.txt`, kept next to its csproj) with `EmbedText.targets`:
   ```xml
   <ItemGroup>
     <EmbeddedText Include="Personality.txt" />
   </ItemGroup>
   <Import Project="../../../Shared/EmbedText.targets" />
   ```
   Every build turns `Personality.txt` into `Personality.txt.cs`, a string constant. Commit that file too: Pulsar's PluginHub compiles only `.cs` files, so an embedded resource would be missing there. Edit the `.txt` file, never the generated one.
2. In `BeforeStart`, set `Personality.Level` and `Personality.DisplayName` from the plugin's saved settings, subscribe a save to `Personality.Changed`, then call `Personality.Register("Character name")`. In `UnloadData`, call `Personality.Unregister()`.
3. Add the chat setting with `Personality.AddChatSetting(this, caption)` right after `AddCaption` (and after `PluginSwitcher.AddSwitcher`): a **Chat: Off / Quiet / Normal / Chatty** dropdown at the top right of the window, the same place in every plugin. It sets `Personality.Level`; save `Personality.Level` with the plugin's other settings from `Personality.Changed` (step 4), which fires for level changes too, including ones made from Wilson's window.
4. Add a **name** field next to it: the name the character's lines show under. Set `Personality.DisplayName` when it changes (null or blank means the character's own name), and save `Personality.DisplayName` whenever `Personality.Changed` fires, because the player can also rename the character from Wilson's window. A chat command such as `/<command> name <new name>` can set it the same way.
5. Call `Personality.Say("event_key")` on the game thread whenever something worth a comment happens. Pass name/value pairs to fill placeholders: `Say("starved", "item", "Steel Plate")` turns `{item}` into `Steel Plate`. It's cheap to call often, because the cycle and cooldowns decide whether anything is said.

The character name passed to `Register` is the character's identity: it's how the director addresses it, how Wilson.txt and the checker name it, and what picks its colour. The display name only changes what's shown in chat.

`Personality.txt`:

```
# Comments and blank lines are skipped.
[starved]
Out of {item} again. I'm a maid, not a miracle worker.
- A leading "- " is dropped, so pasted bullet lists work.
[!grid_lost]
Lines under a key marked with ! are important: said even on Quiet.
[rollcall]
Answering Wilson's roll call. {command} is the plugin's chat command, filled in by Wilson.
```

### When it talks: one turn per cycle

The family keeps one rhythm. After any of our characters speaks, nobody says an ordinary line until a cycle has passed, measured with the setting of whoever would speak next:

| Level | What it says | Cycle after anyone's last line |
|---|---|---|
| Off | Nothing | - |
| Quiet | Important events only | 15 minutes |
| Normal | Everything | 5 minutes |
| Chatty | Everything | 2 minutes |

An important line (`[!key]`) can break the cycle, but never within 30 seconds of anyone's last line. On every level, the same event isn't commented on again for 10 minutes, and the same line is never used twice in a row. Wilson uses the same table for his own setting (`Personality.Cycle`).

**Greetings are retired.** `Say("greeting")` does nothing: with Wilson loaded the crew answers his roll call instead, and without him nobody greets. Plugins can drop their greeting timers and `[greeting]` lines at their next change.

**Roll call.** About ten seconds after the world loads, Wilson calls the roll and asks each loaded character for its `[rollcall]` line, with `{command}` filled in (Wilson's `Cast.cs` knows each plugin's command). A plugin with no `[rollcall]` lines yet answers with a plain built-in line. A character on Quiet answers too; one that's Off doesn't.

### Colours

Each character's name shows in its own colour, picked so none is a colour the game uses in chat (admins are Purple, you are CornflowerBlue, allies LightGreen, neutral players and script messages PaleGoldenrod, enemies Crimson, faction text LimeGreen, private text Violet, everything else White). The palette lives in `Personality.cs`; change a colour there and every plugin picks it up on its next build.

| Character | Colour | RGB |
|---|---|---|
| BaR Maid | Hot pink | 255, 105, 180 |
| Fat Albert | Bright orange | 255, 140, 0 |
| Stockpile Manager | Salmon | 250, 128, 114 |
| Script to Plugin | Electric cyan | 0, 229, 255 |
| OreScout | Gold | 255, 215, 0 |
| Wilson | Fence-post tan | 222, 184, 135 |
| Tim | Teal | 0, 206, 180 |
| Any other | Apricot | 255, 180, 110 |

Lines go through `MyHud.Chat.ShowMessage(sender, text, senderColor, messageColor)`, which only adds them to this screen's chat list (the ModAPI `ShowMessage` uses the same list, without a colour).

### Banter (the director)

The plugins share a private message channel (`0x54494D5F50455253`, "TIM_PERS"). A banter plugin can act as the **director**: it announces itself, and from then on the plugins send it their events instead of talking themselves. The director decides who speaks. It can ask a plugin to say one of its own lines, or show a written exchange between several characters itself.

Messages are `object[]` arrays. Every message a plugin sends has seven elements, `{kind, name, key, values, level, displayName, important}`: `name` is the character name passed to `Register`, `level` is the plugin's Personality setting as text (`"Off"`, `"Quiet"`, `"Normal"` or `"Chatty"`), `displayName` is the name its lines show under, and `important` is `"1"` for an important event and `"0"` otherwise. `key` and `values` are null when the kind doesn't use them.

| Message | Sent by | Meaning |
|---|---|---|
| `{"hello", name, …}` | Plugin, on load | Asks whether a director is loaded. |
| `{"director", directorName}` | Director | Sent on load and in answer to "hello". Plugins send it their events from then on. |
| `{"here", name, …}` | Plugin | Answer to "director", and sent again whenever the plugin's Personality setting or display name changes while a director is loaded. Lets a director that loads after the plugins know who's there. |
| `{"bye", directorName}` | Director, on unload or when set to Off | Plugins go back to talking themselves. |
| `{"event", name, key, values, level, displayName, important}` | Plugin | Something happened. `values` is the name/value `string[]` passed to `Say`. Not sent when the plugin's Personality is Off; on Quiet, only sent for important events. |
| `{"say", directorName, targetName, key, values}` | Director | The target says one of its own lines for that event now, ignoring the cycle and cooldowns. The plugin shows it (and sends "spoke") from inside the call, so the director knows straight away whether it had a line. |
| `{"rename", directorName, targetName, newName, level}` | Director (Wilson's Crew page) | The target's display name becomes `newName` (empty for its own). `level` is optional: a level as text sets the target's `Level`, null or empty leaves it alone. The plugin saves both through `Changed` and confirms with "here". A plugin built before `level` was added only takes the name. |
| `{"spoke", name, …}` | Anyone who showed a line | Starts everyone's cycle. |

The level, display name and importance elements and the "here" and "rename" messages were added after the first version. A director must still accept shorter messages (treating a missing level as Normal, a missing name as the character's own, and a missing importance as not important), and learns about a plugin built before then from its first message. Messages are delivered straight from the sender's `SendModMessage` call, so handlers catch their own exceptions and never register or unregister a handler while handling a message.

Every plugin keeps working on its own, with or without the director. The director is [Wilson](../Projects/Wilson/). Wilson links this file for the cycle table, names and colours, but never calls `Register`.

## Releasing a plugin that uses these files

A plugin that links a file from here needs this folder when it's built from the repo. If a plugin is published through the Pulsar plugin hub, its hub entry must include `Shared` in its source directories as well as the plugin's own folder, or the hub build won't find the file.
