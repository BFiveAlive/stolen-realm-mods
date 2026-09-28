using HarmonyLib;

namespace CharacterViewerMod
{
    /// <summary>
    /// Lets a roguelike character's removed skill trees be changed after level 1.
    ///
    /// The game locks them with one check, <c>RoguelikeSkillTreeRemoval.ExclusionsLocked</c>: true for
    /// any character above level 1. Every path goes through it - setting or clearing a tree, the removal
    /// window's locked state, the "View Removed Trees" tooltip on the selection tile - so answering
    /// "not locked" here unlocks all of them together, and nothing else needs touching.
    ///
    /// Once unlocked, a character follows the same rules a level-1 character always has: no more
    /// removals than the removal points owned, and only trees that are available. Opening the removal
    /// window applies those rules to the stored list, so a character that somehow holds more removals
    /// than the points now allow is trimmed to fit.
    /// </summary>
    [HarmonyPatch(typeof(RoguelikeSkillTreeRemoval), nameof(RoguelikeSkillTreeRemoval.ExclusionsLocked))]
    internal static class TreeRemovalUnlock
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (Plugin.UnlockTreeRemoval == null || !Plugin.UnlockTreeRemoval.Value)
                return true;

            __result = false;
            return false;
        }
    }
}
