using System;
using UnityEngine;

namespace StatusEffectsMod
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
    /// This mattered more here than almost anywhere else. Everything this mod does waits for the
    /// game's status table to come out of the asset bundles, and that wait lived in <c>Update</c>.
    /// So the mod loaded, logged "Waiting for the game's status data", and waited forever: the
    /// per-status config entries were never bound and no override was ever applied. The log line
    /// that should have said so was the last one it printed.
    ///
    /// <see cref="Application.onBeforeRender"/> is a static event, so it keeps firing no matter
    /// what happens to the object the plugin lives on, and it needs neither a Harmony patch nor a
    /// reference to any game type - which keeps this mod's "no patches" property true.
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
                // An exception escaping a delegate on this event would unsubscribe nothing but
                // would run again every frame, so it is logged here and the frame is abandoned.
                Plugin.Log.LogError("Status effects update failed: " + e);
            }
        }
    }
}
