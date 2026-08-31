# AutoEquipMod

Offers to equip an item as soon as you pick it up, from any source.

## How it hooks in

One Harmony patch, on `Character.AddToItemList`. That is the single method every acquired item
passes through — loot, a shop, crafting, a quest reward — which is what makes "from any source" one
hook rather than four.

It is also how a save is loaded. Restoring a character hands it every item it already owns, one
call at a time, and nothing about the call distinguishes that from picking something up.

## The load guard

A fixed timer after the scene loads is not enough: a slow load runs past it, and the items still
arriving would be taken for fresh pickups. With silent equipping of empty slots on, that would
quietly rearrange a party's gear every time you loaded a save.

So the guard holds for as long as `LoadingScreen` says the game is loading — `IsLoading`,
`MainFadeActive`, or `InitialLoadComplete` not yet set — and only then starts its `SettleSeconds`
window. Verified in game: quiet from t=14s through the whole load, listening from t=36s, six
seconds after loading actually finished.

## Deciding what to offer

Which slot an item would take is asked of `Character.GetItemEquipInfo`, not reimplemented — the
rules for two-handed weapons, dual wielding and the two ring slots are the game's to know. A swap
that displaces two items is judged against the better of the two, so a two-hander cannot look like
an upgrade merely by being compared with the weaker hand.

The score is a weighted sum of things the game states plainly:

| Part | Default weight |
|---|---|
| Item level | 1 |
| Rarity, via the game's own `GetStatRarityMod` | 8 |
| Armour | 0.5 |
| Magic armour | 0.5 |
| Each prefix, suffix or endgame modifier | 6 |

It decides only **whether to interrupt you**. It never decides what to wear: the prompt shows the
numbers for both items and the choice is yours. No fixed formula can know what an item is worth to
a particular build, so this one does not pretend to — every weight is exposed, and
`OnlyUpgrades`/`MinimumImprovement` control how much better something has to look before it is
worth a prompt.

## What it will not do

- Touch anyone else's characters. In multiplayer only the local player's own party is considered.
- Equip something the character cannot use — `MeetsEquipLevelRequirements` is checked first.
- Equip anything at all while `AskBeforeReplacing` is on, except into an empty slot when
  `FillEmptySlotsSilently` is also on, where there is nothing to lose.

Equipping goes through `Character.EquipItem`, which unequips whatever conflicts, sets the slot
index, refreshes the character's effects and saves. Reimplementing any of that would be a way to
produce a character the game does not agree with.

## Not yet verified

The pickup → prompt → equip path itself needs an actual play session. Everything up to it is
confirmed: the plugin loads, the patch applies, the loading guard behaves as described, and no
exceptions are logged.
