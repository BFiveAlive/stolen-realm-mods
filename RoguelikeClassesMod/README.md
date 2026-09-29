# Stolen Realm — Roguelike Classes Mod

Adds roguelike starting classes. A class is a few lines of JSON: a name, the skills it begins
with, the gear it wears, its stat spread, and which difficulty unlocks it.

Nothing in the game's logic is replaced. The mod builds preset objects of the game's own type and
adds them to the list the game already reads, so the picker lists them, the tiles render them, and
character creation builds from them exactly as it does for the thirty-four shipped ones.

## Writing a class

`classes.json` sits next to the DLL. One entry per class:

```json
{
  "id": "cryomancer",
  "name": "Cryomancer",
  "description": "Cold from the first turn to the last.",
  "tier": 2,
  "unlock": { "difficulty": 3 },
  "stats": { "might": 6, "dexterity": 8, "vitality": 10, "intelligence": 18, "reflex": 8 },
  "skills": ["Enchant Cold", "Aura Of Frost", "Master of Ice"],
  "equipment": { "armor": "Simple Robe", "mainHand": "Gemstone Wand", "offHand": "Gemstone Wand" }
}
```

| Field | What it does |
|---|---|
| `id` | Stable identifier. The preset's Guid is derived from it and stored on every character made from the class, so **renaming an id orphans existing characters**. |
| `name`, `description` | Shown on the tile. |
| `tier` | 1, 2 or 3 — the rating on the tile. Cosmetic; it does not gate anything. |
| `unlock.difficulty` | The authored difficulty that unlocks it, 1&ndash;6 &mdash; the same ladder the shipped classes use. |
| `unlock.endlessLevel` | Or an endless difficulty instead. Both absent means available from the start. |
| `unlock.description` | Locked-tile text. Generated as "Complete Difficulty III" or "Reach Endless VI" when absent. |
| `stats` | The five base attributes. Every shipped class totals **50**; staying at 50 keeps a new class comparable. |
| `skills` | Skill names as they read in game. Granted at level 1 regardless of tier or prerequisite. |
| `equipment` | `head`, `armor`, `mainHand`, `offHand`, `ring`, `amulet`, by item name. |
| `extraItems` | Unequipped starting items: `{"item": "...", "stacks": 3}`. |
| `gearNote` | Free text for gear not yet pinned to real items. Logged, never used. |
| `appearanceFrom` | A shipped preset whose look to borrow. Defaults to the first roguelike preset. |
| `gender` | `"Male"` or `"Female"`, overriding the donor's. |

A class whose skill or item names do not resolve is **skipped with a message naming what was not
found**, rather than added half-built — a picker entry that produces a character missing the skill
it was chosen for is worse than no entry.

A few skill names genuinely exist twice (Beast Master I is in both Ranger and Nature). Write those
as `"Nature:Beast Master I"`.

## Editing classes in game

Open the Mod Manager (F1), pick **Roguelike Classes Mod** in the Settings rail, and the editor is
there: the classes down the left, three sub-tabs for the one selected, and a strip above to switch
between the editor and the mod's ordinary settings.

| Tab | What it holds |
|---|---|
| Character | Name, description, unlock gate, tile tier, the five attributes with a running total, and the six gear slots — each checked against the item table as you type, so a name that does not resolve says so immediately rather than at save time. |
| Skills | The starting skills with their tree and tier, and a search over the game's own skill table to add more. |
| Appearance | Gender, and "start from an existing look" — any of the game's classes, or **any character you have already made**. |

**Save changes** writes `classes.json` and pushes the values onto the presets the game is already
holding, so the next character created from a class uses them. No restart. Characters already made
are left alone — an edit should not quietly rewrite a roster.

### Copying a look from one of your characters

This is the way to get an appearance you actually want: make a character with the game's own
creator, then copy it onto a class.

The two halves of the game store a look differently. A preset keeps indices — hair type 12, skin
colour 3. A character keeps `ChosenColors` (hex strings) and `ActiveVisuals` (the names of the part
objects switched on). `PresetManager.SyncModelToPreset` converts the first into the second, and
copying from a character is that conversion run backwards: colours matched against the palettes,
visual names matched against the part lists.

Names are matched against every part list rather than read positionally, because a saved
character's `ActiveVisuals` also carries parts that are not customisable and the order is not
something to rely on.

The part lists live on `PresetManager`, which only exists on the menu screens, so they are read the
first time it is seen and kept for the session. Until then the panel says so and copying from a
character is unavailable — copying from a preset still works, since that reads indices directly.

There is no per-feature editing yet, and no 3D preview. The game's own customiser cannot be reused
here: it drives a spawned model by switching GameObjects, keeps its own private indices, and never
touches a preset.

## Unlocking

Both gates write the same condition, because the game only has one stat to gate on.

`CharacterPresetFile.IsUnlocked` tests conditions against `GlobalSaveData.GlobalStats`, and the
only key the game writes is `HighestRoguelikeDifficultyUnlocked`, set after a winning run to
`CurrentDifficultyIndex + 1`. That write is **not** clamped to the authored difficulty list — the
1.2.8 compatibility clamping happens on a separate legacy save key — so endless indices land in it
intact.

