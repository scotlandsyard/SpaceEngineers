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
| OreScout | OreScout | Marker library |
| Sacrificial Stockpile Manager | Stockpile Manager | Main window |
| Script to Plugin | Script to Plugin | Main window |

To add a plugin:
1. Link the file into its project (above).
2. In its session's `BeforeStart`, call `PluginSwitcher.Register("Name", openWindowAction)`. In `UnloadData`, call `PluginSwitcher.Unregister()`.
3. In its window's `RecreateControls`, replace `AddCaption("…");` with `PluginSwitcher.AddSwitcher(this, AddCaption("…"));`.

## Releasing a plugin that uses these files

A plugin that links a file from here needs this folder when it's built from the repo. If a plugin is published through the Pulsar plugin hub, its hub entry must include `Shared` in its source directories as well as the plugin's own folder, or the hub build won't find the file.
