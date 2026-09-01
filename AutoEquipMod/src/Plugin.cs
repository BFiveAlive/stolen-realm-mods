using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoEquipMod
{
    /// <summary>
    /// Offers to equip an item as soon as it is picked up, from whatever source.
    ///
    /// One Harmony patch, on the single method every acquired item passes through. Everything else
    /// is a decision about whether to speak up, and an IMGUI prompt that depends on no game asset.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.autoequipmod";
        public const string Name = "Auto Equip Mod";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            ModConfig.Bind(Config);

            // Even with the mod switched off the patch is applied: it does nothing but ask
            // Watcher, which returns immediately. Patching conditionally would mean the setting
            // could not be turned on without a restart.
            harmony = new Harmony(Guid);

            try
            {
                harmony.PatchAll(typeof(AcquisitionPatch));
            }
            catch (Exception e)
            {
                Log.LogError("Could not patch item acquisition; the mod will do nothing: " + e);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            Quieten("startup");


            Log.LogInfo(Name + " " + Version + " loaded.");
        }

        /// <summary>
        /// Loading a save hands a character every item it already owns, one call at a time, and
        /// the call looks exactly like picking something up. Ignoring acquisitions for a few
        /// seconds after a scene load is what keeps a load from producing a stack of offers.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Quieten("scene " + scene.name);
            Prompt.Clear();
        }

        private void Quieten(string why)
        {
            Watcher.QuietUntil = Time.realtimeSinceStartup
                + Mathf.Max(0f, ModConfig.SettleSeconds.Value);

            if (why != null && ModConfig.LogDecisions.Value)
                Log.LogInfo("Ignoring acquisitions for a moment after " + why + ".");
        }

        /// <summary>
        /// Whether the game is mid-load. Read defensively: this is a game type, so a future
        /// update could rename or remove it, and the honest failure is to assume the game is
        /// running normally rather than to stop working entirely.
        /// </summary>
        private static bool IsLoading()
        {
            try
            {
                var screen = LoadingScreen.Instance;
                if (screen == null)
                    return false;

                return screen.IsLoading || screen.MainFadeActive || !screen.InitialLoadComplete;
            }
            catch
            {
                return false;
            }
        }

        private void Update()
        {
            try
            {
                // Held open for as long as the game says it is loading, so the settle window
                // starts when loading actually finishes rather than when the scene appeared. A
                // slow load would otherwise run past a fixed timer and the items still arriving
                // would be taken for things the player had just picked up - which, with silent
                // equipping on, would rearrange a party's gear on load.
                if (IsLoading())
                    Quieten(null);

                if (Prompt.Any)
                    Prompt.Prune();
            }
            catch (Exception e)
            {
                Log.LogError("Prompt upkeep failed: " + e);
            }
        }

        private void OnGUI()
        {
            try
            {
                Prompt.Draw();
            }
            catch (Exception e)
            {
                // Unity swallows whatever a MonoBehaviour throws, so without this the prompt
                // would simply stop responding with nothing in either log to say why.
                Log.LogError("Prompt draw failed, dropping the queue: " + e);
                Prompt.Clear();
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (harmony != null)
                harmony.UnpatchSelf();
        }
    }
}
