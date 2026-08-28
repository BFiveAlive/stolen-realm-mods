# Status Effects Mod

Edit any of Stolen Realm's ~200 status effects from a config file — how long they last, how
hard they hit, how they stack — with no rebuild and, in most cases, no restart.

Want Inner Warmth to last longer after a freeze? One line:

```ini
[Status Overrides]
Inner Warmth = duration=6
```

Save the file. The change is live on the next status applied.

---

## Installing

Build with `dotnet build -c Release`; the csproj deploys straight into the game's
`BepInEx/plugins/StatusEffectsMod/`. Launch the game once, then edit
`BepInEx/config/bfivealive.stolenrealm.statuseffectsmod.cfg`.

The `[Status Overrides]` section does not exist until that first launch. Statuses live in the
game's asset bundles rather than in its code, so the mod cannot know what to list until the game
has loaded them — it discovers the table at runtime and writes every status into the config for
you, each with its vanilla values in the comment above it:

```ini
## [GL-InnerWarmth] Vanilla: duration 2, stackType ReplaceAny, affects ImmunityChilledEffect.
# Setting type: String
# Default value:
Inner Warmth =
```

Leave a line empty and that status is untouched.

---

## The override language

One line per status. Several settings on a line, separated by **semicolons**:

```ini
Poisoned  = duration=*2; potency=*1.5
Chilled   = duration=-1; maxStacks=8
Stealth   = duration=4; cannotBeDispelled=true
Bleeding  = attr:BleedDamage=*2
```

Every value is resolved against the value the game **shipped** with, never against the current
one. `*2` therefore always means "twice vanilla", however many times you save the file.

### Value forms

| Form | Meaning | Example |
|---|---|---|
| `4` | set to 4 | `duration=4` |
| `*2` or `x2` | twice vanilla | `duration=*2` |
| `+2` | vanilla plus 2 | `duration=+2` |
| `-1` | vanilla minus 1 | `duration=-1` |
| `=-1` | set to negative 1 | `groundMovement=-1` |
| `expr:<script>` | replace with a raw game expression | `duration=expr:Source.Level / 2` |
| `true` / `false` | booleans, also `yes`/`no`, `on`/`off`, `1`/`0`, `toggle` | `infinite=true` |

### Keys

| Key | What it changes |
|---|---|
| `duration` | Rounds the status lasts. See *Durations* below. |
| `infinite` | Never expires. Overrides `duration` entirely. |
| `potency` | Multiplier on **every** attribute the status changes. Multiplier only. |
| `attr:<Name>` | One specific attribute, e.g. `attr:Armor=*2`. Names come from the dump. |
| `maxStacks` | Stack ceiling. |
| `stackBonus` | Per-stack bonus multiplier. **Inert — see below.** |
| `stackType` | `ReplaceSource`, `ReplaceAny`, `ReplaceOthers`, `Add`, `AddAndRefresh`, `IgnoreAndRefresh`, `Ignore` |
| `stackIgnoreSource` | Whether stacks from different casters merge. |
| `tickType` | `TargetTurnStart`, `TargetTurnEnd`, `SourceTurnStart`, `SourceTurnEnd` |
| `expireType` | `TurnStart` or `TurnEnd` |
| `activateImmediately` | Fire on application rather than on the first tick. |
| `decrementOnTurnEnd` | Lose a stack each turn end. **Inert — see below.** |
| `cannotBeDispelled` | Immune to dispels. |
| `endOnCrit` / `endOnAction` | Special end conditions. |
| `isAura`, `auraRadius`, `auraAllies`, `auraEnemies` | Aura behaviour. |
| `maintainMana` | Mana upkeep ratio for maintained statuses. |
| `groundMovement` | Movement cost modifier the status imposes. **Inert — see below.** |
| `damageMod` | Flat damage modifier (also switches on the flag that makes it count). |

An unknown key or an unparseable value is reported in `BepInEx/LogOutput.log` and skipped; the
rest of the line still applies.

### Three keys that currently do nothing

`stackBonus`, `decrementOnTurnEnd` and `groundMovement` are declared and serialised on
`ActionStatusInfo`, and the game's data assigns them — but **nothing in `Assembly-CSharp` ever
reads them back**. They look like leftovers, or hooks for a system that was not finished.

They are accepted and written anyway, in case a future patch starts reading them, but the mod
logs a warning whenever you use one so the setting never looks effective when it is not.

---

## Global multipliers

Broad strokes, for when you do not want to touch 200 lines:

```ini
[Global Multipliers]
AllDurationMultiplier = 1
BeneficialDurationMultiplier = 1.5
HarmfulDurationMultiplier = 0.75
BeneficialPotencyMultiplier = 1
HarmfulPotencyMultiplier = 1
```

These **compose** with the per-status lines rather than competing with them. A status with
`duration=*2` in a game with `AllDurationMultiplier = 1.5` ends up at 3× vanilla.

"Beneficial" and "Harmful" are the game's own tags on each status. Some statuses carry neither
and are untouched by those two multipliers.

Note that debuffs cut both ways: enemies apply Poisoned and Chilled to you as readily as you
apply them to enemies, so `HarmfulDurationMultiplier` is not a straight difficulty slider.

---

## Durations

Durations are in **rounds** — one round is both teams taking a turn — and are whole numbers.
Anything you write is rounded, and values below zero are clamped to zero.

Two things are worth knowing:

- **A status carries the duration it was applied with.** The game snapshots the expiry turn when
  the status lands, so editing the config mid-battle affects the *next* application, not the
  poison already ticking on an enemy.
- **`duration` is written back as a plain integer.** Several places in the game read that field
  with `int.Parse` rather than through its expression evaluator, so a fractional or symbolic
  value would throw there. `expr:` bypasses this deliberately — see the caveat below.

