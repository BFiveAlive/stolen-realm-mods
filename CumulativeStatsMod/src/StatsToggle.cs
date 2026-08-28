using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Adds a two-state button to the top right of the battle stats window that switches the
    /// numbers between the battle that just ended and the running total for the whole run.
    ///
    /// Only the values change. The section and row labels the window builds in
    /// <c>StatManager.BuildOutLabels</c> are identical in both views, so they are left alone.
    /// </summary>
    internal static class StatsToggle
    {
        private const string ButtonName = "CumulativeStatsMod_ToggleButton";

        /// <summary>Sticky for the session; the config only decides where it starts.</summary>
        private static bool showCumulative;
        private static bool modeInitialised;

        private static readonly Vector2 FallbackSize = new Vector2(170f, 46f);

        private static Button button;
        private static TextMeshProUGUI label;
        private static bool hierarchyLogged;

        // Measured off the button we cloned, at clone time. Its *rendered* size, not its
        // sizeDelta: a stretch-anchored source has a sizeDelta near zero, which would produce an
        // invisible button once we re-anchor the clone to a corner.
        private static Vector2 sourceSize = FallbackSize;
        private static Vector3 sourceScale = Vector3.one;

        /// <summary>
        /// The window's rects are meaningless until Unity has laid it out at least once, and
        /// <c>FindStyleSource</c> judges candidates by their size. Giving layout a few frames
        /// stops a first-open race from permanently falling back to the built-from-scratch look.
        /// </summary>
        private const int LayoutSettleFrames = 10;

        private static int framesOpen;

        // The list the window was last populated with, so a toggle can redraw the same rows.
        private static List<Character> lastCharacters;

        public static bool ShowCumulative
        {
            get
            {
                if (!modeInitialised)
                {
                    showCumulative = ModConfig.DefaultToCumulative.Value;
                    modeInitialised = true;
                }

                return showCumulative && ModConfig.Enabled.Value;
            }
        }

        public static void CaptureCharacters(List<Character> characters)
        {
            lastCharacters = characters;
        }

        /// <summary>Creates the button once. Safe to call every frame.</summary>
        public static void Ensure(StatManager manager)
        {
            if (button != null || manager == null || !ModConfig.Enabled.Value)
                return;

            // Content is only active once the window has actually been opened and populated,
            // which is also the point at which its child rects have real sizes.
            if (manager.Content == null || !manager.Content.activeInHierarchy)
            {
                framesOpen = 0;
                return;
            }

            framesOpen++;

            try
            {
                Transform parent = ResolveParent(manager);
                if (parent == null)
                    return;

                Button source = FindStyleSource(manager);
                if (source == null && framesOpen < LayoutSettleFrames)
                    return;

                GameObject go = source != null ? Clone(source, parent) : Build(manager, parent);
                if (go == null)
                    return;

                go.name = ButtonName;
                go.SetActive(true);

                if (source != null)
                    StripInheritedBehaviours(go);

                button = go.GetComponent<Button>();
                if (button == null)
                {
                    UnityEngine.Object.Destroy(go);
                    Plugin.Log.LogError("Toggle button had no Button component; the toggle is disabled.");
                    return;
                }

                // RemoveAllListeners does NOT clear listeners wired up in the Unity inspector,
                // which a clone inherits from whatever it was cloned off. Replacing the whole
                // event object does.
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OnClick);
                button.interactable = true;

                label = go.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (label != null)
                {
                    label.richText = true;
                    label.gameObject.SetActive(true);
                }

                // Opt out of any layout group on the parent. Testing for one with GetComponent
                // is not enough: a *disabled* LayoutGroup still returns non-null while doing no
                // layout, so the safe move is to always add the opt-out.
                LayoutElement layoutElement = go.GetComponent<LayoutElement>();
                if (layoutElement == null)
                    layoutElement = go.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;

                go.transform.SetAsLastSibling();
                Position();

                var parentRect = parent as RectTransform;
                Plugin.Log.LogInfo(
                    "Stats toggle created under '" + parent.name + "'" +
                    (parentRect != null ? " rect=" + parentRect.rect.width + "x" + parentRect.rect.height : "") +
                    (source != null ? ", cloned from '" + source.name + "'" : ", built from scratch") + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not create the stats toggle button: " + e);
                button = null;
            }
        }

        /// <summary>
        /// <c>Content</c> is the window's content root — the object <c>InitSingleton</c> hides and
        /// <c>ExecuteInit</c> shows — so it is both the right thing to anchor against and the
        /// right thing to be hidden alongside. The window's own transform is not: it spans the
        /// whole screen, and its top-right corner is the screen corner, not the panel's.
        /// </summary>
        private static Transform ResolveParent(StatManager manager)
        {
            return manager.Content == null ? null : manager.Content.transform;
        }

        /// <summary>
        /// A cloned button drags along whatever else the original had bolted to it. Three kinds
        /// of passenger actively break the clone: localisers (<c>LocalizedObject</c>,
        /// <c>ManualLocalization</c>, <c>PrefabLocalizer</c>) rewrite the label out from under
        /// us, tooltip carriers pop the original button's help text on hover, and
        /// <c>HideBasedOnGUIState</c> hides the object whenever the game changes GUI state.
        /// Anything else is left in place — that styling is the reason for cloning at all.
        /// </summary>
        private static void StripInheritedBehaviours(GameObject go)
        {
            string[] unwanted = { "Localiz", "Tooltip", "GUIState", "OnHover" };

            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                foreach (string fragment in unwanted)
                {
                    if (typeName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    UnityEngine.Object.Destroy(behaviour);
                    Plugin.Log.LogInfo("Removed inherited '" + typeName + "' from the stats toggle.");
                    break;
                }
            }
        }

        /// <summary>
        /// Picks an existing button in the window to copy styling from, so the toggle looks
        /// native instead of approximating the game's theme. Oversized buttons are skipped:
        /// those are usually invisible full-panel click catchers.
        /// </summary>
        private static Button FindStyleSource(StatManager manager)
        {
            Button best = null;
            float bestArea = 0f;

            foreach (Button candidate in manager.GetComponentsInChildren<Button>(includeInactive: true))
            {
                if (candidate == null || candidate.name == ButtonName)
                    continue;

                var rect = candidate.transform as RectTransform;
                if (rect == null)
                    continue;

                float width = rect.rect.width;
                float height = rect.rect.height;
                if (width <= 20f || height <= 10f || width > 500f || height > 200f)
                    continue;

                // A button with a text child gives us the game's font and material for free.
                bool hasText = candidate.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true) != null;
                float score = (hasText ? 1000000f : 0f) + width * height;
                if (score > bestArea)
                {
                    bestArea = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static GameObject Clone(Button source, Transform parent)
        {
            var rect = source.transform as RectTransform;
            if (rect != null)
            {
                Vector2 size = rect.rect.size;
                sourceSize = size.x > 1f && size.y > 1f ? size : FallbackSize;
                sourceScale = rect.localScale;
            }

            return UnityEngine.Object.Instantiate(source.gameObject, parent);
        }

        /// <summary>
        /// Fallback for a window with no button worth cloning. The font is still lifted from the
        /// window so the text matches, even though the frame is our own.
        /// </summary>
        private static GameObject Build(StatManager manager, Transform parent)
        {
            sourceSize = FallbackSize;
            sourceScale = Vector3.one;

            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, worldPositionStays: false);

            var background = go.GetComponent<Image>();
            background.color = new Color(0.11f, 0.11f, 0.13f, 0.92f);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = FallbackSize;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(go.transform, worldPositionStays: false);

            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 16f;
            text.color = Color.white;

            TextMeshProUGUI fontSource = manager.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (fontSource != null)
            {
                text.font = fontSource.font;
                text.fontSharedMaterial = fontSource.fontSharedMaterial;
            }

            return go;
        }

        /// <summary>
        /// Pins the button to the top-right corner of the panel. Anchor, pivot and anchored
        /// position all sit in that corner, so the placement holds at any resolution or UI scale
        /// without measuring anything.
        /// </summary>
        private static void Position()
        {
            if (button == null)
                return;

            var rect = button.transform as RectTransform;
            if (rect == null)
                return;

            var topRight = new Vector2(1f, 1f);
            rect.anchorMin = topRight;
            rect.anchorMax = topRight;
            rect.pivot = topRight;

            rect.sizeDelta = sourceSize;
            rect.localScale = sourceScale * ModConfig.ButtonScale.Value;

            // With the pivot in the top-right corner, moving inward means going negative on both
            // axes. The offsets stay intuitive: +X right, +Y up.
            rect.anchoredPosition = new Vector2(
                -ModConfig.ButtonMarginX.Value + ModConfig.ButtonOffsetX.Value,
                -ModConfig.ButtonMarginY.Value + ModConfig.ButtonOffsetY.Value);
        }

        /// <summary>Per-frame label and placement refresh while the window is open.</summary>
        public static void UpdateState(StatManager manager)
        {
            // Read live rather than latched, so flipping the config with hot reload on dumps the
            // hierarchy without needing a restart.
            if (!ModConfig.LogWindowHierarchy.Value)
                hierarchyLogged = false;
            else if (!hierarchyLogged && manager != null && manager.Content != null && manager.Content.activeInHierarchy)
            {
                hierarchyLogged = true;
                LogHierarchy(manager.transform, 0);
            }

            if (button == null)
                return;

            bool show = ModConfig.Enabled.Value;
            if (button.gameObject.activeSelf != show)
                button.gameObject.SetActive(show);

            if (!show)
                return;

            // Cheap, and it re-asserts placement if anything in the window relayouts.
            Position();

            if (label != null)
                label.text = BuildLabel();
        }

        private static string BuildLabel()
        {
            int subSize = Mathf.Clamp(ModConfig.SubtextScale.Value, 10, 100);
            string title = ShowCumulative ? "Run Total" : "This Battle";
            string hint = ShowCumulative ? "show this battle" : "show run total";

            return title + "\n<size=" + subSize + "%>" + hint + "</size>";
        }

        private static void OnClick()
        {
            try
            {
                showCumulative = !ShowCumulative;
                modeInitialised = true;

                // Make sure the run total includes everything up to this instant before it is
                // drawn, rather than up to the last scheduled poll.
                StatTracker.Poll();
                Repopulate();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Stats toggle failed: " + e);
            }
        }

        public static void Repopulate()
        {
            StatManager manager = StatManager.Instance;
            if (manager == null)
                return;

            List<Character> characters = lastCharacters;
            if (characters == null && NetworkingManager.Instance != null)
                characters = NetworkingManager.Instance.PartyCharacters;

            if (characters != null)
                manager.PopulateStats(characters);
        }

        private static void LogHierarchy(Transform transform, int depth)
        {
            var rect = transform as RectTransform;
            Plugin.Log.LogInfo(
                new string(' ', depth * 2) + transform.name +
                " [" + string.Join(", ", ComponentNames(transform)) + "]" +
                (rect != null ? " " + rect.rect.width + "x" + rect.rect.height : ""));

            for (int i = 0; i < transform.childCount; i++)
                LogHierarchy(transform.GetChild(i), depth + 1);
        }

        private static string[] ComponentNames(Transform transform)
        {
            Component[] components = transform.GetComponents<Component>();
            var names = new string[components.Length];
            for (int i = 0; i < components.Length; i++)
                names[i] = components[i] == null ? "<missing>" : components[i].GetType().Name;

            return names;
        }
    }
}
