using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GentleFoesMod
{
    [BepInPlugin(Guid, "Gentle Foes Mod", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.gentlefoesmod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> DumpEnemies;

        private void Awake()
        {
            Log = Logger;

            DumpEnemies = Config.Bind("General", "DumpEnemies", true,
                "Write the full enemy list to BepInEx/config/GentleFoesMod/enemies-dump.json on "
                + "startup. That file is what you pick swaps from; turn this off once you have it.");

            try
            {
                new Harmony(Guid).PatchAll(typeof(NamePatch));
                SwapStore.EnsureUserFile();
                Ticker.Install();

                Log.LogInfo("Gentle Foes Mod " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Log.LogError("Gentle Foes Mod failed to start, so nothing was changed: " + e);
            }
        }
    }

    /// <summary>
    /// Waits for the game's data to exist, then applies the swaps once.
    ///
    /// A BepInEx plugin's own Update is never called in this game - measured repeatedly across the
    /// mods here - so this rides <see cref="Application.onBeforeRender"/>, a static event that
    /// keeps firing regardless of what happens to the object the plugin lives on.
    ///
    /// The enemy list is loaded some way into start-up, so there is nothing to patch in Awake.
    /// </summary>
    internal static class Ticker
    {
        private static bool installed;
        private static bool applied;

        internal static void Install()
        {
            if (installed)
                return;

            installed = true;
            Application.onBeforeRender += Tick;
        }

        private static void Tick()
        {
            if (applied)
                return;

            try
            {
                if (Burst2Flame.Game.Instance == null)
                    return;

                if (EnemySwapper.AllEnemies().Count == 0)
                    return;                       // data not loaded yet; try again next frame

                applied = true;

                if (Plugin.DumpEnemies.Value)
                    EnemyDumper.Dump();           // before the swap, so it records the originals

                EnemySwapper.Apply();
            }
            catch (Exception e)
            {
                applied = true;                   // once, not once per frame
                Plugin.Log.LogError("Applying the swaps failed, so the enemies are unchanged: " + e);
            }
        }
    }
}
