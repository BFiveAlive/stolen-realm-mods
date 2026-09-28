using System;

namespace RoguelikeClassesMod
{
    /// <summary>
    /// Turns "unlocks at Endless 6" into the condition the game already knows how to evaluate.
    ///
    /// <c>CharacterPresetFile.IsUnlocked</c> tests each <c>RoguelikePresetUnlockCondition</c>
    /// against <c>GlobalSaveData.Instance.GlobalStats</c>, a plain string-to-float dictionary that
    /// is part of the save. The only key the game writes itself is
    /// <c>HighestRoguelikeDifficultyUnlocked</c>, set by <c>UnlockRoguelikeDifficulty</c> after a
    /// winning run to <c>CurrentDifficultyIndex + 1</c>.
    ///
    /// That write is *not* clamped to the authored difficulty list - the 1.2.8 compatibility
    /// clamping happens on the separate legacy save key - so endless indices land in it intact,
    /// which is what makes an endless-gated unlock possible without the mod tracking anything of
    /// its own.
    ///
    /// Endless numbering comes from <c>DifficultySettings.CreateEndlessRoguelikeDifficulty</c>,
    /// where a generated difficulty at array index <c>i</c> is labelled Endless
    /// <c>i - (authored - 1)</c>. Inverting that gives the index below. It is resolved at runtime
    /// rather than hardcoded, so an update that authors another difficulty does not silently move
    /// every unlock by one.
    /// </summary>
    internal static class UnlockRules
    {
        internal const string DifficultyStat = "HighestRoguelikeDifficultyUnlocked";

        /// <summary>The value <see cref="DifficultyStat"/> must reach for the given endless level.</summary>
        internal static float RequiredValue(int endlessLevel)
        {
            return AuthoredRoguelikeCount() - 1 + Math.Max(0, endlessLevel);
        }

        internal static string DefaultDescription(int endlessLevel)
        {
            return "Reach Endless " + Roman(endlessLevel);
        }

        /// <summary>
        /// An authored difficulty needs no arithmetic: <c>UnlockRoguelikeDifficulty</c> stores the
        /// index directly, and the shipped classes gate on 1 to 5 exactly as written.
        /// </summary>
        internal static float RequiredValueForDifficulty(int difficulty)
        {
            return Math.Max(0, difficulty);
        }

        /// <summary>Worded as the shipped classes word it, so the two ladders read as one.</summary>
        internal static string DefaultDifficultyDescription(int difficulty)
        {
            return "Complete Difficulty " + Roman(difficulty);
        }

        private static int AuthoredRoguelikeCount()
        {
            try
            {
                var settings = GlobalSettingsManager.instance?.difficultySettings;
                if (settings?.DifficultiesRoguelike != null && settings.DifficultiesRoguelike.Count > 0)
                    return settings.DifficultiesRoguelike.Count;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read the authored difficulty list: {e.Message}");
            }

            // Not a guess at the real number so much as a value that keeps an endless-gated class
            // locked rather than accidentally free if the settings are not loaded yet.
            Plugin.Log.LogWarning("Difficulty settings unavailable; endless unlocks fall back to a conservative threshold.");
            return 99;
        }

        /// <summary>Matches the game's own numeral style on the difficulty plates.</summary>
        private static string Roman(int number)
        {
            if (number <= 0)
                return number.ToString();

            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] numerals = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                while (number >= values[i])
                {
                    text.Append(numerals[i]);
                    number -= values[i];
                }
            }

            return text.ToString();
        }
    }
}
