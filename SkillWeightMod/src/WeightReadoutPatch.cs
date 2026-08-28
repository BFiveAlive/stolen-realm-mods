using System;
using System.Collections.Generic;
using System.Text;
using Burst2Flame;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkillWeightMod
{
    /// <summary>
    /// Testing aid: prints each offered skill's computed weight beneath its name in the level-up
    /// window, and shows the per-category breakdown on hover.
    ///
    /// The readout is a clone of the option's own name label, so it inherits the window's font
    /// and styling instead of introducing a second look. It is a separate object rather than
    /// extra text on the name because it needs its own hover region: hovering the numbers shows
    /// the weight breakdown, hovering the name still shows the game's normal skill tooltip.
    /// </summary>
    [HarmonyPatch(typeof(RoguelikeManager), "PopulateSkillChoices")]
    internal static class WeightReadoutPatch
    {
        private const string ReadoutName = "SkillWeightMod_Readout";

        [HarmonyPostfix]
        private static void ShowWeights(RoguelikeManager __instance)
        {
            try
            {
                RoguelikeSkillOption[] buttons = __instance.SkillOptionBtns;
                if (buttons == null)
                    return;

                foreach (RoguelikeSkillOption option in buttons)
                {
                    if (option == null || option.SkillName == null)
                        continue;

                    TextMeshProUGUI readout = EnsureReadout(option);
                    if (readout == null)
                        continue;

                    bool show = ModConfig.ShowWeightsInMenu.Value
                                && option.gameObject.activeSelf
                                && option.SkillInfo != null;

                    if (readout.gameObject.activeSelf != show)
                        readout.gameObject.SetActive(show);

                    if (!show)
                        continue;

                    readout.text = Readout(option.SkillInfo);

                    var trigger = readout.GetComponent<WeightTooltip>();
                    if (trigger != null)
                        trigger.Body = TooltipFor(option.SkillInfo);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Weight readout failed: {e.Message}");
            }
        }

        /// <summary>
        /// Clones the name label once per option and parks it below, outside the option.
        ///
        /// Anchoring to the name label was the first attempt and failed on wrapped names: the
        /// name's own position does not move when it wraps, so a two-line name like "Pain
        /// Suppression" grew downward into the readout. Anchoring to the option's bottom edge
        /// is independent of name length.
        /// </summary>
        private static TextMeshProUGUI EnsureReadout(RoguelikeSkillOption option)
        {
            // Parented as a SIBLING of the option, not inside it. Unity sends pointer-enter to
            // the whole ancestor chain, so a readout living under the icon made the option
            // button fire its own MouseOver too - hovering the numbers showed the skill tooltip
            // no matter how far down they were moved. Outside the button, nothing bubbles.
            Transform parent = option.transform.parent;
            if (parent == null)
                return null;

            // Named per option, since all the readouts now share one parent.
            string childName = ReadoutName + "_" + option.GetInstanceID();

            Transform existing = parent.Find(childName);
            if (existing != null)
                return existing.GetComponent<TextMeshProUGUI>();

            GameObject go = UnityEngine.Object.Instantiate(option.SkillName.gameObject, parent);
            go.name = childName;

            var text = go.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            text.fontSize = Mathf.Max(6f, option.SkillName.fontSize * 0.72f);
            text.richText = true;
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = true;   // needed so the numbers can be hovered independently

            // The option's parent is a layout group; without this it would allocate a slot for
            // every readout and shove the icons aside.
            var layoutElement = go.GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var follow = go.AddComponent<WeightTooltip>();
            follow.Option = option.GetComponent<RectTransform>();

            PositionReadout(option.GetComponent<RectTransform>(), text);
            return text;
        }

        /// <summary>
        /// Places the readout just under the option, in the option's own coordinate space -
        /// they are siblings, so the option's anchors can be copied directly.
        ///
        /// The rect is deliberately wider than the option: the text runs longer than an icon is
        /// wide, and only the area inside this rect is hoverable.
        /// </summary>
        internal static void PositionReadout(RectTransform optionRect, TextMeshProUGUI text)
        {
            var rect = text.GetComponent<RectTransform>();
            if (rect == null || optionRect == null)
                return;

            rect.anchorMin = optionRect.anchorMin;
            rect.anchorMax = optionRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.localScale = optionRect.localScale;

            float height = Mathf.Max(14f, text.fontSize * 1.5f);
            rect.sizeDelta = new Vector2(Mathf.Max(optionRect.rect.width * 1.6f, 130f), height);

            // Start at the option's bottom edge, then apply the configured nudge.
            float toBottom = -optionRect.rect.height * optionRect.pivot.y;
            rect.anchoredPosition = optionRect.anchoredPosition
                                    + new Vector2(ModConfig.WeightReadoutOffsetX.Value,
                                                  toBottom + ModConfig.WeightReadoutOffsetY.Value);
        }

        /// <summary>
        /// "w 5.70 · 3.9% of 77" - the raw weight, its share of that tier's total, and how many
        /// candidates it was competing against. A skill the roll did not weight shows a dash
        /// rather than a plausible-looking wrong number.
        /// </summary>
        private static string Readout(SkillInfo skill)
        {
            if (!GetSkillChoicesPatch.LastRoll.TryGetValue(skill, out GetSkillChoicesPatch.RollInfo info))
                return "<color=#8a8a80>w —</color>";

            return "<color=#d9a441>w " + info.Weight.ToString("F2") +
                   "</color><color=#8a8a80> · " + (info.Share * 100f).ToString("F1") +
                   "% of " + info.PoolSize + "</color>";
        }

        private static string TooltipFor(SkillInfo skill)
        {
            if (!GetSkillChoicesPatch.LastRoll.TryGetValue(skill, out GetSkillChoicesPatch.RollInfo info))
                return "This skill was not weighted by the mod. It may have come from a vanilla " +
                       "fallback, or from a roll made before the readout was switched on.";

            return string.IsNullOrEmpty(info.Breakdown) ? "No synergy: nothing owned matches." : info.Breakdown;
        }

        /// <summary>
        /// Renders the itemised weight for the tooltip. Called once per pick at roll time, so
        /// the text is frozen against the weights that were actually used.
        /// </summary>
        internal static string DescribeBreakdown(SkillInfo skill, List<SkillInfo> owned, float finalWeight)
        {
            try
            {
                List<SkillWeighting.Contribution> parts = SkillWeighting.Explain(skill, owned);

                var sb = new StringBuilder();
                sb.Append("Base 1.00");

                float synergy = 0f;
                foreach (SkillWeighting.Contribution p in parts)
                {
                    synergy += p.Points;
                    sb.Append("\n+").Append(p.Points.ToString("F2")).Append("  ")
                      .Append(p.Source).Append(": ").Append(p.Label);
                    if (p.Shared > 1)
                        sb.Append(" x").Append(p.Shared);
                }

                if (parts.Count == 0)
                    sb.Append("\nNo shared tree or category with anything owned.");

                float scaled = 1f + ModConfig.SynergyStrength.Value * synergy;
                sb.Append("\n\nSynergy ").Append(synergy.ToString("F2"));

                if (!Mathf.Approximately(ModConfig.SynergyStrength.Value, 1f))
                    sb.Append(" x ").Append(ModConfig.SynergyStrength.Value.ToString("F2"))
                      .Append(" strength");

                sb.Append("  ->  ").Append(scaled.ToString("F2"));

                if (!Mathf.Approximately(scaled, finalWeight))
                    sb.Append("\nAfter repetition damping and clamp: ").Append(finalWeight.ToString("F2"));

                return sb.ToString();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not describe weight breakdown: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Hover handler on the readout label. Reuses the game's own universal tooltip so the
    /// breakdown looks like every other tooltip in the UI.
    /// </summary>
    internal class WeightTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Body;

        /// <summary>The skill option this readout sits under.</summary>
        public RectTransform Option;

        private TextMeshProUGUI text;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        /// <summary>
        /// Re-positions after the layout pass. The option's own placement is decided by the
        /// parent's layout group, which has not run yet when PopulateSkillChoices fires, so
        /// reading its position during Update gives last frame's answer - or zero on the first.
        /// Doing it here also means config offsets apply live.
        /// </summary>
        private void LateUpdate()
        {
            if (Option != null && text != null)
                WeightReadoutPatch.PositionReadout(Option, text);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(Body))
                return;

            try
            {
                GUIManager.instance.tooltip.ShowUniversalTooltip("Roll weight", "", Body);
            }
            catch (Exception)
            {
                // A tooltip failing is never worth interrupting the level-up window for.
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try
            {
                GUIManager.instance.tooltip.HideTooltip();
            }
            catch (Exception)
            {
            }
        }
    }
}
