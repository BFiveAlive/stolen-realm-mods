# ModUpdatePatcher

A BepInEx **preloader patcher** that installs mod updates the in-game manager downloaded.

It ships with [ModManager](../ModManager/) and does nothing on its own.

## Why this exists

A running game cannot replace a loaded plugin:

- Mono has no way to unload an assembly from the default AppDomain
- the DLL is locked by the loader
- its Harmony patches are already applied

So [UpdateService](../ModManager/src/UpdateService.cs) only ever *stages* a download, into
`BepInEx/mod-updates/staged/<Folder>.zip`. This patcher unpacks it during the next launch.

Preloader patchers run before the chainloader touches `BepInEx/plugins` at all — the one moment
when those files are unlocked and nothing has been loaded from them. That is the whole reason
this is a patcher rather than a second plugin.

## What it doesn't do

It patches no assembly. `TargetDLLs` is empty and `Patch` is never called; the work happens in
`Initialize`. Both members still have to exist with exactly those signatures or BepInEx will not
recognise the type as a patcher at all — see `AssemblyPatcher.ToPatcherPlugin` in
`BepInEx.Preloader`.

## Safety

- Archive entries are resolved and checked against the game folder before **anything** is
  written, so a `../../..` entry cannot escape it. The zip came off the network; its paths are
  untrusted input.
- The whole archive is validated before the first file is written, so a malformed one cannot
  leave a half-installed mod.
- A failure is logged and swallowed. The worst acceptable outcome is that the update doesn't
  apply and the old version loads normally — never a game that won't boot.
- A staged zip that fails is left in place rather than deleted: it may succeed once whatever
  locked it goes away, and deleting it would lose the download silently.

## Updating the patcher itself

BepInEx loads patcher DLLs with `Assembly.LoadFile`, which locks them, so this assembly cannot
replace itself the way it replaces plugins. Changes to it need the installer, or a manual copy
into `BepInEx/patchers/` while the game is closed. It is deliberately small and boring so that
rarely comes up.
