# Gentle Foes Mod

Swaps what animal enemies look like and what they are called, for players who would rather not
fight bears, boars and wolves.

Nothing else about them changes. Same stats, same skills, same loot, same difficulty — a reskinned
Forest Bear is still a Forest Bear to the game. This is a mod about what is on the screen, not
about the fight.

## How it works

`CharacterInfo` does not hold a model. It holds a list of Resources paths and loads them on demand:

```csharp
[ResourceString] public List<string> PossibleCharacterPrefabsResourceStrings;
```

So giving a bear a goblin's body is copying the goblin's strings over the bear's. No new art, no
asset bundles — every model used is one the game already ships.

Names are left alone in the data and rewritten on the way to the screen. See
[src/NamePatch.cs](src/NamePatch.cs) for why renaming the asset would be a mistake: summons are
matched by `CharacterInfo.name`, so renaming the Wolf asset would quietly break every skill that
summons a wolf.

### This is a local, visual change

The host picks a prefab *index*, and every machine resolves that index against its own copy of the
enemy data. So the mod changes what the machine it is installed on sees, and nothing else. Install
it on one person's machine and they see knights where everyone else sees bears, with the game still
perfectly in step — same enemies, same rolls, same outcome.

## Choosing swaps

On first run the mod writes every enemy it can find to:

```
BepInEx/config/GentleFoesMod/enemies-dump.json
```

That lists each enemy's name, faction, rank, level range and current models. Pick from it, then
edit:

```
BepInEx/config/GentleFoesMod/swaps.json
```

```json
{
  "schema": 1,
  "swaps": [
    { "enemy": "Forest Bear", "lookLike": "Goblin Brute", "name": "Brute" },
    { "enemy": "Wild Boar",   "lookLike": "Goblin",       "name": "Goblin Scrapper" }
  ],
  "groupSwaps": [
    { "group": "ForestCreatures", "lookLikeGroup": "GoblinClan" }
  ]
}
```

- **`enemy`** — the name exactly as the dump gives it.
- **`lookLike`** — another enemy whose model to borrow. Omit to keep the model and only rename.
- **`name`** — what to call it on screen. Omit to keep the name and only change the model.
- **`groupSwaps`** — a whole faction at once. Donors are dealt round-robin from the donor faction,
  so a forest of bears does not become a forest of one identical goblin. Per-enemy entries are
  applied afterwards, so one bear can still be given its own treatment.

Enemy factions include `ForestCreatures`, `DesertCreatures`, `MountainCreatures`, `CaveDenizens`
and `ColdOnes` for the animals, against `GoblinClan`, `Barbarians`, `Pirates`, `Undead` and others
for the rest. The dump lists them all.

Your `swaps.json` lives under `BepInEx/config`, which no release writes to, so updating the mod
cannot overwrite your choices. The shipped `swaps.default.json` beside the plugin is replaced by
every update and is never written to.

## A suggested starting point

The mod ships doing nothing, so the first run only writes the dump. This is a reasonable first
edit, and it deliberately leaves the scorpions and the undead alone:

```json
{
  "schema": 1,
  "swaps": [],
  "groupSwaps": [
    { "group": "ForestCreatures",   "lookLikeGroup": "GoblinClan" },
    { "group": "MountainCreatures", "lookLikeGroup": "Barbarians" },
    { "group": "ColdOnes",          "lookLikeGroup": "Barbarians" }
  ]
}
```

Group rules are the safer way to start, because the faction names above are read straight out of
the game's own `EnemyGroup` list, whereas an individual enemy name has to match exactly. Check the
dump for which animals actually sit in which faction before trusting it.

Names are worth adding per enemy afterwards, since a goblin still called "Forest Bear" rather gives
the game away:

```json
{ "enemy": "Forest Bear", "name": "Goblin Bruiser" }
```

Enemy names confirmed from the game's own text: Forest Bear, Forest Bear Cub, Polar Bear, Polar
Bear Cub, Wild Boar, Wild Boar Alpha, Timber Wolf, Timber Wolf Alpha, Dire Wolf, Tundra Wolf,
Frost Wolf, Arctic Fox, Forest Fox, Mountain Lion, Jungle Tiger, Panther, Moose, Stag, Mammoth,
Raven, Crow, Cave Bat, Hound. Some of those may be summons or encounter names rather than enemies
you meet — the dump is what settles it.

## What to expect

The game has one animator type — `Humanoid` — for every creature in it, so a borrowed model should
animate normally. What cannot be promised from reading the code is that every pairing *looks*
right: scale, where weapons attach, where hit effects land and how the body falls on death are all
properties of the model, and some pairings will sit better than others. Try a few and keep the ones
that work.

Two constraints the game's own code imposes, both handled here but worth knowing if you edit the
mod:

- The replacement list must be the same length as the original. The host sends an index into it,
  and one of the two places the game reads it does not bounds-check.
- The loaded-prefab cache has to be cleared by hand after a swap, because it only notices a change
  when the *number* of models changes.

## Settings

| Setting | Default | What |
|---|---|---|
| `DumpEnemies` | `true` | Write `enemies-dump.json` on startup. Turn it off once you have the list. |

## Building

`dotnet build -c Release`. The csproj deploys into the game's `BepInEx/plugins/GentleFoesMod/`.
See [modding-notes/](../modding-notes/) for the toolchain.
