using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Burst2Flame;
using UnityEngine;

namespace RoguelikeClassesMod
{
    /// <summary>The six things a class can start wearing, in the order the preset stores them.</summary>
    internal enum GearSlot { None, Head, Armor, MainHand, OffHand, Ring, Amulet }

    /// <summary>
    /// Chooses a class's starting gear from what the game actually has, and says what each piece
    /// does.
    ///
    /// Typing a name was the old way round, and it asked the wrong thing of whoever is writing a
    /// class: there are 905 items, their names are not guessable, and a typo only announced itself
    /// as "no item by that name" after the fact. Offering the items that fit the slot turns it into
    /// a choice instead of a memory test, and showing the stats beside the list answers the
    /// question the choice is really about.
    /// </summary>
    internal static class ItemPicker
    {
        private static GearSlot slot = GearSlot.None;
        private static string search = string.Empty;
        private static Vector2 listScroll;

        private static Dictionary<GearSlot, List<ItemInfo>> bySlot;
        private static readonly Dictionary<ItemInfo, string> Described = new Dictionary<ItemInfo, string>();

        internal static GearSlot Slot => slot;

        internal static void Open(GearSlot which) => slot = which;

        internal static void Close() => slot = GearSlot.None;

        internal static void Reset()
        {
            bySlot = null;
            Described.Clear();
        }

        // ------------------------------------------------------------------ the rows

        /// <summary>One slot's row in the Character tab: what is in it, and a way to change it.</summary>
        internal static string DrawSlot(GearSlot which, string label, string value, Action touched)
        {
            bool open = slot == which;

            GUILayout.BeginHorizontal(GUILayout.Height(26f));
            GUILayout.Label(label, GUILayout.Width(150f), GUILayout.Height(26f));

            ItemInfo resolved = string.IsNullOrEmpty(value) || !GameData.Ready
                ? null
                : GameData.FindItem(value, out _);

            var icon = GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f), GUILayout.Height(24f));
            if (resolved != null)
                Icons.Draw(icon, resolved.Icon);
            else if (!string.IsNullOrEmpty(value))
                ModManagerTab.Fill(icon, new Color(1f, 0.4f, 0.4f, 0.15f));

            string shown = string.IsNullOrEmpty(value)
                ? "(empty)"
                : (resolved == null ? value + "   [no item by that name]" : value);

            if (GUILayout.Button(shown, GUILayout.Width(260f), GUILayout.Height(26f)))
                slot = open ? GearSlot.None : which;

            // Always drawn, disabled when there is nothing to clear, rather than appearing only
            // when the slot is filled: a control that comes and goes changes the count IMGUI
            // measured on the layout pass, which throws inside Unity's own text field code.
            bool wasEnabled = GUI.enabled;
            GUI.enabled = !string.IsNullOrEmpty(value);

            bool clear = GUILayout.Button("Clear", GUILayout.Width(60f), GUILayout.Height(26f));

            GUI.enabled = wasEnabled;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(3f);

            if (!clear)
                return value;

