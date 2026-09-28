using System;
using System.Collections.Generic;
using System.Reflection;
using Burst2Flame;
using HarmonyLib;

namespace SkillWeightMod
{
    /// <summary>
    /// Reads the skill trees a player has blocked for a character, via reflection.
    ///
    /// Blocking arrived in the September 2026 update. Calling
    /// <c>RoguelikeSkillTreeRemoval.GetValidatedExclusions</c> directly would bake a hard type
    /// reference into the mod, and on an older game build the JIT would fail to resolve it the
    /// first time the pool is built — the weighted roll would then throw on every single roll and
    /// fall back to vanilla, silently, forever. Going through reflection makes the feature simply
    /// absent on older builds instead, which is the correct behaviour there anyway.
    ///
    /// Resolution is attempted once and cached either way.
    /// </summary>
    internal static class TreeBlocking
    {
        private static readonly List<SkillType> None = new List<SkillType>();

        private static bool resolved;
        private static MethodInfo getValidatedExclusions;

        /// <summary>True when the running game has skill-tree blocking.</summary>
        public static bool Supported
        {
            get
            {
                Resolve();
                return getValidatedExclusions != null;
            }
        }

        /// <summary>
        /// The trees blocked for this character, or an empty list on a game build without the
        /// feature. Never null, and never throws.
        /// </summary>
        public static List<SkillType> For(Character character)
        {
            Resolve();

            if (getValidatedExclusions == null || character == null)
                return None;

            try
            {
                return getValidatedExclusions.Invoke(null, new object[] { character })
                       as List<SkillType> ?? None;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Could not read blocked skill trees; treating none as blocked: {e.Message}");
                return None;
            }
        }

        private static void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            try
            {
                Type type = AccessTools.TypeByName("RoguelikeSkillTreeRemoval");
                if (type == null)
                {
                    Plugin.Log.LogInfo(
                        "This game build has no skill-tree blocking; that part of the roll is skipped.");
                    return;
                }

                getValidatedExclusions = AccessTools.Method(type, "GetValidatedExclusions",
                                                            new[] { typeof(Character) });

                if (getValidatedExclusions == null)
                    Plugin.Log.LogWarning(
                        "RoguelikeSkillTreeRemoval exists but GetValidatedExclusions(Character) does " +
                        "not. Blocked skill trees will NOT be respected - the mod needs updating.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not resolve skill-tree blocking: {e.Message}");
            }
        }
    }
}