### Finding the numbers

Set `DumpStatusData = true`, launch once, and the mod writes `status-dump.json` next to the
plugin: every status with its duration, stacking, and the full list of attributes it changes,
with the exact attribute names that `attr:` expects. Then switch it back off.

The dump always reports **vanilla** values, not your edited ones, so it stays a reference for
what the game actually ships.

---

## Hot reload

`HotReloadConfig` is on by default. Saving the config re-applies it within a fraction of a
second — no restart. Every status is restored to vanilla first and then re-modified, so edits
never compound and deleting a line genuinely reverts it.

`Enabled = false` restores everything to vanilla without uninstalling, which is the fastest way
to check whether this mod is responsible for something.

Turn on `LogChanges` to see every field the mod touched, one line each, in
`BepInEx/LogOutput.log`. Without it a typo is only visible as a warning you have to go looking
for.

---

## How it works

Statuses are `ActionStatusInfo` ScriptableObjects that the game keeps in
`Burst2Flame.Game.Instance.ActionStatuses` and re-reads every time it needs them —
`ActionStatus.Duration`, for instance, evaluates its expression fresh on every access. So this
mod contains **no Harmony patches at all**. It edits those objects in memory and the game picks
the changes up on its own.

That is both simpler and less brittle than intercepting the code paths that read them, and it is
non-destructive: Unity only writes ScriptableObject changes to disk in the editor, so nothing in
the game's files is modified. Deleting the plugin reverts everything.

---

## Caveats

**Multiplayer: install it on the host.** The split is clean, and it favours host-only setups.

Statuses replicate by *index* into `Game.Instance.ActionStatuses`, and this mod never adds or
removes entries, so the two sides always agree on which status is which. From there:

| Config key | Where it is evaluated | Host-only effect |
|---|---|---|
| `duration` | Host computes `ExpireTurn` at apply time; synced as an observable | **Correct for everyone** |
| `maxStacks`, `stackType`, `stackIgnoreSource` | Host, in `ApplyStatusStackingRules` | **Correct for everyone** |
| `cannotBeDispelled`, `endOnCrit`, `endOnAction` | Host, inside server-gated combat resolution | **Correct for everyone** |
| `potency`, `attr:` | **Each client**, via `Character.CalculateAttribute` | Host uses modded values; unmodded clients *display* vanilla ones |
| `infinite` | **Each client**, in `GetTurnsRemaining` | Host shows no expiry; unmodded clients show a countdown |
| `maintainMana` | Host deducts; clients compute their own tooltip | Deduction correct, displayed cost differs |

So a modded host with unmodded clients **does not break or desync anything**. Health, mana and
action points live in a synced `ObservableDictionary`, and damage resolution is server-gated, so
outcomes are consistent. What drifts is derived stats — armour, resists, crit — which each client
recomputes locally from the status definitions it has. An unmodded client sees vanilla numbers on
the character sheet while the host resolves combat with the modded ones.

The reverse case is the useless one: a **modded client with an unmodded host** gets none of its
duration or stacking edits, because it never runs the code that applies them.

Everyone installing it, with the same config, is still the tidiest arrangement. Unlike
`../SkillWeightMod` — whose rolls are purely client-side and safe to mix — this mod's effects are
not per-player.

**A vanilla display bug affects long durations.** When the game builds a skill tooltip with no
character selected, it reads only the *first digit* of the duration string, so a 12-round buff
displays as "1". This is a bug in the base game, harmless there because such durations are rare —
but this mod makes them easy to create. Mechanics are unaffected; only that one tooltip path
misreports. It is left alone deliberately: fixing it means a transpiler on a 400-line method
with intricate control flow, which is far more risk than a cosmetic slip is worth.

**`expr:` is an escape hatch with sharp edges.** It replaces a duration with raw game script,
evaluated against the status's source and target. It is not validated, the global duration
multipliers cannot compose with it and are skipped, and the `int.Parse` paths described above
will throw on a non-numeric result. Useful for things like `expr:Source.Level / 2`; not the
first tool to reach for.

**Blanket `potency` skips attributes that SET an absolute value.** Most of those are on/off flags
— `ImmunityChilledEffect = 1` and the like — where scaling produces nonsense. Target one
deliberately with `attr:` if you really mean it. `attr:` also wins over `potency` for the
attribute it names, since it is the more specific instruction.

---

## Worked example: the freeze cycle

Enough of the cold chain is in code rather than data to be worth writing down. Chilled
accumulating to **5 stacks**, on a character that is not already Frozen and has no Inner Warmth,
triggers Frozen. Inner Warmth in turn suppresses Chilled entirely — `CanBeEffectedByChilled` is
false while it is up — so its duration is exactly the length of the grace period after a freeze.

Stretching that window, and softening the chill that leads into it:

```ini
[Status Overrides]
Inner Warmth = duration=6
Chilled      = duration=-1; maxStacks=4
```

---

## Files

| Path | What |
|---|---|
| `src/Plugin.cs` | Lifecycle: waits for the status table, binds config, hot reload |
| `src/ModConfig.cs` | General settings and global multipliers |
| `src/StatusCatalog.cs` | Discovers statuses, assigns config keys, describes vanilla values |
| `src/StatusOverrides.cs` | Binds the one-entry-per-status `[Status Overrides]` section |
| `src/OverrideValue.cs` | Parses the value language (`*2`, `+1`, `expr:`, booleans, enums) |
| `src/StatusApplier.cs` | Applies parsed overrides to the ScriptableObjects |
| `src/StatusSnapshot.cs` | Captures and restores shipped values, so reloads are idempotent |
| `src/StatusDumper.cs` | `status-dump.json` export |
