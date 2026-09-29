using System;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// The mod's per-frame clock.
    ///
    /// A BepInEx plugin's own <c>Update</c> is never called in this game - measured across several
    /// mods here: a probe in <c>Awake</c> fires every launch, probes in <c>Update</c> and
    /// <c>OnGUI</c> never do, while the same component on a GameObject the *game* owns receives
    /// both normally. <see cref="Application.onBeforeRender"/> is a static event, so it keeps
    /// firing regardless of what happens to the object the plugin lives on.
    ///
    /// Two things need it: reading the appearance part lists once <c>PresetManager</c> exists, and
    /// driving the preview capture, which has to span frames because a model has to be told what
    /// to look like before a camera can photograph it.
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
                AppearanceTables.TryCapture();
                Preview.Tick();
            }
            catch (Exception e)
            {
                // Abandon the frame rather than throw out of a delegate on this event, which would
                // otherwise do the same thing again on every frame that follows.
                Plugin.Log.LogError("Roguelike classes update failed: " + e);
            }
        }
    }
}
