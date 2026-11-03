using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Burst2Flame;
using Newtonsoft.Json;

namespace GentleFoesMod
{
    /// <summary>
    /// Writes out every enemy the game has, so swaps can be chosen from a list rather than guessed.
    ///
    /// Run before the swaps are applied, so it records what each enemy originally looked like. The
    /// file is the publisher's data, not ours, so it stays on the machine that produced it and out
    /// of the repo.
    /// </summary>
    internal static class EnemyDumper
    {
        internal static string Path => System.IO.Path.Combine(SwapStore.UserFolder, "enemies-dump.json");

        internal static void Dump()
        {
            try
            {
                List<CharacterInfo> all = EnemySwapper.AllEnemies();
                if (all.Count == 0)
                    return;

                var rows = all
                    .OrderBy(x => x.EnemyGroup.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new
                    {
                        name = x.name,
                        group = x.EnemyGroup.ToString(),
                        rank = x.enemyType.ToString(),
                        levels = x.MinLevel + "-" + x.MaxLevel,
                        models = x.PossibleCharacterPrefabsResourceStrings,
                    })
                    .ToList();

                var document = new
                {
                    note = "Every enemy in the game. 'name' is what goes in swaps.json; "
                           + "'group' is the faction, which group swaps use.",
                    groups = rows.Select(x => x.group).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    count = rows.Count,
                    enemies = rows,
                };

                Directory.CreateDirectory(SwapStore.UserFolder);

                File.WriteAllText(Path,
                    JsonConvert.SerializeObject(document, Formatting.Indented) + Environment.NewLine,
                    new UTF8Encoding(false));

                Plugin.Log.LogInfo("Wrote " + rows.Count + " enemies to " + Path + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not write the enemy list: " + e.Message);
            }
        }
    }
}
