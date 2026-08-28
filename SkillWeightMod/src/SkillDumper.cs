using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Burst2Flame;

namespace SkillWeightMod
{
    /// <summary>
    /// One-shot export of the game's full skill table to JSON, for building documentation and
    /// for checking how common a given tag or status actually is before weighting on it.
    ///
    /// Skill definitions are ScriptableObjects in the game's asset bundles, not in
    /// Assembly-CSharp, so this is the only way to see the real data without an asset ripper.
    /// Off by default; enable DumpSkillData, launch once, then turn it back off.
    /// </summary>
    internal static class SkillDumper
    {
        private static bool done;

        public static void TryDump()
        {
            if (done || !ModConfig.DumpSkillData.Value)
                return;

            List<SkillInfo> skills = SafeSkills();
            if (skills == null || skills.Count == 0)
                return; // data not loaded yet; try again next frame

            done = true;

            try
            {
                string path = Path.Combine(
                    Path.GetDirectoryName(typeof(SkillDumper).Assembly.Location) ?? ".",
                    "skill-dump.json");

                File.WriteAllText(path, BuildJson(skills), Encoding.UTF8);
                Plugin.Log.LogInfo($"Dumped {skills.Count} skills to {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Skill dump failed: {e}");
            }
        }

        private static List<SkillInfo> SafeSkills()
        {
            try
            {
                return Burst2Flame.Game.Instance?.Skills;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string BuildJson(List<SkillInfo> skills)
        {
            var sb = new StringBuilder();
            sb.Append("[\n");

            bool first = true;
            foreach (SkillInfo skill in skills)
            {
                string entry = SafeSkillJson(skill);
                if (entry == null)
                    continue;

                if (!first)
                    sb.Append(",\n");
                first = false;
                sb.Append(entry);
            }

            sb.Append("\n]\n");
            return sb.ToString();
        }

        /// <summary>One malformed skill must not lose the whole dump.</summary>
        private static string SafeSkillJson(SkillInfo skill)
        {
            try
            {
                return SkillJson(skill);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Skipped a skill during dump: {e.Message}");
                return null;
            }
        }

        private static string SkillJson(SkillInfo skill)
        {
            if (skill == null)
                return null;

            var tags = new SortedSet<string>(StringComparer.Ordinal);
            var targetStatuses = new SortedSet<string>(StringComparer.Ordinal);
            var selfStatuses = new SortedSet<string>(StringComparer.Ordinal);
            var weaponTypes = new SortedSet<string>(StringComparer.Ordinal);
            var weaponTags = new SortedSet<string>(StringComparer.Ordinal);
            var attributes = new SortedSet<string>(StringComparer.Ordinal);
            var amounts = new SortedSet<string>(StringComparer.Ordinal);
            var damageTypes = new SortedSet<string>(StringComparer.Ordinal);

            if (skill.SkillTags != null)
                foreach (SkillTag t in skill.SkillTags)
                    tags.Add(t.ToString());

            damageTypes.Add(skill.DamageType.ToString());

            // Passives have no ActionsGranted at all - that is literally how IsPassive is
            // defined - so their entire effect lives in these two fields on the skill itself.
            // Walking only ActionsGranted leaves every passive looking empty.
            CollectAttributeEffects(skill.AttributeEffects, attributes, amounts);
            CollectStatuses(skill.PassiveActionStatuses, selfStatuses, attributes, amounts);

            int triggers = skill.SkillTriggers?.Length ?? 0;

            var triggerTypes = new SortedSet<string>(StringComparer.Ordinal);
            var triggerStatuses = new SortedSet<string>(StringComparer.Ordinal);
            var stackingStatuses = new SortedSet<string>(StringComparer.Ordinal);

            var effects = new SortedSet<string>(StringComparer.Ordinal);
            bool targetsSelf = false, targetsAllies = false, targetsEnemies = false;
            bool requiresEmptyCell = false, freeAction = false, knockback = false, summons = false;
            bool blast = false, blastFromTargets = false, deactivatable = false;
            int manaCost = 0;

            // A triggered passive keeps its entire mechanic inside the trigger: the condition
            // that fires it and the status it then applies. Without walking this, skills like
            // Bone Collector and Leech Might look completely empty.
            if (skill.SkillTriggers != null)
            {
                foreach (SkillTrigger trigger in skill.SkillTriggers)
                {
                    if (trigger == null)
                        continue;

                    triggerTypes.Add(trigger.TriggerType.ToString());
                    CollectStatuses(trigger.ActionStatuses, triggerStatuses, attributes, amounts);
                    CollectStacking(trigger.ActionStatuses, stackingStatuses);
                    CollectEffects(trigger.GeneralEffects, effects, attributes);

                    if (trigger.Actions == null)
                        continue;

                    foreach (ActionInfo action in trigger.Actions)
                    {
                        if (action == null)
                            continue;

                        CollectStatuses(action.StatusEffects, triggerStatuses, attributes);
                        CollectStatuses(action.SourceStatusEffects, triggerStatuses, attributes);
                        CollectStacking(action.StatusEffects, stackingStatuses);
                        CollectStacking(action.SourceStatusEffects, stackingStatuses);
                        CollectEffects(action.Effects, effects, attributes);
                    }
                }
            }

            if (skill.ActionsGranted != null)
            {
                foreach (ActionInfo action in skill.ActionsGranted)
                {
                    if (action == null)
                        continue;

                    if (action.SkillTags != null)
                        foreach (SkillTag t in action.SkillTags)
                            tags.Add(t.ToString());

                    damageTypes.Add(action.DamageType.ToString());

                    CollectStatuses(action.StatusEffects, targetStatuses, attributes, amounts);
                    CollectStatuses(action.SourceStatusEffects, selfStatuses, attributes, amounts);

                    if (action.RequiredWeaponTypes != null)
                        foreach (var w in action.RequiredWeaponTypes)
                            weaponTypes.Add(w.ToString());

                    if (action.RequiredWeaponTags != null)
                        foreach (SkillTag t in action.RequiredWeaponTags)
                            weaponTags.Add(t.ToString());

                    if (action.ActionType == ActionType.FreeAction)
                        freeAction = true;

                    if (action.UseKnockback)
                        knockback = true;

                    // SummonType has no "none" member and defaults to Character, so it cannot
                    // distinguish a summon on its own - a non-empty Summons list is the marker.
                    if (action.Summons != null && action.Summons.Count > 0)
                        summons = true;

                    // GetManaCost() is the parameterless overload: manaCostBase * ManaCostRatio,
                    // so it needs no Character and is safe to call outside a battle.
                    try { manaCost = Math.Max(manaCost, action.GetManaCost()); }
                    catch (Exception) { }

                    if (action.UseMaxRangeBlastOverride)
                        blast = true;

                    // Deactivatable marks a maintained/toggled ability - auras, seals, stances.
                    // The game keys maintenance cost off it (StatusEffects[0].MaintainManaRatio
                    // is used instead of ManaCostRatio when this is set).
                    if (action.Deactivatable)
                        deactivatable = true;

                    CollectTargeting(action.Targets, ref targetsSelf, ref targetsAllies,
                                     ref targetsEnemies, ref requiresEmptyCell, ref blastFromTargets);

                    CollectEffects(action.Effects, effects, attributes);
                }
            }

            var sb = new StringBuilder();
            sb.Append("  {");
            sb.Append("\"name\":").Append(Str(skill.SkillName)).Append(',');
            sb.Append("\"tree\":").Append(Str(skill.SkillType.ToString())).Append(',');
            sb.Append("\"description\":").Append(Str(skill.Description)).Append(',');
            sb.Append("\"dependency\":").Append(Str(skill.Dependency?.SkillName)).Append(',');
            sb.Append("\"disablingSkills\":").Append(Arr(NamesOf(skill.DisablingSkills))).Append(',');
            sb.Append("\"skillsThatReplace\":").Append(Arr(NamesOf(skill.SkillsThatReplace))).Append(',');
            sb.Append("\"tier\":").Append(skill.Tier.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"passive\":").Append(Bool(SafePassive(skill))).Append(',');
            sb.Append("\"disabled\":").Append(Bool(skill.Disabled)).Append(',');
            sb.Append("\"hiddenFromTree\":").Append(Bool(skill.DontIncludeInTree)).Append(',');
            sb.Append("\"damageTypes\":").Append(Arr(damageTypes)).Append(',');
            sb.Append("\"tags\":").Append(Arr(tags)).Append(',');
            sb.Append("\"appliesToTarget\":").Append(Arr(targetStatuses)).Append(',');
            sb.Append("\"appliesToSelf\":").Append(Arr(selfStatuses)).Append(',');
            sb.Append("\"attributes\":").Append(Arr(attributes)).Append(',');
            sb.Append("\"attributeAmounts\":").Append(Arr(amounts)).Append(',');
            sb.Append("\"weaponTypes\":").Append(Arr(weaponTypes)).Append(',');
            sb.Append("\"weaponTags\":").Append(Arr(weaponTags)).Append(',');
            sb.Append("\"triggers\":").Append(triggers.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"triggerTypes\":").Append(Arr(triggerTypes)).Append(',');
            sb.Append("\"triggerStatuses\":").Append(Arr(triggerStatuses)).Append(',');
            sb.Append("\"stackingStatuses\":").Append(Arr(stackingStatuses)).Append(',');
            sb.Append("\"effects\":").Append(Arr(effects)).Append(',');
            sb.Append("\"targetsSelf\":").Append(Bool(targetsSelf)).Append(',');
            sb.Append("\"targetsAllies\":").Append(Bool(targetsAllies)).Append(',');
            sb.Append("\"targetsEnemies\":").Append(Bool(targetsEnemies)).Append(',');
            sb.Append("\"requiresEmptyCell\":").Append(Bool(requiresEmptyCell)).Append(',');
            sb.Append("\"freeAction\":").Append(Bool(freeAction)).Append(',');
            sb.Append("\"knockback\":").Append(Bool(knockback)).Append(',');
            sb.Append("\"summons\":").Append(Bool(summons)).Append(',');
            sb.Append("\"blast\":").Append(Bool(blast || blastFromTargets)).Append(',');
            sb.Append("\"manaCost\":").Append(manaCost.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"deactivatable\":").Append(Bool(deactivatable));
            sb.Append('}');
            return sb.ToString();
        }

        private static bool SafePassive(SkillInfo skill)
        {
            try
            {
                return skill.IsPassive;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Records each status by name and, one level deeper, the character attributes it
        /// modifies. That second hop is where things like LifeSteal and crit actually live -
        /// they are attributes, not statuses or tags.
        /// </summary>
        private static void CollectStatuses(ActionStatusInfo[] statuses, SortedSet<string> names,
                                            SortedSet<string> attributes, SortedSet<string> amounts = null)
        {
            if (statuses == null)
                return;

            foreach (ActionStatusInfo status in statuses)
            {
                if (status == null)
                    continue;

                names.Add(string.IsNullOrEmpty(status.Name) ? status.name : status.Name);

                if (status.AttributeEffects == null)
                    continue;

                CollectAttributeEffects(status.AttributeEffects, attributes, amounts);
            }
        }

        /// <summary>
        /// Targeting flags are strongly diagnostic on their own: an action that requires an
        /// EMPTY cell is repositioning something, which is how Teleport and Escape identify
        /// themselves without reading a word of their description.
        /// </summary>
        private static void CollectTargeting(ITargetInfo[] targets, ref bool self, ref bool allies,
                                             ref bool enemies, ref bool requiresEmpty, ref bool blast)
        {
            if (targets == null)
                return;

            foreach (ITargetInfo t in targets)
            {
                if (!(t is TargetInfo info))
                    continue;

                self |= info.TargetSelf;
                allies |= info.TargetAllies;
                enemies |= info.TargetEnemies;
                requiresEmpty |= info.RequireEmpty;
                blast |= !string.IsNullOrEmpty(info.Blast) || !string.IsNullOrEmpty(info.BlastRange);
            }
        }

        /// <summary>
        /// GeneralEffect.Action is a symbolic action name, and CharacterVariableEffectInfo names
        /// a character variable - both are code-level identifiers rather than display text.
        /// </summary>
        private static void CollectEffects(IEffectInfo[] fx, SortedSet<string> effects, SortedSet<string> attributes)
        {
            if (fx == null)
                return;

            foreach (IEffectInfo effect in fx)
            {
                if (effect is GeneralEffect general)
                {
                    if (!string.IsNullOrEmpty(general.Action))
                        effects.Add(general.Action);
                }
                else if (effect is CharacterVariableEffectInfo variable)
                {
                    string n = variable.CharacterVariableAttribute?.name;
                    if (!string.IsNullOrEmpty(n))
                        attributes.Add(n);
                }
            }
        }

        /// <summary>
        /// StackType.Add / AddAndRefresh accumulate; the Replace and Ignore variants do not.
        /// This is what separates a stacking battle-long buff from a plain refreshing one.
        /// </summary>
        private static void CollectStacking(ActionStatusInfo[] statuses, SortedSet<string> stacking)
        {
            if (statuses == null)
                return;

            foreach (ActionStatusInfo status in statuses)
            {
                if (status == null)
                    continue;

                if (status.StackType == StackType.Add || status.StackType == StackType.AddAndRefresh)
                    stacking.Add(string.IsNullOrEmpty(status.Name) ? status.name : status.Name);
            }
        }

        private static void CollectAttributeEffects(CharacterEffectInfo[] effects, SortedSet<string> attributes)
        {
            CollectAttributeEffects(effects, attributes, null);
        }

        /// <summary>
        /// Also records "Attribute=Amount". The Amount expression carries the SIGN, which the
        /// attribute name does not: Rage and Berserker's Rage both set DamageReduction, but
        /// negatively - they increase damage taken. Without this a penalty is indistinguishable
        /// from the buff that uses the same attribute.
        /// </summary>
        private static void CollectAttributeEffects(CharacterEffectInfo[] effects,
                                                    SortedSet<string> attributes,
                                                    SortedSet<string> amounts)
        {
            if (effects == null)
                return;

            foreach (CharacterEffectInfo effect in effects)
            {
                if (effect?.CharacterAttribute == null)
                    continue;

                string attr = effect.CharacterAttribute.name;
                if (string.IsNullOrEmpty(attr))
                    continue;

                attributes.Add(attr);
                if (amounts != null && !string.IsNullOrEmpty(effect.Amount))
                    amounts.Add(attr + "=" + effect.Amount);
            }
        }

        /// <summary>Skill cross-references, by name, for rebuilding the eligible pool offline.</summary>
        private static IEnumerable<string> NamesOf(SkillInfo[] skills)
        {
            if (skills == null)
                return new string[0];

            return skills.Where(x => x != null && !string.IsNullOrEmpty(x.SkillName))
                         .Select(x => x.SkillName)
                         .Distinct()
                         .OrderBy(x => x, StringComparer.Ordinal);
        }

        private static string Arr(IEnumerable<string> values)
        {
            return "[" + string.Join(",", values.Select(Str).ToArray()) + "]";
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Str(string value)
        {
            if (value == null)
                return "null";

            var sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
