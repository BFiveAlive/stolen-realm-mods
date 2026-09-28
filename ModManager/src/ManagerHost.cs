using System;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Gives the manager a live MonoBehaviour to run on, because the one BepInEx provides is not.
    ///
    /// A BepInEx plugin is a component on an object the chainloader creates, and in this game that
    /// object stops receiving Unity callbacks: neither <c>Update</c> nor <c>OnGUI</c> is ever
    /// called on it - not on a later frame, not on the first. So the manager could not see the
    /// toggle key and could not have drawn its panel even if it had. Measured, not inferred: a
    /// probe in <c>Awake</c> fires every launch and probes in <c>Update</c> and <c>OnGUI</c> never
    /// do, while the same component attached to a GameObject the *game* owns receives both
    /// normally. An object this mod created for itself and marked <c>DontDestroyOnLoad</c> got
    /// nothing either, so it is the object that is the problem rather than the callbacks.
    ///
    /// The way back in is <see cref="Application.onBeforeRender"/>: a static event, so it keeps
    /// firing no matter what happens to the object the plugin lives on. From there this attaches
    /// itself to the current camera, which is a GameObject the game owns and which therefore still
    /// receives callbacks. A camera is destroyed on every scene change, so the attachment is
    /// re-made whenever it goes missing - that is not a leak guard, it is the normal path, and it
    /// is why the manager keeps working across the main menu, town and battles.
    ///
    /// Deliberately still no Harmony patch and no reference to the game's own assemblies: the
    /// manager stays something that works beside any mod set and has nothing to break when the
    /// game updates.
    /// </summary>
    internal class ManagerHost : MonoBehaviour
    {
        private static ManagerHost current;
        private static bool installed;
        private static bool startupRun;

        /// <summary>Called once from Awake. Everything after this is driven by the frame event.</summary>
        internal static void Install()
        {
            if (installed)
                return;

            installed = true;
            Application.onBeforeRender += EnsureAttached;
        }

        /// <summary>
        /// Unity's <c>==</c> is exactly the test wanted here: a destroyed host compares equal to
        /// null, which is the signal that the camera it lived on is gone and a new one is needed.
        /// </summary>
        private static void EnsureAttached()
        {
            if (current != null)
                return;

            Camera camera = Camera.main;
            if (camera == null)
            {
                // Loading screens and the moments between scenes have no camera. Nothing to draw
                // on and nothing to draw, so waiting for the next frame is the whole handling.
                return;
            }

            try
            {
                current = camera.gameObject.AddComponent<ManagerHost>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not attach the mod manager to a camera: " + e);
            }
        }

        private void Start()
        {
            if (startupRun)
                return;

            // The plugin's own Start coroutine cannot run either - a coroutine is continued by the
            // same loop that calls Update - so the discovery and update check are started here, on
            // an object that is actually being driven.
            startupRun = true;
            StartCoroutine(Plugin.Startup());
        }

        private void Update() => Plugin.Frame();

        private void OnGUI() => Plugin.DrawGui();
    }
}
