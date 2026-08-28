using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Burst2Flame;
using UnityEngine;

namespace StatusEffectsMod
{
    /// <summary>
    /// Writes the config's overrides onto the game's status ScriptableObjects.
    ///
    /// The edits are in-memory only. Unity does not serialise ScriptableObject changes outside
    /// the editor, so nothing on disk is touched and removing the plugin fully reverts the game.
    ///
    /// Every apply starts from <see cref="StatusSnapshot"/>, never from the current value, so
    /// repeated hot reloads are idempotent rather than compounding.
    /// </summary>
    internal static class StatusApplier
    {
        /// <summary>Counts and messages from one whole-catalog apply, for the log line.</summary>
        internal sealed class Report
        {
            public int StatusesChanged;
            public int FieldsChanged;
            public readonly List<string> Problems = new List<string>();
            public readonly List<string> Changes = new List<string>();
        }

        public static Report ApplyAll()
        {
            var report = new Report();

            if (!StatusCatalog.Loaded)
                return report;

            foreach (StatusEntry entry in StatusCatalog.Entries)
            {
                try
                {
                    // Restoring first is what makes a reload idempotent, and it is also how a
                    // key that was edited and then blanked returns to vanilla without a restart.
                    entry.Original.RestoreTo(entry.Status);
                }
                catch (Exception e)
                {
                    report.Problems.Add($"{entry.ConfigKey}: could not restore vanilla values ({e.Message})");
                }
            }

            foreach (StatusEntry entry in StatusCatalog.Entries)
            {
                try
                {
                    ApplyOne(entry, report);
                }
                catch (Exception e)
                {
                    // One malformed line must never cost the other 200 statuses their edits.
                    report.Problems.Add($"{entry.ConfigKey}: {e.Message}");
                }
            }

            return report;
        }

        private static void ApplyOne(StatusEntry entry, Report report)
        {
            string spec = StatusOverrides.Get(entry)?.Value ?? string.Empty;

            List<KeyValuePair<string, string>> tokens = Tokenise(spec, entry.ConfigKey, report);

            // Global multipliers act on every status of the matching disposition, so a status
            // with no per-status line still needs a pass when they are not 1.
            float durationScale = GlobalDurationScale(entry.Status);
            float potencyScale = GlobalPotencyScale(entry.Status);

            var state = new ApplyState
            {
                Entry = entry,
                Report = report,
                DurationScale = durationScale,
                PotencyScale = potencyScale,
            };

            foreach (KeyValuePair<string, string> token in tokens)
                ApplyToken(state, token.Key, token.Value);

            // Duration and potency are resolved last because a global multiplier has to compose
            // with whatever the per-status token decided, and the token may not have run at all.
            FinishDuration(state);
            FinishPotency(state);

            if (state.FieldsChanged > 0)
            {
                report.StatusesChanged++;
                report.FieldsChanged += state.FieldsChanged;
            }
        }

        /// <summary>Working state for one status, threaded through the token handlers.</summary>
        private sealed class ApplyState
        {
            public StatusEntry Entry;
            public Report Report;
            public int FieldsChanged;

            public float DurationScale = 1f;
            public float PotencyScale = 1f;

            /// <summary>Set when a per-status duration token was seen and understood.</summary>
            public OverrideValue DurationOverride;

            /// <summary>Set when a per-status blanket potency token was seen.</summary>
            public OverrideValue PotencyOverride;

            /// <summary>
            /// Indices of AttributeEffects already claimed by an attr: token. Blanket potency
            /// skips these: both rewrite from the vanilla amount, so without this the one that
            /// ran second would silently erase the other, and potency always runs second.
            /// </summary>
            public readonly HashSet<int> AttributesOverridden = new HashSet<int>();

            public void Changed(string field, object from, object to)
            {
                FieldsChanged++;
                Report.Changes.Add($"{Entry.ConfigKey}.{field}: {from} -> {to}");
            }

            public void Problem(string message)
            {
                Report.Problems.Add($"{Entry.ConfigKey}: {message}");
            }
        }

