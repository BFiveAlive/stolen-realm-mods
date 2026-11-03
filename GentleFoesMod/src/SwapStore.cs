using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace GentleFoesMod
{
    /// <summary>
    /// Where the swap list comes from.
    ///
    /// Two files, for the reason the classes mod learned the hard way: the one beside the plugin
    /// is the mod's content and every update overwrites it, so it is never written to. Anything
    /// you change lives under BepInEx/config, which no release names and so cannot touch.
    ///
    /// Your entries win over the shipped ones, matched on the enemy name.
    /// </summary>
    internal static class SwapStore
    {
        private static SwapLibraryFile library;

        internal static string PluginFolder =>
            Path.GetDirectoryName(typeof(SwapStore).Assembly.Location) ?? ".";

        internal static string DefaultsPath => Path.Combine(PluginFolder, "swaps.default.json");

        internal static string UserFolder => Path.Combine(Paths.ConfigPath, "GentleFoesMod");

        internal static string UserPath => Path.Combine(UserFolder, "swaps.json");

        internal static SwapLibraryFile Library => library ?? (library = Load());

        private static SwapLibraryFile Load()
        {
            SwapLibraryFile shipped = Read(DefaultsPath) ?? new SwapLibraryFile();
            SwapLibraryFile mine = Read(UserPath);

            if (mine == null)
                return shipped;

            // Yours replace the shipped entry for the same enemy, rather than being added beside
            // it - two rules for one bear would otherwise fight, and which won would depend on
            // list order.
            var merged = new SwapLibraryFile { Schema = 1, Swaps = new List<SwapDefinition>(shipped.Swaps ?? new List<SwapDefinition>()) };

            foreach (SwapDefinition entry in mine.Swaps ?? new List<SwapDefinition>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Enemy))
                    continue;

                int at = merged.Swaps.FindIndex(
                    x => x != null && string.Equals(x.Enemy, entry.Enemy, StringComparison.OrdinalIgnoreCase));

                if (at >= 0)
                    merged.Swaps[at] = entry;
                else
                    merged.Swaps.Add(entry);
            }

            merged.GroupSwaps = (mine.GroupSwaps != null && mine.GroupSwaps.Count > 0)
                ? mine.GroupSwaps
                : shipped.GroupSwaps;

            return merged;
        }

        private static SwapLibraryFile Read(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<SwapLibraryFile>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read " + Path.GetFileName(path) + ", so it was ignored: " + e.Message);
                return null;
            }
        }

        /// <summary>Creates an empty user file the first time, so there is something to edit.</summary>
        internal static void EnsureUserFile()
        {
            if (File.Exists(UserPath))
                return;

            try
            {
                Directory.CreateDirectory(UserFolder);

                var starter = new SwapLibraryFile
                {
                    Schema = 1,
                    Swaps = new List<SwapDefinition>
                    {
                        new SwapDefinition { Enemy = "Forest Bear", LookLike = null, Name = null },
                    },
                };

                string json = JsonConvert.SerializeObject(starter, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include });

                // No BOM: the same byte cost the in-game updater a working manifest for three
                // releases, and nothing that reads this wants one either.
                File.WriteAllText(UserPath, json + Environment.NewLine, new UTF8Encoding(false));

                Plugin.Log.LogInfo("Wrote a starter swap file at " + UserPath + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write a starter swap file: " + e.Message);
            }
        }
    }
}
