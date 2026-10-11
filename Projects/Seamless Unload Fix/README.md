# Seamless Unload Fix

A client-side Pulsar plugin that lets Space Engineers finish unloading a world when a mod crashes while the world is unloading. It has no window, no settings and no chat command: it works as soon as it's enabled. Nothing is sent to the server, and it changes nothing about gameplay.

It doesn't need the Nexus Seamless Switcher plugin (SeamlessClient), but it helps most where that plugin is used. It is an add-on, not part of Seamless Switcher and not made by its authors.

## The problem it fixes

When you leave a world, the game unloads every mod's session component one after the other. If one of them throws an error, the game stops right there and skips the rest: the other mods, scripts, terminal controls and block definitions stay in memory while the game thinks the world is gone. Every join after that fails until you restart the game. Typical signs in the game log:

- a `NullReferenceException` in `MyBlockVariantGroup.ResolveBlocks` while loading a world;
- a mod's log file "being used by another process" while loading a world.

This happens most after a Seamless server transfer that the target server refused: by then the switch has already removed your player, and mods that use the player while unloading throw an error.

## What it does

- **Safe unload**: unloads the mods' session components itself, in the game's own order, each one on its own. One that throws is written to the game log and skipped, and the rest of the world unloads normally.
- **Leftover definitions**: if a world starts loading while the last world's block definitions are still loaded, it clears them first, the way a normal unload does.

### With Seamless Switcher

If Seamless Switcher is loaded (any build), two more fixes switch on by themselves:

- **Mod list**: when a world unloads, Seamless's list of mods to notify on a transfer is emptied, so mods from earlier worlds aren't called again.
- **Switch state**: when no world is loaded, a "transfer in progress" left over from a transfer that didn't finish is reset, so the next normal join isn't treated as the end of a transfer.

Each of these checks that the part of Seamless it needs exists, and turns itself off with a log line if not.

## What you see

Usually nothing. When it had to step in, a chat line from **Seamless Unload Fix** says so once you're in the next world. Details go to the game log (`%AppData%\SpaceEngineers\SpaceEngineers_<date>.log`) on lines starting with `[SeamlessUnloadFix]`. At start-up the log lists which fixes are on.

## Limits

- It can't undo leftovers from before it was installed: restart the game once.
- A mod that threw an error while unloading may have left something of its own behind. The log names the mod so it can be reported to its author.
- It only covers mod session components and block definitions; other parts of the unload are left as they are.
- If a server refuses your transfer (for example because you were kicked from it in the last 5 minutes), the transfer still fails. You just get back to the menu cleanly and can rejoin without restarting.
