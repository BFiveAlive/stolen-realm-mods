using System;
using System.Linq;
using Burst2Flame;
using HarmonyLib;

namespace AutoEquipMod
{
    /// <summary>
    /// Notices an item arriving in a character's inventory.
    ///
    /// <c>Character.AddToItemList</c> is the one place every item passes through, whatever brought
    /// it - loot, a shop, crafting, a quest reward - which is what makes "from any source" possible
    /// without hooking each of those separately.
    ///
    /// It is also how a save is loaded: restoring a character hands it every item it already owns,
    /// one at a time, and nothing about the call distinguishes that from picking something up. The
    /// settle window in <see cref="Watcher"/> is what keeps a load from producing a hundred offers.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.AddToItemList))]
    internal static class AcquisitionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Character __instance, Item item)
        {
            // A postfix, so the item is fully in the inventory before anything looks at it, and
            // so a fault here cannot stop the character from receiving it.
            try
            {
                Watcher.Notice(__instance, item);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Ignoring an acquired item after an error: " + e);
            }
        }
    }

    /// <summary>
    /// Decides which acquisitions are worth acting on, and holds the ones that are.
    /// </summary>
    internal static class Watcher
    {
        /// <summary>Set on every scene load; acquisitions are ignored until it has elapsed.</summary>
        public static float QuietUntil;

        public static void Notice(Character character, Item item)
        {
            if (!ModConfig.Enabled.Value || character == null || item == null)
                return;

            if (UnityEngine.Time.realtimeSinceStartup < QuietUntil)
                return;

            if (!IsMine(character))
                return;

            var offer = Appraisal.Consider(character, item);
            if (offer == null)
                return;

            // Nothing is lost by filling a slot that is empty, so there is nothing to ask about.
            if (offer.SlotEmpty && ModConfig.FillEmptySlotsSilently.Value)
            {
                Equipper.Equip(offer, "empty slot");
                return;
            }

            if (!ModConfig.AskBeforeReplacing.Value)
            {
                Equipper.Equip(offer, "asking is off");
                return;
            }

            Prompt.Enqueue(offer);
        }

        /// <summary>
        /// Only the local player's own characters. In a multiplayer game the others belong to
        /// someone else, and equipping their items would be both rude and desynchronising.
        /// </summary>
        private static bool IsMine(Character character)
        {
            try
            {
                var logic = GameLogic.instance;
                if (logic == null)
                    return false;

                if (logic.AllMyCharacters != null && logic.AllMyCharacters.Contains(character))
                    return true;

                var controlled = logic.ControlledCharacters;
                return controlled != null && controlled.Contains(character);
            }
            catch
            {
                // Asked too early, before the party exists. Treating that as "not mine" costs a
                // missed offer; treating it as "mine" would act on someone else's character.
                return false;
            }
        }
    }

    /// <summary>Performs the swap, through the game's own equip path.</summary>
    internal static class Equipper
    {
        public static bool Equip(Offer offer, string reason)
        {
            try
            {
                // The game's own method: it unequips whatever conflicts, sets the slot index,
                // refreshes the character's effects and saves. Reimplementing any of that would
                // be a way to produce a character the game does not agree with.
                offer.Character.EquipItem(offer.Item);

                Plugin.Log.LogInfo(string.Format("Equipped {0} on {1} ({2}){3}",
                    Appraisal.Name(offer.Item), offer.Character.CharacterName, reason,
                    offer.Replacing != null ? ", replacing " + Appraisal.Name(offer.Replacing) : string.Empty));

                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not equip " + Appraisal.Name(offer.Item) + ": " + e);
                return false;
            }
        }
    }
}
