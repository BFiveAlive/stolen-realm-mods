using System.Collections.Generic;
using Burst2Flame;
using HarmonyLib;

namespace SkillWeightMod
{
    /// <summary>Hooks that drive <see cref="RerollFeature"/>.</summary>
    internal static class RerollPatches
    {
        /// <summary>
        /// Records the arguments of every roll so the reroll button can repeat it exactly.
        /// ForcedTier in particular is not recoverable from anywhere else once the window is up.
        /// </summary>
        [HarmonyPatch(typeof(RoguelikeManager), "PopulateSkillChoices")]
        [HarmonyPostfix]
        private static void CaptureRollArgs(Character character, int level, List<SkillInfo> alreadyObtained, int forcedTier)
        {
            RerollFeature.CaptureRollArgs(character, level, alreadyObtained, forcedTier);
        }

        /// <summary>
        /// Creates the button lazily and refreshes its state. Runs on the manager's own Update
        /// so it stays correct as health, mana and the level-up stage change.
        /// </summary>
        [HarmonyPatch(typeof(RoguelikeManager), "Update")]
        [HarmonyPostfix]
        private static void RefreshRerollButton(RoguelikeManager __instance)
        {
            if (!ModConfig.RerollEnabled.Value)
                return;

            RerollFeature.EnsureButton(__instance);
            RerollFeature.UpdateState(__instance);
        }
    }
}
