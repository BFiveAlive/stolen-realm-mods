using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Burst2Flame;

namespace AutoEquipMod
{
    /// <summary>One item worth offering, and everything the prompt needs to explain itself.</summary>
    internal sealed class Offer
    {
        public Character Character;
        public Item Item;

        /// <summary>What it would displace, or null when the slot is empty.</summary>
        public Item Replacing;

        public float NewScore;
        public float OldScore;

        public bool SlotEmpty => Replacing == null;

        public float PercentBetter =>
            OldScore <= 0.01f ? 100f : (NewScore - OldScore) / OldScore * 100f;
    }

    /// <summary>
    /// Decides whether a newly acquired item is worth offering, and against what.
    ///
    /// The score is a weighted sum of things the game states plainly - level, rarity, armour,
    /// how many modifiers rolled - rather than an attempt to model what an item is actually worth
    /// to a particular build, which no fixed formula gets right. It is used only to decide whether
    /// to ask; the prompt shows the numbers and the choice stays with the player.
    /// </summary>
    internal static class Appraisal
    {
        public static Offer Consider(Character character, Item item)
        {
            if (character == null || item == null || item.ItemInfo == null)
                return null;

            if (!item.ItemInfo.IsEquippable || !WantsType(item.ItemInfo.ItemType))
                return null;

            // A character cannot be offered something it is not allowed to wear.
            if (!item.MeetsEquipLevelRequirements(character))
                return null;

            // Already worn, or already offered and declined.
            if (item.equipped)
                return null;

            Item current = CurrentlyIn(character, item);

            var offer = new Offer
            {
                Character = character,
                Item = item,
                Replacing = current,
                NewScore = Score(item),
                OldScore = current != null ? Score(current) : 0f
            };

            if (ModConfig.LogDecisions.Value)
            {
                Plugin.Log.LogInfo(string.Format(
                    "{0}: {1} scores {2:0.0} against {3} ({4:0.0})",
                    character.CharacterName, Name(item), offer.NewScore,
                    current != null ? Name(current) : "an empty slot", offer.OldScore));
            }

            if (offer.SlotEmpty)
                return offer;

            if (ModConfig.OnlyUpgrades.Value && offer.NewScore <= offer.OldScore)
                return null;

            if (offer.PercentBetter < ModConfig.MinimumImprovement.Value)
                return null;

            return offer;
        }

        /// <summary>
        /// What the item would displace. The game works out which slots an item occupies, so this
        /// asks it rather than reimplementing the rules for two-handed weapons, dual wielding and
        /// the two ring slots.
        /// </summary>
        private static Item CurrentlyIn(Character character, Item item)
        {
            ItemEquipInfo plan;

            try
            {
                plan = character.GetItemEquipInfo(item.ItemInfo);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not work out where " + Name(item) + " would go: " + e.Message);
                return null;
            }

            var displaced = new List<Item>();

            foreach (int slot in plan.ToUnequip)
            {
                var worn = character.EquippedItems.FirstOrDefault(x => x != null && x.equippedSlotIndex == slot);
                if (worn != null)
                    displaced.Add(worn);
            }

            if (displaced.Count == 0)
            {
                return character.EquippedItems
                    .FirstOrDefault(x => x != null && x.equippedSlotIndex == plan.EquipIndex);
            }

            // A two-hander displaces two items; comparing against the weaker one would make the
            // swap look better than it is, so the whole cost of the swap is what it is judged on.
            return displaced.OrderByDescending(Score).First();
        }

        public static float Score(Item item)
        {
            if (item == null || item.ItemInfo == null)
                return 0f;

            float score = 0f;

            score += item.EffectiveLevel * ModConfig.WeightItemLevel.Value;

            // The game's own stat multiplier for the rarity, so the ordering here is the game's
            // ordering rather than a guess at what "Rare" is worth relative to "Legendary".
            score += RarityFactor(item) * ModConfig.WeightRarity.Value;

            score += Safe(() => item.Armor) * ModConfig.WeightArmour.Value;
            score += Safe(() => item.MagicArmor) * ModConfig.WeightMagicArmour.Value;
            score += ModCount(item) * ModConfig.WeightMods.Value;

            return score;
        }

        private static float RarityFactor(Item item)
        {
            try
            {
                return ItemInfo.GetStatRarityMod(item.ItemInfo.Rarity);
            }
            catch
            {
                return 1f;
            }
        }

        private static int ModCount(Item item)
        {
            try
            {
                return item.ItemMods != null ? item.ItemMods.Count : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static float Safe(Func<float> read)
        {
            try
            {
                float value = read();
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool WantsType(ItemType type)
        {
            switch (type)
            {
                case ItemType.Weapon: return ModConfig.ConsiderWeapons.Value;
                case ItemType.Shield: return ModConfig.ConsiderShields.Value;
                case ItemType.Head:
                case ItemType.Armor: return ModConfig.ConsiderArmour.Value;
                case ItemType.Ring:
                case ItemType.Amulet: return ModConfig.ConsiderJewellery.Value;
                default: return false;
            }
        }

        public static string Name(Item item)
        {
            try
            {
                return string.IsNullOrEmpty(item.ItemName) ? item.ItemInfo.ItemName : item.ItemName;
            }
            catch
            {
                return "an item";
            }
        }

        /// <summary>A one-line summary of what the item is, for the prompt.</summary>
        public static string Describe(Item item)
        {
            if (item == null)
                return "nothing";

            var parts = new List<string>();

            try
            {
                parts.Add(item.ItemInfo.Rarity + " " + item.ItemInfo.ItemType);
                parts.Add("level " + item.itemLevel.ToString(CultureInfo.InvariantCulture));

                float armour = Safe(() => item.Armor);
                if (armour > 0f)
                    parts.Add("armour " + armour.ToString("0.#", CultureInfo.InvariantCulture));

                float magic = Safe(() => item.MagicArmor);
                if (magic > 0f)
                    parts.Add("magic armour " + magic.ToString("0.#", CultureInfo.InvariantCulture));

                int mods = ModCount(item);
                if (mods > 0)
                    parts.Add(mods + (mods == 1 ? " modifier" : " modifiers"));
            }
            catch
            {
                // A partially built item during a load can throw on any of these; whatever was
                // gathered before that is still worth showing.
            }

            return string.Join("  ·  ", parts.ToArray());
        }
    }
}
