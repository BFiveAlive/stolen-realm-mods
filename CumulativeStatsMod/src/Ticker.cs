using HarmonyLib;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Gives the mod a per-frame clock, because a BepInEx plugin's own <c>Update</c> is not one
    /// in this game.
    ///
    /// Measured rather than assumed: a probe written from <c>Plugin.Awake</c> appears every
    /// launch, and a probe written from <c>Plugin.Update</c> never appears at all - not on a
    /// later frame, not on the first. The same was true of a MonoBehaviour this mod created
    /// itself on a <c>DontDestroyOnLoad</c> object, so it is not simply BepInEx's manager being
    /// destroyed on the first scene load.
    ///
    /// Everything this mod does per frame hangs off that clock: sampling <c>Root.BattleStats</c>
    /// into the run totals, and creating and refreshing the toggle button. Without it the mod
    /// loads, logs, patches - and then silently does nothing, which is exactly what it had been
    /// doing.
    ///
    /// Harmony patches are unaffected, because they run inside the game's own call stack, and
    /// <c>GUIManager</c> exists from the main menu onwards. Postfixing its <c>Update</c> costs
    /// one call per frame and restores the clock.
    /// </summary>
    [HarmonyPatch(typeof(GUIManager), "Update")]
    internal static class Ticker
    {
        [HarmonyPostfix]
        private static void Tick() => Plugin.TickFromGame();
    }
}
