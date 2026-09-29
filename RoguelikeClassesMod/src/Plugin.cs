using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Adds roguelike starting classes described in classes.json.
    ///
    /// Nothing in the game's own logic is replaced. The mod builds preset ScriptableObjects and
    /// adds them to the list the game already reads, so the picker, the tiles and character
    /// creation all treat a modded class exactly as they treat a shipped one.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.roguelikeclassesmod";
        public const string Name = "Roguelike Classes Mod";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DumpGameData;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Add the classes from classes.json to the roguelike character picker. Turning this " +
                "off leaves the game's own classes untouched; characters already created from a " +
                "modded class keep their skills and gear, since those live in the character's save.");

            DumpGameData = Config.Bind("General", "DumpGameData", false,
                "Write the game's shipped presets, item names, difficulty list and roguelike roll " +
                "tuning to game-data.json under the save folder, once. All of it lives in asset " +
                "bundles rather than in code, so this is the only way to read it. Needed to write " +
                "equipment by name and to judge a new class against the existing ones.");

            if (!Enabled.Value && !DumpGameData.Value)
            {
                Log.LogInfo($"{Name} {Version} loaded but switched off.");
                return;
            }

            var harmony = new Harmony(Guid);

            if (Enabled.Value)
                harmony.PatchAll(typeof(PresetInjection));

            if (DumpGameData.Value)
                harmony.PatchAll(typeof(DumpTrigger));

            Log.LogInfo($"{Name} {Version} loaded.");
        }
    }

    /// <summary>
    /// Drives the one-shot dump from a method the game itself calls every frame.
    ///
    /// A plugin's own Update is not a usable clock here: BepInEx's manager object stops being
    /// updated once the game loads its first scene, so anything waiting there for game data to
    /// appear waits forever. Harmony patches are unaffected, because they run inside the game's
    /// own call stack, and GUIManager exists from the main menu onwards.
    /// </summary>
    [HarmonyPatch(typeof(GUIManager), "Update")]
    internal static class DumpTrigger
    {
        [HarmonyPostfix]
        private static void Tick() => GameDataDumper.TryDump();
    }
}
