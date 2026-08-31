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
        public const string Version = "0.3.1";

        internal static ManualLogSource Log;
        internal static Plugin Instance { get; private set; }

        private bool open;

        // Whatever the game had before the window was opened, so closing puts it back rather than
        // leaving the cursor unlocked in a game that wanted it captured.
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;
        private BaseInputModule suppressedInputModule;

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

        /// <summary>
        /// Stops clicks on this panel also pressing whatever game UI is behind it.
        ///
        /// It is the input module that gets disabled, not the EventSystem. Disabling the
        /// EventSystem component makes the static EventSystem.current null, and Stolen Realm's
        /// GUIManager.PointerOverUIObject dereferences that without a null check from
        /// CursorManager.Update - so suppressing the EventSystem threw once per frame, for every
        /// frame the manager was open. Disabling the module leaves EventSystem.current valid
        /// while still stopping every raycast and pointer event, which is all that was wanted.
        /// </summary>
        private void SuppressEventSystem()
        {
            var current = EventSystem.current;
            if (current == null)
                return;

            var module = current.currentInputModule;
            if (module == null || !module.enabled)
                return;

            module.enabled = false;
            suppressedInputModule = module;
        }

        private void RestoreEventSystem()
        {
            if (suppressedInputModule == null)
                return;

            // Null-checked through Unity's overloaded operator: the object may have been destroyed
            // by a scene change while the window was open.
            if (suppressedInputModule)
                suppressedInputModule.enabled = true;

            suppressedInputModule = null;
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
