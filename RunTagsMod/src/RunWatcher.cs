using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace RunTagsMod
{
    /// <summary>
    /// Notes who is playing, while they are playing.
    ///
    /// Everything needed is readable during a session and gone afterwards: the lobby knows its
    /// owner and members, and the party knows which characters are in it. Sampled every few
    /// seconds rather than hooked to an event, because what matters is only that it was true at
    /// some point during the run - and someone joining halfway through should be picked up too.
    /// </summary>
    internal static class RunWatcher
    {
        private static float nextSampleAt;

        internal static void Tick()
        {
            if (!ModConfig.Enabled.Value || Time.realtimeSinceStartup < nextSampleAt)
                return;

            nextSampleAt = Time.realtimeSinceStartup + 5f;

            try
            {
                Sample();
                TagStore.SaveIfDirty();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not note who is playing: " + e);
            }
        }

        private static void Sample()
        {
            Root root = NetworkingManager.Instance?.NetworkManager?.Root;
            if (root == null || !root.PlayingRoguelike)
                return;

            List<Character> party = NetworkingManager.Instance.PartyCharacters?
                .Where(x => x != null && x.IsRoguelikeCharacter && !string.IsNullOrEmpty(x.Guid))
                .ToList();

            if (party == null || party.Count == 0)
                return;

            string host = HostName();
            List<string> players = PlayerNames(host);

            foreach (Character character in party)
            {
                TagStore.Record(character.Guid, new RunTag
                {
                    Difficulty = root.CurrentDifficultyIndex,
                    Host = host,
                    Players = players,
                    UpdatedUtc = DateTime.UtcNow.ToString("o"),
                });
            }
        }

        /// <summary>
        /// Whoever owns the lobby. Playing alone there is no lobby at all, so the answer is the
        /// only person here.
        /// </summary>
        private static string HostName()
        {
            try
            {
                Lobby? lobby = SteamManager.instance?.CurrentLobby;

                if (lobby.HasValue)
                {
                    string owner = lobby.Value.Owner.Name;
                    if (!string.IsNullOrEmpty(owner))
                        return owner;
                }

                return SteamClient.IsValid ? SteamClient.Name : null;
            }
            catch (Exception)
            {
                // No Steam, or an offline session. The label falls back to the difficulty alone.
                return null;
            }
        }

        /// <summary>Everyone in the lobby but the host, in a stable order.</summary>
        private static List<string> PlayerNames(string host)
        {
            var names = new List<string>();

            try
            {
                Lobby? lobby = SteamManager.instance?.CurrentLobby;
                if (!lobby.HasValue)
                    return names;

                foreach (Friend member in lobby.Value.Members)
                {
                    string name = member.Name;

                    if (string.IsNullOrEmpty(name) ||
                        string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!names.Contains(name))
                        names.Add(name);
                }
            }
            catch (Exception)
            {
                // Members can be unavailable mid-join; the next sample picks them up.
            }

            return names;
        }
    }
}