        /// <summary>
        /// Splits "duration=*2; maxStacks=10" into its key/value pairs.
        ///
        /// Semicolon is the only separator on purpose. A comma would read just as naturally but
        /// would also cut expr: values in half, since game script calls like Max(1, 2) contain
        /// commas of their own.
        /// </summary>
        private static List<KeyValuePair<string, string>> Tokenise(string spec, string configKey, Report report)
        {
            var result = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrWhiteSpace(spec))
                return result;

            foreach (string rawToken in spec.Split(';'))
            {
                string token = rawToken.Trim();
                if (token.Length == 0)
                    continue;

                int split = token.IndexOf('=');
                if (split <= 0)
                {
                    string hint = token.Contains(",")
                        ? " (separate settings with ';', not ',')"
                        : string.Empty;

                    report.Problems.Add($"{configKey}: '{token}' is not key=value{hint}");
                    continue;
                }

                string key = token.Substring(0, split).Trim();
                string value = token.Substring(split + 1).Trim();

                if (key.Length == 0)
                {
                    report.Problems.Add($"{configKey}: '{token}' has no key");
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(key, value));
            }

            return result;
        }

        private static void ApplyToken(ApplyState state, string key, string rawValue)
        {
            ActionStatusInfo status = state.Entry.Status;
            StatusSnapshot original = state.Entry.Original;

            // attr:<AttributeName> is the only key with a variable name, so it is matched before
            // the fixed table.
            if (key.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAttributeOverride(state, key.Substring(5).Trim(), rawValue);
                return;
            }

            OverrideValue value = OverrideValue.Parse(rawValue, out string parseError);
            if (value == null)
            {
                state.Problem($"{key}: {parseError}");
                return;
            }

            switch (key.ToLowerInvariant())
            {
                case "duration":
                case "dur":
                    // Deferred: the global multiplier has to compose with it.
                    state.DurationOverride = value;
                    return;

                case "potency":
                case "power":
                    state.PotencyOverride = value;
                    return;

                case "infinite":
                    SetBool(state, "infinite", original.Infinite, value,
                            v => status.Infinite = v);
                    return;

                case "maxstacks":
                case "stacks":
                    SetFloat(state, "maxStacks", original.MaxStacks, value,
                             v => status.MaxStacks = Mathf.Max(0f, v));
                    return;

                case "stackbonus":
                    SetFloat(state, "stackBonus", original.StackBonusMultplier, value,
                             v => status.StackBonusMultplier = v);
                    WarnInert(state, "stackBonus");
                    return;

                case "stacktype":
                    SetEnum(state, "stackType", original.StackType, value,
                            v => status.StackType = v);
                    return;

                case "stackignoresource":
                    SetBool(state, "stackIgnoreSource", original.StackIgnoreSource, value,
                            v => status.StackIgnoreSource = v);
                    return;

                case "ticktype":
                    SetEnum(state, "tickType", original.TickType, value,
                            v => status.TickType = v);
                    return;

                case "expiretype":
                    SetEnum(state, "expireType", original.ExpireType, value,
                            v => status.ExpireType = v);
                    return;

                case "activateimmediately":
                    SetBool(state, "activateImmediately", original.ActivateImmediately, value,
                            v => status.ActivateImmediately = v);
                    return;

                case "decrementonturnend":
                    SetBool(state, "decrementOnTurnEnd", original.DecrementOnTurnEnd, value,
                            v => status.DecrementOnTurnEnd = v);
                    WarnInert(state, "decrementOnTurnEnd");
                    return;

                case "cannotbedispelled":
                    SetBool(state, "cannotBeDispelled", original.CannotBeDispelled, value,
                            v => status.CannotBeDispelled = v);
                    return;

                case "endoncrit":
                    SetBool(state, "endOnCrit", original.EndOnCrit, value,
                            v => status.EndOnCrit = v);
                    return;

                case "endonaction":
                    SetBool(state, "endOnAction", original.EndOnAction, value,
                            v => status.EndOnAction = v);
                    return;

                case "isaura":
                    SetBool(state, "isAura", original.IsAura, value,
                            v => status.IsAura = v);
                    return;

                case "auraradius":
                    SetFloat(state, "auraRadius", original.AuraRadius, value,
                             v => status.AuraRadius = Mathf.Max(0, Mathf.RoundToInt(v)));
                    return;

                case "auraallies":
                    SetBool(state, "auraAllies", original.AuraEffectsAllies, value,
                            v => status.AuraEffectsAllies = v);
                    return;

                case "auraenemies":
                    SetBool(state, "auraEnemies", original.AuraEffectsEnemies, value,
                            v => status.AuraEffectsEnemies = v);
                    return;

                case "maintainmana":
                    SetFloat(state, "maintainMana", original.MaintainManaRatio, value,
                             v => status.MaintainManaRatio = v);
                    return;

                case "groundmovement":
                    SetFloat(state, "groundMovement", original.GroundMovementMod, value,
                             v => status.GroundMovementMod = Mathf.RoundToInt(v));
                    WarnInert(state, "groundMovement");
                    return;

                case "damagemod":
                    // The flag and the value travel together; setting one without the other
                    // silently does nothing, which would look like the mod ignoring the config.
                    SetFloat(state, "damageMod", original.FlatDamageModifier, value, v =>
                    {
                        status.FlatDamageModifier = v;
                        status.UseFlatDamageModifier = true;
                    });
                    return;

                default:
                    state.Problem($"unknown key '{key}'");
                    return;
            }
        }

