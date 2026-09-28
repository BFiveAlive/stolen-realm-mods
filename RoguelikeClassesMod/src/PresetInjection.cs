using System;
using System.Collections.Generic;
using System.Linq;
using Burst2Flame;
using HarmonyLib;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Puts the built presets where the game looks for them.
    ///
    /// <c>Game.CharacterPresetFiles</c> is a lazily populated cache over
    /// <c>Resources.LoadAll&lt;CharacterPresetFile&gt;("Character Preset Files")</c>, and every
    /// consumer - the picker, <c>GetCharacterPresetFiles</c>, the guid lookup - reads that
    /// property rather than holding its own copy. Writing an enlarged array back into the cache
    /// on the first call is therefore enough: nothing else needs patching, and the injection
    /// happens exactly when the game first wants the list rather than on a timer.
    ///
    /// The postfix is idempotent. Once the array contains our presets it does nothing, so the
    /// cost after the first call is one boolean check.
    /// </summary>
    [HarmonyPatch]
    internal static class PresetInjection
    {
        private static readonly List<CharacterPresetFile> Injected = new List<CharacterPresetFile>();

        private static bool building;
        private static bool failed;

        internal static int Count => Injected.Count;

        internal static bool IsOurs(CharacterPresetFile preset)
        {
            return preset != null && preset.name != null && preset.name.StartsWith(PresetFactory.NamePrefix, StringComparison.Ordinal);
        }

        [HarmonyPatch(typeof(Burst2Flame.Game), nameof(Burst2Flame.Game.CharacterPresetFiles), MethodType.Getter)]
        [HarmonyPostfix]
        private static void AppendOurs(Burst2Flame.Game __instance, ref CharacterPresetFile[] __result)
        {
            // Building a preset needs an appearance donor, which reads this same property. Without
            // this guard that re-entry would recurse until the stack gave out.
            if (building || failed || __result == null)
                return;

            if (Injected.Count > 0 && __result.Length > 0 && Array.IndexOf(__result, Injected[0]) >= 0)
                return;

            try
            {
                building = true;
                Inject(__instance, ref __result);
            }
            catch (Exception e)
            {
                failed = true;
                Plugin.Log.LogError($"Could not add the modded classes; the game keeps its own list untouched: {e}");
            }
            finally
            {
                building = false;
            }
        }

        private static void Inject(Burst2Flame.Game game, ref CharacterPresetFile[] result)
        {
            if (Injected.Count == 0)
            {
                if (!GameData.Ready)
                    return;                     // skills not loaded yet; the next call will retry

                Injected.AddRange(ClassLibrary.BuildAll());
                if (Injected.Count == 0)
                {
                    failed = true;              // nothing to add and nothing to retry for
                    return;
                }
            }

            CharacterPresetFile[] combined = result.Concat(Injected).ToArray();

            // Write it back into the cache so every later reader sees the same array rather than
            // this call allocating a fresh one each time.
            AccessTools.Field(typeof(Burst2Flame.Game), "_CharacterPresetFiles")?.SetValue(game, combined);

            // The guid lookup builds its dictionary from the array once and keeps it. Dropping it
            // makes the next lookup rebuild, which is what lets a saved character resolve back to
            // the modded class it was created from.
            AccessTools.Field(typeof(Burst2Flame.Game), "_CharacterPresetFile_Dict")?.SetValue(game, null);

            result = combined;

            Plugin.Log.LogInfo($"Added {Injected.Count} roguelike classes; the picker now lists " +
                               $"{combined.Count(x => x != null && x.PresetType == GameMode.Roguelike)} in total.");
        }
    }
}