The game authors six roguelike difficulties, and `CreateEndlessRoguelikeDifficulty` labels the
generated entry at index `i` as Endless `i - (authored - 1)`. So Endless 1 is index 6, and
`endlessLevel: 8` becomes a requirement of 13. The mod resolves the authored count at runtime
rather than hardcoding it, so an update that adds a difficulty does not silently shift every
unlock by one.

An authored difficulty needs no arithmetic at all: the index is stored directly, so
`"difficulty": 3` becomes a requirement of 3 — exactly what the shipped classes gated on
Difficulty III already ask for. The two ladders are therefore the same ladder, and a modded class
appears among the classes of its own strength rather than in a separate tier bolted onto the end.

### Where the shipped classes sit

The classes in `classes.json` are placed against the ceilings the shipped ladder observes, which
are worth knowing before adding another:

| Gate | Highest tier it grants at level 1 | Total tiers granted |
|---|---|---|
| Free | 2 | up to 4 |
| Difficulty I | 3 | up to 6 |
| Difficulty II | 4 | up to 9 |
| Difficulty III | 5 | up to 12 |
| Difficulty IV | 5 | up to 13 |
| Difficulty V | 5 | up to 20 |

Both ceilings bind: a class goes at whichever gate is later. That is why Fist of the North Star
(`[5, 1]`, a total of only 6) sits at Difficulty III — One Punch Monk at level 1 is a Difficulty
III privilege regardless of how little else the class carries.

Nothing shipped here reaches Difficulty V, whose band starts at 12 and runs to The Enlightened's
20. Filling it means classes with four to six skills, not three.

## What a class cannot do yet

Seventeen of the thirty-four shipped roguelike presets carry a `SpecialPresetInfo` passive: a
named trait with a tradeoff, like the Mage's *Studious* (+15% elemental damage, −15% physical
damage taken reduction). This mod does not offer those, and the reason is ordering — the game
sweeps every preset **once at start-up** to register the skill triggers those passives declare,
and a preset injected after that sweep would have its triggers ignored. Supporting them means
injecting before that sweep, or registering the triggers separately.

Skills, gear and stats have no such requirement, which is why those work.

## How the injection works

`Game.CharacterPresetFiles` is a lazily populated cache over
`Resources.LoadAll<CharacterPresetFile>("Character Preset Files")`, and every consumer reads that
property rather than keeping its own copy. A Harmony postfix on the getter builds the classes on
the first call and writes the enlarged array back into the cache, so:

- the injection happens exactly when the game first wants the list, not on a timer;
- nothing else needs patching;
- it is idempotent — once the array holds our presets the postfix does nothing.

It also drops `_CharacterPresetFile_Dict`, the guid lookup's cached dictionary, so a saved
character can resolve back to the modded class it was created from.

Two smaller details worth knowing:

- **Guids are derived from the id** by hashing, not generated randomly, because a character's save
  file stores the Guid and it has to match on the next launch and on another machine.
- **Appearance is copied wholesale from a shipped preset.** The appearance fields are
  index-into-palette pairs whose colours are normally resolved by an editor-only method, so taking
  both halves from a preset that already works avoids reimplementing that and avoids an index
  landing out of range.

## Settings

`BepInEx/config/bfivealive.stolenrealm.roguelikeclassesmod.cfg`.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Add the classes. Off leaves the game's own list untouched; characters already made from a modded class keep their skills and gear, since those live in the character's save. |
| `DumpGameData` | `false` | Write `game-data.json` under the save folder, once. |

### The dump

`DumpGameData` writes everything needed to author a class and nothing that can be decompiled,
because it all lives in asset bundles:

- **the shipped presets** — skills, tiers, gear, stats, unlock conditions, passives: the yardstick
  for how strong a starting class is meant to be;
- **this mod's own classes as they resolved**, so equipment written by name can be checked rather
  than assumed;
- **the item table** — every name equipment can be written against;
- **the difficulty list** and the index each endless level maps to;
- **the roguelike roll tuning** — tier chance curves, per-tier maximums, `NumSkillOptions`.

It lands under `%USERPROFILE%\AppData\LocalLow\Burst2Flame Entertainment\Stolen Realm\RoguelikeClassesMod\`
rather than beside the plugin, because the game is usually installed under Program Files where a
non-elevated process cannot write.

The dump is driven by a Harmony postfix on `GUIManager.Update`, not by the plugin's own `Update`.
That is not a stylistic choice: BepInEx's manager object stops being updated once the game loads
its first scene, so a plugin waiting in its own `Update` for game data waits forever. Harmony
patches are unaffected, because they run inside the game's own call stack.

## Multiplayer

`CharacterPresetFileGuid` is synced between clients. A peer without the mod resolves it to null;
every consumer in the game null-checks it, so it should degrade rather than crash — but that is
read off the decompile, not tested in a live session.

## Building

Requires .NET SDK 8.

```sh
dotnet build -c Release
```

The build copies the DLL **and `classes.json`** into `BepInEx/plugins/RoguelikeClassesMod/`. If
Steam is not on `C:`, override the path:

```sh
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Stolen Realm"
```
