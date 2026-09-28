using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CharacterViewerMod
{
    /// <summary>
    /// Lets you look at a character's stats, inventory, skills and fortunes from the character
    /// selection screen, in both campaign and roguelike, without starting a game.
    ///
    /// It adds "View Character" to each character's options menu (right-click a character) and
    /// opens the game's own character menu for it, so everything shown is exactly what the game
    /// shows in play. While viewing, changes are blocked: a character that is not in a party is
    /// not loaded into the session, so the game would not save anything done to it anyway.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "bfivealive.stolenrealm.characterviewermod";
        public const string Name = "Character Viewer Mod";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> UnlockTreeRemoval;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Adds View Character to each character's options (right-click a character) on the character " +
                "selection screen, opening their stats, inventory, skills and fortunes read-only.");

            UnlockTreeRemoval = Config.Bind("Roguelike", "UnlockTreeRemoval", false,
                "Let a roguelike character's removed skill trees be changed after level 1. The game " +
                "locks them once the character levels up; this reopens that one check, and every " +
                "other rule still applies - no more removals than the removal points owned, and " +
                "only trees that are available. Opening the removal window re-applies those rules " +
                "to the stored list, so a character holding more removals than its points now allow " +
                "is trimmed to fit when the window opens.");

            SelfTest.ReadCommandLine();

            // PatchAll throws if a target method cannot be resolved, so logging after it makes the
            // log line itself proof that every patch bound.
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded." + (SelfTest.Enabled ? " Self-test is on." : string.Empty));
        }
    }
}
