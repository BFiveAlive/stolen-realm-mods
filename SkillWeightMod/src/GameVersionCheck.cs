using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace SkillWeightMod
{
    /// <summary>
    /// Warns when the game's own GetSkillChoices has changed since this mod last mirrored it.
    ///
    /// GetSkillChoicesPatch is a prefix that returns false, so the vanilla body never runs. A
    /// game update that adds logic there is therefore skipped silently - no error, no log line,
    /// the mod simply goes on using the old rules. The September 2026 update added skill-tree
    /// blocking to that method and the mod quietly overrode it until someone noticed in play.
    ///
    /// This fingerprints the vanilla IL at startup, before Harmony patches anything, and logs a
    /// loud warning on mismatch. It does not disable the mod: a changed fingerprint means
    /// "review needed", not necessarily "broken" - a recompile can alter the IL without
    /// changing behaviour.
    ///
    /// To re-baseline after mirroring a new game version, run tools/ilhash against the updated
    /// Assembly-CSharp.dll, or copy the "actual" hash from the warning into ExpectedHash.
    /// </summary>
    internal static class GameVersionCheck
    {
        // Assembly-CSharp.dll dated 2026-09-10. RoguelikeManager.GetSkillChoices(numOptions,
        // level, alreadyObtained, forcedTier, character), 887 bytes of IL.
        private const string ExpectedHash = "2E30D18FEDFEDA884F033585B98F37517C4E2597BEB67E23291636488B0B5123";

        private static readonly string[] ExpectedParameters =
            { "numOptions", "level", "alreadyObtained", "forcedTier", "character" };

        /// <summary>Call before PatchAll, so the IL read is the game's own and not a detour.</summary>
        public static void Run()
        {
            try
            {
                MethodInfo method = AccessTools.Method(typeof(RoguelikeManager), "GetSkillChoices");
                if (method == null)
                {
                    Plugin.Log.LogError(
                        "RoguelikeManager.GetSkillChoices no longer exists. The weighting patch cannot " +
                        "bind; skill rolls will be vanilla. The mod needs updating for this game version.");
                    return;
                }

                // Resolve now so the log says plainly whether blocked trees will be honoured,
                // rather than leaving it to be inferred from the absence of a warning.
                if (TreeBlocking.Supported)
                    Plugin.Log.LogInfo("Skill-tree blocking detected; blocked trees will be respected.");

                string[] actualParameters = method.GetParameters().Select(p => p.Name).ToArray();
                bool signatureMatches = actualParameters.SequenceEqual(ExpectedParameters);

                byte[] il = method.GetMethodBody()?.GetILAsByteArray();
                string actualHash = il == null ? "(no body)" : Hex(SHA256.Create().ComputeHash(il));
                bool bodyMatches = string.Equals(actualHash, ExpectedHash, StringComparison.OrdinalIgnoreCase);

                if (signatureMatches && bodyMatches)
                {
                    Plugin.Log.LogInfo("GetSkillChoices matches the version this mod mirrors.");
                    return;
                }

                // An OLDER build is a supported, expected case - the mod runs on both. Say so
                // plainly instead of alarming anyone still on the stable branch.
                if (!TreeBlocking.Supported && !actualParameters.Contains("character"))
                {
                    Plugin.Log.LogInfo(
                        "Running on a game build from before skill-tree blocking was added. " +
                        "That feature is skipped; everything else works normally.");
                    return;
                }

                Plugin.Log.LogWarning(
                    "=====================================================================\n" +
                    "  The game's skill-roll code (RoguelikeManager.GetSkillChoices) has\n" +
                    "  CHANGED since this mod was written. The mod replaces that method, so\n" +
                    "  any new rule the game added there is being SKIPPED until the mod is\n" +
                    "  updated to mirror it. Rolls still work, but may ignore new game options.\n" +
                    (signatureMatches ? "" :
                    $"  Parameters now: ({string.Join(", ", actualParameters)})\n" +
                    $"  Expected:       ({string.Join(", ", ExpectedParameters)})\n") +
                    $"  IL sha256 now: {actualHash}\n" +
                    $"  Expected:      {ExpectedHash}\n" +
                    "  Set SynergyStrength = 0 and OfferedDecay = 1 in the config to hand the\n" +
                    "  roll back to the game entirely until then.\n" +
                    "=====================================================================");
            }
            catch (Exception e)
            {
                // A failing self-check must never take the mod down with it.
                Plugin.Log.LogWarning($"Could not verify the game version: {e.Message}");
            }
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }
    }
}
