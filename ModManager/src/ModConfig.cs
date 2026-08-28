using BepInEx.Configuration;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// The manager's own settings. It reads every other plugin's config through BepInEx, but its
    /// own live here like any other mod's would - which also means the manager shows up in its own
    /// settings list, and dogfoods the drawers.
    /// </summary>
    internal static class ModConfig
    {
        public static ConfigEntry<KeyboardShortcut> ToggleKey;
        public static ConfigEntry<float> UiScale;
        public static ConfigEntry<bool> UnlockCursorWhileOpen;
        public static ConfigEntry<bool> BlockGameUiWhileOpen;

        public static ConfigEntry<bool> CheckForUpdatesOnStartup;
        public static ConfigEntry<string> ManifestUrl;

        public static ConfigEntry<float> SaveDebounceSeconds;
        public static ConfigEntry<bool> VerboseLogging;

        /// <summary>
        /// Where the updater looks for the list of published mods. Pointed at the raw file in the
        /// repo rather than the GitHub API: raw.githubusercontent.com needs no token for a public
        /// repo and is not subject to the API's 60-requests-per-hour unauthenticated limit.
        /// </summary>
        public const string DefaultManifestUrl =
            "https://raw.githubusercontent.com/BFiveAlive/stolen-realm-mods/main/mods.json";

        public static void Bind(ConfigFile cfg)
        {
            ToggleKey = cfg.Bind("General", "ToggleKey",
                new KeyboardShortcut(KeyCode.F1),
                "Opens and closes the mod manager. Modifiers are supported, e.g. LeftControl+F1.");

            UiScale = cfg.Bind("General", "UiScale", 1.0f,
                new ConfigDescription(
                    "Size of the manager window and its text. Raise it on a 4K display.",
                    new AcceptableValueRange<float>(0.6f, 2.5f)));

            UnlockCursorWhileOpen = cfg.Bind("General", "UnlockCursorWhileOpen", true,
                "Force the mouse cursor visible and unlocked while the manager is open, and put " +
                "back whatever the game had when it closes.");

            BlockGameUiWhileOpen = cfg.Bind("General", "BlockGameUiWhileOpen", true,
                "Disable the game's UI event system while the manager is open, so a click on this " +
                "panel does not also press whatever is behind it. Note this cannot block keyboard " +
                "shortcuts the game reads directly from Input, so a keystroke typed into a text " +
                "box here may still reach the game.");

            CheckForUpdatesOnStartup = cfg.Bind("Updates", "CheckForUpdatesOnStartup", true,
                "Fetch the published mod list once at startup and flag anything out of date. " +
                "Nothing is ever downloaded without you asking for it.");

            ManifestUrl = cfg.Bind("Updates", "ManifestUrl", DefaultManifestUrl,
                "The mods.json describing what is published. Point this at your own fork to " +
                "update from somewhere else.");

            SaveDebounceSeconds = cfg.Bind("Advanced", "SaveDebounceSeconds", 0.5f,
                new ConfigDescription(
                    "How long to wait after the last edit before writing a config file to disk. " +
                    "Dragging a slider changes a value every frame; without this, each frame " +
                    "would be a disk write and would wake every mod's config file watcher.",
                    new AcceptableValueRange<float>(0.05f, 5f)));

            VerboseLogging = cfg.Bind("Advanced", "VerboseLogging", false,
                "Log every discovered plugin and setting to BepInEx/LogOutput.log. Useful when a " +
                "mod's settings are not appearing in the list.");
        }
    }
}
