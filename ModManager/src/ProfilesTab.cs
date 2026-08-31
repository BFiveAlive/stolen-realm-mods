using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// Saves and loads shareable snapshots of every mod's settings.
    ///
    /// Drawn with absolute rects like the settings tab, so nothing here can produce a
    /// Layout/Repaint control-count mismatch when the file list changes underneath it - which it
    /// does, every time something is exported or deleted.
    /// </summary>
    internal static class ProfilesTab
    {
        private static string exportName = "my-settings";
        private static bool changedOnly;
        private static string message = string.Empty;
        private static bool messageIsError;

        private static List<string> files;
        private static string pendingDelete;
        private static Vector2 scroll;

        private const float RowHeight = 58f;
        private const string NameControl = "modmanager.profilename";

        public static void Refresh()
        {
            files = null;
            pendingDelete = null;
        }

        public static void Draw(Rect area)
        {
            if (files == null)
                files = ConfigProfile.List();

            float x = area.x + 26f;
            float width = area.width - 52f;
            float y = area.y + 20f;

            Skin.Text(new Rect(x, y, width, 30f), "Settings profiles", Skin.Header, Skin.Ink);
            y += 34f;

            float blurbHeight = Skin.Body.CalcHeight(new GUIContent(Blurb), width);
            GUI.Label(new Rect(x, y, width, blurbHeight), Blurb, Skin.Body);
            y += blurbHeight + 20f;

            y = DrawExport(x, y, width);

            if (!string.IsNullOrEmpty(message))
            {
                Skin.Text(new Rect(x, y, width, 24f), message, Skin.Warning,
                    messageIsError ? Skin.Bad : Skin.Good);
            }

            y += 34f;

            Skin.HLine(x, y, width, Skin.Line);
            y += 18f;

            Skin.Text(new Rect(x, y, width - 300f, 22f), "SAVED PROFILES", Skin.SmallCaps, Skin.InkDim);
            Skin.Text(new Rect(x + width - 300f, y, 300f, 22f), ConfigProfile.Folder,
                Skin.ValueRight, Skin.InkDim);
            y += 28f;

            DrawList(new Rect(x, y, width, area.yMax - y - 20f));
        }

        private const string Blurb =
            "A profile is every setting from every loaded mod, written in BepInEx's own format. "
            + "Share the file and someone else gets exactly your configuration; import one and "
            + "your own settings are overwritten by whatever it contains. Settings for mods you "
            + "don't have are skipped rather than treated as an error.";

        private static float DrawExport(float x, float y, float width)
        {
            Skin.Text(new Rect(x, y, width, 22f), "EXPORT", Skin.SmallCaps, Skin.InkDim);
            y += 26f;

            var nameBox = new Rect(x, y, 340f, 32f);
            GUI.SetNextControlName(NameControl);
            exportName = GUI.TextField(nameBox, exportName ?? string.Empty, Skin.Field);

            if (string.IsNullOrEmpty(exportName) && GUI.GetNameOfFocusedControl() != NameControl)
            {
                Skin.Text(new Rect(nameBox.x + 11f, nameBox.y, nameBox.width - 22f, nameBox.height),
                    "profile name", Skin.Value, Skin.InkDim);
            }

            Skin.Text(new Rect(nameBox.xMax + 8f, y, 90f, 32f), ConfigProfile.Extension,
                Skin.Value, Skin.InkDim);

            var save = new Rect(nameBox.xMax + 96f, y, 150f, 32f);
            if (GUI.Button(save, "Export", Skin.Button))
                DoExport();

            changedOnly = GUI.Toggle(new Rect(save.xMax + 20f, y + 3f, 340f, 26f), changedOnly,
                "  only settings changed from default", Skin.Toggle);

            return y + 44f;
        }

        private static void DoExport()
        {
            try
            {
                string path = ConfigProfile.Export(exportName, SettingsBrowser.Plugins, changedOnly);

                files = null;
                Say("Saved " + Path.GetFileName(path) + " to " + ConfigProfile.Folder, false);
                Plugin.Log.LogInfo("Exported settings profile to " + path);
            }
            catch (Exception e)
            {
                Say(e.Message, true);
            }
        }

        private static void DrawList(Rect area)
        {
            if (files.Count == 0)
            {
                Skin.Text(new Rect(area.x, area.y + 6f, area.width, 26f),
                    "No profiles saved yet.", Skin.Body, Skin.InkDim);
                return;
            }

            var content = new Rect(0f, 0f, area.width - 18f, files.Count * RowHeight);
            scroll = GUI.BeginScrollView(area, scroll, content);

            for (int i = 0; i < files.Count; i++)
                DrawRow(new Rect(0f, i * RowHeight, content.width, RowHeight), files[i], i);

            GUI.EndScrollView();
        }

        private static void DrawRow(Rect rect, string path, int index)
        {
            if (index % 2 == 1)
                Skin.Fill(rect, Skin.RowAlt);

            Skin.Text(new Rect(rect.x + 14f, rect.y + 8f, rect.width - 320f, 24f),
                Path.GetFileNameWithoutExtension(path), Skin.RowNameBold, Skin.Ink);

            Skin.Text(new Rect(rect.x + 14f, rect.y + 30f, rect.width - 320f, 20f),
                Describe(path), Skin.Value, Skin.InkDim);

            bool confirming = pendingDelete == path;

            var second = new Rect(rect.xMax - 116f, rect.y + 13f, 102f, 32f);
            var first = new Rect(second.x - 112f, rect.y + 13f, 102f, 32f);

            if (confirming)
            {
                if (GUI.Button(first, "Cancel", Skin.Button))
                    pendingDelete = null;

                if (GUI.Button(second, "Delete", Skin.Button))
                {
                    try
                    {
                        ConfigProfile.Delete(path);
                        Say("Deleted " + Path.GetFileNameWithoutExtension(path) + ".", false);
                    }
                    catch (Exception e)
                    {
                        Say("Could not delete: " + e.Message, true);
                    }

                    pendingDelete = null;
                    files = null;
                }

                return;
            }

            if (GUI.Button(first, "Import", Skin.Button))
            {
                var report = ConfigProfile.Import(path);

                Say(report.Failed ? report.Error
                        : "Imported " + Path.GetFileNameWithoutExtension(path) + ": " + report,
                    report.Failed);

                Plugin.Log.LogInfo("Imported " + path + ": " + report);
            }

            // Two-step rather than an immediate delete: a profile is the only copy of a
            // configuration someone may have spent a while assembling.
            if (GUI.Button(second, "Delete…", Skin.ButtonQuiet))
                pendingDelete = path;
        }

        private static string Describe(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                     + "  ·  " + (info.Length / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void Say(string text, bool error)
        {
            message = text;
            messageIsError = error;
        }
    }
}