        /// <summary>
        /// Some fields on ActionStatusInfo are declared and serialised but never read anywhere
        /// in Assembly-CSharp - leftovers, or hooks for a system that was not finished. The mod
        /// still writes them, in case a future patch starts reading them, but says plainly that
        /// nothing will come of it today rather than letting the setting look effective.
        /// </summary>
        private static void WarnInert(ApplyState state, string field)
        {
            state.Problem($"{field}: the base game never reads this field, so setting it has no " +
                          "effect in the current build. Written anyway, in case a patch changes that.");
        }

        private static void SetBool(ApplyState state, string field, bool original,
                                    OverrideValue value, Action<bool> write)
        {
            bool resolved = value.AsBool(original, out string error);
            if (error != null)
            {
                state.Problem($"{field}: {error}");
                return;
            }

            if (resolved == original)
                return;

            write(resolved);
            state.Changed(field, original, resolved);
        }

        private static void SetFloat(ApplyState state, string field, float original,
                                     OverrideValue value, Action<float> write)
        {
            if (value.Op == OverrideOp.Expression)
            {
                state.Problem($"{field}: expr: is only supported for duration");
                return;
            }

            float resolved = value.Apply(original);
            if (Mathf.Approximately(resolved, original))
                return;

            write(resolved);
            state.Changed(field, Num(original), Num(resolved));
        }

        private static void SetEnum<T>(ApplyState state, string field, T original,
                                       OverrideValue value, Action<T> write) where T : struct
        {
            T resolved = value.AsEnum(original, out string error);
            if (error != null)
            {
                state.Problem($"{field}: {error}");
                return;
            }

            if (resolved.Equals(original))
                return;

            write(resolved);
            state.Changed(field, original, resolved);
        }

