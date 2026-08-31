using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModManager
{
    /// <summary>
    /// The settings tab: a mod/section rail on the left, the settings themselves in the middle,
    /// and a detail panel on the right for whichever one is selected.
    ///
    /// Two things drive the design. One mod binds 470 settings in a single section, so the list is
    /// virtualised - only the rows intersecting the viewport are built - and every row is drawn
    /// into an absolute Rect at a constant pitch, because virtualising needs a pitch to compute
    /// with. The other is that the interesting question is often "where is the setting about X",
    /// not "what does this mod expose", so typing in the search box switches the whole tab into a
    /// flat results view across every loaded mod and the rail becomes a scope filter.
    ///
    /// Nothing here uses GUILayout, which also means none of it can produce the control-count
    /// mismatch that a list changing length mid-frame used to cause.
    /// </summary>
    internal static class SettingsBrowser
    {
        private static List<PluginSettings> plugins = new List<PluginSettings>();
        private static List<SettingRow> everything = new List<SettingRow>();

        private static int selectedPlugin;
        private static string selectedSection;
        private static string selectedRowId;

        private static string query = string.Empty;
        private static string sectionFilter = string.Empty;

        /// <summary>Index into <see cref="plugins"/> that search results are limited to, or -1 for all.</summary>
        private static int searchScope = -1;

        private static Vector2 railScroll;
        private static Vector2 listScroll;
        private static Vector2 detailScroll;

        /// <summary>
        /// One line of the list: either a setting, or the name of the section that follows.
        /// Dividers are list entries rather than extra space so every line keeps the same pitch,
        /// which is what makes the list virtualisable.
        /// </summary>
        private struct ListItem
        {
            public SettingRow Row;
            public string Header;

            public bool IsHeader => Row == null;
        }

        // The filtered list is rebuilt only when something it depends on changes. Recomputing it
        // per frame would mean a substring scan over 583 settings on every event.
        private static List<ListItem> items = new List<ListItem>();
        private static string rowsKey;

        /// <summary>
        /// Whether a mod's sections are worth navigating separately. Splitting fifteen settings
        /// across five list entries costs more navigation than it saves, so below the threshold
        /// they are shown on one page with the sections as dividers.
        /// </summary>
        private static bool Combined(PluginSettings plugin)
        {
            return plugin.Rows.Count <= Mathf.Max(0, ModConfig.CombineSectionsBelow.Value);
        }

        private static bool Searching => !string.IsNullOrEmpty(query.Trim());

        /// <summary>The discovered plugins, shared with the profiles tab.</summary>
        internal static List<PluginSettings> Plugins => plugins;

        public static void Refresh()
        {
            plugins = ConfigDiscovery.Collect();
            everything = plugins.SelectMany(p => p.Rows).ToList();

            EntryDrawer.ClearTransientState();

            if (selectedPlugin >= plugins.Count)
                selectedPlugin = 0;

            // The selected section is deliberately kept across a rescan: the panel rescans every
            // time it is opened (mods can bind settings long after startup - Status Effects Mod
            // binds 470 of them once the game's status data has loaded), and resetting the view
            // each time would throw away where the reader was.
            rowsKey = null;
        }

        public static void Draw(Rect area)
        {
            if (plugins.Count == 0)
            {
                Skin.Text(new Rect(area.x + 24f, area.y + 24f, area.width - 48f, 30f),
                    "No loaded mod exposes any settings.", Skin.Body, Skin.InkMuted);
                return;
            }

            EnsureSelection();

            var strip = new Rect(area.x, area.y, area.width, Skin.SearchStrip);
            var footer = new Rect(area.x, area.yMax - Skin.FooterHeight, area.width, Skin.FooterHeight);
            var body = new Rect(area.x, strip.yMax, area.width,
                area.height - Skin.SearchStrip - Skin.FooterHeight);

            DrawSearchStrip(strip);

            var rail = new Rect(body.x, body.y, Skin.RailWidth, body.height);
            var detail = new Rect(body.xMax - Skin.DetailWidth, body.y, Skin.DetailWidth, body.height);
            var list = new Rect(rail.xMax, body.y, detail.x - rail.xMax, body.height);

            RebuildRowsIfNeeded();

            DrawRail(rail);
            DrawList(list);
            DrawDetail(detail);
            DrawFooter(footer);
        }

        private static void EnsureSelection()
        {
            if (selectedPlugin < 0 || selectedPlugin >= plugins.Count)
                selectedPlugin = 0;

            var plugin = plugins[selectedPlugin];

            // A combined mod has no section selection to keep valid: the page is all of them.
            if (Combined(plugin))
            {
                selectedSection = null;
                return;
            }

            if (selectedSection == null || !plugin.Sections.Any(s => s.Key == selectedSection))
                selectedSection = plugin.Sections.Count > 0 ? plugin.Sections[0].Key : null;
        }

        // --- search strip -------------------------------------------------------------------

        private const string SearchControl = "modmanager.search";

        private static void DrawSearchStrip(Rect area)
        {
            Skin.Fill(area, Skin.PanelHigh);
            Skin.HLine(area.x, area.yMax - 1f, area.width, Skin.Line);

            var box = new Rect(area.x + 20f, area.y + 10f, area.width - 40f - 110f, 40f);

            GUI.SetNextControlName(SearchControl);
            string typed = GUI.TextField(box, query, Skin.Field);
            if (typed != query)
            {
                query = typed;
                listScroll = Vector2.zero;
                searchScope = -1;
            }

            if (string.IsNullOrEmpty(query) && GUI.GetNameOfFocusedControl() != SearchControl)
            {
                Skin.Text(new Rect(box.x + 11f, box.y, box.width - 22f, box.height),
                    "Search all settings across every mod…", Skin.Body, Skin.InkDim);
            }

            var clear = new Rect(box.xMax + 10f, box.y, 90f, 40f);
            if (GUI.Button(clear, Searching ? "Clear" : "Focus", Skin.Button))
            {
                if (Searching)
                {
                    query = string.Empty;
                    searchScope = -1;
                    GUI.FocusControl(null);
                }
                else
                {
                    GUI.FocusControl(SearchControl);
                }
            }
        }

        // --- rail ---------------------------------------------------------------------------

        private static void DrawRail(Rect area)
        {
            Skin.Fill(area, Skin.Rail);
            Skin.VLine(area.xMax - 1f, area.y, area.height, Skin.Line);

            var inner = new Rect(area.x, area.y, area.width - 1f, area.height);

            if (Searching)
                DrawSearchScopeRail(inner);
            else
                DrawBrowseRail(inner);
        }

        private static void DrawBrowseRail(Rect area)
        {
            float height = 12f;
            for (int i = 0; i < plugins.Count; i++)
            {
                height += Skin.PluginRowHeight;
                if (i == selectedPlugin && !Combined(plugins[i]))
                    height += plugins[i].Sections.Count * Skin.SectionRowHeight + 6f;
            }

            var content = new Rect(0f, 0f, area.width - 18f, height);
            railScroll = GUI.BeginScrollView(area, railScroll, content);

            float y = 6f;
            for (int i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                var row = new Rect(0f, y, content.width, Skin.PluginRowHeight);
                bool active = i == selectedPlugin;

                if (active)
                    Skin.SelectionMarker(row);

                Skin.Text(new Rect(row.x + 18f, row.y + 5f, row.width - 90f, 22f),
                    plugin.Name, active ? Skin.RowNameBold : Skin.RowName,
                    active ? Skin.Ink : Skin.InkMuted);

                Skin.Text(new Rect(row.x + 18f, row.y + 27f, row.width - 90f, 18f),
                    "v" + plugin.Version, Skin.Value, Skin.InkDim);

                GUI.Label(new Rect(row.xMax - 66f, row.y + 5f, 52f, 22f),
                    plugin.Rows.Count.ToString(), Skin.Badge);

                if (Clicked(row))
                {
                    selectedPlugin = i;
                    selectedSection = plugin.Sections.Count > 0 ? plugin.Sections[0].Key : null;
                    sectionFilter = string.Empty;
                    listScroll = Vector2.zero;
                }

                y += Skin.PluginRowHeight;

                if (!active || Combined(plugin))
                    continue;

                foreach (var section in plugin.Sections)
                {
                    var sub = new Rect(0f, y, content.width, Skin.SectionRowHeight);
                    bool chosen = section.Key == selectedSection;

                    if (chosen)
                        Skin.SelectionMarker(sub);

                    Skin.Text(new Rect(sub.x + 36f, sub.y, sub.width - 90f, sub.height),
                        section.Key, chosen ? Skin.RowNameBold : Skin.RowName,
                        chosen ? Skin.Ink : Skin.InkMuted);

                    Skin.Text(new Rect(sub.xMax - 66f, sub.y, 52f, sub.height),
                        section.Value.Count.ToString(), Skin.ValueRight,
                        chosen ? Skin.Accent : Skin.InkDim);

                    if (Clicked(sub))
                    {
                        selectedSection = section.Key;
                        sectionFilter = string.Empty;
                        listScroll = Vector2.zero;
                    }

                    y += Skin.SectionRowHeight;
                }

                y += 6f;
            }

            GUI.EndScrollView();
        }

        /// <summary>
        /// While searching the rail scopes the results to one mod. Deliberately single-select
        /// rather than a set of checkboxes: it answers the same question - "just this mod" - with
        /// one piece of state instead of a combination per mod.
        /// </summary>
        private static void DrawSearchScopeRail(Rect area)
        {
            var counts = new int[plugins.Count];
            string needle = query.Trim();

            for (int i = 0; i < plugins.Count; i++)
                counts[i] = plugins[i].Rows.Count(r => Matches(r, needle));

            float y = area.y + 10f;

            Skin.Text(new Rect(area.x + 18f, y, area.width - 36f, 20f), "SCOPE", Skin.SmallCaps, Skin.InkDim);
            y += 26f;

            var all = new Rect(area.x, y, area.width, Skin.SectionRowHeight + 6f);
            if (searchScope < 0)
                Skin.SelectionMarker(all);

            Skin.Text(new Rect(all.x + 18f, all.y, all.width - 90f, all.height), "All mods",
                searchScope < 0 ? Skin.RowNameBold : Skin.RowName,
                searchScope < 0 ? Skin.Ink : Skin.InkMuted);

            Skin.Text(new Rect(all.xMax - 66f, all.y, 52f, all.height), counts.Sum().ToString(),
                Skin.ValueRight, searchScope < 0 ? Skin.Accent : Skin.InkDim);

            if (Clicked(all))
            {
                searchScope = -1;
                listScroll = Vector2.zero;
            }

            y += all.height + 4f;

            for (int i = 0; i < plugins.Count; i++)
            {
                var row = new Rect(area.x, y, area.width, Skin.SectionRowHeight + 6f);
                bool chosen = searchScope == i;
                bool empty = counts[i] == 0;

                if (chosen)
                    Skin.SelectionMarker(row);

                Skin.Text(new Rect(row.x + 18f, row.y, row.width - 90f, row.height), plugins[i].Name,
                    chosen ? Skin.RowNameBold : Skin.RowName,
                    empty ? Skin.InkDim : (chosen ? Skin.Ink : Skin.InkMuted));

                Skin.Text(new Rect(row.xMax - 66f, row.y, 52f, row.height), counts[i].ToString(),
                    Skin.ValueRight, chosen ? Skin.Accent : Skin.InkDim);

                if (!empty && Clicked(row))
                {
                    searchScope = i;
                    listScroll = Vector2.zero;
                }

                y += row.height;
            }
        }

        // --- the list -----------------------------------------------------------------------

        private static void RebuildRowsIfNeeded()
        {
            string key = Searching
                ? "q " + query.Trim() + " " + searchScope
                : "b " + selectedPlugin + " " + selectedSection + " " + sectionFilter;

            if (key == rowsKey)
                return;

            rowsKey = key;
            items = new List<ListItem>();

            if (Searching)
            {
                string needle = query.Trim();
                var source = searchScope >= 0 && searchScope < plugins.Count
                    ? plugins[searchScope].Rows.AsEnumerable()
                    : everything.AsEnumerable();

                foreach (var row in source.Where(r => Matches(r, needle)))
                    items.Add(new ListItem { Row = row });

                return;
            }

            var plugin = plugins[selectedPlugin];
            string filter = sectionFilter.Trim();

            if (Combined(plugin))
            {
                foreach (var group in plugin.Sections)
                {
                    var kept = Keep(group.Value, filter);
                    if (kept.Count == 0)
                        continue;

                    // A single section needs no divider; it would just say the same thing as the
                    // heading directly above it.
                    if (plugin.Sections.Count > 1)
                        items.Add(new ListItem { Header = group.Key });

                    foreach (var row in kept)
                        items.Add(new ListItem { Row = row });
                }

                return;
            }

            var section = plugin.Sections.FirstOrDefault(s => s.Key == selectedSection);

            foreach (var row in Keep(section.Value, filter))
                items.Add(new ListItem { Row = row });
        }

        private static List<SettingRow> Keep(List<SettingRow> source, string filter)
        {
            if (source == null)
                return new List<SettingRow>();

            if (filter.Length == 0)
                return source;

            return source.Where(r => Contains(r.Key, filter) || Contains(r.Description, filter)).ToList();
        }

        private static bool Matches(SettingRow row, string needle)
        {
            return Contains(row.Key, needle)
                || Contains(row.Section, needle)
                || Contains(row.Description, needle)
                || Contains(row.Owner != null ? row.Owner.Name : null, needle);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawList(Rect area)
        {
            float headerHeight = Searching ? 62f : 96f;
            var header = new Rect(area.x, area.y, area.width, headerHeight);

            if (Searching)
                DrawSearchHeader(header);
            else
                DrawSectionHeader(header);

            Skin.HLine(area.x, header.yMax - 1f, area.width, Skin.Line);

            var view = new Rect(area.x, header.yMax, area.width, area.height - headerHeight);

            if (items.Count == 0)
            {
                Skin.Text(new Rect(view.x + 20f, view.y + 24f, view.width - 40f, 26f),
                    Searching ? "Nothing matches “" + query.Trim() + "”."
                              : "Nothing here matches that filter.",
                    Skin.Body, Skin.InkMuted);
                return;
            }

            // Before the rows: an open dropdown has to consume its click before any row control
            // can take it, because IMGUI hands the mouse to whichever control was drawn first.
            EntryDrawer.HandleMenuInput();

            float pitch = Searching ? Skin.ResultRowHeight : Skin.ListRowHeight;
            var content = new Rect(0f, 0f, view.width - 18f, items.Count * pitch);

            listScroll = GUI.BeginScrollView(view, listScroll, content);

            // Only the rows on screen are built. With 470 settings in one section, drawing them
            // all would mean 470 text fields per event, twice a frame.
            int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / pitch) - 1);
            int last = Mathf.Min(items.Count - 1,
                Mathf.CeilToInt((listScroll.y + view.height) / pitch) + 1);

            for (int i = first; i <= last; i++)
            {
                var rect = new Rect(0f, i * pitch, content.width, pitch);
                var item = items[i];

                if (item.IsHeader)
                    DrawDivider(rect, item.Header);
                else if (Searching)
                    DrawResultRow(rect, item.Row, i);
                else
                    DrawSettingRow(rect, item.Row, i);
            }

            GUI.EndScrollView();

            EntryDrawer.PlaceMenu(listScroll, view);
            EntryDrawer.PaintMenu();
        }

        private static void DrawDivider(Rect rect, string title)
        {
            Skin.Text(new Rect(rect.x + 18f, rect.y + 10f, rect.width - 36f, rect.height - 10f),
                title.ToUpperInvariant(), Skin.SmallCaps, Skin.Accent);

            Skin.HLine(rect.x + 18f, rect.yMax - 6f, rect.width - 36f, Skin.Line);
        }

        private static void DrawSectionHeader(Rect area)
        {
            var plugin = plugins[selectedPlugin];
            bool combined = Combined(plugin);

            var section = plugin.Sections.FirstOrDefault(s => s.Key == selectedSection);
            var scope = combined ? plugin.Rows : section.Value;
            int total = scope != null ? scope.Count : 0;

            Skin.Text(new Rect(area.x + 20f, area.y + 8f, area.width - 260f, 28f),
                combined ? plugin.Name : (selectedSection ?? "—"), Skin.Header, Skin.Ink);

            Skin.Text(new Rect(area.x + 20f, area.y + 36f, area.width - 260f, 20f),
                (combined ? "v" + plugin.Version : plugin.Name)
                    + "  ·  " + total + (total == 1 ? " setting" : " settings"),
                Skin.Value, Skin.InkDim);

            var reset = new Rect(area.xMax - 174f, area.y + 12f, 154f, 30f);
            if (GUI.Button(reset, combined ? "Reset this mod" : "Reset this section", Skin.Button)
                && scope != null)
            {
                foreach (var row in scope)
                    ConfigWriter.Reset(row);

                EntryDrawer.ClearTransientState();
            }

            var filter = new Rect(area.x + 20f, area.y + 60f, area.width - 40f, 30f);
            string typed = GUI.TextField(filter, sectionFilter, Skin.Field);
            if (typed != sectionFilter)
            {
                sectionFilter = typed;
                listScroll = Vector2.zero;
            }

            if (string.IsNullOrEmpty(sectionFilter))
            {
                Skin.Text(new Rect(filter.x + 11f, filter.y, filter.width - 22f, filter.height),
                    total > 0 ? "Filter these " + total + " settings…" : "Filter…",
                    Skin.Value, Skin.InkDim);
            }
        }

        private static void DrawSearchHeader(Rect area)
        {
            string needle = query.Trim();

            Skin.Text(new Rect(area.x + 20f, area.y + 10f, area.width - 40f, 28f),
                items.Count + (items.Count == 1 ? " setting matches “" : " settings match “")
                    + needle + "”",
                Skin.Header, Skin.Ink);

            int mods = items.Select(i => i.Row.Owner).Distinct().Count();
            Skin.Text(new Rect(area.x + 20f, area.y + 38f, area.width - 40f, 20f),
                searchScope >= 0
                    ? "in " + plugins[searchScope].Name
                    : "across " + mods + (mods == 1 ? " mod" : " mods"),
                Skin.Value, Skin.InkDim);
        }

        private static void DrawSettingRow(Rect rect, SettingRow row, int index)
        {
            bool selected = row.Id == selectedRowId;

            if (selected)
                Skin.Fill(rect, Skin.Selected);
            else if (index % 2 == 1)
                Skin.Fill(rect, Skin.RowAlt);

            var name = new Rect(rect.x + 18f, rect.y, NameWidth, rect.height);

            Skin.Text(name, row.Key, selected ? Skin.RowNameBold : Skin.RowName,
                IsModified(row) ? Skin.Accent : Skin.Ink);

            var control = new Rect(rect.xMax - Skin.ControlWidth - 18f, rect.y,
                Skin.ControlWidth, rect.height);

            DrawBrief(row, name, control.x, rect.y, rect.height);

            EntryDrawer.Draw(control, row);

            // The whole strip left of the control selects the row, so the hint is as clickable as
            // the name is - it is the part that tells you there is more to read on the right.
            if (Clicked(new Rect(rect.x, rect.y, control.x - rect.x - 8f, rect.height)))
                Select(row);
        }

        private static void DrawResultRow(Rect rect, SettingRow row, int index)
        {
            bool selected = row.Id == selectedRowId;

            if (selected)
                Skin.Fill(rect, Skin.Selected);
            else if (index % 2 == 1)
                Skin.Fill(rect, Skin.RowAlt);

            var crumb = new Rect(rect.x + 18f, rect.y + 6f, rect.width - 40f, 20f);
            Skin.Text(crumb,
                (row.Owner != null ? row.Owner.Name : "?") + "  ›  " + row.Section,
                Skin.Value, Skin.InkDim);

            var name = new Rect(rect.x + 18f, rect.y + 26f, NameWidth, 30f);

            Skin.Text(name, row.Key, selected ? Skin.RowNameBold : Skin.RowName,
                IsModified(row) ? Skin.Accent : Skin.Ink);

            var control = new Rect(rect.xMax - Skin.ControlWidth - 18f, rect.y + 22f,
                Skin.ControlWidth, 38f);

            DrawBrief(row, name, control.x, name.y, name.height);

            EntryDrawer.Draw(control, row);

            if (Clicked(new Rect(rect.x, rect.y, rect.width - Skin.ControlWidth - 40f, rect.height)))
                Select(row);
        }

        /// <summary>Fixed, so the hints beside every row start on the same column.</summary>
        private const float NameWidth = 310f;

        /// <summary>
        /// The first sentence of the description, on the same line as the name. Rows stay one
        /// line each - which is what makes a 470-row section navigable - while still saying what
        /// each setting is without a click. The full text is in the detail panel.
        /// </summary>
        private static void DrawBrief(SettingRow row, Rect name, float controlX, float y, float height)
        {
            float x = name.xMax + 10f;
            float right = controlX - 20f;

            // Said in words at the end of the row rather than as a symbol next to the name. A
            // glyph has to be learned before it means anything, and this is the one piece of
            // information here that changes what the reader should expect to happen.
            if (row.RequiresRestart)
            {
                var note = new Rect(right - RestartNoteWidth, y, RestartNoteWidth, height);
                Skin.Text(note, "needs restart", Skin.NoteRight, Skin.AccentDim);
                right -= RestartNoteWidth + 14f;
            }

            if (string.IsNullOrEmpty(row.Brief))
                return;

            float width = right - x;
            if (width < 90f)
                return;

            Skin.Text(new Rect(x, y, width, height),
                Skin.Ellipsize(row.Brief, Skin.Value, width), Skin.Value, Skin.InkDim);
        }

        private const float RestartNoteWidth = 104f;

        // --- detail panel -------------------------------------------------------------------

        private static void DrawDetail(Rect area)
        {
            Skin.Fill(area, Skin.Rail);
            Skin.VLine(area.x, area.y, area.height, Skin.Line);

            var row = FindSelected();

            if (row == null)
            {
                Skin.Text(new Rect(area.x + 22f, area.y + 24f, area.width - 44f, 60f),
                    "Select a setting to see what it does, what it defaults to, and whether it "
                    + "needs a restart.", Skin.Body, Skin.InkDim);
                return;
            }

            float width = area.width - 44f;
            var content = new Rect(0f, 0f, width, DetailHeight(row, width));

            detailScroll = GUI.BeginScrollView(
                new Rect(area.x + 22f, area.y + 18f, area.width - 26f, area.height - 36f),
                detailScroll, content);

            float y = 0f;

            Skin.Text(new Rect(0f, y, width, 30f), row.Key, Skin.Header, Skin.Ink);
            y += 32f;

            Skin.Text(new Rect(0f, y, width, 20f),
                (row.Owner != null ? row.Owner.Name : "?") + "  ›  " + row.Section,
                Skin.Value, Skin.InkDim);
            y += 26f;

            Skin.HLine(0f, y, width, Skin.Line);
            y += 16f;

            if (!string.IsNullOrEmpty(row.Description))
            {
                y = Caption(y, width, "WHAT IT DOES");
                float h = Skin.Body.CalcHeight(new GUIContent(row.Description), width);
                GUI.Label(new Rect(0f, y, width, h), row.Description, Skin.Body);
                y += h + 18f;
            }

            var schema = StructuredSchema.From(row);

            if (schema != null)
            {
                y = StructuredEditor.Draw(new Rect(0f, y, width, 0f), row, schema);
            }
            else
            {
                y = Caption(y, width, "CURRENT");
                Skin.Text(new Rect(0f, y, width, 22f),
                    Safe(() => EntryDrawer.Format(row.Entry.BoxedValue)),
                    Skin.RowName, IsModified(row) ? Skin.Accent : Skin.Ink);
                y += 28f;
            }

            y = Caption(y, width, "DEFAULT");
            Skin.Text(new Rect(0f, y, width, 22f), Safe(() => EntryDrawer.Format(row.Entry.DefaultValue)),
                Skin.RowName, Skin.InkMuted);
            y += 28f;

            y = Caption(y, width, "TYPE");
            Skin.Text(new Rect(0f, y, width, 22f), FriendlyType(row), Skin.RowName, Skin.InkMuted);
            y += 28f;

            if (EntryDrawer.TryDescribeRange(row, out string range))
            {
                y = Caption(y, width, "RANGE");
                Skin.Text(new Rect(0f, y, width, 22f), range, Skin.RowName, Skin.InkMuted);
                y += 28f;
            }

            if (row.RequiresRestart)
            {
                y += 6f;
                Skin.Text(new Rect(0f, y, width, 22f), "Takes effect after a restart",
                    Skin.Warning, Skin.Accent);
                y += 28f;
            }

            y += 10f;

            GUI.enabled = IsModified(row);
            if (GUI.Button(new Rect(0f, y, 180f, 32f), "Reset to default", Skin.Button))
            {
                ConfigWriter.Reset(row);
                EntryDrawer.ClearTransientState();
            }
            GUI.enabled = true;

            GUI.EndScrollView();
        }

        private static float Caption(float y, float width, string text)
        {
            Skin.Text(new Rect(0f, y, width, 18f), text, Skin.SmallCaps, Skin.InkDim);
            return y + 20f;
        }

        /// <summary>
        /// Measured rather than guessed, because the description is the tallest thing here and
        /// varies from one line to a paragraph.
        /// </summary>
        private static float DetailHeight(SettingRow row, float width)
        {
            float height = 32f + 26f + 16f + 28f * 3f + 40f + 42f;

            var schema = StructuredSchema.From(row);
            if (schema != null)
                height += StructuredEditor.Height(row, schema, width);

            if (!string.IsNullOrEmpty(row.Description))
                height += 20f + Skin.Body.CalcHeight(new GUIContent(row.Description), width) + 18f;

            if (EntryDrawer.TryDescribeRange(row, out _))
                height += 48f;

            if (row.RequiresRestart)
                height += 34f;

            return height;
        }

        private static string FriendlyType(SettingRow row)
        {
            var type = row.Entry.SettingType;

            if (type == typeof(bool)) return "on / off";
            if (type == typeof(string)) return "text";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return "whole number";
            if (type == typeof(float) || type == typeof(double)) return "number";
            if (type.IsEnum) return "one of: " + string.Join(", ", Enum.GetNames(type));

            return type.Name;
        }

        private static void Select(SettingRow row)
        {
            if (selectedRowId == row.Id)
                return;

            selectedRowId = row.Id;
            detailScroll = Vector2.zero;

            // The structured editor keeps per-field edit buffers keyed by row; carrying them
            // across a change of selection would show one status's half-typed number under
            // another status's field.
            StructuredEditor.Reset();
        }

        private static SettingRow FindSelected()
        {
            if (string.IsNullOrEmpty(selectedRowId))
                return null;

            // Looked up in the visible list first so the common case is short, then across
            // everything, because a selection survives changing section.
            foreach (var item in items)
            {
                if (!item.IsHeader && item.Row.Id == selectedRowId)
                    return item.Row;
            }

            return everything.FirstOrDefault(r => r.Id == selectedRowId);
        }

        // --- footer -------------------------------------------------------------------------

        private static void DrawFooter(Rect area)
        {
            Skin.Fill(area, Skin.PanelHigh);
            Skin.HLine(area.x, area.y, area.width, Skin.Line);

            int restarts = ConfigWriter.ChangedRequiringRestart.Count;

            string message = restarts > 0
                ? restarts + (restarts == 1
                    ? " changed setting takes effect after restarting the game."
                    : " changed settings take effect after restarting the game.")
                : "Changes are saved automatically.";

            Skin.Text(new Rect(area.x + 20f, area.y, area.width - 240f, area.height),
                message, Skin.Warning, restarts > 0 ? Skin.Accent : Skin.InkDim);

            var reset = new Rect(area.xMax - 214f, area.y + 7f, 194f, 30f);
            if (GUI.Button(reset, "Reset " + plugins[selectedPlugin].Name, Skin.ButtonQuiet))
            {
                foreach (var row in plugins[selectedPlugin].Rows)
                    ConfigWriter.Reset(row);

                EntryDrawer.ClearTransientState();
            }
        }

        // --- helpers ------------------------------------------------------------------------

        private static bool IsModified(SettingRow row)
        {
            try
            {
                return !Equals(row.Entry.BoxedValue, row.Entry.DefaultValue);
            }
            catch
            {
                return false;
            }
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                return "<unreadable: " + e.Message + ">";
            }
        }

        /// <summary>
        /// A plain left-click inside a rect. Used instead of an invisible GUI.Button so a row can
        /// be selectable without its whole area swallowing clicks meant for the control it holds.
        /// </summary>
        private static bool Clicked(Rect rect)
        {
            var e = Event.current;

            if (e == null || e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition))
                return false;

            e.Use();
            return true;
        }
    }
}
