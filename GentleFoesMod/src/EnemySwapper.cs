using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;

namespace GentleFoesMod
{
    /// <summary>
    /// Points an enemy at a different model, and gives it a different name on screen.
    ///
    /// The model is the easy half. <c>CharacterInfo</c> does not hold a prefab, it holds a list of
    /// Resources paths that it loads on demand, so borrowing another enemy's look is copying its
    /// strings. Nothing else about the enemy changes: same stats, same skills, same loot, same
    /// difficulty. Only the thing you look at.
    ///
    /// Two details in the game's code shape how this has to be done:
    ///
    /// - <b>The list must stay the same length.</b> The host picks a prefab *index* and each
    ///   machine resolves it against its own copy of this list. One of the two places that read it
    ///   does not bounds-check, so a shorter list would throw on whatever index the host chose.
    ///   Donor paths are therefore dealt out to fill exactly as many slots as the original had.
    /// - <b>The cache has to be cleared by hand.</b> The lazy getter only reloads when the loaded
    ///   count differs from the string count, and a same-length swap leaves that equal, so it
    ///   would keep serving the bear it had already loaded.
    /// </summary>
    internal static class EnemySwapper
    {
        /// <summary>Display names, keyed by the name the game gives the enemy.</summary>
        private static readonly Dictionary<string, string> Renames =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal static bool TryRename(string original, out string replacement)
        {
            replacement = null;
            return original != null && Renames.TryGetValue(original, out replacement);
        }

        /// <summary>
        /// Each enemy's models as the game shipped them, taken before anything is changed.
        ///
        /// Donors are read from here rather than from the live data so that the result does not
        /// depend on the order the rules happen to run in: swapping bears to goblins and goblins
        /// to skeletons in the same file gives bears goblins either way, not skeletons.
        /// </summary>
        private static readonly Dictionary<CharacterInfo, List<string>> Original =
            new Dictionary<CharacterInfo, List<string>>();

        internal static int ModelsChanged { get; private set; }
        internal static int NamesChanged { get; private set; }

        internal static void Apply()
        {
            List<CharacterInfo> all = AllEnemies();
            if (all.Count == 0)
            {
                Plugin.Log.LogWarning("The game's enemy list is empty, so nothing was swapped.");
                return;
            }

            Original.Clear();
            foreach (CharacterInfo info in all)
                Original[info] = Paths(info);

            SwapLibraryFile library = SwapStore.Library;
            var problems = new List<string>();

            foreach (GroupSwapDefinition group in library.GroupSwaps ?? new List<GroupSwapDefinition>())
                ApplyGroup(group, all, problems);

            // After the group rules, so a single enemy can be given a different treatment from
            // the rest of its faction.
            foreach (SwapDefinition swap in library.Swaps ?? new List<SwapDefinition>())
                ApplyOne(swap, all, problems);

            foreach (string problem in problems)
                Plugin.Log.LogWarning("  " + problem);

            Plugin.Log.LogInfo("Gentle Foes: " + ModelsChanged + " model(s) and " + NamesChanged
                               + " name(s) changed, " + problems.Count + " entr(ies) skipped.");
        }

        private static void ApplyOne(SwapDefinition swap, List<CharacterInfo> all, List<string> problems)
        {
            if (swap == null || string.IsNullOrEmpty(swap.Enemy))
                return;

            CharacterInfo target = Find(all, swap.Enemy);
            if (target == null)
            {
                problems.Add("no enemy called '" + swap.Enemy + "'");
                return;
            }

            if (!string.IsNullOrEmpty(swap.LookLike))
            {
                CharacterInfo donor = Find(all, swap.LookLike);
                if (donor == null)
                    problems.Add("no enemy called '" + swap.LookLike + "' to borrow a look from");
                else
                    Reskin(target, OriginalPaths(donor), problems);
            }

            if (!string.IsNullOrEmpty(swap.Name))
            {
                Renames[target.name] = swap.Name;
                NamesChanged++;
            }
        }

        private static void ApplyGroup(GroupSwapDefinition group, List<CharacterInfo> all, List<string> problems)
        {
            if (group == null || string.IsNullOrEmpty(group.Group) || string.IsNullOrEmpty(group.LookLikeGroup))
                return;

            if (!TryGroup(group.Group, out EnemyGroup from) || !TryGroup(group.LookLikeGroup, out EnemyGroup to))
            {
                problems.Add("'" + group.Group + "' or '" + group.LookLikeGroup + "' is not an enemy group");
                return;
            }

            List<CharacterInfo> targets = all.Where(x => x.EnemyGroup == from).ToList();
            List<CharacterInfo> donors = all.Where(x => x.EnemyGroup == to && OriginalPaths(x).Count > 0).ToList();

            if (donors.Count == 0)
            {
                problems.Add("nothing in " + group.LookLikeGroup + " has a model to lend");
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                // Dealt round-robin rather than all taking the first donor: a forest of bears
                // should not become a forest of one identical goblin.
                Reskin(targets[i], OriginalPaths(donors[i % donors.Count]), problems);

                if (!string.IsNullOrEmpty(group.Name))
                {
                    Renames[targets[i].name] = group.Name;
                    NamesChanged++;
                }
            }
        }

        /// <summary>Fills the target's model slots from the donor's, keeping the slot count.</summary>
        private static void Reskin(CharacterInfo target, List<string> donorPaths, List<string> problems)
        {
            List<string> original = OriginalPaths(target);

            if (original.Count == 0)
            {
                problems.Add(target.name + " has no model slots to fill");
                return;
            }

            if (donorPaths.Count == 0)
            {
                problems.Add("the donor for " + target.name + " has no model to lend");
                return;
            }

            var replacement = new List<string>(original.Count);
            for (int i = 0; i < original.Count; i++)
                replacement.Add(donorPaths[i % donorPaths.Count]);

            target.PossibleCharacterPrefabsResourceStrings = replacement;

            // Drops anything already loaded. Without this the same-length list looks unchanged to
            // the lazy getter and it keeps handing out the model it loaded before.
            target.ClearPrefabs();

            ModelsChanged++;
        }

        private static List<string> OriginalPaths(CharacterInfo info)
        {
            return info != null && Original.TryGetValue(info, out List<string> paths) ? paths : Paths(info);
        }

        private static List<string> Paths(CharacterInfo info)
        {
            return info?.PossibleCharacterPrefabsResourceStrings == null
                ? new List<string>()
                : info.PossibleCharacterPrefabsResourceStrings.Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        private static CharacterInfo Find(List<CharacterInfo> all, string name)
        {
            return all.FirstOrDefault(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGroup(string name, out EnemyGroup group)
        {
            group = default(EnemyGroup);

            foreach (EnemyGroup candidate in Enum.GetValues(typeof(EnemyGroup)))
            {
                if (!string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                group = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Every enemy the game knows. Both lists are read because the game itself reads both -
        /// Enemies for the battle pool, Characters for everything else - and a few entries appear
        /// in only one of them.
        /// </summary>
        internal static List<CharacterInfo> AllEnemies()
        {
            var all = new List<CharacterInfo>();

            if (Game.Instance == null)
                return all;

            Add(all, Game.Instance.Enemies);
            Add(all, Game.Instance.Characters);
            return all;
        }

        private static void Add(List<CharacterInfo> into, List<CharacterInfo> from)
        {
            if (from == null)
                return;

            foreach (CharacterInfo info in from)
            {
                if (info != null && !string.IsNullOrEmpty(info.name) && !into.Contains(info))
                    into.Add(info);
            }
        }
    }
}
