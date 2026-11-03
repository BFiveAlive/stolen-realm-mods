using System;
using Burst2Flame;
using HarmonyLib;

namespace GentleFoesMod
{
    /// <summary>
    /// Changes what an enemy is called on screen, and only on screen.
    ///
    /// The obvious move - renaming the <c>CharacterInfo</c> asset - is the wrong one. A spawning
    /// enemy copies its name from that asset, but so does the summon system, which matches summons
    /// by <c>CharacterInfo.name</c>; renaming a Wolf would quietly break every skill that summons
    /// one. The asset keeps its name and the display is rewritten instead.
    ///
    /// <c>LocalizedCharacterName</c> is where the UI asks, so it is where the answer changes.
    /// Anything reading the raw <c>CharacterName</c> would still show the original - from a read of
    /// the game's code that appears to be internal bookkeeping rather than anything on screen, but
    /// it is the thing to check first if a stray "Forest Bear" turns up somewhere.
    /// </summary>
    [HarmonyPatch(typeof(Character), "LocalizedCharacterName", MethodType.Getter)]
    internal static class NamePatch
    {
        [HarmonyPostfix]
        private static void Rename(Character __instance, ref string __result)
        {
            try
            {
                // Players are never renamed: a character of yours could share a name with an enemy,
                // and seeing your own fighter renamed would be alarming.
                if (__instance == null || !__instance.IsAI)
                    return;

                if (EnemySwapper.TryRename(__instance.CharacterName, out string replacement))
                    __result = replacement;
            }
            catch (Exception)
            {
                // A name is not worth throwing over, and this runs inside the game's own UI draw.
            }
        }
    }
}
