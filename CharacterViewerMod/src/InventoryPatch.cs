using System;
using System.Collections.Generic;
using Burst2Flame;
using HarmonyLib;

namespace CharacterViewerMod
{
    /// <summary>
    /// Shows the viewed character's own bag in the character menu's inventory list.
    ///
    /// In play that list is the session's pool, <c>Root.OwnedUnequippedItems</c>: every unequipped item
    /// of every party character this player owns. A character viewed from the selection screen is not
    /// in the party or the session, so the pool has nothing of theirs and the list came up empty. While
    /// viewing, the list is filled from the character's own <c>Items</c> instead, keeping the unequipped
    /// ones exactly as the pool does.
    /// </summary>
    [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.RefreshInventory))]
    internal static class InventoryPatch
    {
        private static readonly List<SortType> SortTypes = new List<SortType> { SortType.Rarity, SortType.Level, SortType.Recent };

        [HarmonyPrefix]
        private static bool Prefix(InventoryManager __instance)
        {
            Character character = Viewer.Viewing;
            if (character == null)
                return true;

            try
            {
                if (__instance == null || __instance.Inventory == null || character.Items == null)
                    return true;

                var unequipped = new List<Item>();
                foreach (Item item in character.Items)
                {
                    if (item != null && !item.equipped)
                        unequipped.Add(item);
                }

                __instance.Inventory.InitializeInventory(unequipped, SortTypes);
                return false;
            }
            catch (Exception e)
            {
                // Fall back to the game's own list rather than leave the inventory broken.
                Plugin.Log.LogError("Could not show " + character.CharacterName + "'s own inventory: " + e);
                return true;
            }
        }
    }
}
