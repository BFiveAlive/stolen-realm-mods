# Stolen Realm mods

Mods for [Stolen Realm](https://store.steampowered.com/app/1289810/Stolen_Realm/), plus an
installer and an in-game mod manager.

Built against Unity 2022.3 / Mono, loaded with [BepInEx 5](https://github.com/BepInEx/BepInEx)
and Harmony. Nothing here modifies the game's own files — BepInEx hooks in through a proxy DLL
next to the executable, so uninstalling is deleting the files that were added.

## Install

Download `StolenRealmModInstaller.exe` from the
[latest release](https://github.com/BFiveAlive/stolen-realm-mods/releases/latest) and run it.

It finds your Steam install, asks which mods you want, and sets up BepInEx if it isn't there
already. Then launch the game from Steam as usual and press **F1**.

```
StolenRealmModInstaller.exe               pick from a list
StolenRealmModInstaller.exe --all         install everything
StolenRealmModInstaller.exe --uninstall   remove BepInEx and all mods
```

Windows only. The installer is unsigned, so SmartScreen will ask before running it.

## The mods

| Mod | What it does |
|---|---|
| [ModManager](ModManager/) | In-game settings editor and updater for every mod loaded, whether or not it was written for this |
| [SkillWeightMod](SkillWeightMod/) | Weights roguelike skill offers toward what your character already has, with an optional reroll |
| [CumulativeStatsMod](CumulativeStatsMod/) | Toggles the post-battle stats window between the last battle and the whole run |
| [StatusEffectsMod](StatusEffectsMod/) | Config-driven edits to status effect duration, potency and stacking |

## Mod manager

Press **F1** in game. Everything is drawn with IMGUI, so it works on the main menu, in a battle,
and anywhere else.

- **Settings** lists every loaded BepInEx plugin's configuration, read straight from BepInEx's own
  registry. A mod does not have to know this manager exists to appear here.
- **Updates** compares what's installed against `mods.json` in this repo, and downloads what you
  ask it to.

A `↻` next to a setting means it only takes effect after restarting. Everything else applies
immediately, provided the mod reads its config at the point of use.

### Why updates need a restart

Mono cannot unload an assembly from the default AppDomain, the plugin DLL is locked by the
loader, and its Harmony patches are already applied. So a download is written to
`BepInEx/mod-updates/staged/` and installed by [ModUpdatePatcher](ModUpdatePatcher/) during the
next launch, in the preloader — before the chainloader has touched `BepInEx/plugins` at all,
which is the one moment those files are free.

## Building

Needs the .NET 8 SDK and a Stolen Realm install; the mods reference `Assembly-CSharp.dll` and the
Sirenix assemblies from the game's `Managed/` folder.

```sh
dotnet build ModManager/ModManager.csproj -c Release
```

Each csproj copies its output into the game's `BepInEx/plugins/<ModName>/` after a successful
build. Override the game path with `-p:GameDir="D:\Games\Stolen Realm"`.

To cut a release — builds everything, packages the zips, regenerates `mods.json` with fresh
checksums, and publishes the installer:

```powershell
pwsh tools/release.ps1                 # build and package into dist/
pwsh tools/release.ps1 -Publish        # ...and create the GitHub release
```

There is no CI build. The mods compile against assemblies from the game install that cannot be
redistributed, so releases are cut locally.

## mods.json

The single list of what is published. Both the installer and the in-game updater read this same
file, so a release is described in one place.

Release zips are laid out relative to the game folder — every path inside starts with `BepInEx/`
— which makes installing one operation for both: extract over the game root.

## Licence

MIT, see [LICENSE](LICENSE). BepInEx is LGPL-2.1 and is redistributed by the installer unmodified.
No game files are included in this repository or in any release.
