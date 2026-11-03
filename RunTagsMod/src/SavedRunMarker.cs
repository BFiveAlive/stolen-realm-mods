using System;
using System.Reflection;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RunTagsMod
{
    /// <summary>
    /// Marks the difficulty your saved run is actually at, on the difficulty picker.
    ///
    /// This exists because of one line in <c>MainMenu.InitPlayer</c>:
    ///
    /// <code>
    /// if (Root.PlayingRoguelike &amp;&amp; !loadingSavedGame)
    ///     Root.CurrentDifficultyIndex = GlobalSaveData.Instance.HighestRoguelikeDifficultyIndexUnlocked;
    /// </code>
    ///
    /// A new run silently defaults to whatever the host has unlocked, and unlocks are local to
    /// each player. So when a different person hosts, the difficulty can change without anyone
    /// choosing it - and the first sign is the fight being harder or easier than last week.
    ///
    /// The marker does not argue with that; it just says which tile the saved run belongs to. If
    /// the selected tile and the marked tile are not the same one, the mismatch is visible before
    /// starting rather than afterwards.
    /// </summary>
    [HarmonyPatch(typeof(DifficultyItem), nameof(DifficultyItem.RefreshStateLine))]
    internal static class SavedRunMarker
    {
        /// <summary>
        /// <c>stateText</c> is private and built lazily by the tile's own layout code, so it is
        /// read by reflection rather than rebuilt here - whatever the game styles it as, this
        /// inherits.
        /// </summary>
        private static readonly FieldInfo StateTextField =
            AccessTools.Field(typeof(DifficultyItem), "stateText");

        [HarmonyPostfix]
        private static void Mark(DifficultyItem __instance)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !ModConfig.MarkSavedRun.Value || __instance == null)
                    return;

                if (!__instance.IsRoguelikeDifficulty)
                    return;

                int saved = SavedDifficulty();
                if (saved < 0 || saved != __instance.DifficultyIndex)
                    return;

                var text = StateTextField?.GetValue(__instance) as TextMeshProUGUI;
                if (text == null)
                    return;

                // Written over whatever the tile was about to say. A tile can be both cleared and
                // the one your run is on, and which run you are resuming is the more useful of
                // the two when you are looking at this screen.
                text.text = ModConfig.SavedRunLabel.Value;
                text.color = new Color(0.847f, 0.651f, 0.341f);
                text.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not mark the saved run's difficulty: " + e.Message);
            }
        }

        /// <summary>
        /// The difficulty of the roguelike run waiting to be continued, or -1 when there is none.
        ///
        /// Read from the save rather than from <c>Root</c>: on this screen Root holds whatever has
        /// been selected or defaulted, which is the thing being compared against.
        /// </summary>
        private static int SavedDifficulty()
        {
            try
            {
                QuestSaveFile saved = GlobalSaveData.Instance?.GetQuestSaveFileToLoad(GameMode.Roguelike);
                return saved?.CurrentDifficultyIndex ?? -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
