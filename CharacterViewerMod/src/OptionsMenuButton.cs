using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterViewerMod
{
    /// <summary>
    /// Adds "View Character" to a character tile's options menu - the one right-click opens, with
    /// Rename and Delete.
    ///
    /// The button is a copy of one already in that menu, so it has the game's styling without
    /// rebuilding it. Two things a copy drags along are removed: listeners wired in the Unity
    /// inspector (<c>RemoveAllListeners</c> does not clear those, so the whole click event is replaced)
    /// and localisers/tooltips that would rewrite the label or show the original's help text.
    /// </summary>
    [HarmonyPatch(typeof(CharacterChoiceItem), nameof(CharacterChoiceItem.ShowEllipseMenu))]
    internal static class OptionsMenuButton
    {
        internal const string ButtonName = "CharacterViewerMod_ViewButton";
        private const string Label = "View Character";

        private static bool hierarchyLogged;

        [HarmonyPostfix]
        private static void Postfix(CharacterChoiceItem __instance)
        {
            try
            {
                if (!Plugin.Enabled.Value || __instance == null || __instance.Character == null
                    || __instance.ellipseMenu == null || !__instance.ellipseMenu.activeSelf)
                    return;

                Ensure(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not add View Character to the options menu: " + e);
            }
        }

        /// <summary>The tile's View Character button, created on first use. Null if it cannot be made.</summary>
        internal static Button Ensure(CharacterChoiceItem item)
        {
            Transform menu = item.ellipseMenu.transform;

            Transform existing = FindButton(menu);
            if (existing != null)
                return existing.GetComponent<Button>();

            if (!hierarchyLogged)
            {
                hierarchyLogged = true;
                LogHierarchy(menu, 0);
            }

            Button source = menu.GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(b => b != null && b.name != ButtonName
                                     && b.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true) != null);

            if (source == null)
            {
                Plugin.Log.LogWarning("The character options menu has no button to copy; View Character was not added.");
                return null;
            }

            GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            copy.name = ButtonName;
            StripInheritedBehaviours(copy);

            // First among the options, where it is found before Rename and Delete.
            copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex());

            Button button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => OnClick(item));
            button.interactable = true;

            TextMeshProUGUI text = copy.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (text != null)
                text.text = Label;

            copy.SetActive(true);

            Plugin.Log.LogInfo("Added View Character to the character options menu, copied from '" + source.name
                               + "' under '" + source.transform.parent.name + "'.");
            return button;
        }

        internal static Transform FindButton(Transform menu)
        {
            return menu.GetComponentsInChildren<Transform>(includeInactive: true).FirstOrDefault(t => t.name == ButtonName);
        }

        private static void OnClick(CharacterChoiceItem item)
        {
            try
            {
                Character character = item.Character;
                item.HideEllipseMenu();
                Viewer.Open(character);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("View Character failed: " + e);
            }
        }

        private static void StripInheritedBehaviours(GameObject go)
        {
            string[] unwanted = { "Localiz", "Tooltip", "GUIState", "OnHover" };

            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (unwanted.Any(fragment => typeName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                    UnityEngine.Object.Destroy(behaviour);
            }
        }

        /// <summary>Once, so a layout problem can be diagnosed from the log alone.</summary>
        private static void LogHierarchy(Transform transform, int depth)
        {
            var rect = transform as RectTransform;
            string components = string.Join(", ", transform.GetComponents<Component>()
                .Select(c => c == null ? "<missing>" : c.GetType().Name));

            Plugin.Log.LogInfo(new string(' ', depth * 2) + transform.name + " [" + components + "]"
                               + (rect != null ? " " + rect.rect.width.ToString("0") + "x" + rect.rect.height.ToString("0") : string.Empty)
                               + (transform.gameObject.activeSelf ? string.Empty : " (inactive)"));

            for (int i = 0; i < transform.childCount; i++)
                LogHierarchy(transform.GetChild(i), depth + 1);
        }
    }
}
