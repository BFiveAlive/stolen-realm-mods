using System;

namespace ModManager
{
    /// <summary>
    /// The shape of mods.json, the single list of what is published.
    ///
    /// Both consumers read this same file: the in-game updater here, and the standalone installer.
    /// Fields are public and the classes are [Serializable] because UnityEngine.JsonUtility is
    /// doing the parsing - the game ships Newtonsoft, but depending on a version of it that the
    /// game chose is a needless way to break on a game update.
    /// </summary>
    [Serializable]
    internal class Manifest
    {
        public int schemaVersion;
        public BepInExRelease bepinex;
        public ModRelease[] mods;
    }

    [Serializable]
    internal class BepInExRelease
    {
        public string version;
        public string url;
        public string sha256;
    }

    [Serializable]
    internal class ModRelease
    {
        /// <summary>Matches the plugin's BepInPlugin GUID, which is how installed mods are identified.</summary>
        public string guid;

        public string name;

        /// <summary>Folder under BepInEx/plugins, used for display and for the staging file name.</summary>
        public string folder;

        public string version;
        public string description;

        /// <summary>
        /// Zip of the release, laid out relative to the game root - so every path inside starts
        /// with BepInEx/. Installing is then the same operation for the installer and the updater:
        /// extract over the game folder.
        /// </summary>
        public string url;

        /// <summary>Lowercase hex SHA-256 of the zip. Empty means "do not verify".</summary>
        public string sha256;

        /// <summary>Whether a fresh install should tick this mod by default.</summary>
        public bool recommended;
    }

    /// <summary>One row of the Updates tab: what is installed against what is published.</summary>
    internal sealed class UpdateStatus
    {
        public ModRelease Release;
        public string InstalledVersion;
        public bool Installed;
        public bool UpdateAvailable;
        public bool Staged;

        public string DisplayName =>
            Release != null && !string.IsNullOrEmpty(Release.name) ? Release.name : Release?.folder ?? "?";

        /// <summary>
        /// Compares as System.Version rather than as text, so 0.10.0 correctly beats 0.9.0.
        /// A version that will not parse is treated as "not newer": prompting someone to
        /// download over a build we cannot reason about is the worse failure.
        /// </summary>
        public static bool IsNewer(string candidate, string installed)
        {
            if (string.IsNullOrEmpty(candidate))
                return false;

            if (string.IsNullOrEmpty(installed))
                return true;

            if (Version.TryParse(candidate, out var a) && Version.TryParse(installed, out var b))
                return a > b;

            return false;
        }
    }
}
