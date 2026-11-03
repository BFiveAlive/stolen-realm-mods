using System;
using Burst2Flame;
using HarmonyLib;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Closes a battle the moment the game says it is over.
    ///
    /// Battle boundaries are otherwise inferred from the stats dropping, which only happens when
    /// the *next* battle clears them - so a finished fight did not reach the archive until another
    /// one started, or until leaving the run emptied the list.
    ///
    /// This is the game's own announcement that a battle finished, and it is declared
    /// <c>NetworkMethodType.All</c>, so it arrives on every client rather than only on the host.
    /// That matters: the stats themselves are synced, so a client's archive should fill in at the
    /// same moment the host's does.
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.SendGameCompleteFromServer))]
    internal static class BattleEndPatch
    {
        [HarmonyPostfix]
        private static void BattleOver()
        {
            try
            {
                StatTracker.CloseBattle();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not close the battle for the archive: " + e);
            }
        }
    }
}
