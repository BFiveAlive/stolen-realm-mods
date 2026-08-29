using System;
using System.Collections.Generic;

namespace ModManager
{
    /// <summary>
    /// The shape of mods.json, the single list of what is published.
    ///
    /// Both consumers read this same file: the in-game updater here, and the standalone installer.
    /// Parsed by the reader in Json.cs, with the mapping below written out by hand. See that
    /// file for why UnityEngine.JsonUtility is not used.
    /// </summary>
    public class Manifest
    {
        public int schemaVersion;
        public BepInExRelease bepinex;
        public ModRelease[] mods;

        /// <summary>
        /// Builds a manifest from JSON text, or throws if the document is not an object.
        /// Individual fields are read leniently, so a manifest from a newer release that adds
        /// fields still parses.
        /// </summary>
        public static Manifest Parse(string text)
        {
            var root = Json.AsObject(Json.Parse(text));
            if (root == null)
                throw new FormatException("The manifest is not a JSON object.");

            var manifest = new Manifest
            {
                schemaVersion = Json.GetInt(root, "schemaVersion")
            };

            root.TryGetValue("bepinex", out object bepinexValue);
            var bepinex = Json.AsObject(bepinexValue);

            if (bepinex != null)
            {
                manifest.bepinex = new BepInExRelease
                {
                    version = Json.GetString(bepinex, "version"),
                    url = Json.GetString(bepinex, "url"),
                    sha256 = Json.GetString(bepinex, "sha256")
                };
            }

            root.TryGetValue("mods", out object modsValue);
            var mods = Json.AsArray(modsValue);

            var releases = new List<ModRelease>();

            if (mods != null)
            {
                foreach (var item in mods)
                {
                    var entry = Json.AsObject(item);
                    if (entry == null)
                        continue;

                    releases.Add(new ModRelease
                    {
                        guid = Json.GetString(entry, "guid"),
                        name = Json.GetString(entry, "name"),
                        folder = Json.GetString(entry, "folder"),
                        version = Json.GetString(entry, "version"),
                        description = Json.GetString(entry, "description"),
                        url = Json.GetString(entry, "url"),
                        sha256 = Json.GetString(entry, "sha256"),
                        recommended = Json.GetBool(entry, "recommended")
                    });
                }
            }

            manifest.mods = releases.ToArray();
            return manifest;
        }
    }

    public class BepInExRelease
    {
        public string version;
        public string url;
        public string sha256;
    }

    public class ModRelease
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
