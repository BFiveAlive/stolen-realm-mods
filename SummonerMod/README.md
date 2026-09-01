# SummonerMod

Tunes how strong summons are.

**Ships off.** `Enabled` starts `false`. Every multiplier defaults to 1, so switching it on
changes nothing by itself - but two of the settings scale crit for your whole party rather than
for summons alone, which is not a change that should arrive unannounced with an install.

It patches nothing. Everything the game uses to decide a summon's damage, health, dodge and how
many you may have at once is a value in a single global settings object, so this mod reads those,
remembers what they were, and multiplies them. No Harmony patch means nothing here to break when
the game updates — only field names to keep up with.

## What actually decides a summon's strength

Found by decompiling `Character` and `GlobalSettings`. Everything in the first table is a value
this mod exposes; everything below it is either a formula rather than a value, or lives somewhere
this mod deliberately does not reach.

### Exposed

| Setting | Game field | What it does |
|---|---|---|
| `DamagePerMight` | `AbilityPwrPerMightSummon` | Summon damage per point of the summoner's Might |
| `HealthPercentPerIntelligence` | `SummonLifePerInt` | Summon max health, as a percentage, per Intelligence |
| `HealthFlatPerIntelligence` | `SummonLifeFlatPerInt` | Flat summon health per Intelligence |
| `DodgePerReflex` | `DodgeRatingPerReflexSummon` | Summon dodge rating per Reflex |
| `DamageByLevel` | `SummonDamageMultiplerNodes` | The whole summon damage-by-level curve (12 points) |
| `HealthByLevel` | `SummonHealthMultiplierNodes` | The whole summon health-by-level curve (12 points) |
| `BaseHealthByTier` | `SummonBaseHealthBonusPerEnemyType` | Per-level base health by creature tier — Fodder, Soldier, Elite, Champion, Boss |
| `ExtraSummonLimit` | `maxSummonLimitBase` | How many summons may be active at once |
| `CritDamagePerDexterity` | `CritDamagePerDex` | Crit damage per Dexterity — **shared with your whole party** |
| `CritRatingPerDexterity` | `CritRatingPerDex` | Crit rating per Dexterity — **shared with your whole party** |

The last two are in a section called *Shared With The Whole Party* because the game reads the same
field for `CritDamageSummonPerDex` and for every other character. They are genuinely part of a
summoner's damage, but there is no way to raise them for summons alone.

### Not exposed, and why

- **`AttackPowerBasedOnSummonMaster` / `SpellPowerBasedOnSummonMaster`** — a summon inherits its
  master's attack or spell power through `master.AttackPower * 0.1 * 2^((Level-1)/29) *
  creatureWeaponDamage * tierWeaponDamage`. That is code, not data. Changing it means a Harmony
  patch on a property, which is a much larger promise to keep across game updates than reading a
  field, so it is left alone.
- **`SummonLimitBonus`, `SummonLevelBonus`, `MonkStrength`, `MonkSpeed`** — per-character stats
  granted by skills and gear, not global values. `MonkStrength` and `MonkSpeed` each multiply the
  relevant scaling by 1.33. Editing them would mean rewriting the skills that grant them.
- **`SummonDamageMultiplier` / `SummonHealthMultiplier`** — two string fields on the settings
  object that nothing else in the assembly reads. They look like leftovers; exposing a setting
  that does nothing would be worse than not having it.

## Overlap with the other mods here

**There is none, and nothing needs syncing.** The three mods that change game data each own a
different set of it:

- **SummonerMod** — the global settings object.
- **StatusEffectsMod** — status effect definitions.
- **SkillWeightMod** — which skills the roguelike offers you.

No two of them write the same value, so there is no edit to conflict over and no reason for one to
reach into another's config. Verified rather than assumed: no other mod in this repo references
`globalSettings` at all.

One thing is worth knowing about even though it is not a conflict: **SkillWeightMod's
`[Narrow Weights] Summon`** decides how often summon skills are *offered* on level-up. That is
availability rather than strength, so it belongs where it is — but if summons feel rare rather than
weak, that is the setting to reach for.

## Applying

The settings object is a `ScriptableObject`, which lives for the whole process. Editing it in place
would compound on every reload — `x2` would mean 2x, then 4x, then 8x — so the shipped values are
snapshotted once and every apply starts from them. Verified: applying twice in one session still
reports `damage per Might 1 -> 2`, not `2 -> 4`.

Nothing is written to disk. Unity only serialises `ScriptableObject` edits in the editor, so
removing the mod restores the game exactly.

`HotReloadConfig` watches the file, so edits apply without restarting.
