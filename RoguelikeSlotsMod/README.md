# Roguelike Slots Mod

Gives each roguelike party its own save file, so several runs can be in progress at once.

The game keeps exactly one. `QuestSaveRoguelike.json`, with the alternate-folder path hardcoded
off, so starting a second run overwrites the first. Fine for one person playing one run; no use at
all for several groups across several weeks.

## How it works

One property needs redirecting:

```csharp
public static string FilenameRoguelikeQuest =>
    Path.Combine(FileSystem.persistentDataPath, "QuestSaveRoguelike.json");
```

Backup and temp are both built from it (`+ ".backup"`, `+ ".temp"`), and
`CurrentRoguelikeQuestFilename` returns it unchanged while the alternate folder is off — so moving
this one moves all four consistently and leaves the game's own naming intact. Slots land beside the
original as `QuestSaveRoguelike.<slot>.json`.

A slot is named from a hash of its party's character guids, so the same group resuming next week
lands on the same file without anyone remembering a number.

## The safety property

**While a run is underway, the slot is taken from the party, not from any menu.**

Choosing a slot is only a suggestion about what to *load*. The moment real characters are in a run,
the file being written is derived from those characters. So picking the wrong slot beforehand can
show you the wrong run — but it cannot save one run over another's file.

Nothing here ever deletes a save. A slot that stops being referenced is left on disk, and the
original `QuestSaveRoguelike.json` is **copied**, never moved, when a run already in progress is
carried over on first use.

## Choosing which run to continue

The game's own menu assumes one save exists, and asks for it before you have picked a party — so
there is nowhere in its flow to say which run you meant. That step lives in the Mod Manager instead,
under **Roguelike Saves**: each run is listed by its party, difficulty, level and when it was last
saved, and "Resume this" points the menu's Continue at it.

You cannot change slots while a run is in progress; leave to the main menu first.

## Turning it off

Setting `Enabled` to `false` puts the game back on its single save. Nothing is deleted — the slot
files stay on disk and come back when you switch it on again.

## Settings

| Setting | Default | What |
|---|---|---|
| `Enabled` | `true` | Give each party its own save file |

## Building

`dotnet build -c Release`. The csproj deploys into the game's `BepInEx/plugins/RoguelikeSlotsMod/`.
See [modding-notes/](../modding-notes/) for the toolchain.
