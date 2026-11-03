using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RoguelikeSlotsMod
{
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Give each roguelike party its own save file, so several runs can be in progress "
                + "at once. Turning this off puts the game back on its single save; nothing is "
                + "deleted either way, so the slot files stay on disk.");
        }
    }

    [BepInPlugin(Guid, "Roguelike Slots Mod", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.roguelikeslotsmod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            try
            {
                ModConfig.Bind(Config);
                new Harmony(Guid).PatchAll(typeof(QuestPathPatch));
                Ticker.Install();

                Log.LogInfo("Roguelike Slots Mod " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Log.LogError("Roguelike Slots Mod failed to start, so the game keeps its single save: " + e);
            }
        }
    }

    /// <summary>
    /// The mod's clock. A BepInEx plugin's own Update is never called in this game - measured
    /// across every mod here - so this rides a static event that keeps firing regardless.
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
                SlotWatcher.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Slot update failed: " + e);
            }
        }
    }
}
