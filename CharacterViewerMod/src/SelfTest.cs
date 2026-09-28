using System;
using System.IO;
using System.Linq;
using Burst2Flame;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterViewerMod
{
    /// <summary>
    /// -cvtest &lt;folder&gt;: walks the viewer through every screen on the character selection screen
    /// and reports what happened, with screenshots taken by the game itself.
    ///
    /// Opens the character, skill tree and fortunes tabs for the first character not in the party,
    /// closes and checks the selection was restored, opens the options menu and uses the View
    /// Character button, and finally tries a change (which must be blocked). The change comes last
    /// because blocking it shows the view-only notice, which nobody is there to dismiss and which would
    /// cover every screenshot after it. Changes nothing that is saved. Off unless the argument is given.
    /// </summary>
    internal static class SelfTest
    {
        private enum Step
        {
            Wait, OpenCharacter, ShotCharacter, OpenSkills, ShotSkills, OpenFortunes, ShotFortunes,
            Close, CheckClosed, OpenOptions, UseButton, ShotViaButton, TryChange, FinalClose, Done
        }

        private static string shotDir;
        private static Step step = Step.Wait;
        private static float nextAt;
        private static Character subject;
        private static Character selectionBefore;
        private static int shots;

        internal static bool Enabled => shotDir != null;

        internal static void ReadCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-cvtest", StringComparison.OrdinalIgnoreCase))
                    shotDir = args[i + 1];
            }
        }

        internal static void Tick(CharacterChoiceManager manager)
        {
            if (!Enabled || step == Step.Done || Time.realtimeSinceStartup < nextAt)
                return;

            try
            {
                Advance(manager);
            }
            catch (Exception e)
            {
                Say("FAILED at " + step + ": " + e);
                step = Step.Done;
            }
        }

        private static void Advance(CharacterChoiceManager manager)
        {
            GameLogic logic = GameLogic.instance;
            float now = Time.realtimeSinceStartup;

            switch (step)
            {
                case Step.Wait:
                {
                    CharacterChoiceItem item = TileFor(manager, null);
                    if (item == null)
                    {
                        nextAt = now + 1f;
                        return;
                    }

                    subject = item.Character;
                    selectionBefore = logic.CurrentlySelectedCharacter;
                    Say("subject '" + subject.CharacterName + "' level " + subject.Level
                        + (subject.IsRoguelikeCharacter ? " (roguelike)" : " (campaign)")
                        + ", in session=" + subject.IsNetworkLoaded + ", has model=" + (subject.PlayerMovement != null)
                        + "; selection before: " + Name(selectionBefore));

                    nextAt = now + 3f;
                    step = Step.OpenCharacter;
                    return;
                }

                case Step.OpenCharacter:
                    Viewer.Open(subject);
                    nextAt = now + 5f;
                    step = Step.ShotCharacter;
                    return;

                case Step.ShotCharacter:
                {
                    InventoryManager inventory = LoadableUIWindow<InventoryManager>.Instance;
                    int owned = subject.Items != null ? subject.Items.Count : -1;
                    int unequipped = subject.Items != null ? subject.Items.Count(x => x != null && !x.equipped) : -1;
                    int listed = inventory != null && inventory.Inventory != null && inventory.Inventory.AllItems != null
                        ? inventory.Inventory.AllItems.Count
                        : -1;

                    Say("character tab: viewing=" + Viewer.Active + ", menu open=" + MenuOpen()
                        + ", inventory showing " + Name(inventory != null ? inventory.CurrentInventoryCharacter : null)
                        + ", inventory open=" + (inventory != null && inventory.gameObject.activeInHierarchy)
                        + "; items owned " + owned + ", unequipped " + unequipped + ", in the inventory list " + listed
                        + (listed == unequipped ? " (matches)" : " (MISMATCH)"));
                    Shoot("character");
                    nextAt = now + 3f;
                    step = Step.OpenSkills;
                    return;
                }

                case Step.OpenSkills:
                    CharacterMenusManager.Instance.OpenSkillTreeMenu();
                    nextAt = now + 5f;
                    step = Step.ShotSkills;
                    return;

                case Step.ShotSkills:
                    Say("skill tree tab: viewing=" + Viewer.Active + ", menu open=" + MenuOpen()
                        + ", campaign tree open=" + SkillTreeManager.IsNotNullAndIsActive
                        + ", roguelike tree open=" + SkillTreeManagerRoguelike.IsNotNullAndIsActive);
                    Shoot("skills");
                    nextAt = now + 3f;
                    step = Step.OpenFortunes;
                    return;

                case Step.OpenFortunes:
                    CharacterMenusManager.Instance.OpenFortuneMenu();
                    nextAt = now + 5f;
                    step = Step.ShotFortunes;
                    return;

                case Step.ShotFortunes:
                    Say("fortunes tab: viewing=" + Viewer.Active + ", menu open=" + MenuOpen()
                        + ", fortunes open=" + FortuneWindow.IsNotNullAndIsActive
                        + ", view-only notice shown without a change=" + NoticeOpen());
                    Shoot("fortunes");
                    nextAt = now + 3f;
                    step = Step.Close;
                    return;

                case Step.Close:
                    CharacterMenusManager.Instance.CloseWindow();
                    nextAt = now + 3f;
                    step = Step.CheckClosed;
                    return;

                case Step.CheckClosed:
                    Say("after closing: viewing=" + Viewer.Active + ", menu open=" + MenuOpen()
                        + ", selection " + Name(logic.CurrentlySelectedCharacter) + " (before: " + Name(selectionBefore) + ")"
                        + (logic.CurrentlySelectedCharacter == selectionBefore ? " - restored" : " - NOT RESTORED"));
                    Shoot("closed");
                    nextAt = now + 3f;
                    step = Step.OpenOptions;
                    return;

                case Step.OpenOptions:
                {
                    CharacterChoiceItem item = TileFor(manager, subject);
                    if (item == null)
                    {
                        Say("FAILED: no tile for '" + subject.CharacterName + "' to open the options menu on");
                        step = Step.Done;
                        return;
                    }

                    item.ShowEllipseMenu();
                    nextAt = now + 2f;
                    step = Step.UseButton;
                    return;
                }

                case Step.UseButton:
                {
                    CharacterChoiceItem item = TileFor(manager, subject);
                    Transform found = item != null && item.ellipseMenu != null ? OptionsMenuButton.FindButton(item.ellipseMenu.transform) : null;
                    var rect = found as RectTransform;

                    Say("options menu open=" + (item != null && item.ellipseMenu != null && item.ellipseMenu.activeSelf)
                        + ", View Character button present=" + (found != null)
                        + (rect != null ? ", size " + rect.rect.width.ToString("0") + "x" + rect.rect.height.ToString("0") : string.Empty));
                    Shoot("options-menu");

                    Button button = found != null ? found.GetComponent<Button>() : null;
                    if (button == null)
                    {
                        Say("SELFTEST DONE (without the button)");
                        step = Step.Done;
                        return;
                    }

                    button.onClick.Invoke();
                    nextAt = now + 5f;
                    step = Step.ShotViaButton;
                    return;
                }

                case Step.ShotViaButton:
                    Say("opened with the button: viewing=" + Viewer.Active + " (" + Name(Viewer.Viewing) + "), menu open=" + MenuOpen());
                    Shoot("via-button");
                    nextAt = now + 3f;
                    step = Step.TryChange;
                    return;

                case Step.TryChange:
                {
                    CharacterAttribute attribute = Burst2Flame.Game.Instance.LevelableCharacterAttributes[0];
                    float before = AttributeValue(subject, attribute);
                    int skillsBefore = subject.SkillsFromPoints.Count;

                    subject.AddAttribute(attribute, 1f, 1f);

                    float after = AttributeValue(subject, attribute);
                    Say("change attempt while viewing: " + attribute.name + " " + before + " -> " + after
                        + (Mathf.Approximately(before, after) ? " (blocked, as it should be)" : " (NOT BLOCKED)")
                        + "; skills " + skillsBefore + " -> " + subject.SkillsFromPoints.Count
                        + "; view-only notice shown=" + NoticeOpen());
                    Shoot("blocked-change");
                    nextAt = now + 3f;
                    step = Step.FinalClose;
                    return;
                }

                case Step.FinalClose:
                    CharacterMenusManager.Instance.CloseWindow();
                    Say("SELFTEST DONE");
                    step = Step.Done;
                    return;
            }
        }

        /// <summary>A tile showing <paramref name="character"/>, or the first character not in the party when null.</summary>
        private static CharacterChoiceItem TileFor(CharacterChoiceManager manager, Character character)
        {
            if (manager == null || manager.notInPartyHolder == null)
                return null;

            return manager.notInPartyHolder.GetComponentsInChildren<CharacterChoiceItem>()
                .FirstOrDefault(x => x != null && x.Character != null && x.gameObject.activeInHierarchy
                                     && (character == null || x.Character == character));
        }

        private static float AttributeValue(Character character, CharacterAttribute attribute)
        {
            return character.SavedMap != null && character.SavedMap.TryGetValue(attribute.Guid, out float value) ? value : float.NaN;
        }

        private static bool MenuOpen()
        {
            return CharacterMenusManager.Instance != null && CharacterMenusManager.Instance.gameObject.activeSelf;
        }

        private static bool NoticeOpen()
        {
            return ConfirmWindow.Instance != null && ConfirmWindow.Instance.gameObject.activeInHierarchy;
        }

        private static string Name(Character character) => character != null ? "'" + character.CharacterName + "'" : "none";

        private static void Shoot(string label)
        {
            try
            {
                Directory.CreateDirectory(shotDir);
                string file = Path.Combine(shotDir, $"{++shots:00}-{label}-{Screen.width}x{Screen.height}.png");
                ScreenCapture.CaptureScreenshot(file);
                Say("screenshot " + file);
            }
            catch (Exception e)
            {
                Say("screenshot failed: " + e.Message);
            }
        }

        private static void Say(string message) => Plugin.Log.LogInfo("SELFTEST " + message);
    }
}
