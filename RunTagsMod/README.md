# Run Tags Mod

Replaces the `Roguelike` label on a character card with the difficulty, who hosted, and who played.

Every roguelike character says the same word, so that space tells you nothing you cannot already
see. What actually differs between them — whose game it was, how hard, and who else was in it — is
the thing you need when deciding which run to pick up next week.

```
VI: Host|Firs|Seco|Thir
```

## Why it has to be recorded

The game stores nothing about this. A character file knows its own level, skills and gear, and
nothing about the session it was played in. The lobby knows its owner and members — but the
character selection screen runs with no lobby open, so by the time a card is drawn there is no live
answer to read.

So the mod samples it while you play, every few seconds, and writes it to
`BepInEx/config/RunTagsMod/run-tags.json`, keyed by character guid because names repeat. Sampling
rather than hooking an event means someone joining halfway through is still picked up.

**Characters you have already played show the usual label until their next run.** The history did
not exist before the mod did, and nothing can reconstruct it.

## Fitting the label

The card has room for roughly twenty-odd characters before the text starts crowding the name above
it. The difficulty is written first, and the names share whatever is left.

They are not trimmed equally. An equal share wastes room whenever one name is short — three names
in fifteen characters would give `Al`, `Bo` and `Chr` five each, when the first two need only six
between them. The shortest name is served first and anything it does not need passes to the next,
so:

| Who | Label |
|---|---|
| VI, Hostname + FirstPlayer, SecondPlayer, ThirdPlayer | `VI: Host\|Firs\|Seco\|Thir` |
| VI, Hostname + FirstPlayer, SecondPlayer | `VI: Hostn\|FirstP\|Second` |
| VI, Ann + Bartholomew | `VI: Ann\|Bartholomew` |
| III, Dave alone | `III: Dave` |
| VI, Hostname + four others | `VI: Hostname +4` |

Below three characters a name is unreadable, so rather than shave everyone to initials the party
becomes a count. The host is shown in italics; the rich-text tags do not render, so they are not
counted against the budget.

## Marking the saved run's difficulty

A **new** roguelike run does not ask what difficulty you want. `MainMenu.InitPlayer` does this:

```csharp
if (Root.PlayingRoguelike && !loadingSavedGame)
    Root.CurrentDifficultyIndex = GlobalSaveData.Instance.HighestRoguelikeDifficultyIndexUnlocked;
```

It defaults to the host's highest unlock — and unlocks are local to each player. So when a
different person hosts, the difficulty can change without anyone choosing it, and the first sign is
the fight being harder or easier than last week.

The mod marks the tile your saved run is actually at, on the difficulty picker. It does not argue
with the default; it just says which tile the run belongs to. If the selected tile and the marked
tile are not the same one, the mismatch is visible before starting rather than after.

## Settings

| Setting | Default | What |
|---|---|---|
| `Enabled` | `true` | Replace the label at all |
| `LabelBudget` | `23` | Characters the label may use |
| `ItalicHost` | `true` | Italicise the host's name |
| `MarkSavedRun` | `true` | Mark the saved run's tile on the difficulty picker |
| `SavedRunLabel` | `Your run` | What that marker says |

## Building

`dotnet build -c Release`. The csproj deploys into the game's `BepInEx/plugins/RunTagsMod/`.
See [modding-notes/](../modding-notes/) for the toolchain.
