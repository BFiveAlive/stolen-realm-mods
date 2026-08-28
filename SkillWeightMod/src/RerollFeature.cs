using System;
using System.Collections.Generic;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkillWeightMod
{
    /// <summary>
    /// Adds a "Reroll" button to the roguelike skill-selection window that redraws the offered
    /// skills at the cost of a slice of the character's health and mana.
    ///
    /// The button is a clone of the window's existing Accept button, so it inherits the game's
    /// styling rather than trying to reproduce it.
    /// </summary>
    internal static class RerollFeature
    {
        private const string ButtonName = "SkillWeightMod_RerollButton";

        private static Button button;
        private static TextMeshProUGUI label;

        // Captured from the most recent PopulateSkillChoices call so a reroll can repeat it
        // with identical arguments (notably ForcedTier, which we cannot otherwise recover).
        private static Character rollCharacter;
        private static int rollLevel;
        private static List<SkillInfo> rollObtained;
        private static int rollForcedTier;
        private static bool haveRollArgs;

        private static readonly System.Reflection.MethodInfo PopulateSkillChoicesMethod =
            AccessTools.Method(typeof(RoguelikeManager), "PopulateSkillChoices");

        public static void CaptureRollArgs(Character character, int level, List<SkillInfo> alreadyObtained, int forcedTier)
        {
            rollCharacter = character;
            rollLevel = level;
            rollObtained = alreadyObtained;
            rollForcedTier = forcedTier;
            haveRollArgs = character != null;
        }

        /// <summary>Creates the button once, cloned from AcceptButton. Safe to call every frame.</summary>
        public static void EnsureButton(RoguelikeManager manager)
        {
            if (button != null || manager == null || manager.AcceptButton == null)
                return;

            // Accept's parent ("Content") is the panel's own content root, which is what we want
            // to anchor against. SkillSelectWindow is NOT it: that object spans the whole screen,
            // so anchoring to its top-right put the button in the screen corner, outside the
            // visible panel entirely.
            Transform parent = manager.AcceptButton.transform.parent;

            GameObject go = UnityEngine.Object.Instantiate(manager.AcceptButton.gameObject, parent);
            go.name = ButtonName;

            button = go.GetComponent<Button>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(go);
                Plugin.Log.LogError("Cloned Accept button had no Button component; reroll disabled.");
                return;
            }

            // RemoveAllListeners does NOT clear listeners wired up in the Unity inspector, which
            // the clone inherits from AcceptButton. Replacing the whole event object does.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => OnClick(manager));

            label = go.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
                label.richText = true;

            // Last sibling so nothing in the window paints over it.
            go.transform.SetAsLastSibling();

            // Opt out of any layout group on the parent. Testing for one with GetComponent was
            // not enough: a *disabled* LayoutGroup still returns non-null while doing no
            // layout, which left the clone sitting exactly on top of Accept.
            LayoutElement layoutElement = go.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            PositionButton(manager);

            LayoutGroup parentLayout = parent.GetComponent<LayoutGroup>();
            var parentRect = parent as RectTransform;
            Plugin.Log.LogInfo(
                "Reroll button created under '" + parent.name + "'" +
                (parentRect != null ? " rect=" + parentRect.rect.width + "x" + parentRect.rect.height : "") +
                " (parent layout group: " +
                (parentLayout == null ? "none" : parentLayout.GetType().Name + ", enabled=" + parentLayout.enabled) + ").");
        }

        /// <summary>
        /// Pins the button to the top-right corner of the skill window. Anchor, pivot and
        /// anchored position are all set to that corner, so the placement holds at any
        /// resolution or UI scale without measuring anything.
        /// </summary>
        private static void PositionButton(RoguelikeManager manager)
        {
            if (button == null || manager.AcceptButton == null)
                return;

            var acceptRect = manager.AcceptButton.GetComponent<RectTransform>();
            var rect = button.GetComponent<RectTransform>();
            if (acceptRect == null || rect == null)
                return;

            var topRight = new Vector2(1f, 1f);
            rect.anchorMin = topRight;
            rect.anchorMax = topRight;
            rect.pivot = topRight;

            rect.sizeDelta = acceptRect.sizeDelta;
            rect.localScale = acceptRect.localScale * ModConfig.RerollButtonScale.Value;

            // With the pivot in the top-right corner, moving inward means going negative on
            // both axes. The offsets stay intuitive: +X right, +Y up.
            rect.anchoredPosition = new Vector2(
                -ModConfig.RerollButtonMarginX.Value + ModConfig.RerollButtonOffsetX.Value,
                -ModConfig.RerollButtonMarginY.Value + ModConfig.RerollButtonOffsetY.Value);
        }

        /// <summary>Per-frame visibility, affordability and label refresh.</summary>
        public static void UpdateState(RoguelikeManager manager)
        {
            if (button == null)
                return;

            Character character = manager.CurrentRoguelikeSkillSelectingCharacter;

            bool show = ModConfig.RerollEnabled.Value
                        && manager.SkillSelectWindow != null && manager.SkillSelectWindow.activeSelf
                        && manager.CurLevelUpStage == LevelUpStage.Skills
                        && (manager.SkillRerollSection == null || !manager.SkillRerollSection.activeSelf)
                        && character != null;

            if (button.gameObject.activeSelf != show)
                button.gameObject.SetActive(show);

            if (!show)
                return;

            // Cheap, and it re-asserts placement if anything in the window relayouts.
            PositionButton(manager);

            button.interactable = CanAfford(character);

            if (label != null)
                label.text = BuildLabel(character);
        }

        /// <summary>
        /// Affordability is measured against the character's *maximum* pools, so the threshold
        /// and the cost sit on the same scale and paying can never reach zero.
        /// Characters with no mana pool ignore the mana half entirely.
        /// </summary>
        public static bool CanAfford(Character character)
        {
            if (character == null || character.MaxHealth <= 0f)
                return false;

            if (character.Health / character.MaxHealth <= ModConfig.RerollHealthThreshold.Value)
                return false;

            if (character.MaxMana > 0f &&
                character.Mana / character.MaxMana <= ModConfig.RerollManaThreshold.Value)
                return false;

            return true;
        }

        private static float HealthCost(Character character)
        {
            float basis = ModConfig.RerollCostFromMaxPool.Value ? character.MaxHealth : character.Health;
            return basis * ModConfig.RerollHealthCost.Value;
        }

        private static float ManaCost(Character character)
        {
            if (character.MaxMana <= 0f)
                return 0f;

            float basis = ModConfig.RerollCostFromMaxPool.Value ? character.MaxMana : character.Mana;
            return basis * ModConfig.RerollManaCost.Value;
        }

        /// <summary>
        /// "Reroll" over a smaller cost line. The percentages come from config rather than being
        /// hardcoded, so the subtext stays truthful if the costs are retuned.
        /// </summary>
        private static string BuildLabel(Character character)
        {
            int hpPercent = Mathf.RoundToInt(ModConfig.RerollHealthCost.Value * 100f);
            int manaPercent = Mathf.RoundToInt(ModConfig.RerollManaCost.Value * 100f);

            string cost;
            if (character.MaxMana <= 0f)
                cost = "-" + hpPercent + "% HP";
            else if (hpPercent == manaPercent)
                cost = "-" + hpPercent + "% HP & Mana";
            else
                cost = "-" + hpPercent + "% HP & -" + manaPercent + "% Mana";

            int subSize = Mathf.Clamp(ModConfig.RerollSubtextScale.Value, 10, 100);

            return "Reroll\n<size=" + subSize + "%>" + cost + "</size>";
        }

        private static void OnClick(RoguelikeManager manager)
        {
            try
            {
                Character character = manager.CurrentRoguelikeSkillSelectingCharacter;
                if (character == null || !CanAfford(character))
                    return;

                if (!haveRollArgs || PopulateSkillChoicesMethod == null)
                {
                    Plugin.Log.LogWarning("Reroll pressed but the original roll arguments were never captured.");
                    return;
                }

                float hp = HealthCost(character);
                float mp = ManaCost(character);

                // this["Health"] is how the game itself writes these (see SetHealthAndManaToMax),
                // so the observable sync layer picks the change up normally.
                character["Health"] = Mathf.Max(1f, character.Health - hp);
                if (mp > 0f)
                    character["Mana"] = Mathf.Max(0f, character.Mana - mp);

                PopulateSkillChoicesMethod.Invoke(manager,
                    new object[] { rollCharacter, rollLevel, rollObtained, rollForcedTier });

                // The previously highlighted option no longer exists; this setter also drops
                // AcceptButton back to non-interactable.
                manager.SelectedSkillOption = null;

                Plugin.Log.LogInfo(
                    "Rerolled skills for " + character.CharacterName +
                    ": -" + hp.ToString("F0") + " HP, -" + mp.ToString("F0") + " MP");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Reroll failed: " + e);
            }
        }
    }
}