        /// <summary>
        /// Resolves the duration once, folding the per-status token and the global multiplier
        /// together, and writes it back as a plain integer.
        ///
        /// Integer matters: several places in the game read <c>Duration</c> with
        /// <c>int.Parse</c> rather than through the expression evaluator, and a value like
        /// "3.5" would throw there. Rounds are whole numbers in this game anyway.
        /// </summary>
        private static void FinishDuration(ApplyState state)
        {
            OverrideValue value = state.DurationOverride;
            float scale = state.DurationScale;

            if (value == null && Mathf.Approximately(scale, 1f))
                return;

            ActionStatusInfo status = state.Entry.Status;
            StatusSnapshot original = state.Entry.Original;

            if (value != null && value.Op == OverrideOp.Expression)
            {
                // A raw expression is handed over verbatim. The global multiplier cannot be
                // folded into it safely, so it is deliberately not applied rather than guessed.
                if (!Mathf.Approximately(scale, 1f))
                    state.Problem("duration: global duration multiplier skipped, this status uses a raw expr:");

                status.Duration = value.Text;
                state.Changed("duration", original.Duration, value.Text);
                return;
            }

            if (!original.TryGetNumericDuration(out float baseDuration))
            {
                // The shipped value is blank or an expression. Set still works, since it does
                // not need to know what it is replacing.
                if (value != null && value.Op == OverrideOp.Set)
                {
                    int replacement = Mathf.Max(0, Mathf.RoundToInt(value.Number));
                    string text = replacement.ToString(CultureInfo.InvariantCulture);
                    if (text == original.Duration)
                        return;

                    status.Duration = text;
                    state.Changed("duration", original.Duration, text);
                    return;
                }

                // Relative arithmetic has no numeric base to build on. Only say so when the
                // user actually asked for it: a global multiplier sweeps every status in the
                // game, and warning about each one it cannot scale would bury the real
                // problems in the log.
                if (value != null)
                {
                    string shipped = string.IsNullOrWhiteSpace(original.Duration)
                        ? "vanilla duration is blank"
                        : $"vanilla duration '{original.Duration}' is an expression";

                    state.Problem($"duration: {shipped}, so a relative change has no base. " +
                                  "Use a plain number, or expr: to replace it outright.");
                }

                return;
            }

            float resolved = value != null ? value.Apply(baseDuration) : baseDuration;
            resolved *= scale;

            int rounded = Mathf.Max(0, Mathf.RoundToInt(resolved));
            string durationText = rounded.ToString(CultureInfo.InvariantCulture);

            if (durationText == original.Duration)
                return;

            status.Duration = durationText;
            state.Changed("duration", original.Duration, durationText);
        }

        /// <summary>
        /// Scales every attribute amount on the status by the per-status potency token and the
        /// global multiplier together, by wrapping each shipped expression in "(expr)*factor".
        ///
        /// Wrapping rather than computing is what makes this work at all: the amounts are game
        /// script, frequently referencing Source.SpellPower and the like, so there is no number
        /// to multiply until the game evaluates them.
        /// </summary>
        private static void FinishPotency(ApplyState state)
        {
            OverrideValue value = state.PotencyOverride;
            float factor = state.PotencyScale;

            if (value != null)
            {
                if (value.Op == OverrideOp.Expression)
                {
                    state.Problem("potency: expr: is not supported, use a multiplier like *1.5");
                    return;
                }

                if (value.Op != OverrideOp.Multiply)
                {
                    // Setting an absolute potency would flatten every attribute on the status to
                    // the same number, which is never what anyone means by it.
                    state.Problem("potency: only a multiplier makes sense here, e.g. potency=*1.5");
                    return;
                }

                factor *= value.Number;
            }

            if (Mathf.Approximately(factor, 1f))
                return;

            ScaleAttributes(state, factor);
        }

        private static void ScaleAttributes(ApplyState state, float factor)
        {
            ActionStatusInfo status = state.Entry.Status;
            string[] originals = state.Entry.Original.AttributeAmounts;

            CharacterEffectInfo[] effects = status.AttributeEffects;
            if (effects == null || originals == null)
                return;

            int count = Math.Min(effects.Length, originals.Length);
            int scaled = 0;

            for (int i = 0; i < count; i++)
            {
                CharacterEffectInfo effect = effects[i];
                if (effect == null)
                    continue;

                string amount = originals[i];
                if (string.IsNullOrWhiteSpace(amount))
                    continue;

                // A Set writes an absolute value, very often a binary 1 standing for "has this
                // immunity". Scaling those turns a flag into nonsense, so blanket potency leaves
                // them alone; attr: can still target one deliberately.
                if (effect.CharacterEffectMethod == CharacterEffectMethod.Set)
                    continue;

                // An attr: line for this attribute is the more specific instruction, so it wins.
                if (state.AttributesOverridden.Contains(i))
                    continue;

                effect.Amount = Wrap(amount, factor);
                scaled++;
            }

            if (scaled > 0)
                state.Changed("potency", "vanilla", $"x{Num(factor)} on {scaled} attribute(s)");
        }

