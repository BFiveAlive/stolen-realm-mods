# Installer

A standalone console app that sets up BepInEx and the mods.

```
StolenRealmModInstaller.exe   find the game and install every mod, after confirming
  --game <path>       use this Stolen Realm folder instead of searching
  --manifest <url>    read the mod list from somewhere else
  --choose            pick which mods to install instead of installing all
  -y, --yes           no prompts; installs everything
  --uninstall         remove BepInEx and all mods, leaving the game vanilla
```

## What gets installed

Everything in the mod list. The mods are independent of one another and each one sits inert until
its settings are changed, so installing all of them costs a few hundred kilobytes and decides
nothing — which beats asking someone to choose between six mods they have not seen yet. The run
lists what it is about to install and waits for confirmation, and `--choose` brings back the
tick-list for anyone who wants a smaller install.

## Finding the game

Steam spreads games across library folders on any drive, and the list of them lives in
`steamapps/libraryfolders.vdf` next to the main install. The installer reads that, which covers
essentially every normal setup. Failing that it checks a handful of well-known paths on each
fixed drive, and failing *that* it asks.

## Installing

Every release zip — the mods' and BepInEx's — is laid out relative to the game folder, so every
path inside starts with `BepInEx/`. Installing anything is therefore the same operation: extract
over the game root. Archive entries are validated against that root first, so nothing can be
written outside it.

Downloads are checked against the SHA-256 in `mods.json` where one is given. A mod that fails
doesn't abandon the others — each is an independent set of files.

What was installed is recorded in `BepInEx/mod-updates/installed.json`, so a later run can show
which mods are already present and at what version.

## Uninstalling

`--uninstall` deletes `BepInEx/` and the doorstop files (`winhttp.dll`, `doorstop_config.ini`).
Because BepInEx never modifies the game's own files, that genuinely leaves a vanilla install
behind. It offers to keep `BepInEx/config` so your settings survive a reinstall.

## Building

```sh
dotnet publish Installer/Installer.csproj -c Release
```

Published self-contained so it runs on a machine with no .NET installed, which is the normal case
for someone who only wants to play the game. That costs about 60 MB in the single file — a
framework-dependent build is far smaller but would send players off to install a runtime first.

Trimming is off on purpose: `System.Text.Json`'s reflection-based deserialiser is exactly what
the trimmer cannot see, and a manifest that only fails to parse in the published build is a
miserable way to discover that.

The exe is unsigned, so SmartScreen warns on first run. Code signing needs a certificate.
