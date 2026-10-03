# Wilson

A client-side Pulsar plugin: **the neighbour over the fence**. Wilson directs the chat personalities of the rest of the family (BaR Maid, Fat Albert, Sacrificial Stockpile Manager, Script to Plugin and OreScout), and has a few words of his own.

Without Wilson, each plugin says its own lines in chat. With Wilson loaded, the plugins tell him when something happens and he decides who speaks: usually the plugin itself, and now and then a short written exchange between several characters, one line every few seconds. Every character's name has its own colour in chat. Chat lines only show on your own screen, so it's fine on any server.

## Opening it

- Type `/wilson` in chat, or
- Pick **Wilson** in the plugin list at the top left of any of the family's windows (`/tim` opens the one used last).

## What he does

| Moment | With Wilson |
|---|---|
| World load | About ten seconds in, Wilson calls the roll. Each loaded character answers once, saying how to call it up (`/barmaid`, `/fat`, `/ssm`, `/stp`, `/scout`), and Wilson wraps up with `/wilson`. Nobody greets separately any more. |
| A plugin's event (BaR Maid runs short of an item, Fat Albert's ship is too heavy, OreScout finds rare ore...) | If it's that character's turn, Wilson either asks it to say one of its own lines, or plays an exchange written for that event when every character in it is loaded. |
| An exchange ends in an argument | Sometimes Wilson steps in with a line to calm things down. |
| A long quiet spell | Now and then, Wilson shares one of his odd proverbs. |
| You type `/tim` | Sometimes Wilson's guest Tim drops by: always the first time in a session, then now and then. |

### One turn per cycle

A turn is one character's line or one short exchange. After a turn, nobody says an ordinary line until the cycle is over:

| Wilson's setting | Cycle | Exchanges |
|---|---|---|
| Off | Wilson doesn't direct; each plugin talks on its own (with the same cycles) | - |
| Quiet | 15 minutes, important moments only | None (the roll call still happens) |
| Normal (default) | 5 minutes | About a third of the turns |
| Chatty | 2 minutes | About half of the turns |

Important moments (a starved assembler, a ship too heavy to lift, a crashed script, full containers) can break in, but never within 30 seconds of the last line. A character isn't asked about the same event again for 10 minutes, the same exchange trigger doesn't come round again for 30, and each character also keeps to its own chat setting's cycle.

Set Wilson's setting with the **Wilson:** button in the window, or `/wilson off`, `/wilson quiet`, `/wilson normal` or `/wilson chatty`.

Each plugin's own chat setting still counts. A plugin set to Off sends Wilson nothing and is never in an exchange or the roll call; a plugin on Quiet only reports its important moments, says them in its own words, and answers the roll call.

## Names

Every character can go by a name you choose. In the window, pick a character in the table, type a name under it and press **Rename**; **Own name** puts its own back. Each plugin keeps its name in its own settings, so the name stays when Wilson isn't loaded; Wilson keeps his own and Tim's. Lines that mention another character by name keep the original name.

## The window

- **Characters:** Wilson, Tim and the five plugin characters: the name each goes by, its plugin, its chat setting and whether it can be in exchanges right now.
- **Rename / Own name:** see Names above.
- **What Wilson decided:** the latest events and what came of them (who spoke, which exchange played, or why it passed).
- **Try an exchange** (or `/wilson try`): plays a random exchange now between the loaded characters, with made-up names and numbers, ignoring the timers.
- **Roll call** (or `/wilson rollcall`): calls the roll again now.
- **Help:** everything above, in game.

Wilson's setting and the names for Wilson and Tim are kept in `Wilson_Settings.txt` in the game's local storage.

## Lines

Wilson's and Tim's own lines and the exchanges are in [`Source/Wilson.txt`](Source/Wilson.txt), built into the DLL:

```
[rollcall_open]                     Wilson's own lines: rollcall_open, rollcall_close ({command} = /wilson), idle, squabble
- Roll call, neighbours. ...
[tim]                               Tim's lines, when he drops by on /tim
- ...
[exchange trigger=barmaid.starved]  plays when BaR Maid's "starved" event happens
BaR Maid: Taps are bone dry on {item}!
Wilson: Well now, ...
```

A trigger is `<character>.<event>`, with the character ids `barmaid`, `fatalbert`, `stockpile`, `scripttoplugin` and `orescout` (see `Source/Wilson/Cast.cs`), or `any.tim` for a Tim and Wilson exchange on `/tim`. Placeholders are filled from the event's values, so an exchange can only use the ones its event passes. Each plugin answers the roll call from its own `[rollcall]` lines, with `{command}` filled in.

## How it works

Wilson is the director in the plugins' personality protocol: see "Personality.cs" in [`Shared/README.md`](../../Shared/README.md) for the rhythm, the colours and the messages. He announces himself on load, answers each plugin's "hello", and sends "bye" on unload or when set to Off, so the plugins go back to talking themselves. Plugins from older builds still work with him; they show up in his window once they've said something.
