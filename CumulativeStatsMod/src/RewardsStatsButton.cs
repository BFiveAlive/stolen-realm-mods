using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CumulativeStatsMod
{
    /// <summary>
    /// Adds a Battle Stats button to the adventure rewards screen - the summary the game shows
    /// once a run ends, in victory or defeat.
    ///
    /// That screen already reports the run as a whole: damage and healing totals, kills by enemy
    /// rank, the items and fortunes collected. What it has no route to is
    /// <c>StatManager</c>, the per-character breakdown reachable from every *other* post-battle
    /// screen, so the final fight is the one fight of a run whose stats cannot be looked at. This
    /// button opens that window, and because it is the same window, the This Battle / Run Total
    /// toggle is already on it: the last battle and the whole run, from one place.
    ///
    /// Built the same way as <see cref="StatsToggle"/> - cloned from a button already on the
    /// screen so it inherits the game's styling, stripped of the passengers a clone drags along,
    /// and pinned to a corner by anchor rather than by measurement.
    /// </summary>
    internal static class RewardsStatsButton
    {
        private const string ButtonName = "CumulativeStatsMod_RewardsStatsButton";

        /// <summary>
        /// AdventureRewards.instance falls back to FindObjectOfType whenever its cache is empty,
        /// which is most of the time - the window only exists at the end of a run. Calling it
        /// every frame would pay for a scene-wide search all game, so the lookup is throttled
        /// until it succeeds.
        /// </summary>
        private const float LookupIntervalSeconds = 0.5f;

        private static float nextLookupAt;
        private static AdventureRewards cachedRewards;

        private static Button button;
        private static TextMeshProUGUI label;

        private static Vector2 sourceSize = new Vector2(170f, 46f);
        private static Vector3 sourceScale = Vector3.one;

        /// <summary>
        /// Set while the stats window is up because this button opened it.
        ///
        /// It has to be tracked, for two reasons that were both bugs. The window is a plain
        /// GameObject that something else switches on and off; opened from here it came up
        /// *behind* the rewards screen, so it looked like nothing happened, and then nothing ever
        /// closed it, so it was still sitting there over the character select screen when the next
        /// game was started. Owning the window while it is ours fixes both.
        /// </summary>
        private static bool openedByUs;

        /// <summary>The sorting state to put back when the window closes.</summary>
        private static Canvas raised;
        private static bool hadCanvas;
        private static bool previousOverride;
        private static int previousOrder;

        /// <summary>Called every frame. Does nothing until the rewards screen is actually up.</summary>
        public static void Tick()
        {
            if (!ModConfig.Enabled.Value || !ModConfig.ShowOnRewardsScreen.Value)
            {
                Hide();
                return;
            }

            AdventureRewards rewards = Resolve();
            if (rewards == null || rewards.Contents == null || !rewards.Contents.activeInHierarchy)
            {
                // The screen has gone. Anything this button opened goes with it, rather than
                // surviving into whatever the game shows next.
                if (openedByUs)
                    CloseOurWindow();

                RunRecorder.Reset();
                return;
            }

            Ensure(rewards);
            Refresh();
        }

        /// <summary>
        /// A destroyed window leaves a reference that compares equal to null under Unity's
        /// operator, which is exactly the signal wanted here: the screen is gone, so the button
        /// that lived on it is gone too and the next one has to be rebuilt.
        /// </summary>
        private static AdventureRewards Resolve()
        {
            if (cachedRewards != null)
                return cachedRewards;

            button = null;
            label = null;

            if (Time.realtimeSinceStartup < nextLookupAt)
                return null;

            nextLookupAt = Time.realtimeSinceStartup + LookupIntervalSeconds;
            cachedRewards = AdventureRewards.instance;
            return cachedRewards;
        }

        private static void Ensure(AdventureRewards rewards)
        {
            if (button != null)
                return;

            try
            {
                Transform parent = rewards.Contents.transform;

                Button source = rewards.MainActionButton;
                GameObject go = source != null ? Clone(source, parent) : null;
                if (go == null)
                {
                    Plugin.Log.LogWarning("The rewards screen had no button to copy; the stats button was not added.");
                    return;
                }

                go.name = ButtonName;
                go.SetActive(true);
                StripInheritedBehaviours(go);

                button = go.GetComponent<Button>();
                if (button == null)
                {
                    UnityEngine.Object.Destroy(go);
                    Plugin.Log.LogError("Rewards stats button had no Button component; it was not added.");
                    return;
                }

                // RemoveAllListeners does not clear listeners wired up in the inspector, which a
                // clone inherits - here that would be "To Town" or "Retry", ending the run.
                // Replacing the whole event object is what actually detaches them.
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(OnClick);
                button.interactable = true;

                label = go.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                if (label != null)
                {
                    label.richText = true;
                    label.gameObject.SetActive(true);
                    label.text = "Battle Stats";
                }

                // Always opt out of layout rather than testing for a layout group: a disabled one
                // still returns non-null from GetComponent while doing no layout, so testing tells
                // you nothing useful.
                LayoutElement layoutElement = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;

                go.transform.SetAsLastSibling();
                Position();

                Plugin.Log.LogInfo("Added the Battle Stats button to the adventure rewards screen.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not add the rewards stats button: " + e);
                button = null;
            }
        }

        private static GameObject Clone(Button source, Transform parent)
        {
            // The rendered size, not sizeDelta: a stretch-anchored source has a sizeDelta near
            // zero, which becomes an invisible button once the clone is re-anchored to a corner.
            var rect = source.transform as RectTransform;
            if (rect != null)
            {
                Vector2 size = rect.rect.size;
                if (size.x > 1f && size.y > 1f)
                    sourceSize = size;

                sourceScale = rect.localScale;
            }

            return UnityEngine.Object.Instantiate(source.gameObject, parent);
        }

        /// <summary>
        /// Localisers rewrite the label out from under us, tooltip carriers pop the original
        /// button's help text, and HideBasedOnGUIState hides the object whenever the game changes
        /// GUI state. Everything else is left alone - the styling is the whole point of cloning.
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
                    break;
                }
            }
        }

        /// <summary>
        /// Anchor, pivot and anchored position all in the top-right corner, so the placement
        /// holds at any resolution or UI scale without measuring the panel.
        /// </summary>
        private static void Position()
        {
            var rect = button == null ? null : button.transform as RectTransform;
            if (rect == null)
                return;

            var topRight = new Vector2(1f, 1f);
            rect.anchorMin = topRight;
            rect.anchorMax = topRight;
            rect.pivot = topRight;

            rect.sizeDelta = sourceSize;
            rect.localScale = sourceScale * ModConfig.ButtonScale.Value;

            // Pivot in the top-right corner means moving inward is negative on both axes.
            rect.anchoredPosition = new Vector2(
                -ModConfig.RewardsButtonMarginX.Value,
                -ModConfig.RewardsButtonMarginY.Value);
        }

        private static void Refresh()
        {
            if (button == null)
                return;

            // Cheap, and it re-asserts placement if the screen relayouts behind us.
            Position();

            if (label != null)
                label.text = "Battle Stats";
        }

        /// <summary>
        /// Puts the window in front of the rewards screen.
        ///
        /// Both live on the same canvas, so whichever draws later wins, and the rewards screen is
        /// built after the stats window. A Canvas of its own with overrideSorting lifts the window
        /// clear without touching the game's own ordering; a raycaster comes with it or the window
        /// would be visible but dead to the mouse.
        /// </summary>
        private static void RaiseAbove(StatManager manager, AdventureRewards rewards)
        {
            try
            {
                var target = manager.gameObject;

                raised = target.GetComponent<Canvas>();
                hadCanvas = raised != null;

                if (!hadCanvas)
                    raised = target.AddComponent<Canvas>();
                else
                {
                    previousOverride = raised.overrideSorting;
                    previousOrder = raised.sortingOrder;
                }

                Canvas above = rewards == null ? null : rewards.GetComponentInParent<Canvas>();

                raised.overrideSorting = true;
                raised.sortingOrder = (above == null ? 0 : above.sortingOrder) + 100;

                if (target.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    target.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            catch (Exception e)
            {
                // Worst case it opens behind the rewards screen again, which is no worse than
                // before and not worth losing the window over.
                Plugin.Log.LogWarning("Could not raise the stats window: " + e.Message);
            }
        }

        private static void CloseOurWindow()
        {
            openedByUs = false;

            try
            {
                StatManager manager = StatManager.Instance;
                if (manager != null)
                    manager.CloseWindow();

                if (raised != null)
                {
                    // Put the ordering back rather than leaving a canvas the game did not ask for
                    // sitting on one of its windows for the rest of the session.
                    if (hadCanvas)
                    {
                        raised.overrideSorting = previousOverride;
                        raised.sortingOrder = previousOrder;
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(raised);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not close the stats window: " + e.Message);
            }
            finally
            {
                raised = null;
            }
        }

        private static void Hide()
        {
            if (button != null && button.gameObject.activeSelf)
                button.gameObject.SetActive(false);
        }

        /// <summary>
        /// Opens the stats window the way the game's own post-battle screen does, then hands it
        /// the party so it has characters to draw even if it was never opened this session.
        /// PostBattleManager sets exactly the same list before opening it.
        /// </summary>
        private static void OnClick()
        {
            try
            {
                if (StatManager.Instance == null)
                    StatManager.LoadInstanceReference();

                StatManager manager = StatManager.Instance;
                if (manager == null)
                {
                    Plugin.Log.LogWarning("The stats window could not be loaded, so it was not opened.");
                    return;
                }

                if (NetworkingManager.Instance != null && NetworkingManager.Instance.PartyCharacters != null)
                    manager.SetCharacters(NetworkingManager.Instance.PartyCharacters);

                // Fold everything up to this instant into the run total before it is drawn,
                // rather than up to the last scheduled sample.
                StatTracker.Poll();

                manager.OpenWindow();

                openedByUs = true;
                RaiseAbove(manager, cachedRewards);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Opening the stats window from the rewards screen failed: " + e);
            }
        }
    }
}
