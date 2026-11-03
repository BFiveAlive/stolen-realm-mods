using System;
using HarmonyLib;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Catches the end of a run, with its outcome.
    ///
    /// The rewards screen is told whether the run was won when it is opened, so that is where the
    /// answer is - reading it back off the screen afterwards would mean matching the title against
    /// localised text, which breaks in any language but English.
    /// </summary>
    [HarmonyPatch(typeof(AdventureRewards), "OpenAdventureRewards")]
    internal static class RunEndPatch
    {
        [HarmonyPostfix]
        private static void Opened(bool victory)
        {
            try
            {
                // Everything up to this instant belongs to the run that just ended, including the
                // final battle, which has not folded into the totals yet.
                StatTracker.Poll();
                RunRecorder.RunEnded(victory);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not archive the finished run: " + e);
            }
        }
    }
}
