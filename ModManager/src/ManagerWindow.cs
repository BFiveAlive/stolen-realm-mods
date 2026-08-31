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
    /// The window shell: the frame, the title bar and the top-level tabs. The Settings tab's
    /// contents live in <see cref="SettingsBrowser"/>.
    ///
    /// Everything is drawn with IMGUI, which means it needs nothing from the game's own UI: it
    /// works on the main menu, mid-battle, and on a screen where no game canvas exists yet. The
    /// window fills most of the screen and is fixed there rather than being draggable - at this
    /// size there is nowhere useful to drag it to, and not having a drag region removes a whole
    /// class of "the click went to the wrong place" bug.
    /// </summary>
    internal static class ManagerWindow
    {
        private const int WindowId = 0x5A1E;

        private static Tab tab = Tab.Settings;

        /// <summary>
        /// Structural changes waiting for the next Layout event.
        ///
        /// The Settings tab is drawn with absolute rects and needs none of this, but the Updates
        /// tab still uses GUILayout: IMGUI lays out on the Layout event and reuses that layout for
        /// Repaint, so switching tab on the click event in between would change how many controls
        /// the next Repaint draws and Unity throws "Getting control N's position in a group with
        /// only M controls". Queueing the change and applying it at the top of the next Layout
        /// keeps every pass of a frame consistent.
        /// </summary>
        private static readonly List<Action> Deferred = new List<Action>();

        private static void Defer(Action action)
        {
            Deferred.Add(action);
        }

        /// <summary>Rebuilt on open rather than every frame; the loaded plugin set cannot change.</summary>
        public static void Refresh()
        {
            SettingsBrowser.Refresh();
        }

        public static void Draw()
        {
            Skin.Build();

            float scale = Mathf.Clamp(ModConfig.UiScale.Value, 0.6f, 2.5f);

            // Scaling the whole GUI matrix keeps every control crisp and consistent. The window
            // rect is in the scaled space afterwards, so it is sized against the scaled screen.
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float maxWidth = Screen.width / scale;
            float maxHeight = Screen.height / scale;

            // Dims the game behind the panel so the text on top of it stays legible whatever is
            // on screen. DrawTexture does not take input, so this cannot swallow a click.
            Skin.Fill(new Rect(0f, 0f, maxWidth, maxHeight), Skin.Backdrop);

            float fill = Mathf.Clamp(ModConfig.WindowFill.Value, 0.5f, 1f);
            float width = maxWidth * fill;
            float height = maxHeight * fill;

            var rect = new Rect((maxWidth - width) / 2f, (maxHeight - height) / 2f, width, height);

            windowWidth = rect.width;
            windowHeight = rect.height;

            GUI.Window(WindowId, rect, DrawContents, GUIContent.none, Skin.Window);

            GUI.matrix = previousMatrix;
        }

        private static void DrawContents(int id)
        {
            if (Event.current.type == EventType.Layout && Deferred.Count > 0)
            {
                foreach (var action in Deferred)
                    action();

                Deferred.Clear();
            }

            var window = new Rect(0f, 0f, windowWidth, windowHeight);

            Skin.Frame(window, Skin.Line);

            var title = new Rect(0f, 0f, window.width, Skin.TitleBarHeight);
            DrawTitleBar(title);

            var body = new Rect(0f, title.yMax, window.width, window.height - title.height);

            switch (tab)
            {
                case Tab.Settings:
                    SettingsBrowser.Draw(body);
                    break;

                case Tab.Updates:
                    // UpdatesTab scrolls itself, so this only gives it a padded region to
                    // lay out inside.
                    GUILayout.BeginArea(new Rect(body.x + 24f, body.y + 18f,
                        body.width - 48f, body.height - 36f));
                    UpdatesTab.Draw();
                    GUILayout.EndArea();
                    break;

                default:
                    DrawAbout(body);
                    break;
            }
        }

        // GUI.Window gives the callback window-relative coordinates but not the size, so it is
        // stashed when the rect is computed rather than recomputed from Screen here (which would
        // be wrong on the frame the resolution changes).
        private static float windowWidth;
        private static float windowHeight;

        private static void DrawTitleBar(Rect area)
        {
            Skin.Fill(area, Skin.PanelHigh);
            Skin.HLine(area.x, area.yMax - 1f, area.width, Skin.Line);
            Skin.Fill(new Rect(area.x, area.y, 5f, area.height), Skin.Accent);

            Skin.Text(new Rect(area.x + 22f, area.y, 320f, area.height),
                "Stolen Realm Mods", Skin.Title, Skin.Ink);

            Skin.Text(new Rect(area.x + 262f, area.y, 360f, area.height),
                Summary(), Skin.Subtitle, Skin.InkDim);

            float x = area.xMax - 60f;

            var close = new Rect(x, area.y + 12f, 40f, 30f);
            if (GUI.Button(close, "✕", Skin.Button))
                Plugin.Instance.CloseWindow();

            x -= 10f;

            int available = UpdateService.Statuses.Count(s => s.UpdateAvailable);

            x = DrawTab(x, area, Tab.About, "About", 92f);
            x = DrawTab(x, area, Tab.Updates,
                available > 0 ? "Updates (" + available + ")" : "Updates", 132f);
            DrawTab(x, area, Tab.Settings, "Settings", 108f);
        }

        private static float DrawTab(float right, Rect bar, Tab target, string label, float width)
        {
            var rect = new Rect(right - width, bar.y + 10f, width, bar.height - 20f);
            bool active = tab == target;

            if (active)
            {
                Skin.Fill(rect, Skin.Panel);
                Skin.Fill(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), Skin.Accent);
            }

            Skin.Text(rect, label, Skin.TabLabel, active ? Skin.Ink : Skin.InkMuted);

            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                Defer(() => tab = target);
                e.Use();
            }

            return rect.x - 6f;
        }

        private static string Summary()
        {
            int mods = BepInEx.Bootstrap.Chainloader.PluginInfos.Count;
            return mods + (mods == 1 ? " mod loaded" : " mods loaded");
        }

        private static void DrawAbout(Rect area)
        {
            float x = area.x + 26f;
            float width = area.width - 52f;
            float y = area.y + 22f;

            Skin.Text(new Rect(x, y, width, 30f), "Stolen Realm Mod Manager " + Plugin.Version,
                Skin.Header, Skin.Ink);
            y += 38f;

            y = Paragraph(x, y, width,
                "Settings are read straight from BepInEx, so every loaded mod that binds its "
                + "configuration the normal way appears here without needing to know about this "
                + "manager at all.");

            y = Paragraph(x, y, width,
                "↻ marks a setting that only takes effect after a restart. Everything else "
                + "applies as soon as you change it, provided the mod reads its config at the "
                + "point of use.");

            y = Paragraph(x, y, width,
                "Updates are downloaded to a staging folder and put in place during the next "
                + "launch, before any mod is loaded. A running assembly cannot be replaced, so "
                + "there is no way to finish an update without restarting.");

            y += 10f;
            Skin.Text(new Rect(x, y, width, 22f), "LOADED PLUGINS", Skin.SmallCaps, Skin.InkDim);
            y += 26f;

            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                         .OrderBy(p => p.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                Skin.Text(new Rect(x, y, 300f, 24f), info.Metadata.Name, Skin.RowName, Skin.Ink);
                Skin.Text(new Rect(x + 310f, y, 90f, 24f), "v" + info.Metadata.Version,
                    Skin.Value, Skin.InkMuted);
                Skin.Text(new Rect(x + 410f, y, width - 410f, 24f), info.Metadata.GUID,
                    Skin.Value, Skin.InkDim);
                y += 26f;
            }

            y += 14f;
            Skin.Text(new Rect(x, y, width, 22f),
                "Config files: " + BepInEx.Paths.ConfigPath, Skin.Value, Skin.InkDim);
            y += 24f;
            Skin.Text(new Rect(x, y, width, 22f),
                "Log: " + System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log"),
                Skin.Value, Skin.InkDim);
        }

        private static float Paragraph(float x, float y, float width, string text)
        {
            float height = Skin.Body.CalcHeight(new GUIContent(text), width);
            GUI.Label(new Rect(x, y, width, height), text, Skin.Body);
            return y + height + 14f;
        }
    }
}