        private static void ApplyAttributeOverride(ApplyState state, string attributeName, string rawValue)
        {
            if (attributeName.Length == 0)
            {
                state.Problem("attr: needs an attribute name, e.g. attr:Armor=*2");
                return;
            }

            OverrideValue value = OverrideValue.Parse(rawValue, out string parseError);
            if (value == null)
            {
                state.Problem($"attr:{attributeName}: {parseError}");
                return;
            }

            ActionStatusInfo status = state.Entry.Status;
            string[] originals = state.Entry.Original.AttributeAmounts;

            CharacterEffectInfo[] effects = status.AttributeEffects;
            if (effects == null || effects.Length == 0 || originals == null)
            {
                state.Problem($"attr:{attributeName}: this status has no attribute effects");
                return;
            }

            int count = Math.Min(effects.Length, originals.Length);
            int matched = 0;

            for (int i = 0; i < count; i++)
            {
                CharacterEffectInfo effect = effects[i];
                if (effect == null)
                    continue;

                if (!string.Equals(StatusCatalog.AttributeName(effect), attributeName,
                                   StringComparison.OrdinalIgnoreCase))
                    continue;

                matched++;
                state.AttributesOverridden.Add(i);

                string amount = originals[i] ?? string.Empty;

                switch (value.Op)
                {
                    case OverrideOp.Expression:
                        effect.Amount = value.Text;
                        break;

                    case OverrideOp.Multiply:
                        if (amount.Trim().Length == 0)
                        {
                            state.Problem($"attr:{attributeName}: vanilla amount is empty, nothing to multiply");
                            continue;
                        }
                        effect.Amount = Wrap(amount, value.Number);
                        break;

                    case OverrideOp.Add:
                        effect.Amount = amount.Trim().Length == 0
                            ? Num(value.Number)
                            : $"({amount}) + {Num(value.Number)}";
                        break;

                    default:
                        effect.Amount = Num(value.Number);
                        break;
                }

                state.Changed($"attr:{attributeName}", Shorten(amount), Shorten(effect.Amount));
            }

            if (matched == 0)
            {
                state.Problem($"attr:{attributeName}: no such attribute on this status " +
                              $"(it has: {AvailableAttributes(status)})");
            }
        }

        private static string AvailableAttributes(ActionStatusInfo status)
        {
            CharacterEffectInfo[] effects = status.AttributeEffects;
            if (effects == null || effects.Length == 0)
                return "none";

            var names = new List<string>();
            foreach (CharacterEffectInfo effect in effects)
            {
                string name = StatusCatalog.AttributeName(effect);
                if (name.Length > 0 && !names.Contains(name))
                    names.Add(name);
            }

            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        /// <summary>
        /// Parenthesises before multiplying. The shipped amounts are expressions, so
        /// "a + b" scaled without brackets would silently become "a + b*2".
        /// </summary>
        private static string Wrap(string expression, float factor)
        {
            return $"({expression}) * {Num(factor)}";
        }

        private static float GlobalDurationScale(ActionStatusInfo status)
        {
            float scale = ModConfig.AllDurationMultiplier.Value;

            if (IsBeneficial(status))
                scale *= ModConfig.BeneficialDurationMultiplier.Value;
            else if (IsHarmful(status))
                scale *= ModConfig.HarmfulDurationMultiplier.Value;

            return scale;
        }

        private static float GlobalPotencyScale(ActionStatusInfo status)
        {
            float scale = 1f;

            if (IsBeneficial(status))
                scale *= ModConfig.BeneficialPotencyMultiplier.Value;
            else if (IsHarmful(status))
                scale *= ModConfig.HarmfulPotencyMultiplier.Value;

            return scale;
        }

        private static bool IsBeneficial(ActionStatusInfo status)
        {
            try
            {
                return status.IsBeneficial;
            }
            catch (Exception)
            {
                // SkillTags can be null on a malformed asset; neither category is a safe answer,
                // and "no global multiplier" is the harmless one.
                return false;
            }
        }

        private static bool IsHarmful(ActionStatusInfo status)
        {
            try
            {
                return status.IsHarmful;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            string collapsed = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return collapsed.Length <= 48 ? collapsed : collapsed.Substring(0, 45) + "...";
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
