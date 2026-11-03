using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace CumulativeStatsMod
{
    /// <summary>One fight, as it ended.</summary>
    internal sealed class BattleRecord
    {
        [JsonProperty("number")]
        public int Number { get; set; }

        [JsonProperty("endedUtc")]
        public string EndedUtc { get; set; }

        /// <summary>Stats for that fight alone, per character name, keyed by BattleStat name.</summary>
        [JsonProperty("stats")]
        public Dictionary<string, Dictionary<string, float>> Stats { get; set; }
            = new Dictionary<string, Dictionary<string, float>>();

        /// <summary>Biggest-hit records for this battle. Empty on a client - see ExtraStats.</summary>
        [JsonProperty("records")]
        public Dictionary<string, Dictionary<string, float>> Records { get; set; }
            = new Dictionary<string, Dictionary<string, float>>();
    }

    /// <summary>A party member, as they were when the run finished.</summary>
    internal sealed class MemberRecord
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("startingClass")]
        public string StartingClass { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("treesRemoved")]
        public List<string> TreesRemoved { get; set; } = new List<string>();

        [JsonProperty("attributes")]
        public Dictionary<string, float> Attributes { get; set; } = new Dictionary<string, float>();

        [JsonProperty("skills")]
        public List<string> Skills { get; set; } = new List<string>();

        [JsonProperty("equipment")]
        public List<string> Equipment { get; set; } = new List<string>();
    }

    /// <summary>One roguelike game, start to finish.</summary>
    internal sealed class RunRecord
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("startedUtc")]
        public string StartedUtc { get; set; }

        [JsonProperty("endedUtc")]
        public string EndedUtc { get; set; }

        /// <summary>"won", "lost" or "unfinished" - the last meaning the game was left mid-run.</summary>
        [JsonProperty("outcome")]
        public string Outcome { get; set; } = "unfinished";

        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        [JsonProperty("levelReached")]
        public int LevelReached { get; set; }

        [JsonProperty("party")]
        public List<MemberRecord> Party { get; set; } = new List<MemberRecord>();

        [JsonProperty("battles")]
        public List<BattleRecord> Battles { get; set; } = new List<BattleRecord>();

        /// <summary>Everything above added up, so the viewer does not have to.</summary>
        [JsonProperty("totals")]
        public Dictionary<string, Dictionary<string, float>> Totals { get; set; }
            = new Dictionary<string, Dictionary<string, float>>();

        /// <summary>
        /// The run's biggest-hit records: the largest of each battle's, not their sum. A record
        /// added up would be meaningless.
        /// </summary>
        [JsonProperty("records")]
        public Dictionary<string, Dictionary<string, float>> Records { get; set; }
            = new Dictionary<string, Dictionary<string, float>>();

        [JsonIgnore]
        public string Title
        {
            get
            {
                string who = Party.Count == 0
                    ? "(no party)"
                    : string.Join(", ", Party.Select(x => x.Name).ToArray());

                return who + "  -  " + Outcome + ", " + Battles.Count + " battle"
                       + (Battles.Count == 1 ? "" : "s");
            }
        }

        [JsonIgnore]
        public DateTime Started
        {
            get
            {
                return DateTime.TryParse(StartedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out DateTime value) ? value : DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// Where finished runs are kept, and how they are read back.
    ///
    /// One file per run, under BepInEx/config, so a run that is already written can never be lost
    /// by a later one going wrong - and so updating the mod cannot touch them. Files are named by
    /// start time, which makes the folder readable on its own and sorts correctly without opening
    /// anything.
    /// </summary>
    internal static class RunArchive
    {
        internal static string Folder => Path.Combine(Paths.ConfigPath, "CumulativeStatsMod", "runs");

        private static List<RunRecord> cache;

        /// <summary>Every archived run, newest first. Read once and kept.</summary>
        internal static List<RunRecord> All()
        {
            if (cache != null)
                return cache;

            cache = new List<RunRecord>();

            try
            {
                if (!Directory.Exists(Folder))
                    return cache;

                foreach (string path in Directory.GetFiles(Folder, "*.json"))
                {
                    try
                    {
                        RunRecord run = JsonConvert.DeserializeObject<RunRecord>(
                            File.ReadAllText(path, Encoding.UTF8));

                        if (run != null)
                            cache.Add(run);
                    }
                    catch (Exception e)
                    {
                        // One unreadable run must not hide the rest.
                        Plugin.Log.LogWarning("Could not read " + Path.GetFileName(path) + ": " + e.Message);
                    }
                }

                cache = cache.OrderByDescending(x => x.Started).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read the run archive: " + e.Message);
            }

            return cache;
        }

        internal static void Forget() => cache = null;

        internal static bool Save(RunRecord run)
        {
            if (run == null)
                return false;

            try
            {
                Directory.CreateDirectory(Folder);

                string json = JsonConvert.SerializeObject(run, Formatting.Indented,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                // No BOM. The same byte cost the in-game updater a working manifest for three
                // releases, and nothing that reads these wants one either.
                File.WriteAllText(PathFor(run), json + Environment.NewLine, new UTF8Encoding(false));

                Forget();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not write the run archive entry: " + e.Message);
                return false;
            }
        }

        internal static void Delete(RunRecord run)
        {
            try
            {
                string path = PathFor(run);
                if (File.Exists(path))
                    File.Delete(path);

                Forget();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not delete that run: " + e.Message);
            }
        }

        private static string PathFor(RunRecord run) => Path.Combine(Folder, Safe(run.Id) + ".json");

        private static string Safe(string id)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var text = new StringBuilder(id.Length);

            foreach (char c in id ?? "run")
                text.Append(invalid.Contains(c) ? '_' : c);

            return text.Length == 0 ? "run" : text.ToString();
        }
    }
}
