using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace RoguelikeSlotsMod
{
    /// <summary>What a slot holds, so the picker can show something other than a hash.</summary>
    internal sealed class SlotInfo
    {
        [JsonProperty("characters")]
        public List<string> Characters { get; set; } = new List<string>();

        [JsonProperty("difficulty")]
        public int Difficulty { get; set; } = -1;

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("lastPlayedUtc")]
        public string LastPlayedUtc { get; set; }

        [JsonIgnore]
        public string Who => Characters.Count == 0
            ? "(unknown party)"
            : string.Join(", ", Characters.ToArray());
    }

    /// <summary>
    /// Names for the slots.
    ///
    /// A slot is identified by a hash of its party's guids, which is stable and meaningless to
    /// read. This keeps the human half beside it - who was in the party, how far they got - so
    /// the picker can offer "Rowan, Oakley - Difficulty III, level 4" rather than "a3f19c02".
    /// </summary>
    internal static class SlotIndex
    {
        private static Dictionary<string, SlotInfo> entries;
        private static bool dirty;

        internal static string Folder => Path.Combine(Paths.ConfigPath, "RoguelikeSlotsMod");

        internal static string FilePath => Path.Combine(Folder, "slots.json");

        internal static Dictionary<string, SlotInfo> All
        {
            get
            {
                if (entries == null)
                    Load();

                return entries;
            }
        }

        internal static SlotInfo For(string slot)
        {
            return slot != null && All.TryGetValue(slot, out SlotInfo info) ? info : null;
        }

        internal static void Record(string slot, SlotInfo info)
        {
            if (string.IsNullOrEmpty(slot) || info == null)
                return;

            if (All.TryGetValue(slot, out SlotInfo existing) && Same(existing, info))
                return;

            All[slot] = info;
            dirty = true;
        }

        private static bool Same(SlotInfo a, SlotInfo b)
        {
            return a.Difficulty == b.Difficulty
                   && a.Level == b.Level
                   && a.Characters.SequenceEqual(b.Characters);
        }

        private static void Load()
        {
            entries = new Dictionary<string, SlotInfo>(StringComparer.Ordinal);

            try
            {
                if (!File.Exists(FilePath))
                    return;

                var read = JsonConvert.DeserializeObject<Dictionary<string, SlotInfo>>(
                    File.ReadAllText(FilePath, Encoding.UTF8));

                if (read == null)
                    return;

                foreach (KeyValuePair<string, SlotInfo> pair in read)
                {
                    if (pair.Value != null)
                        entries[pair.Key] = pair.Value;
                }
            }
            catch (Exception e)
            {
                // The slots themselves still work; they just show as hashes.
                Plugin.Log.LogError("Could not read the slot index: " + e.Message);
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
                File.WriteAllText(FilePath, json + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not write the slot index: " + e.Message);
            }
        }
    }
}
