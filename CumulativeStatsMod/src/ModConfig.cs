using BepInEx.Configuration;

namespace CumulativeStatsMod
{
    /// <summary>
    /// All tuning lives in BepInEx/config/bfivealive.stolenrealm.cumulativestatsmod.cfg.
    /// Every value is read fresh at the point of use, so with HotReloadConfig on, a saved
    /// edit takes effect on the next frame without restarting the game.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> DefaultToCumulative;
        public static ConfigEntry<bool> HotReloadConfig;

        public static ConfigEntry<float> ButtonMarginX;
        public static ConfigEntry<float> ButtonMarginY;
        public static ConfigEntry<float> ButtonScale;
        public static ConfigEntry<float> ButtonOffsetX;
        public static ConfigEntry<float> ButtonOffsetY;
        public static ConfigEntry<int> SubtextScale;

        public static ConfigEntry<float> CompactNumberThreshold;

        public static ConfigEntry<bool> PersistBetweenSessions;
        public static ConfigEntry<int> RetentionDays;
        public static ConfigEntry<int> MaxTrackedCharacters;

        public static ConfigEntry<bool> LogWindowHierarchy;
        public static ConfigEntry<bool> LogTracking;

        public static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Master switch. Off hides the toggle button and stops all tracking; the stats " +
                "window then behaves exactly like vanilla.");

            DefaultToCumulative = cfg.Bind("General", "DefaultToCumulative", false,
                "Which side of the toggle the stats window starts on each time the game is " +
                "launched. False = the vanilla per-battle view. The choice you make with the " +
                "button then sticks for the rest of the session.");

            HotReloadConfig = cfg.Bind("General", "HotReloadConfig", true,
                "Watch this file and apply edits immediately, without restarting the game. " +
                "Handy for nudging the button offsets below into place. Toggling this setting " +
                "itself needs a restart, since the watcher is created at startup.");

            ButtonMarginX = cfg.Bind("Button", "ButtonMarginX", 24f,
                "Distance in pixels from the right edge of the stats panel.");

            ButtonMarginY = cfg.Bind("Button", "ButtonMarginY", 16f,
                "Distance in pixels from the top edge of the stats panel.");

            ButtonScale = cfg.Bind("Button", "ButtonScale", 0.8f,
                "Multiplier on the size of the button. The button is cloned from an existing " +
                "button in the window, which is usually sized for a more prominent role.");

            ButtonOffsetX = cfg.Bind("Button", "ButtonOffsetX", 0f,
                "Extra nudge on top of ButtonMarginX. Positive moves right.");

            ButtonOffsetY = cfg.Bind("Button", "ButtonOffsetY", 0f,
                "Extra nudge on top of ButtonMarginY. Positive moves up.");

            SubtextScale = cfg.Bind("Button", "SubtextScale", 65,
                "Size of the small second line on the button, as a percentage of the main line.");

            CompactNumberThreshold = cfg.Bind("Display", "CompactNumberThreshold", 1000000f,
                "In the run-total view only, values at or above this are abbreviated (1.2M, " +
                "345K) so they still fit the column, which vanilla sizes for single-battle " +
                "numbers. Set to 0 to always print the exact figure.");

            PersistBetweenSessions = cfg.Bind("Data", "PersistBetweenSessions", true,
                "Save run totals to disk so they survive quitting and reloading a run. Off " +
                "means totals only cover battles fought since the game was launched.");

            RetentionDays = cfg.Bind("Data", "RetentionDays", 60,
                "Drop saved totals for characters not seen for this many days. Roguelike " +
                "characters are created fresh per run, so without this the file grows forever.");

            MaxTrackedCharacters = cfg.Bind("Data", "MaxTrackedCharacters", 200,
                "Hard cap on saved records. The least recently seen are dropped first.");

            LogWindowHierarchy = cfg.Bind("Debug", "LogWindowHierarchy", false,
                "Dump the stats window's UI hierarchy to the BepInEx log the next time the " +
                "window opens. Only useful for diagnosing button placement.");

            LogTracking = cfg.Bind("Debug", "LogTracking", false,
                "Log each time a finished battle is folded into the run total.");
        }
    }
}
