using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModManager
{
    /// <summary>
    /// In-game settings editor and updater for every BepInEx mod that is loaded.
    ///
    /// It deliberately patches nothing. Settings come from BepInEx's own plugin and config
    /// registries, and the panel is IMGUI, so there is no Harmony patch here to break when the
    /// game updates and no game type this assembly needs to resolve.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.modmanager";
        public const string Name = "Mod Manager";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance { get; private set; }

        private bool open;

        // Whatever the game had before the window was opened, so closing puts it back rather than
        // leaving the cursor unlocked in a game that wanted it captured.
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;
        private EventSystem suppressedEventSystem;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModConfig.Bind(Config);

            Log.LogInfo(Name + " " + Version + " loaded. Press "
                + ModConfig.ToggleKey.Value + " to open.");
        }

        private IEnumerator Start()
        {
            // Chainloader is still working through the plugin list during Awake, so waiting a
            // frame is what makes the discovered plugin set complete.
            yield return null;

            ManagerWindow.Refresh();

            if (!ModConfig.CheckForUpdatesOnStartup.Value)
                yield break;

            // A few seconds in, so a slow or unreachable network never delays the main menu.
            yield return new WaitForSeconds(4f);
            yield return UpdateService.Check();

            if (UpdateService.AnyUpdatesAvailable)
                Log.LogMessage(UpdateService.Message + " Press " + ModConfig.ToggleKey.Value + " to review.");
        }

        private void Update()
        {
            try
            {
                if (ModConfig.ToggleKey.Value.IsDown())
                    Toggle();

                if (open && Input.GetKeyDown(KeyCode.Escape))
                    CloseWindow();

                ConfigWriter.Tick();

                if (open)
                    EnforceCursor();
            }
            catch (Exception e)
            {
                // Unity swallows whatever a MonoBehaviour throws, so without this the manager
                // would simply stop responding with nothing in either log to say why.
                Log.LogError("Mod manager update failed: " + e);
            }
        }

        private void OnGUI()
        {
            if (!open)
                return;

            try
            {
                ManagerWindow.Draw();
            }
            catch (Exception e)
            {
                Log.LogError("Mod manager draw failed, closing the window: " + e);
                CloseWindow();
            }
        }

        private void Toggle()
        {
            if (open) CloseWindow();
            else OpenWindow();
        }

        public void OpenWindow()
        {
            if (open)
                return;

            open = true;

            ManagerWindow.Refresh();

            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;

            if (ModConfig.BlockGameUiWhileOpen.Value)
                SuppressEventSystem();
        }

        public void CloseWindow()
        {
            if (!open)
                return;

            open = false;

            // A value edited seconds before quitting must not be lost to the debounce.
            ConfigWriter.FlushAll();

            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLock;

            RestoreEventSystem();
        }

        private void EnforceCursor()
        {
            if (!ModConfig.UnlockCursorWhileOpen.Value)
                return;

            // Reapplied every frame rather than once on open: the game sets these itself whenever
            // it changes screens, and would otherwise take the cursor back mid-edit.
            if (!Cursor.visible)
                Cursor.visible = true;

            if (Cursor.lockState != CursorLockMode.None)
                Cursor.lockState = CursorLockMode.None;
        }

        private void SuppressEventSystem()
        {
            var current = EventSystem.current;
            if (current == null || !current.enabled)
                return;

            current.enabled = false;
            suppressedEventSystem = current;
        }

        private void RestoreEventSystem()
        {
            if (suppressedEventSystem == null)
                return;

            // Null-checked through Unity's overloaded operator: the object may have been destroyed
            // by a scene change while the window was open.
            if (suppressedEventSystem)
                suppressedEventSystem.enabled = true;

            suppressedEventSystem = null;
        }

        internal void BeginUpdateCheck()
        {
            StartCoroutine(UpdateService.Check());
        }

        internal void BeginDownload(UpdateStatus status)
        {
            StartCoroutine(UpdateService.Download(status));
        }

        private void OnApplicationQuit()
        {
            ConfigWriter.FlushAll();
        }

        private void OnDestroy()
        {
            RestoreEventSystem();
        }
    }
}
