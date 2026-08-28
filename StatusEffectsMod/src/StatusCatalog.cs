using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Burst2Flame;

namespace StatusEffectsMod
{
    /// <summary>
    /// One status the mod is prepared to edit, with the config key it answers to and the
    /// shipped values it started from.
    /// </summary>
    internal sealed class StatusEntry
    {
        public ActionStatusInfo Status;

        /// <summary>Unity asset name, e.g. "GL-InnerWarmth". Unique, but not friendly.</summary>
        public string AssetName;

        /// <summary>In-game display name, e.g. "Inner Warmth". Friendly, but not always unique.</summary>
        public string DisplayName;

        /// <summary>The key this status is bound under in the config file.</summary>
        public string ConfigKey;

        public StatusSnapshot Original;
    }

    /// <summary>
    /// Reads the game's master status table and turns it into a stable, human-readable list.
    ///
    /// Statuses are ScriptableObjects in the game's asset bundles rather than anything declared
    /// in Assembly-CSharp, so the only honest way to enumerate them is to walk the live list the
    /// game itself uses.
    /// </summary>
    internal static class StatusCatalog
    {
        public static List<StatusEntry> Entries { get; private set; }

        public static bool Loaded => Entries != null;

        /// <summary>
        /// Builds the catalog once the game's data is available. Returns false while the master
        /// list is still empty, so the caller can simply try again next frame.
        /// </summary>
        public static bool TryLoad()
        {
            if (Loaded)
                return true;

            List<ActionStatusInfo> statuses = SafeStatuses();
            if (statuses == null || statuses.Count == 0)
                return false;

            var entries = new List<StatusEntry>();
            var seenAssets = new HashSet<ActionStatusInfo>();

            foreach (ActionStatusInfo status in statuses)
            {
                if (status == null || !seenAssets.Add(status))
                    continue;

                if (!ShouldBind(status))
                    continue;

                entries.Add(new StatusEntry
                {
                    Status = status,
                    AssetName = SafeAssetName(status),
                    DisplayName = SafeDisplayName(status),
                    Original = new StatusSnapshot(status),
                });
            }

            AssignConfigKeys(entries);

            Entries = entries
                .OrderBy(e => e.ConfigKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return true;
        }

        private static List<ActionStatusInfo> SafeStatuses()
        {
            try
            {
                return Burst2Flame.Game.Instance?.ActionStatuses;
            }
            catch (Exception)
            {
                // Game.Instance touches Unity objects that may not exist this early.
                return null;
            }
        }

        /// <summary>
        /// Fortunes, quest items and quest statuses share the ActionStatusInfo type but are
        /// content rather than combat mechanics, and there are a great many of them. Binding
        /// them by default would bury the statuses people actually want to tune, so they are
        /// opt-in.
        /// </summary>
        private static bool ShouldBind(ActionStatusInfo status)
        {
            if (status.StatusType == StatusType.Normal)
                return true;

            return ModConfig.IncludeFortunesAndQuestStatuses.Value;
        }

        private static string SafeAssetName(ActionStatusInfo status)
        {
            try
            {
                return status.name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string SafeDisplayName(ActionStatusInfo status)
        {
            try
            {
                return status.Name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Prefers the display name, because that is what the player sees on the status bar and
        /// in tooltips. Falls back to the asset name when the display name is missing or
        /// already taken, so every entry ends up with exactly one unambiguous key.
        /// </summary>
        private static void AssignConfigKeys(List<StatusEntry> entries)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Two passes: every unique display name is claimed before any fallback runs, so a
            // collision demotes only the colliding entries rather than whichever came first.
            var displayNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (StatusEntry entry in entries)
            {
                string candidate = Sanitise(entry.DisplayName);
                if (candidate.Length == 0)
                    continue;

                displayNameCounts.TryGetValue(candidate, out int count);
                displayNameCounts[candidate] = count + 1;
            }

            foreach (StatusEntry entry in entries)
            {
                string display = Sanitise(entry.DisplayName);
                string asset = Sanitise(entry.AssetName);

                string key = display;
                if (key.Length == 0 || displayNameCounts[key] > 1)
                    key = asset.Length > 0 ? asset : display;

                if (key.Length == 0)
                    key = "Unnamed";

                string unique = key;
                for (int suffix = 2; !used.Add(unique); suffix++)
                    unique = $"{key} #{suffix}";

                entry.ConfigKey = unique;
            }
        }

        /// <summary>
        /// Characters BepInEx refuses in a section or key name. Its own message lists them as
        /// <c>= \n \t \ " ' [ ]</c>, and it throws rather than escaping them; carriage return is
        /// added here for the same reason it excludes newline.
        ///
        /// The apostrophe is the one that matters in practice - "Berserker's Rage" and
        /// "Bounty Hunter's Mark" are real status names.
        /// </summary>
        private static readonly char[] ForbiddenInConfigKeys =
            { '=', '\n', '\r', '\t', '\\', '"', '\'', '[', ']' };

        /// <summary>
        /// Strips the characters BepInEx will not accept in a key. Everything else - spaces,
        /// colons, ampersands - is left alone so keys stay readable.
        /// </summary>
        private static string Sanitise(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (Array.IndexOf(ForbiddenInConfigKeys, c) >= 0)
                    continue;

                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// A one-line summary of what the game shipped for this status, used as the config
        /// entry's description. Editing is far easier when the vanilla value is sitting right
        /// above the line you are changing.
        /// </summary>
        public static string DescribeVanilla(StatusEntry entry)
        {
            var parts = new List<string>();

            StatusSnapshot original = entry.Original;

            parts.Add(original.Infinite
                ? "duration infinite"
                : $"duration {(string.IsNullOrEmpty(original.Duration) ? "unset" : original.Duration)}");

            if (original.MaxStacks > 0f && original.MaxStacks < 100f)
                parts.Add($"maxStacks {Num(original.MaxStacks)}");

            parts.Add($"stackType {original.StackType}");

            if (original.IsAura)
                parts.Add($"aura radius {original.AuraRadius}");

            string attributes = DescribeAttributes(entry);
            if (attributes.Length > 0)
                parts.Add($"affects {attributes}");

            string identity = entry.ConfigKey.Equals(entry.AssetName, StringComparison.OrdinalIgnoreCase)
                ? entry.DisplayName
                : entry.AssetName;

            string prefix = string.IsNullOrEmpty(identity) ? string.Empty : $"[{identity}] ";

            return prefix + "Vanilla: " + string.Join(", ", parts) + ".";
        }

        private static string DescribeAttributes(StatusEntry entry)
        {
            CharacterEffectInfo[] effects = entry.Status.AttributeEffects;
            if (effects == null || effects.Length == 0)
                return string.Empty;

            var names = new List<string>();
            foreach (CharacterEffectInfo effect in effects)
            {
                string name = AttributeName(effect);
                if (name.Length > 0 && !names.Contains(name))
                    names.Add(name);
            }

            // Long attribute lists would swamp the description; the JSON dump has the full set.
            if (names.Count > 6)
                return string.Join(", ", names.Take(6)) + $", +{names.Count - 6} more";

            return string.Join(", ", names);
        }

        public static string AttributeName(CharacterEffectInfo effect)
        {
            if (effect == null)
                return string.Empty;

            try
            {
                CharacterAttribute attribute = effect.CharacterAttribute;
                if (attribute == null)
                    return string.Empty;

                return attribute.name ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
