# Mod Manager

An in-game settings editor and updater for every BepInEx mod that is loaded.

Press **F1** (configurable) to open it.

## Settings

Settings are discovered rather than declared. BepInEx keeps a registry of loaded plugins
(`Chainloader.PluginInfos`), and each plugin's `ConfigFile` is enumerable — carrying the section,
key, type, default, description and acceptable value range of every setting it bound. The manager
reads that and builds the UI from it, so **a mod does not need to know this manager exists** to be
fully editable here. The three other mods in this repo required no changes at all.

Editing writes the value into the entry immediately, so a mod that reads `Entry.Value` at the
point of use reacts on the next frame. The *file* write is debounced by half a second: without
that, dragging a slider would be a disk write every frame, and a reload every frame for any mod
running a config file watcher.

### Restart-required settings

Marked with `↻`. A setting consumed once — at patch time, when building UI, when creating a
watcher — cannot take effect until the next launch.

BepInEx has no field for this, so it comes from two places:

1. A config tag object with a bool `RequiresRestart` or `IsRestartRequired` member, read by
   reflection. No shared assembly needed, and compatible with ConfigurationManager's attributes:

   ```csharp
   cfg.Bind("General", "HotReload", true,
       new ConfigDescription("...", null, new { RequiresRestart = true }));
   ```

2. Failing that, the description is scanned for the word "restart" — which is what the mods here
   already say in prose.

### Types it can edit

`bool`, enums, every numeric type, `string` and `KeyboardShortcut` get a purpose-built widget;
a numeric setting with an `AcceptableValueRange` gets a slider.

Anything else falls back to a text box over the exact TOML the `.cfg` file stores, via
`GetSerializedValue`/`SetSerializedValue`. A setting of a type this code has never heard of is
still editable — just less prettily. `[Flags]` enums and enums with more than a dozen members
(`KeyCode`) deliberately use that path too.

## Updates

Compares the version BepInEx recorded from each plugin's `[BepInPlugin]` attribute against
`mods.json`, so there is no local manifest to drift out of step with what is actually loaded.

Downloads are verified against the SHA-256 in the manifest and written to
`BepInEx/mod-updates/staged/`. They are installed by [ModUpdatePatcher](../ModUpdatePatcher/) on
the next launch — see the root README for why a restart is unavoidable. A staged update can be
cancelled from the Updates tab before then.

## Tabs contributed by other mods

Some mods need an editor rather than a list of key-value pairs. A mod can add its own top-level tab
here by declaring, anywhere in its assembly:

```csharp
public static class ModManagerTab
{
    public static string Title => "Roguelike Classes";
    public static void Draw(Rect body) { ... }
    public static void Refresh() { }          // optional
}
```

It is found by reflection on the name, so **neither assembly references the other** - the same
reason the structured-setting descriptor is read that way. `Draw` gets a window-relative rect and
may use `GUI` or `GUILayout` inside it.

The panel appears **under that mod's own entry in Settings**, not as another tab across the top: a
strip above it switches between the panel and the mod's ordinary settings rows. A mod's editor
belongs with its settings rather than competing with Settings, Updates and Profiles for the top
bar.

`GUI.skin` is set to the manager's own skin around the call and restored afterwards, so a plain
`GUILayout.Label` or `GUILayout.Button` in a contributed panel comes out in the manager's font and
colours. A panel cannot ask for a style by name - it has nothing to reference - so handing it the
skin is the only way for it to match without one.

A panel that throws is disabled for the rest of the session and reported once, with the inner
exception rather than the reflection wrapper. The manager has to stay usable for every other mod,
and an exception per frame inside `OnGUI` is unreadable.

`RoguelikeClassesMod` uses this for its class editor.

## Notes

This mod references no game assembly. It uses BepInEx's registries and IMGUI, so there is no
Harmony patch to break and no game type to resolve — which is also why it looks like a debug
panel rather than like Stolen Realm.

If a mod builds its own `ConfigFile` instead of using the inherited `Config` (see gotcha 11 in the
modding notes), it will not appear automatically. It can register itself from its `Awake`:

```csharp
ConfigDiscovery.RegisterExtraConfigFile(Plugin.Guid, myConfigFile);
```

## Settings of its own

| Setting | Default | |
|---|---|---|
| `ToggleKey` | `F1` | Opens the manager. Modifiers supported. |
| `UiScale` | `1.0` | Raise on a 4K display. |
| `UnlockCursorWhileOpen` | `true` | Reapplied each frame, since the game takes the cursor back on screen changes. |
| `BlockGameUiWhileOpen` | `true` | Disables the UI event system so a click here doesn't also press what's behind it. Cannot block keyboard shortcuts the game reads directly from `Input`. |
| `CheckForUpdatesOnStartup` | `true` | Checks four seconds in, so a slow network never delays the main menu. |
| `ManifestUrl` | this repo | Point at a fork to update from elsewhere. |
| `SaveDebounceSeconds` | `0.5` | Delay before an edit reaches disk. |
| `VerboseLogging` | `false` | Logs every discovered plugin and setting, for when a mod's settings don't appear. |
