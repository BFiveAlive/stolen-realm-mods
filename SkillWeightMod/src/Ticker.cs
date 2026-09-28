using System;
using UnityEngine;

namespace SkillWeightMod
{
    /// <summary>
    /// Gives the mod a per-frame clock, because a BepInEx plugin's own <c>Update</c> is not one in
    /// this game.
    ///
    /// Measured, not inferred: a probe in <c>Awake</c> fires every launch, and probes in
    /// <c>Update</c> and <c>OnGUI</c> never fire at all - not on a later frame, not on the first.
    /// The same component attached to a GameObject the *game* owns receives both normally, and an
    /// object a mod creates for itself and marks <c>DontDestroyOnLoad</c> receives neither, so it
    /// is the object BepInEx puts plugins on that stops being driven rather than the callbacks
    /// being gone.
    ///
    /// Harmony patches are unaffected, because they run inside the game's own call stack. That is
    /// why the weighting kept working while everything hung off Update silently did not: the
    /// config watcher this mod's README describes, and the skill dump, had both been dead.
    ///
    /// <see cref="Application.onBeforeRender"/> is a static event, so it keeps firing no matter
    /// what happens to the object the plugin lives on.
    /// </summary>
    internal static class Ticker
    {
        private static bool installed;

        internal static void Install()
        {
            if (installed)
                return;

            installed = true;
            Application.onBeforeRender += Tick;
        }

        private static void Tick()
        {
            try
            {
                Plugin.Frame();
            }
            catch (Exception e)
            {
                // Abandon the frame rather than throw out of a delegate on this event, which
                // would otherwise do the same thing again on every frame that follows.
                Plugin.Log.LogError("Skill weight update failed: " + e);
            }
        }
    }
}
