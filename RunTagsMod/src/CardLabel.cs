using System;
using Burst2Flame;
using HarmonyLib;

namespace RunTagsMod
{
    /// <summary>
    /// Replaces "Roguelike" on a character card with the difficulty, the host and who played.
    ///
    /// The game sets that label to one of two fixed words:
    ///
    /// <code>
    /// gameModeText.text = Character.IsRoguelikeCharacter ? Localize("Roguelike") : Localize("Campaign");
    /// </code>
    ///
    /// which tells you nothing you cannot already see - every roguelike character says the same
    /// thing. The space is the right place for what actually differs between them.
    ///
    /// A postfix, so the game writes its label first and this only overwrites it when there is
    /// something better to say. A character never played under this mod keeps the original word
    /// rather than going blank.
    /// </summary>
    [HarmonyPatch(typeof(CharacterChoiceItem), "UpdateCharacterUI")]
    internal static class CardLabel
    {
        [HarmonyPostfix]
        private static void Relabel(CharacterChoiceItem __instance)
        {
            try
            {
                if (!ModConfig.Enabled.Value || __instance == null || __instance.gameModeText == null)
                    return;

                Character character = __instance.Character;
                if (character == null || !character.IsRoguelikeCharacter || string.IsNullOrEmpty(character.Guid))
                    return;

                RunTag tag = TagStore.For(character.Guid);
                if (tag == null)
                    return;                     // never played under this mod; leave it alone

                string label = LabelFitter.Build(tag.Difficulty, tag.Host, tag.Players,
                    ModConfig.LabelBudget.Value, ModConfig.ItalicHost.Value);

                if (string.IsNullOrEmpty(label))
                    return;

                // Rich text has to be on for the host's italics, and it is off on some of these
                // labels by default.
                if (ModConfig.ItalicHost.Value)
                    __instance.gameModeText.richText = true;

                __instance.gameModeText.text = label;
            }
            catch (Exception e)
            {
                // This runs while the selection screen draws; a label is never worth breaking it.
                Plugin.Log.LogWarning("Could not relabel a character card: " + e.Message);
            }
        }
    }
}
