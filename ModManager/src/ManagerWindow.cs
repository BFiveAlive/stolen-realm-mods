using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModManager
{
    internal enum Tab
    {
        Settings,
        Updates,
        About
    }

    /// <summary>
    /// The manager panel itself. Everything is drawn with IMGUI, which means it needs nothing
    /// from the game's own UI: it works on the main menu, mid-battle, and on a screen where no
    /// game canvas exists yet.
    /// </summary>
    internal static class ManagerWindow
    {
        private const int WindowId = 0x5A1E;

        private static Rect windowRect = new Rect(80f, 60f, 720f, 620f);
        private static Vector2 scroll;
        private static string search = string.Empty;
        private static Tab tab = Tab.Settings;

        private static readonly HashSet<string> Collapsed = new HashSet<string>();

        private static List<PluginSettings> plugins;

        /// <summary>Rebuilt on open rather than every frame; the loaded plugin set cannot change.</summary>
        public static void Refresh()
        {
            plugins = ConfigDiscovery.Collect();
            EntryDrawer.ClearTransientState();
        }

        public static void Draw()
        {
            Skin.Build();

            float scale = Mathf.Clamp(ModConfig.UiScale.Value, 0.6f, 2.5f);

            // Scaling the whole GUI matrix keeps every control crisp and consistent. The window
            // rect is in the scaled space afterwards, so it is clamped against the scaled screen.
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float maxWidth = Screen.width / scale;
            float maxHeight = Screen.height / scale;

            windowRect.width = Mathf.Min(windowRect.width, maxWidth - 20f);
            windowRect.height = Mathf.Min(windowRect.height, maxHeight - 20f);
            windowRect.x = Mathf.Clamp(windowRect.x, -windowRect.width + 120f, maxWidth - 120f);
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, maxHeight - 60f);

            windowRect = GUILayout.Window(WindowId, windowRect, DrawContents,
                "Stolen Realm Mods", Skin.Window);

            GUI.matrix = previousMatrix;
        }

        private static void DrawContents(int id)
        {
            DrawTabs();

            GUILayout.Space(6f);

            switch (tab)
            {
                case Tab.Settings:
                    DrawSettings();
                    break;
                case Tab.Updates:
                    UpdatesTab.Draw();
                    break;
                default:
                    DrawAbout();
                    break;
            }

            // Last, so it never steals a click from a control drawn above it.
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }

        private static void DrawTabs()
        {
            GUILayout.BeginHorizontal();

            DrawTabButton(Tab.Settings, "Settings");

            int available = UpdateService.Statuses.Count(s => s.UpdateAvailable);
            DrawTabButton(Tab.Updates, available > 0 ? "Updates (" + available + ")" : "Updates");

            DrawTabButton(Tab.About, "About");

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close", Skin.SmallButton, GUILayout.Width(64f)))
                Plugin.Instance.CloseWindow();

            GUILayout.EndHorizontal();
        }

        private static void DrawTabButton(Tab target, string label)
        {
            var style = tab == target ? Skin.TabActive : Skin.Tab;
            if (GUILayout.Button(label, style, GUILayout.Width(120f)))
                tab = target;
        }

        private static void DrawSettings()
        {
            if (plugins == null)
                Refresh();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", Skin.Muted, GUILayout.Width(44f));
            search = GUILayout.TextField(search ?? string.Empty, Skin.Field);
            if (GUILayout.Button("clear", Skin.SmallButton, GUILayout.Width(50f)))
            {
                search = string.Empty;
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            scroll = GUILayout.BeginScrollView(scroll);

            bool anyShown = false;

            foreach (var plugin in plugins)
            {
                var matching = Filter(plugin);
                if (matching.Count == 0)
                    continue;

                anyShown = true;
                DrawPlugin(plugin, matching);
            }

            if (!anyShown)
            {
                GUILayout.Space(12f);
                GUILayout.Label(string.IsNullOrEmpty(search)
                        ? "No loaded mod exposes any settings."
                        : "Nothing matches \"" + search + "\".",
                    Skin.Muted);
            }

            GUILayout.EndScrollView();

            DrawRestartBanner();
        }

        /// <summary>
        /// Rows matching the search box. A plugin whose own name matches shows all of its
        /// settings, so searching for a mod by name is a way to isolate it.
        /// </summary>
        private static List<SettingRow> Filter(PluginSettings plugin)
        {
            if (string.IsNullOrEmpty(search))
                return plugin.Rows;

            string needle = search.Trim();

            if (plugin.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                return plugin.Rows;

            return plugin.Rows.Where(r =>
                r.Key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Section.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
                || r.Description.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private static void DrawPlugin(PluginSettings plugin, List<SettingRow> rows)
        {
            bool collapsed = Collapsed.Contains(plugin.Guid);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button((collapsed ? "▶  " : "▼  ") + plugin.Name, Skin.Foldout))
            {
                if (collapsed) Collapsed.Remove(plugin.Guid);
                else Collapsed.Add(plugin.Guid);
            }

            GUILayout.Label("v" + plugin.Version, Skin.Badge, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            if (collapsed)
                return;

            // Sections are only worth a heading when there is more than one to tell apart.
            var sections = rows.GroupBy(r => r.Section).ToList();
            bool showSectionHeaders = sections.Count > 1;

            foreach (var section in sections)
            {
                if (showSectionHeaders)
                    GUILayout.Label(section.Key, Skin.SectionHeader);

                foreach (var row in section)
                    DrawRow(row);
            }

            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset " + plugin.Name + " to defaults", Skin.SmallButton))
            {
                foreach (var row in plugin.Rows)
                    ConfigWriter.Reset(row);

                EntryDrawer.ClearTransientState();
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawRow(SettingRow row)
        {
            GUILayout.BeginVertical(Skin.Row);
            GUILayout.BeginHorizontal();

            string label = row.RequiresRestart ? row.Key + " ↻" : row.Key;
            GUILayout.Label(label, GUILayout.Width(Skin.LabelWidth));

            EntryDrawer.Draw(row);

            GUILayout.FlexibleSpace();

            // Only offered where it would do something, so the eye is not drawn to a row that is
            // already at its default.
            if (!Equals(SafeValue(row), row.Entry.DefaultValue)
                && GUILayout.Button("reset", Skin.SmallButton, GUILayout.Width(52f)))
            {
                ConfigWriter.Reset(row);
                EntryDrawer.ClearTransientState();
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(row.Description))
            {
                GUILayout.Space(1f);
                GUILayout.Label(row.Description, Skin.Muted);
            }

            GUILayout.EndVertical();
        }

        private static object SafeValue(SettingRow row)
        {
            try
            {
                return row.Entry.BoxedValue;
            }
            catch
            {
                return null;
            }
        }

        private static void DrawRestartBanner()
        {
            if (ConfigWriter.ChangedRequiringRestart.Count == 0)
                return;

            GUILayout.Space(4f);
            GUILayout.Label("↻  " + ConfigWriter.ChangedRequiringRestart.Count
                + " changed setting(s) only take effect after restarting the game.", Skin.Warning);
        }

        private static void DrawAbout()
        {
            GUILayout.Space(6f);
            GUILayout.Label("Stolen Realm Mod Manager " + Plugin.Version, Skin.Header);
            GUILayout.Space(6f);

            GUILayout.Label(
                "Settings are read straight from BepInEx, so every loaded mod that binds its "
                + "configuration the normal way appears here without needing to know about this "
                + "manager at all.",
                Skin.Muted);

            GUILayout.Space(6f);
            GUILayout.Label("↻ marks a setting that only takes effect after a restart. "
                + "Everything else applies as soon as you change it, provided the mod reads its "
                + "config at the point of use.", Skin.Muted);

            GUILayout.Space(10f);
            GUILayout.Label("Loaded plugins", Skin.SectionHeader);

            foreach (var pair in BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                         .OrderBy(p => p.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(pair.Metadata.Name, GUILayout.Width(260f));
                GUILayout.Label("v" + pair.Metadata.Version, Skin.Muted, GUILayout.Width(70f));
                GUILayout.Label(pair.Metadata.GUID, Skin.Muted);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            GUILayout.Label("Config files: " + BepInEx.Paths.ConfigPath, Skin.Muted);
            GUILayout.Label("Log: " + System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log"), Skin.Muted);
        }
    }
}
