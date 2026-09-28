# Character Viewer Mod

Look at a character's **stats, inventory, skills and fortunes** from the character selection
screen, without starting a game. Works for campaign and roguelike characters.

## Using it

1. On the character selection screen, **right-click a character** to open its options (the same
   menu as Rename and Delete).
2. Choose **View Character**.

The game's own character menu opens for that character, with its **Character**, **Skill Tree**
and **Fortunes** tabs. The inventory list on the Character tab shows that character's own
unequipped items. (In play the same list shows every item across your party, but a character on
the selection screen isn't in a party, so it shows just theirs.) Close it the usual way (the close button or Escape) and you are back on the
selection screen with your previous selection as it was.

## View only

Nothing can be changed from here: equipping or unequipping items, spending or reassigning
attribute points, learning or removing skills, and removing fortunes are all blocked while the
viewer is open, with a one-time notice.

That is deliberate. A character that has not joined a party is not loaded into the game session,
so the game would not save anything done to it - a change would look like it worked and then be
gone. To change a character, add it to your party and start the game as normal.

## Settings

`BepInEx/config/bfivealive.stolenrealm.characterviewermod.cfg`, or the in-game Mod Manager:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Adds View Character to the options menu. Off removes it on the next menu open |
| `UnlockTreeRemoval` | `false` | Lets a roguelike character's removed skill trees be changed after level 1 |

## Changing removed skill trees after level 1

Roguelike skill-tree removals are chosen at level 1 and locked from then on. `UnlockTreeRemoval`
reopens them.

The lock is a single check — `RoguelikeSkillTreeRemoval.ExclusionsLocked`, true for any character
above level 1 — and every path goes through it: setting or clearing a tree, the removal window's
own locked state, and the "View Removed Trees" tooltip on the selection tile. Answering "not
locked" there unlocks all of them together, so nothing else needs touching.

Every other rule still applies. A character follows exactly what a level-1 character always
could: no more removals than the removal points owned, and only trees that are available. Note
that opening the removal window re-applies those rules to the stored list — so a character that
somehow holds more removals than its points now allow is **trimmed to fit** when the window
opens, rather than keeping them.

## How it works

The character menu (`CharacterMenusManager`) shows `GameLogic.CurrentlySelectedCharacter`, and
only for characters the player owns - every character on the selection screen qualifies. So
View Character selects the character, opens the game's menu, and on close restores the previous
selection and the party-leader flags that selecting changes. The changes listed above are blocked
with Harmony prefixes that only act while the viewer is open, so the game is unchanged in play.

`-cvtest <folder>` on the game's command line runs a self-check on the selection screen: it opens
each tab, tries a change, closes, and uses the View Character button, logging results with a
`SELFTEST` prefix and saving screenshots to the folder.
