using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Shows what is installed against what is published, and stages downloads.
    ///
    /// The tab never claims to have installed anything. A staged mod is on disk and inert until
    /// ModUpdatePatcher unpacks it during the next launch, and the wording here says so.
    /// </summary>
    internal static class UpdatesTab
    {
        private static Vector2 scroll;

        public static void Draw()
        {
            GUILayout.BeginHorizontal();

            bool busy = UpdateService.Phase == UpdatePhase.Checking
                     || UpdateService.Phase == UpdatePhase.Downloading;

            GUI.enabled = !busy;
            if (GUILayout.Button("Check for updates", Skin.SmallButton, GUILayout.Width(150f)))
                Plugin.Instance.BeginUpdateCheck();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (UpdateService.AnyStaged)
                GUILayout.Label("Restart required to finish installing", Skin.Warning);

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(UpdateService.Message))
            {
                GUILayout.Space(4f);
                GUILayout.Label(UpdateService.Message,
                    UpdateService.Phase == UpdatePhase.Failed ? Skin.Warning : Skin.Muted);
            }

            if (UpdateService.Phase == UpdatePhase.Downloading)
            {
                GUILayout.Space(2f);
                DrawProgressBar(UpdateService.DownloadProgress);
            }

            GUILayout.Space(6f);

            if (UpdateService.Statuses.Count == 0)
            {
                GUILayout.Label(UpdateService.Phase == UpdatePhase.Checking
                        ? "Checking..."
                        : "No mod list loaded yet. Use \"Check for updates\" above.",
                    Skin.Muted);
                return;
            }

            scroll = GUILayout.BeginScrollView(scroll);

            foreach (var status in UpdateService.Statuses)
                DrawRow(status);

            GUILayout.EndScrollView();
        }

        private static void DrawRow(UpdateStatus status)
        {
            GUILayout.Space(8f);
            GUILayout.BeginVertical(Skin.Row);

            GUILayout.BeginHorizontal();

            GUILayout.Label(status.DisplayName, Skin.Foldout, GUILayout.Width(240f));

            GUILayout.Label(VersionText(status), Skin.Muted, GUILayout.Width(180f));

            GUILayout.FlexibleSpace();

            DrawAction(status);

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(status.Release.description))
                GUILayout.Label(status.Release.description, Skin.Muted);

            GUILayout.EndVertical();
        }

        private static string VersionText(UpdateStatus status)
        {
            if (!status.Installed)
                return "not installed  ·  v" + status.Release.version + " available";

            if (status.UpdateAvailable)
                return "v" + status.InstalledVersion + "  →  v" + status.Release.version;

            return "v" + status.InstalledVersion + "  ·  up to date";
        }

        private static void DrawAction(UpdateStatus status)
        {
            if (status.Staged)
            {
                GUILayout.Label("staged", Skin.Badge, GUILayout.Width(60f));

                if (GUILayout.Button("cancel", Skin.SmallButton, GUILayout.Width(64f)))
                    UpdateService.Unstage(status);

                return;
            }

            bool busy = UpdateService.Phase == UpdatePhase.Downloading
                     || UpdateService.Phase == UpdatePhase.Checking;

            if (!status.UpdateAvailable && status.Installed)
            {
                // Re-downloading a current version is the standard way out of a corrupted install,
                // so it stays available - just not as the obvious action.
                GUI.enabled = !busy;
                if (GUILayout.Button("reinstall", Skin.SmallButton, GUILayout.Width(80f)))
                    Plugin.Instance.BeginDownload(status);
                GUI.enabled = true;
                return;
            }

            GUI.enabled = !busy;

            string label = status.Installed ? "update" : "install";
            if (GUILayout.Button(label, Skin.SmallButton, GUILayout.Width(80f)))
                Plugin.Instance.BeginDownload(status);

            GUI.enabled = true;
        }

        private static void DrawProgressBar(float progress)
        {
            var rect = GUILayoutUtility.GetRect(100f, 6f, GUILayout.ExpandWidth(true));

            GUI.DrawTexture(rect, Texture2D.blackTexture, ScaleMode.StretchToFill);

            var filled = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height);
            GUI.DrawTexture(filled, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        }
    }
}
