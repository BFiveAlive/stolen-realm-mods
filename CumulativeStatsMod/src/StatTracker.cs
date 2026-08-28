using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Keeps a per-character running total of every <see cref="BattleStat"/> across the battles
    /// of a single roguelike run.
    ///
    /// The game stores battle stats in <c>Root.BattleStats</c> and wipes that list at the start
    /// of every battle (<c>BattleManager.InitBattleAndStart</c> calls <c>StatManager.ClearStats</c>).
    /// Rather than patching the wipe, this samples the list and infers the boundary: within a
    /// battle these counters only ever climb, so the first sample where a character's total has
    /// *dropped* is the first sample of a new battle.
    ///
    /// Sampling rather than hooking <c>ClearStats</c> matters in co-op: the wipe is guarded by
    /// <c>IsServer</c> and only ever runs on the host, while <c>Root.BattleStats</c> is synced to
    /// every client. Reading the same data the vanilla window reads works on both.
    /// </summary>
    internal static class StatTracker
    {
        /// <summary>
        /// Totals are split in two so a session that ends mid-run can be resumed exactly:
        /// Archive holds finished battles, Snapshot holds the live values of the battle currently
        /// in <c>Root.BattleStats</c>. Saving both means that on reload — whether or not the game
        /// still has the last battle's stats in memory — the same boundary rule produces the same
        /// total instead of double counting it.
        /// </summary>
        private sealed class Record
        {
            public string Name = "";
            public long LastSeenUnix;
            public readonly Dictionary<int, float> Archive = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Snapshot = new Dictionary<int, float>();
        }

        private static readonly BattleStat[] AllStats = (BattleStat[])Enum.GetValues(typeof(BattleStat));

        private const float PollIntervalSeconds = 0.5f;
        private const float SaveIntervalSeconds = 20f;
        private const string FileHeader = "cumulativestatsmod-v1";

        private static readonly Dictionary<string, Record> records = new Dictionary<string, Record>();

        // Reused every poll so sampling twice a second allocates nothing.
        private static readonly Dictionary<string, Dictionary<int, float>> live =
            new Dictionary<string, Dictionary<int, float>>();
        private static readonly Dictionary<string, string> liveNames = new Dictionary<string, string>();
        private static readonly Stack<Dictionary<int, float>> bufferPool = new Stack<Dictionary<int, float>>();
        private static readonly List<string> reusedKeyList = new List<string>();

        private static float nextPollAt;
        private static float nextSaveAt;
        private static bool dirty;
        private static bool loaded;

        private static string DataPath => Path.Combine(BepInEx.Paths.ConfigPath, Plugin.Guid + ".data");

        /// <summary>Throttled entry point for the plugin's Update.</summary>
        public static void Tick()
        {
            if (!ModConfig.Enabled.Value)
                return;

            if (Time.realtimeSinceStartup >= nextPollAt)
                Poll();

            if (dirty && Time.realtimeSinceStartup >= nextSaveAt)
                Save();
        }

        /// <summary>
        /// Samples <c>Root.BattleStats</c> and folds any battle that just ended into the archive.
        /// Fails silently: this runs twice a second and a transient null anywhere in the
        /// networking chain is normal during loading screens.
        /// </summary>
        public static void Poll()
        {
            nextPollAt = Time.realtimeSinceStartup + PollIntervalSeconds;

            if (!loaded)
                Load();

            try
            {
                Root root = ResolveRoot();
                if (root == null || root.BattleStats == null)
                    return;

                ReadLive(root);
                Reconcile();
            }
            catch (Exception e)
            {
                if (ModConfig.LogTracking.Value)
                    Plugin.Log.LogWarning("Battle stat poll failed: " + e.Message);
            }
            finally
            {
                ReleaseLive();
            }
        }

        private static Root ResolveRoot()
        {
            NetworkingManager networking = NetworkingManager.Instance;
            if (networking == null || networking.NetworkManager == null)
                return null;

            return networking.NetworkManager.Root;
        }

        private static void ReadLive(Root root)
        {
            foreach (CharacterBattleStats entry in root.BattleStats)
            {
                if (entry == null || entry.BattleStats == null)
                    continue;

                Character character = entry.Character;
                if (character == null || string.IsNullOrEmpty(character.Guid))
                    continue;

                Dictionary<int, float> values = RentBuffer();
                foreach (BattleStat stat in AllStats)
                {
                    int key = (int)stat;
                    if (entry.BattleStats.ContainsKey(key))
                        values[key] = entry.BattleStats[key];
                }

                live[character.Guid] = values;
                liveNames[character.Guid] = character.CharacterName;
            }
        }

        /// <summary>
        /// Per character: if the live total dropped, the previous battle is over, so its snapshot
        /// moves into the archive. Either way the snapshot then becomes the live values, which
        /// keeps Archive + Snapshot correct at every moment — including while the post-battle
        /// menu is showing the battle that has just finished.
        /// </summary>
        private static void Reconcile()
        {
            foreach (KeyValuePair<string, Dictionary<int, float>> pair in live)
            {
                Record record = GetOrCreate(pair.Key);
                record.Name = liveNames[pair.Key];
                record.LastSeenUnix = NowUnix();

                // Half a point of slack: these are all whole numbers by the time they are
                // displayed, and float accumulation should never decide a battle boundary.
                if (Total(pair.Value) + 0.5f < Total(record.Snapshot))
                    FoldSnapshot(record);

                CopyInto(pair.Value, record.Snapshot);
                dirty = true;
            }

            // A wipe usually removes the character's entry from the list outright rather than
            // zeroing it, so those characters never appear in the loop above.
            reusedKeyList.Clear();
            foreach (KeyValuePair<string, Record> pair in records)
            {
                if (pair.Value.Snapshot.Count > 0 && !live.ContainsKey(pair.Key))
                    reusedKeyList.Add(pair.Key);
            }

            foreach (string guid in reusedKeyList)
            {
                FoldSnapshot(records[guid]);
                dirty = true;
            }
        }

        private static void FoldSnapshot(Record record)
        {
            if (record.Snapshot.Count == 0)
                return;

            foreach (KeyValuePair<int, float> pair in record.Snapshot)
            {
                record.Archive.TryGetValue(pair.Key, out float existing);
                record.Archive[pair.Key] = existing + pair.Value;
            }

            if (ModConfig.LogTracking.Value)
                Plugin.Log.LogInfo("Folded a finished battle into the run total for " + record.Name + ".");

            record.Snapshot.Clear();
        }

        /// <summary>Run total for one stat: finished battles plus whatever is live right now.</summary>
        public static float Cumulative(Character character, BattleStat stat)
        {
            if (character == null || string.IsNullOrEmpty(character.Guid))
                return 0f;

            if (!records.TryGetValue(character.Guid, out Record record))
                return 0f;

            record.Archive.TryGetValue((int)stat, out float archived);
            record.Snapshot.TryGetValue((int)stat, out float current);
            return archived + current;
        }

        // ---- storage ---------------------------------------------------------------------

        private static void Load()
        {
            loaded = true;

            if (!ModConfig.PersistBetweenSessions.Value)
                return;

            try
            {
                string path = DataPath;
                if (!File.Exists(path))
                    return;

                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || lines[0].Trim() != FileHeader)
                {
                    Plugin.Log.LogWarning("Ignoring saved run totals: unrecognised file format.");
                    return;
                }

                for (int i = 1; i < lines.Length; i++)
                    ParseRecord(lines[i]);

                Prune();
                Plugin.Log.LogInfo("Loaded run totals for " + records.Count + " character(s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read saved run totals, starting fresh: " + e.Message);
            }
        }

        private static void ParseRecord(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            string[] parts = line.Split('|');
            if (parts.Length < 5 || string.IsNullOrEmpty(parts[0]))
                return;

            Record record = GetOrCreate(parts[0]);
            record.Name = parts[1];
            long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out record.LastSeenUnix);
            ParseValues(parts[3], record.Archive);
            ParseValues(parts[4], record.Snapshot);
        }

        private static void ParseValues(string text, Dictionary<int, float> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            foreach (string pair in text.Split(';'))
            {
                int split = pair.IndexOf('=');
                if (split <= 0)
                    continue;

                if (int.TryParse(pair.Substring(0, split), NumberStyles.Integer, CultureInfo.InvariantCulture, out int key)
                    && float.TryParse(pair.Substring(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    into[key] = value;
                }
            }
        }

        public static void Save()
        {
            dirty = false;
            nextSaveAt = Time.realtimeSinceStartup + SaveIntervalSeconds;

            if (!ModConfig.PersistBetweenSessions.Value)
                return;

            try
            {
                Prune();

                var sb = new StringBuilder();
                sb.Append(FileHeader).Append('\n');
                foreach (KeyValuePair<string, Record> pair in records)
                {
                    sb.Append(pair.Key).Append('|')
                      .Append(Sanitise(pair.Value.Name)).Append('|')
                      .Append(pair.Value.LastSeenUnix.ToString(CultureInfo.InvariantCulture)).Append('|')
                      .Append(FormatValues(pair.Value.Archive)).Append('|')
                      .Append(FormatValues(pair.Value.Snapshot)).Append('\n');
                }

                // Write beside the target and swap, so a crash mid-write cannot leave a
                // half-written file where the real totals used to be.
                string path = DataPath;
                string temp = path + ".tmp";
                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not save run totals: " + e.Message);
            }
        }

        /// <summary>
        /// Roguelike characters are created fresh for every run, so without pruning the data
        /// file would accumulate one dead record per run forever.
        /// </summary>
        private static void Prune()
        {
            long cutoff = NowUnix() - (long)Math.Max(0, ModConfig.RetentionDays.Value) * 86400L;

            reusedKeyList.Clear();
            foreach (KeyValuePair<string, Record> pair in records)
            {
                if (pair.Value.LastSeenUnix < cutoff)
                    reusedKeyList.Add(pair.Key);
            }

            foreach (string guid in reusedKeyList)
                records.Remove(guid);

            int max = Math.Max(1, ModConfig.MaxTrackedCharacters.Value);
            while (records.Count > max)
            {
                string oldest = null;
                long oldestSeen = long.MaxValue;
                foreach (KeyValuePair<string, Record> pair in records)
                {
                    if (pair.Value.LastSeenUnix < oldestSeen)
                    {
                        oldestSeen = pair.Value.LastSeenUnix;
                        oldest = pair.Key;
                    }
                }

                if (oldest == null)
                    break;

                records.Remove(oldest);
            }
        }

        // ---- small helpers ---------------------------------------------------------------

        private static Record GetOrCreate(string guid)
        {
            if (!records.TryGetValue(guid, out Record record))
            {
                record = new Record();
                records[guid] = record;
            }

            return record;
        }

        private static Dictionary<int, float> RentBuffer()
        {
            if (bufferPool.Count == 0)
                return new Dictionary<int, float>();

            Dictionary<int, float> buffer = bufferPool.Pop();
            buffer.Clear();
            return buffer;
        }

        private static void ReleaseLive()
        {
            foreach (KeyValuePair<string, Dictionary<int, float>> pair in live)
                bufferPool.Push(pair.Value);

            live.Clear();
            liveNames.Clear();
        }

        private static float Total(Dictionary<int, float> values)
        {
            float total = 0f;
            foreach (KeyValuePair<int, float> pair in values)
                total += pair.Value;

            return total;
        }

        private static void CopyInto(Dictionary<int, float> from, Dictionary<int, float> to)
        {
            to.Clear();
            foreach (KeyValuePair<int, float> pair in from)
                to[pair.Key] = pair.Value;
        }

        private static string FormatValues(Dictionary<int, float> values)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<int, float> pair in values)
            {
                if (sb.Length > 0)
                    sb.Append(';');

                sb.Append(pair.Key.ToString(CultureInfo.InvariantCulture))
                  .Append('=')
                  .Append(pair.Value.ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>The name is cosmetic, only there to make the data file readable by hand.</summary>
        private static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            return name.Replace('|', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        private static long NowUnix()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
