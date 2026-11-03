using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Burst2Flame;
using HarmonyLib;

namespace RoguelikeSlotsMod
{
    /// <summary>
    /// Gives each roguelike party its own save file.
    ///
    /// The game keeps exactly one: <c>QuestSaveRoguelike.json</c>, with the alternate-folder path
    /// hardcoded off, so starting a second run overwrites the first. That is fine for one person
    /// playing one run, and no use at all for several groups across several weeks.
    ///
    /// Only one property needs redirecting. Backup and temp are both built from it -
    /// <c>FilenameRoguelikeQuest + ".backup"</c> - and <c>CurrentRoguelikeQuestFilename</c> returns
    /// it unchanged while the alternate folder is off, so moving this one moves all four
    /// consistently and leaves the game's own naming intact.
    ///
    /// Nothing here ever deletes a save. A slot that stops being referenced is left on disk.
    /// </summary>
    internal static class Slots
    {
        /// <summary>The slot being read and written. Empty means the game's own single file.</summary>
        internal static string Active { get; private set; } = string.Empty;

        /// <summary>
        /// Set while a run is in progress, from the party itself.
        ///
        /// While playing, the party is the authority on which slot to write - not whatever was
        /// picked in a menu beforehand. Without this, choosing the wrong slot and then loading a
        /// different run would save one run over another's file.
        /// </summary>
        internal static bool LockedToParty { get; private set; }

        internal static string Folder => FileSystem.persistentDataPath;

        internal static string VanillaPath => Path.Combine(Folder, "QuestSaveRoguelike.json");

        internal static string PathFor(string slot)
        {
            return string.IsNullOrEmpty(slot)
                ? VanillaPath
                : Path.Combine(Folder, "QuestSaveRoguelike." + slot + ".json");
        }

        /// <summary>
        /// Points the game at a slot. Re-reads the save so the menu's Continue reflects it, since
        /// the quest file is read once and kept in memory.
        /// </summary>
        internal static void Activate(string slot, bool fromParty)
        {
            if (LockedToParty && !fromParty)
                return;                     // a run is being played; its own slot wins

            bool changed = !string.Equals(Active, slot ?? string.Empty, StringComparison.Ordinal);

            Active = slot ?? string.Empty;
            LockedToParty = fromParty;

            if (!changed)
                return;

            try
            {
                GlobalSaveData.LoadRoguelikeQuest();
                Plugin.Log.LogInfo("Roguelike save slot is now " + Describe(Active) + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not load slot " + Describe(Active) + ": " + e.Message);
            }
        }

        internal static void ReleasePartyLock() => LockedToParty = false;

        internal static string Describe(string slot) =>
            string.IsNullOrEmpty(slot) ? "the original single save" : slot;

        /// <summary>
        /// A slot name for a party: stable whoever is listed first, and the same every session.
        ///
        /// Derived from the characters rather than being a counter, so the same group resuming
        /// next week lands on the same file without anyone having to remember a number.
        /// </summary>
        internal static string SlotFor(IEnumerable<Character> party)
        {
            List<string> guids = party
                .Where(x => x != null && !string.IsNullOrEmpty(x.Guid))
                .Select(x => x.Guid)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            if (guids.Count == 0)
                return null;

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", guids.ToArray())));

                var text = new StringBuilder(8);
                for (int i = 0; i < 4; i++)
                    text.Append(hash[i].ToString("x2"));

                return text.ToString();
            }
        }

        /// <summary>Every slot file on disk, newest first.</summary>
        internal static List<string> Existing()
        {
            var found = new List<string>();

            try
            {
                if (!Directory.Exists(Folder))
                    return found;

                foreach (string path in Directory.GetFiles(Folder, "QuestSaveRoguelike.*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);     // QuestSaveRoguelike.<slot>
                    int dot = name.IndexOf('.');

                    if (dot > 0 && dot < name.Length - 1)
                        found.Add(name.Substring(dot + 1));
                }

                found = found
                    .OrderByDescending(x => File.GetLastWriteTimeUtc(PathFor(x)))
                    .ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not list the save slots: " + e.Message);
            }

            return found;
        }

        internal static DateTime WrittenAt(string slot)
        {
            try
            {
                string path = PathFor(slot);
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Copies the game's single save into a slot the first time, so a run already in progress
        /// is not stranded by this mod being installed. Copied, never moved - if any of this is
        /// wrong, the original is still exactly where the game left it.
        /// </summary>
        internal static void MigrateIfNeeded()
        {
            try
            {
                if (!File.Exists(VanillaPath) || Existing().Count > 0)
                    return;

                string slot = "carried-over";
                string target = PathFor(slot);

                if (File.Exists(target))
                    return;

                File.Copy(VanillaPath, target);
                Plugin.Log.LogInfo("Copied the existing roguelike save into slot '" + slot
                                   + "'. The original file was left untouched.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not carry the existing save over: " + e.Message);
            }
        }

        /// <summary>Picks up whichever slot was played most recently, for the menu's Continue.</summary>
        internal static void ActivateMostRecent()
        {
            List<string> slots = Existing();
            if (slots.Count > 0)
                Activate(slots[0], fromParty: false);
        }
    }

    /// <summary>
    /// The one redirect. Everything the game writes for a roguelike quest hangs off this property.
    /// </summary>
    [HarmonyPatch(typeof(GlobalSaveData), nameof(GlobalSaveData.FilenameRoguelikeQuest), MethodType.Getter)]
    internal static class QuestPathPatch
    {
        [HarmonyPostfix]
        private static void Redirect(ref string __result)
        {
            try
            {
                if (!ModConfig.Enabled.Value || string.IsNullOrEmpty(Slots.Active))
                    return;

                __result = Slots.PathFor(Slots.Active);
            }
            catch (Exception)
            {
                // Leaving the vanilla path is always safe; it is where the game would have looked.
            }
        }
    }
}
