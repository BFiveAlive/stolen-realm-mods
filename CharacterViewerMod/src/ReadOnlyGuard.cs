using System.Collections.Generic;
using Burst2Flame;
using HarmonyLib;

namespace CharacterViewerMod
{
    /// <summary>
    /// Blocks changes to a character while it is being viewed from the selection screen.
    ///
    /// The character menu is fully interactive in play - equip, spend points, reassign, learn skills.
    /// From the selection screen those would be traps: a character that has not joined a party is not
    /// loaded into the session (<c>Character.Save</c> returns early unless <c>IsNetworkLoaded</c>), so a
    /// change would appear to work and then be lost. Each prefix lets the call through normally unless
    /// the viewer is open, so nothing changes in play.
    /// </summary>
    [HarmonyPatch]
    internal static class ReadOnlyGuard
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.AddAttribute), new[] { typeof(CharacterAttribute), typeof(float), typeof(float) })]
        private static bool AddAttribute() => Allow("spending an attribute point");

        // The skill tree calls both of these whenever it closes or you switch tabs away from it,
        // usually with empty lists. Those are still blocked, but only a real change shows the notice.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.AddSkills), new[] { typeof(List<SkillInfo>), typeof(bool) })]
        private static bool AddSkills(List<SkillInfo> skillInfos) => Allow("learning a skill", skillInfos != null && skillInfos.Count > 0);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.RemoveSkills), new[] { typeof(List<SkillInfo>), typeof(bool) })]
        private static bool RemoveSkills(List<SkillInfo> skillInfos) => Allow("removing a skill", skillInfos != null && skillInfos.Count > 0);

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.EquipItem), new[] { typeof(Item), typeof(bool) })]
        private static bool EquipItem() => Allow("equipping an item");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.UnequipItem), new[] { typeof(Item), typeof(bool), typeof(bool) })]
        private static bool UnequipItem() => Allow("unequipping an item");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FortuneWindow), nameof(FortuneWindow.UnequipFortune), new[] { typeof(int) })]
        private static bool UnequipFortune() => Allow("removing a fortune");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.BeginAttributeRespec))]
        private static bool BeginAttributeRespec() => Allow("reassigning attribute points");

        /// <summary>
        /// True lets the call through. While viewing, every call is blocked, but only one that would
        /// actually change something shows the notice - the game makes harmless empty calls too.
        /// </summary>
        private static bool Allow(string what, bool isChange = true)
        {
            if (!Viewer.Active)
                return true;

            if (isChange)
                Viewer.NotifyBlocked(what);

            return false;
        }
    }
}
