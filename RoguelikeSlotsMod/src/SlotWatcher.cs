using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeSlotsMod
{
    /// <summary>
    /// Keeps the active slot pointed at whatever is actually being played.
    ///
    /// While a roguelike party exists, the party decides the slot. That is the safety property
    /// this whole mod rests on: a slot chosen in a menu is only a suggestion about what to load,
    /// and the moment a real run is underway the file being written is derived from the characters
    /// in it. Picking the wrong slot beforehand can therefore load the wrong run, but it cannot
    /// save one run over another's file.
    /// </summary>
    internal static class SlotWatcher
    {
        private static float nextSampleAt;
        private static bool started;

        internal static void Tick()
        {
            if (!ModConfig.Enabled.Value)
                return;

            if (!started)
            {
                started = true;
                Slots.MigrateIfNeeded();
                Slots.ActivateMostRecent();
            }

            if (Time.realtimeSinceStartup < nextSampleAt)
                return;

            nextSampleAt = Time.realtimeSinceStartup + 3f;

            try
            {
                Sample();
                SlotIndex.SaveIfDirty();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not follow the active run: " + e);
            }
        }

        private static void Sample()
        {
            Root root = NetworkingManager.Instance?.NetworkManager?.Root;

            List<Character> party = root == null || !root.PlayingRoguelike
                ? null
                : NetworkingManager.Instance.PartyCharacters?
                    .Where(x => x != null && x.IsRoguelikeCharacter && !string.IsNullOrEmpty(x.Guid))
                    .ToList();

            if (party == null || party.Count == 0)
            {
                // Back at the menu. The slot stays where it is so Continue still points at the
                // last run, but it is no longer pinned, so it can be changed again.
                Slots.ReleasePartyLock();
                return;
            }

            string slot = Slots.SlotFor(party);
            if (string.IsNullOrEmpty(slot))
                return;

            Slots.Activate(slot, fromParty: true);

            SlotIndex.Record(slot, new SlotInfo
            {
                Characters = party.Select(x => x.CharacterName).Where(x => !string.IsNullOrEmpty(x)).ToList(),
                Difficulty = root.CurrentDifficultyIndex,
                Level = Level(root),
                LastPlayedUtc = DateTime.UtcNow.ToString("o"),
            });
        }

        private static int Level(Root root)
        {
            try
            {
                return root.ObservableQuestData?.CurrentRoguelikeLevelIndex ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
