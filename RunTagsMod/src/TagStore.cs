using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace RunTagsMod
{
    /// <summary>What a character's run looked like, last time it was played.</summary>
    internal sealed class RunTag
    {
        [JsonProperty("difficulty")]
        public int Difficulty { get; set; } = -1;

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("players")]
        public List<string> Players { get; set; } = new List<string>();

        [JsonProperty("updatedUtc")]
        public string UpdatedUtc { get; set; }
    }

    /// <summary>
    /// Remembers who hosted each character's run.
    ///
    /// The game stores nothing about this - a character file knows its own level and skills and
    /// nothing about the session it was played in - and the character selection screen runs with
    /// no lobby open, so by the time the card is drawn there is no live answer to read. It has to
    /// be written down while the game is being played and read back later.
    ///
    /// Kept under BepInEx/config so updating the mod cannot lose it, and keyed by character guid
    /// rather than name, because names repeat.
    /// </summary>
    internal static class TagStore
    {
        private static Dictionary<string, RunTag> tags;
        private static bool dirty;

        internal static string Folder => Path.Combine(Paths.ConfigPath, "RunTagsMod");

        internal static string FilePath => Path.Combine(Folder, "run-tags.json");

        internal static Dictionary<string, RunTag> All
        {
            get
            {
                if (tags == null)
                    Load();

                return tags;
            }
        }

        internal static RunTag For(string characterGuid)
        {
            if (string.IsNullOrEmpty(characterGuid))
                return null;

            return All.TryGetValue(characterGuid, out RunTag tag) ? tag : null;
        }

        internal static void Record(string characterGuid, RunTag tag)
        {
            if (string.IsNullOrEmpty(characterGuid) || tag == null)
                return;

            if (All.TryGetValue(characterGuid, out RunTag existing) && Same(existing, tag))
                return;                     // nothing changed; no need to rewrite the file

            All[characterGuid] = tag;
            dirty = true;
        }

        private static bool Same(RunTag a, RunTag b)
        {
            if (a.Difficulty != b.Difficulty || !string.Equals(a.Host, b.Host, StringComparison.Ordinal))
                return false;

            if (a.Players.Count != b.Players.Count)
                return false;

            for (int i = 0; i < a.Players.Count; i++)
            {
                if (!string.Equals(a.Players[i], b.Players[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static void Load()
        {
            tags = new Dictionary<string, RunTag>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(FilePath))
                    return;

                var read = JsonConvert.DeserializeObject<Dictionary<string, RunTag>>(
                    File.ReadAllText(FilePath, Encoding.UTF8));

                if (read != null)
                {
                    foreach (KeyValuePair<string, RunTag> pair in read)
                    {
                        if (pair.Value != null)
                            tags[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read the run tags, so cards will show the usual label: "
                                    + e.Message);
            }
        }

        internal static void SaveIfDirty()
        {
            if (!dirty)
                return;

            dirty = false;

            try
            {
                Directory.CreateDirectory(Folder);

                string json = JsonConvert.SerializeObject(All, Formatting.Indented);

                // No BOM, as everywhere else here.
                File.WriteAllText(FilePath, json + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not write the run tags: " + e.Message);
            }
        }
    }
}
