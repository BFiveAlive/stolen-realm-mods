using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CharacterViewerMod
{
    /// <summary>
    /// Opens the game's character menu for a character on the selection screen, and puts things
    /// back when it closes.
    ///
    /// The character menu shows whatever <c>GameLogic.CurrentlySelectedCharacter</c> is, and only
    /// for characters the player owns - which every character on the selection screen is. So
    /// viewing is: select the character, open the menu, and on close restore the selection that was
    /// there before. Selecting also marks the character party leader and clears that flag on the
    /// party, so those flags are remembered and restored too.
    /// </summary>
    internal static class Viewer
    {
        private static Character previousSelection;
        private static readonly Dictionary<Character, bool> PreviousLeaderFlags = new Dictionary<Character, bool>();
        private static bool blockedNoticeShown;

        /// <summary>The character being viewed, or null.</summary>
        internal static Character Viewing { get; private set; }

        internal static bool Active => Viewing != null;

        internal static void Open(Character character)
        {
            try
            {
                if (!Plugin.Enabled.Value || character == null)
                    return;

                GameLogic logic = GameLogic.instance;
                CharacterMenusManager menus = CharacterMenusManager.Instance;

                if (logic == null || menus == null)
                {
                    Plugin.Log.LogWarning("The character menu is not available yet; cannot view " + character.CharacterName + ".");
                    return;
                }

                if (logic.AllMyCharacters == null || !logic.AllMyCharacters.Contains(character) || !character.Owned)
                {
                    Plugin.Log.LogWarning(character.CharacterName + " is not one of this player's characters; not viewing.");
                    return;
                }

                // Only remember the state from before the first view: switching from one viewed
                // character to another must still restore the original selection on close.
                if (!Active)
                {
                    previousSelection = logic.CurrentlySelectedCharacter;
                    PreviousLeaderFlags.Clear();
                    foreach (Character mine in logic.AllMyCharacters)
                    {
                        if (mine != null)
                            PreviousLeaderFlags[mine] = mine.IsPartyLeader;
                    }
                }

                Viewing = character;
                blockedNoticeShown = false;

                logic.CurrentlySelectedCharacter = character;
                if (logic.CurrentlySelectedCharacter != character)
                {
                    Plugin.Log.LogWarning("The game did not select " + character.CharacterName + "; not viewing.");
                    End();
                    return;
                }

                menus.OpenCharacterMenu();
                Plugin.Log.LogInfo("Viewing " + character.CharacterName + " from the character selection screen (view only).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not open the character viewer: " + e);
                End();
            }
        }

        /// <summary>Restores the selection and party-leader flags from before viewing.</summary>
        internal static void End()
        {
            if (!Active)
                return;

            Character viewed = Viewing;

            // Cleared first, so nothing below is blocked by the view-only guard and a failure
            // cannot leave the guard switched on.
            Viewing = null;

            try
            {
                GameLogic logic = GameLogic.instance;
                if (logic != null)
                {
                    logic.CurrentlySelectedCharacter = previousSelection;

                    foreach (KeyValuePair<Character, bool> pair in PreviousLeaderFlags)
                    {
                        if (pair.Key != null && pair.Key.IsPartyLeader != pair.Value)
                            pair.Key.IsPartyLeader = pair.Value;
                    }
                }

                Plugin.Log.LogInfo("Stopped viewing " + (viewed != null ? viewed.CharacterName : "a character") + "; selection restored.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not fully restore the selection after viewing: " + e);
            }
            finally
            {
                previousSelection = null;
                PreviousLeaderFlags.Clear();
            }
        }

        internal static void NotifyBlocked(string what)
        {
            Plugin.Log.LogInfo("Blocked while viewing from the selection screen: " + what + ".");

            if (blockedNoticeShown)
                return;

            blockedNoticeShown = true;

            try
            {
                if (ConfirmWindow.Instance != null)
                {
                    ConfirmWindow.Instance.ShowPopupMessage("View only",
                        "Characters opened from the selection screen can be looked at but not changed. " +
                        "Add the character to your party and start the game to equip items or spend points.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not show the view-only notice: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Every route that closes the character menu - its close button, Escape, the tab toggles -
    /// ends in this private method, so it is the one place to notice the viewer closing.
    /// </summary>
    [HarmonyPatch(typeof(CharacterMenusManager), "CloseMenuManager")]
    internal static class CloseMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (Viewer.Active)
                Viewer.End();
        }
    }

    /// <summary>
    /// The selection screen's own per-frame update: a safety net for a menu closed some other way,
    /// and the clock for the self-test. Patched on the game's component because objects a mod
    /// creates are not reliably ticked in this game.
    /// </summary>
    [HarmonyPatch(typeof(CharacterChoiceManager), "Update")]
    internal static class SelectionUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(CharacterChoiceManager __instance)
        {
            try
            {
                if (Viewer.Active)
                {
                    CharacterMenusManager menus = CharacterMenusManager.Instance;
                    if (menus == null || !menus.gameObject.activeSelf)
                        Viewer.End();
                }

                SelfTest.Tick(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Character viewer update failed: " + e);
            }
        }
    }
}
