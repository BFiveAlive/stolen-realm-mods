using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RunTagsMod
{
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> LabelBudget;
        public static ConfigEntry<bool> ItalicHost;
        public static ConfigEntry<bool> MarkSavedRun;
        public static ConfigEntry<string> SavedRunLabel;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Replace the 'Roguelike' label on a character card with the difficulty, host and party.");

            LabelBudget = cfg.Bind("Display", "LabelBudget", 23,
                "How many characters the label may use. The card fits roughly 20-25 before the "
                + "text crowds the name above it. Names are trimmed to fit, shortest served first.");

            ItalicHost = cfg.Bind("Display", "ItalicHost", true,
                "Show the host's name in italics. The tags do not count against the budget.");

            MarkSavedRun = cfg.Bind("Difficulty", "MarkSavedRun", true,
                "On the difficulty picker, mark the tile your saved roguelike run is actually at. "
                + "A new run defaults to the host's highest unlock, which differs per player, so "
                + "this is how a difficulty change shows up before you start rather than after.");

            SavedRunLabel = cfg.Bind("Difficulty", "SavedRunLabel", "Your run",
                "What the marker says on that tile.");
        }
    }

    [BepInPlugin(Guid, "Run Tags Mod", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.runtagsmod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            try
            {
                ModConfig.Bind(Config);
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(CardLabel));
                harmony.PatchAll(typeof(SavedRunMarker));
                Ticker.Install();

                Log.LogInfo("Run Tags Mod " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Log.LogError("Run Tags Mod failed to start, so cards keep their usual label: " + e);
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
                RunWatcher.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Run tags update failed: " + e);
            }
        }
    }
}
