using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Burst2Flame;

namespace StatusEffectsMod
{
    /// <summary>
    /// One-shot export of every status and its adjustable fields to JSON.
    ///
    /// Statuses are ScriptableObjects in the game's asset bundles rather than anything visible
    /// in a decompile, so short of an asset ripper this is the only way to see what the values
    /// actually are. Off by default; switch it on, launch once, switch it off.
    ///
    /// It writes the SHIPPED values from the snapshot, not the modified ones, so the file stays
    /// a reference for what vanilla does rather than a mirror of the current config.
    /// </summary>
    internal static class StatusDumper
    {
        private static bool done;

        public static void TryDump()
        {
            if (done || !ModConfig.DumpStatusData.Value || !StatusCatalog.Loaded)
                return;

            done = true;

            try
            {
                string path = Path.Combine(
                    Path.GetDirectoryName(typeof(StatusDumper).Assembly.Location) ?? ".",
                    "status-dump.json");

                File.WriteAllText(path, BuildJson(), Encoding.UTF8);
                Plugin.Log.LogInfo($"Dumped {StatusCatalog.Entries.Count} statuses to {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Status dump failed: {e}");
            }
        }

        private static string BuildJson()
        {
            var sb = new StringBuilder();
            sb.Append("[\n");

            bool first = true;
            foreach (StatusEntry entry in StatusCatalog.Entries)
            {
                string json = SafeEntryJson(entry);
                if (json == null)
                    continue;

                if (!first)
                    sb.Append(",\n");
                first = false;
                sb.Append(json);
            }

            sb.Append("\n]\n");
            return sb.ToString();
        }

        private static string SafeEntryJson(StatusEntry entry)
        {
            try
            {
                return EntryJson(entry);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Skipped {entry.ConfigKey} in dump: {e.Message}");
                return null;
            }
        }

        private static string EntryJson(StatusEntry entry)
        {
            StatusSnapshot original = entry.Original;
            ActionStatusInfo status = entry.Status;

            var sb = new StringBuilder();
            sb.Append("  {\n");
            sb.Append($"    \"configKey\": {Str(entry.ConfigKey)},\n");
            sb.Append($"    \"displayName\": {Str(entry.DisplayName)},\n");
            sb.Append($"    \"assetName\": {Str(entry.AssetName)},\n");
            sb.Append($"    \"statusType\": {Str(status.StatusType.ToString())},\n");
            sb.Append($"    \"beneficial\": {Bool(SafeBeneficial(status))},\n");
            sb.Append($"    \"harmful\": {Bool(SafeHarmful(status))},\n");
            sb.Append($"    \"duration\": {Str(original.Duration)},\n");
            sb.Append($"    \"infinite\": {Bool(original.Infinite)},\n");
            sb.Append($"    \"maxStacks\": {Num(original.MaxStacks)},\n");
            sb.Append($"    \"stackBonus\": {Num(original.StackBonusMultplier)},\n");
            sb.Append($"    \"stackType\": {Str(original.StackType.ToString())},\n");
            sb.Append($"    \"tickType\": {Str(original.TickType.ToString())},\n");
            sb.Append($"    \"expireType\": {Str(original.ExpireType.ToString())},\n");
            sb.Append($"    \"cannotBeDispelled\": {Bool(original.CannotBeDispelled)},\n");
            sb.Append($"    \"endOnCrit\": {Bool(original.EndOnCrit)},\n");
            sb.Append($"    \"endOnAction\": {Bool(original.EndOnAction)},\n");
            sb.Append($"    \"isAura\": {Bool(original.IsAura)},\n");
            sb.Append($"    \"auraRadius\": {original.AuraRadius},\n");
            sb.Append($"    \"maintainManaRatio\": {Num(original.MaintainManaRatio)},\n");
            sb.Append($"    \"groundMovementMod\": {original.GroundMovementMod},\n");
            sb.Append($"    \"description\": {Str(SafeDescription(status))},\n");
            sb.Append($"    \"attributeEffects\": {AttributesJson(entry)}\n");
            sb.Append("  }");

            return sb.ToString();
        }

        private static string AttributesJson(StatusEntry entry)
        {
            CharacterEffectInfo[] effects = entry.Status.AttributeEffects;
            string[] amounts = entry.Original.AttributeAmounts;

            if (effects == null || effects.Length == 0 || amounts == null)
                return "[]";

            var parts = new List<string>();
            int count = Math.Min(effects.Length, amounts.Length);

            for (int i = 0; i < count; i++)
            {
                CharacterEffectInfo effect = effects[i];
                if (effect == null)
                    continue;

                string name = StatusCatalog.AttributeName(effect);

                parts.Add("{" +
                          $"\"attribute\": {Str(name)}, " +
                          $"\"method\": {Str(effect.CharacterEffectMethod.ToString())}, " +
                          $"\"amount\": {Str(amounts[i])}" +
                          "}");
            }

            return parts.Count == 0 ? "[]" : "[\n      " + string.Join(",\n      ", parts) + "\n    ]";
        }

        private static bool SafeBeneficial(ActionStatusInfo status)
        {
            try { return status.IsBeneficial; } catch (Exception) { return false; }
        }

        private static bool SafeHarmful(ActionStatusInfo status)
        {
            try { return status.IsHarmful; } catch (Exception) { return false; }
        }

        private static string SafeDescription(ActionStatusInfo status)
        {
            try { return status.Description ?? string.Empty; } catch (Exception) { return string.Empty; }
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Str(string value)
        {
            if (value == null)
                return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');

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
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