            touched();
            return null;
        }

        // ------------------------------------------------------------- the side panel

        /// <summary>
        /// The list of what fits the open slot, and the stats of what is in it. Drawn in the
        /// Character tab's right-hand column, so choosing a piece never hides the rest of the class.
        /// </summary>
        internal static void DrawPanel(Rect area, ClassDefinition definition, Action touched)
        {
            if (slot == GearSlot.None)
            {
                GUI.Label(new Rect(area.x, area.y, area.width, 60f),
                    "Pick a gear slot on the left to choose what goes in it.", ModManagerTab.WrappedStyle());
                return;
            }

            float y = area.y;

            GUI.Label(new Rect(area.x, y, area.width, 22f), Title(slot));
            y += 26f;

            GUI.SetNextControlName("gear-search");
            search = GUI.TextField(new Rect(area.x, y, area.width - 66f, 24f), search ?? string.Empty);
            if (GUI.Button(new Rect(area.xMax - 62f, y, 62f, 24f), "Clear"))
                search = string.Empty;

            y += 30f;

            // The list gets the upper half and the stats the lower: both matter at once, since the
            // stats are how you tell two similarly named items apart.
            float listHeight = Mathf.Max(120f, (area.yMax - y) * 0.52f);
            DrawList(new Rect(area.x, y, area.width, listHeight), definition, touched);

            y += listHeight + 12f;

            ItemInfo current = Current(definition);
            if (current == null)
            {
                GUI.Label(new Rect(area.x, y, area.width, 40f), "This slot is empty.", ModManagerTab.NoteStyle());
                return;
            }

            GUIStyle wrapped = ModManagerTab.WrappedStyle();
            string text = Describe(current);
            GUI.Label(new Rect(area.x, y, area.width, Mathf.Max(40f, area.yMax - y)), text, wrapped);
        }

        private static void DrawList(Rect area, ClassDefinition definition, Action touched)
        {
            List<ItemInfo> candidates = Candidates(slot);

            if (!string.IsNullOrEmpty(search))
            {
                candidates = candidates
                    .Where(x => x.ItemName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            const float row = 26f;
            var content = new Rect(0f, 0f, area.width - 20f, candidates.Count * row);

            listScroll = GUI.BeginScrollView(area, listScroll, content);

            string chosen = Value(definition, slot);

            for (int i = 0; i < candidates.Count; i++)
            {
                var line = new Rect(0f, i * row, content.width, row - 2f);
                bool active = string.Equals(chosen, candidates[i].ItemName, StringComparison.OrdinalIgnoreCase);

                if (active)
                    ModManagerTab.Fill(line, new Color(0.847f, 0.651f, 0.341f, 0.20f));

                Icons.Draw(new Rect(line.x + 2f, line.y + 2f, 20f, 20f), candidates[i].Icon);

                if (GUI.Button(new Rect(line.x + 26f, line.y, line.width - 26f, line.height),
                        candidates[i].ItemName, ModManagerTab.ListItemStyle()))
                {
                    Assign(definition, slot, candidates[i].ItemName);
                    touched();
                }
            }

            GUI.EndScrollView();

            if (candidates.Count == 0)
                GUI.Label(new Rect(area.x + 6f, area.y + 6f, area.width, 24f), "nothing matches that filter",
                    ModManagerTab.NoteStyle());
        }

        private static string Title(GearSlot which)
        {
            switch (which)
            {
                case GearSlot.MainHand: return "Main hand";
                case GearSlot.OffHand: return "Off hand";
                default: return which.ToString();
            }
        }

        // -------------------------------------------------------------------- stats

        /// <summary>
        /// What a piece of gear does, as text.
        ///
        /// Built from the item's own fields rather than from the game's tooltip, which needs a
        /// character to scale against and a level to scale to. Values here are the item at level 1,
        /// which is what a class starts at.
        /// </summary>
        internal static string Describe(ItemInfo item)
        {
            if (item == null)
                return string.Empty;

            if (Described.TryGetValue(item, out string cached))
                return cached;

            var text = new StringBuilder();

            text.AppendLine(item.ItemName);
            text.AppendLine(item.Rarity + "  ·  " + (item is WeaponInfo weapon ? Describe(weapon) : item.ItemType.ToString()));

            if (item.MinLevel > 1)
                text.AppendLine("needs level " + item.MinLevel);

            text.AppendLine();

            Stat(text, "Might", item.Might);
            Stat(text, "Dexterity", item.Dexterity);
            Stat(text, "Vitality", item.Vitality);
            Stat(text, "Intelligence", item.Intelligence);
            Stat(text, "Reflex", item.Reflex);

            Stat(text, "Armour", item.ArmorRatio);
            Stat(text, "Magic armour", item.MagicArmorRatio);
            Stat(text, "Shield armour", item.ArmorRatioShield);
            Stat(text, "Shield magic armour", item.MagicArmorRatioShield);

            if (item is WeaponInfo weaponInfo)
            {
                try
                {
                    text.AppendLine("Damage  " + weaponInfo.GetWeaponDamageByLevel(1).ToString("0", CultureInfo.InvariantCulture)
                                    + "  " + weaponInfo.DamageType);
                    text.AppendLine("Range  " + weaponInfo.AttackRange);
                }
                catch (Exception)
                {
                    // Damage is worked out against global settings, which are not always loaded
                    // when this panel is first drawn. The rest of the entry is still worth showing.
                }
            }

            if (item.GrantedSkills != null && item.GrantedSkills.Count > 0)
            {
                string granted = string.Join(", ", item.GrantedSkills
                    .Where(x => x != null && x.SkillType != SkillType.Basic)
                    .Select(x => x.SkillName).ToArray());

                if (granted.Length > 0)
                    text.AppendLine("Grants  " + granted);
            }

            if (!string.IsNullOrEmpty(item.OptionalDescription))
            {
                text.AppendLine();
                text.AppendLine(item.OptionalDescription);
            }

            string result = text.ToString().TrimEnd();
            Described[item] = result;
            return result;
        }

        private static string Describe(WeaponInfo weapon)
        {
            string type = weapon.EquipmentType.ToString().Replace('_', ' ');
            return weapon.IsTwoHanded ? type + " (two handed)" : type;
        }

        private static void Stat(StringBuilder text, string label, float value)
        {
            if (Mathf.Abs(value) > 0.001f)
                text.AppendLine(label + "  " + value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// What can go in each slot. Rings and amulets have their own item type; a head slot takes
        /// Head and an armour slot Armor. Hands are the awkward pair: anything wieldable goes in the
        /// main hand, while the off hand also takes shields and refuses two-handed weapons, which is
        /// the one rule here that the game would otherwise enforce only at equip time.
        /// </summary>
        private static List<ItemInfo> Candidates(GearSlot which)
        {
            if (bySlot == null)
                Build();

            return bySlot.TryGetValue(which, out List<ItemInfo> found) ? found : new List<ItemInfo>();
        }

        private static void Build()
        {
            bySlot = new Dictionary<GearSlot, List<ItemInfo>>();

            if (!GameData.Ready || Burst2Flame.Game.Instance.Items == null)
                return;

            List<ItemInfo> all = Burst2Flame.Game.Instance.Items
                .Where(x => x != null && !string.IsNullOrEmpty(x.ItemName))
                .ToList();

            bySlot[GearSlot.Head] = Sorted(all.Where(x => x.ItemType == ItemType.Head));
            bySlot[GearSlot.Armor] = Sorted(all.Where(x => x.ItemType == ItemType.Armor));
            bySlot[GearSlot.Ring] = Sorted(all.Where(x => x.ItemType == ItemType.Ring));
            bySlot[GearSlot.Amulet] = Sorted(all.Where(x => x.ItemType == ItemType.Amulet));

            bySlot[GearSlot.MainHand] = Sorted(all.Where(x => x.ItemType == ItemType.Weapon));

            bySlot[GearSlot.OffHand] = Sorted(all.Where(x =>
                x.ItemType == ItemType.Shield ||
                (x.ItemType == ItemType.Weapon && !(x is WeaponInfo w && w.IsTwoHanded))));
        }

        private static List<ItemInfo> Sorted(IEnumerable<ItemInfo> items)
        {
            return items.OrderBy(x => x.Rarity).ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Value(ClassDefinition definition, GearSlot which)
        {
            EquipmentBlock gear = definition.Equipment;
            if (gear == null)
                return null;

            switch (which)
            {
                case GearSlot.Head: return gear.Head;
                case GearSlot.Armor: return gear.Armor;
                case GearSlot.MainHand: return gear.MainHand;
                case GearSlot.OffHand: return gear.OffHand;
                case GearSlot.Ring: return gear.Ring;
                case GearSlot.Amulet: return gear.Amulet;
                default: return null;
            }
        }

        private static void Assign(ClassDefinition definition, GearSlot which, string value)
        {
            definition.Equipment = definition.Equipment ?? new EquipmentBlock();
            EquipmentBlock gear = definition.Equipment;

            switch (which)
            {
                case GearSlot.Head: gear.Head = value; break;
                case GearSlot.Armor: gear.Armor = value; break;
                case GearSlot.MainHand: gear.MainHand = value; break;
                case GearSlot.OffHand: gear.OffHand = value; break;
                case GearSlot.Ring: gear.Ring = value; break;
                case GearSlot.Amulet: gear.Amulet = value; break;
            }
        }

        private static ItemInfo Current(ClassDefinition definition)
        {
            string value = Value(definition, slot);
            return string.IsNullOrEmpty(value) || !GameData.Ready ? null : GameData.FindItem(value, out _);
        }
    }
}
