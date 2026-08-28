# Stolen Realm — Cumulative Stats Mod

Adds a toggle to the top right of the post-battle stats window that switches every number in it
between **This Battle** (vanilla) and **Run Total** — the same stats summed over every battle of
the current roguelike run.

Nothing else about the window changes: same sections, same rows, same layout, same formatting.
Only the values move.

## What it actually changes

Vanilla keeps battle stats in `Root.BattleStats`, a list of `CharacterBattleStats`, each holding
a `BattleStat -> float` dictionary. `BattleManager.InitBattleAndStart` wipes that list at the
start of every battle, so the window only ever shows one battle at a time.

This mod:

1. **Samples `Root.BattleStats` twice a second** and keeps a per-character running total
   (`StatTracker`).
2. **Adds a button** cloned from an existing button in the window, pinned to the panel's
   top-right corner (`StatsToggle`).
3. **Prefixes `StatManager.GetStatDisplay`** so that, in run-total mode, it emits the tracker's
   numbers instead of the live ones (`StatManagerPatches`).

The prefix reproduces vanilla's formatting exactly — the sizes, the colours, the section
subtotal, the per-row `Mathf.Ceil` — and returns `true` (run the original) on anything it does
not fully understand, so a mistake there costs the vanilla view rather than a broken window.

### Why sampling instead of hooking the wipe

`StatManager.ClearStats` looks like the obvious hook, but its only caller guards it with
`if (NetworkingManager.Instance.IsServer)`, so in co-op it never runs on a client — while
`Root.BattleStats` itself is synced to everyone. Sampling the same data the vanilla window reads
works identically on host and client.

The battle boundary is inferred rather than announced: within a battle these counters only ever
climb, so the first sample where a character's total has *dropped* (or where their entry has
vanished from the list) is the first sample of a new battle. That is when the finished battle is
folded into the archive.

### Why totals are stored in two halves

Each character's record keeps `Archive` (finished battles) and `Snapshot` (the battle currently
in `Root.BattleStats`) separately, and the displayed total is their sum. That keeps the total
correct *while the post-battle menu is open* — the battle you just fought is in `Snapshot`, not
yet folded — and it means a run resumed in a later session cannot double count: whether or not
the game still has the last battle's stats in memory on load, the same boundary rule produces
the same answer.

## Scope: one run, not all runs

Totals are keyed by `Character.Guid`, which is generated per character and saved in the
character's save file. Roguelike characters are created fresh for every run, so a new run starts
from zero automatically — there is no "reset" to remember.

The same mechanism runs in campaign mode, where characters persist indefinitely; there the
"Run Total" column reads as that character's lifetime total since the mod was installed. If you
only want the roguelike behaviour, ignore the button outside of it.

Records are kept in `BepInEx/config/bfivealive.stolenrealm.cumulativestatsmod.data`, a plain text
file, one line per character. Dead runs are pruned by age (`RetentionDays`) and by count
(`MaxTrackedCharacters`), so it does not grow without bound.

## Installing / enabling

The mod is loaded by [BepInEx 5](https://github.com/BepInEx/BepInEx) via its doorstop hook, so
there is no launcher and no launch option — once the files are in place, **starting the game
from Steam normally is all that is required**.

Layout inside the Stolen Realm install directory:

```
Stolen Realm/
  winhttp.dll                                              <- doorstop hook (BepInEx)
  doorstop_config.ini                                      <- must contain: enabled = true
  BepInEx/
    core/                                                  <- BepInEx itself
    plugins/CumulativeStatsMod/CumulativeStatsMod.dll      <- this mod
    config/bfivealive.stolenrealm.cumulativestatsmod.cfg    <- generated on first run
    config/bfivealive.stolenrealm.cumulativestatsmod.data   <- saved run totals
    LogOutput.log                                          <- check here for the load line
```

Confirm it loaded by looking for `Cumulative Stats Mod 0.1.0 loaded.` in `LogOutput.log`. That
line sits *after* `Harmony.PatchAll`, which throws if a target method cannot be resolved, so its
presence also proves both patches bound.

## Building

```sh
export PATH="$HOME/.dotnet/tools:/c/Program Files/dotnet:$PATH"
dotnet build -c Release
```

The csproj copies the DLL into the game's plugins folder on every successful build. Override the
game location with `-p:GameDir="D:\Games\Stolen Realm"` if Steam lives elsewhere.

## Configuration

`BepInEx/config/bfivealive.stolenrealm.cumulativestatsmod.cfg`, generated on first run. With
`HotReloadConfig` on (the default) edits apply on the next frame — useful for nudging the button
into place without restarting.

> **Careful:** a generated `.cfg` overrides the code default permanently. Changing the literal in
> the source does nothing to a machine that has already run the mod; edit the `.cfg` instead, or
> delete it and let it regenerate.

| Key | Default | What it does |
|---|---|---|
| `General.Enabled` | `true` | Off hides the button and stops all tracking. |
| `General.DefaultToCumulative` | `false` | Which view the window starts on each launch. Your click then sticks for the session. |
| `General.HotReloadConfig` | `true` | Apply edits to this file without restarting. Toggling this one needs a restart. |
| `Button.ButtonMarginX` / `Y` | `24` / `16` | Distance in pixels from the panel's right and top edges. |
| `Button.ButtonScale` | `0.8` | Size multiplier. The button is cloned from one sized for a more prominent role. |
| `Button.ButtonOffsetX` / `Y` | `0` | Extra nudge on top of the margins. `+X` right, `+Y` up. |
| `Button.SubtextScale` | `65` | Size of the small second line, as a percentage of the first. |
| `Display.CompactNumberThreshold` | `1000000` | Abbreviate run totals at or above this (`1.2M`, `345K`). `0` always prints the exact figure. |
| `Data.PersistBetweenSessions` | `true` | Save totals to disk so they survive quitting and reloading a run. |
| `Data.RetentionDays` | `60` | Drop saved totals for characters not seen this long. |
| `Data.MaxTrackedCharacters` | `200` | Hard cap on saved records; least recently seen dropped first. |
| `Debug.LogWindowHierarchy` | `false` | Dump the stats window's UI tree to the log. For diagnosing button placement. |
| `Debug.LogTracking` | `false` | Log each time a finished battle is folded into a run total. |

### If the button lands in the wrong place

The button is anchored to the top-right of `StatManager.Content`, the window's content root.
That is the same relationship that worked for `SkillWeightMod`'s reroll button, but it is a
property of the prefab rather than something the code can verify. If it ends up outside the
panel or under something:

1. Set `Debug.LogWindowHierarchy = true` and open the stats window.
2. Read the dumped tree in `BepInEx/LogOutput.log` to find the object that actually is the
   visible panel.
3. Nudge with `ButtonOffsetX` / `ButtonOffsetY`, which apply live with hot reload on.

## Multiplayer

Reading `Root.BattleStats` is passive and per-client; nothing is written back to the game's
observable state, so this cannot desync a lobby. Each participant sees run totals only for
battles their own client observed, and only players who install the mod get the toggle.

## Compatibility

- Verified against `Assembly-CSharp.dll` dated 2025-06-19 (Unity 2022.3.10, Mono, unobfuscated).
- Patches two methods on `StatManager`: `GetStatDisplay` and `PopulateStats`.
- Does not touch the skill system, so it composes cleanly with `SkillWeightMod`.
