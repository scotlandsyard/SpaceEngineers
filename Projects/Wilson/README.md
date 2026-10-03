# Wilson

A client-side Pulsar plugin: **the neighbour over the fence**. Wilson directs the chat personalities of the rest of the family (BaR Maid, Fat Albert, Sacrificial Stockpile Manager, Script to Plugin and OreScout), and has a few words of his own.

Without Wilson, each plugin says its own lines in chat. With Wilson loaded, the plugins tell him when something happens and he decides who speaks: usually the plugin itself, and every so often a short written exchange between several characters, one line every few seconds. Chat lines only show on your own screen, so it's fine on any server.

## Opening it

- Type `/wilson` in chat, or
- Pick **Wilson** in the plugin list at the top left of any of the family's windows (`/tim` opens the one used last).

## What he does

| Moment | With Wilson |
|---|---|
| A plugin's event (BaR Maid runs short of an item, Fat Albert's ship is too heavy, OreScout finds rare ore...) | Wilson either asks that plugin to say one of its own lines, or plays an exchange written for that event, if every character in it is loaded. |
| World load | One greeting exchange with whichever characters are loaded, instead of each plugin greeting separately. Alone, Wilson says hello himself. |
| An exchange ends in an argument | Sometimes Wilson steps in with a line to calm things down. |
| A long quiet spell | Rarely, Wilson shares one of his odd proverbs. |

House rules, the same as the plugins keep on their own: one voice at a time, 8 seconds of quiet after anyone speaks, the same event isn't commented on again for 3 minutes, and each character keeps its own pace. The same exchange trigger doesn't come round again for 10 to 30 minutes, depending on Wilson's setting.

## Settings

| Wilson's setting | What he does |
|---|---|
| Off | Nothing at all: he doesn't direct, and each plugin talks on its own as if he weren't loaded. |
| Quiet | One voice at a time and the greeting; only the odd exchange. |
| Normal (default) | An exchange every so often, sometimes breaks up an argument, rarely a proverb. |
| Chatty | Exchanges often, arguments broken up more often, proverbs after shorter quiet spells. |

Set it with the **Wilson:** button in the window, or `/wilson off`, `/wilson quiet`, `/wilson normal` or `/wilson chatty`. It's kept in `Wilson_Settings.txt` in the game's local storage.

Each plugin's own chat setting still counts. A plugin set to Off sends Wilson nothing and is never in an exchange; a plugin on Quiet only reports its important moments and says them in its own words.

## The window

- **Characters:** each character, its plugin, its chat setting and whether it can be in exchanges right now.
- **What Wilson decided:** the latest events and what came of them (who spoke, which exchange played, or why it passed).
- **Try an exchange** (or `/wilson try`): plays a random exchange now between the loaded characters, with made-up names and numbers, ignoring the timers.
- **Help:** everything above, in game.

## Lines

Wilson's own lines and the exchanges are in [`Source/Wilson.txt`](Source/Wilson.txt), built into the DLL:

```
[greeting]                          Wilson's own lines: greeting, idle, squabble
- Well now, ...
[exchange trigger=barmaid.starved]  plays when BaR Maid's "starved" event happens
BaR Maid: Taps are bone dry on {item}!
Wilson: Well now, ...
```

A trigger is `<character>.<event>`, with the character ids `barmaid`, `fatalbert`, `stockpile`, `scripttoplugin` and `orescout` (see `Source/Wilson/Cast.cs`), or `any.greeting` for world load. Placeholders are filled from the event's values, so an exchange can only use the ones its event passes.

## How it works

Wilson is the director in the plugins' personality protocol: see "Banter (the director)" in [`Shared/README.md`](../../Shared/README.md). He announces himself on load, answers each plugin's "hello", and sends "bye" on unload or when set to Off, so the plugins go back to talking themselves. Plugins built before the protocol carried each plugin's chat setting still work with him; they show up in his window once they've said something.
