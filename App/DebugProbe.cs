// v0.11 App · DebugProbe: stable ids, tree/state JSON and dispatch onto MainForm's public test surface
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public sealed class DebugProbe
    {
        private static readonly string[] Views = new string[] { "all", "today", "week", "overdue", "done" };
        private readonly Control _ui;
        private readonly Store _store;
        private string _pendingTitle = "";

        public DebugProbe(Control ui, Store store) { _ui = ui; _store = store; }

        private MainForm Main { get { return _ui as MainForm; } }
        public MainForm MainForm { get { return Main; } }
        public SettingsForm Settings;
        public WelcomeForm Welcome;
        public FloatingForm Floating;
        public void AttachShells(FloatingForm f, SettingsForm s, WelcomeForm w) { Floating = f; Settings = s; Welcome = w; }
        public bool DraftNew() { if (Main == null) return false; Main.BeginDraft(); return true; }
        public bool DraftEdit() { return Main != null && Main.BeginDraftSelected(); }
        public void DraftSetTitle(string t) { if (Main != null) Main.SetDraftTitle(t); }
        public void DraftSetNote(string t) { if (Main != null) Main.SetDraftNote(t); }
        public bool DraftCommit() { return Main != null && Main.DraftCommit(); }
        public int DraftCancel() { return Main == null ? 0 : Main.DraftCancel(); }
        public bool DraftClickOutside() { return Main != null && Main.DraftClickOutside(); }
        public bool SelectRow(long id) { return Main != null && Main.TestSelectById(id); }
        public static readonly string[] ThemeKeys = new string[] { "v1", "v6", "v7", "v8" };
        public static readonly string[] FontKeys = new string[] { "s", "m", "l" };

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public string StateJson()
        {
            StringBuilder sb = new StringBuilder("{\"ok\":true");
            sb.Append(",\"theme\":").Append(J(_store.GetSetting("theme", "system")));
            sb.Append(",\"visibleRows\":").Append(Main == null ? 0 : Main.TestVisibleRows());
            sb.Append(",\"editing\":").Append(Main != null && Main.TestIsEditing() ? "true" : "false");
            sb.Append(",\"editingId\":").Append(Main == null ? 0 : Main.TestEditingId());
            sb.Append(",\"dirty\":").Append(Main != null && Main.TestIsDirty() ? "true" : "false");
            string logicalWindow = _ui.Width.ToString() + "x" + _ui.Height.ToString();
            RECT wr;
            string physicalWindow = logicalWindow;
            if (_ui.IsHandleCreated && GetWindowRect(_ui.Handle, out wr))
                physicalWindow = (wr.Right - wr.Left).ToString() + "x" + (wr.Bottom - wr.Top).ToString();
            sb.Append(",\"window\":\"").Append(physicalWindow).Append("\"");
            sb.Append(",\"windowPhysical\":\"").Append(physicalWindow).Append("\"");
            sb.Append(",\"windowLogical\":\"").Append(logicalWindow).Append("\"");
            sb.Append(",\"floatingExpanded\":").Append(Floating != null && Floating.TestExpanded ? "true" : "false");
            sb.Append(",\"draftActive\":").Append(Main != null && Main.DraftActive ? "true" : "false");
            sb.Append(",\"draftConfirm\":").Append(Main != null && Main.DraftConfirm ? "true" : "false");
            sb.Append(",\"draftTitle\":").Append(J(Main == null ? "" : Main.DraftTitle));
            sb.Append(",\"confirming\":").Append(Main != null && Main.ConfirmingId != 0 ? "true" : "false");
            sb.Append(",\"confirmingId\":").Append(Main == null ? 0 : Main.ConfirmingId);
            sb.Append(",\"addButton\":").Append(J(Main == null ? "" : Main.TestAddButtonText()));
            sb.Append(",\"selectedId\":").Append(Main == null ? 0 : Main.SelectedId);
            sb.Append(",\"cmdStatus\":").Append(J(Main == null ? "" : Main.CmdStatus));
            sb.Append("}");
            return sb.ToString();
        }

        public string TreeJson()
        {
            int w = _ui.ClientSize.Width;
            int h = _ui.ClientSize.Height;
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"elements\":[");
            bool first = true;
            for (int i = 0; i < Views.Length; i++)
            {
                Rect(sb, ref first, "rail.view." + Views[i], new Rectangle(20, 96 + i * 36, 168, 34), "\"click\"");
            }
            Rect(sb, ref first, "rail.search", new Rectangle(20, h - 82, 168, 32), "\"click\",\"type\"");
            Rect(sb, ref first, "compose.title", new Rectangle(248, 44 + 16, Math.Max(120, w - 248 - 360), 32), "\"click\",\"type\"");
            Rect(sb, ref first, "compose.add", new Rectangle(w - 120 - 88, 44 + 16, 88, 32), "\"click\"");
            Rect(sb, ref first, "status.theme", new Rectangle(20, h - 44, 120, 30), "\"click\"");
            if (Settings != null && !Settings.IsDisposed)
            {
                for (int ti = 0; ti < ThemeKeys.Length; ti++) Rect(sb, ref first, "settings.theme." + ThemeKeys[ti], new Rectangle(w - 320, 100 + ti * 30, 120, 26), "\"click\"");
                for (int fi = 0; fi < FontKeys.Length; fi++) Rect(sb, ref first, "settings.font." + FontKeys[fi], new Rectangle(w - 180, 100 + fi * 30, 120, 26), "\"click\"");
                Rect(sb, ref first, "settings.close", new Rectangle(w - 120, 30, 88, 30), "\"click\"");
            }
            if (Welcome != null && !Welcome.IsDisposed) Rect(sb, ref first, "welcome.start", new Rectangle(20, 120, 120, 36), "\"click\"");
            if (Floating != null && !Floating.IsDisposed)
            {
                Rect(sb, ref first, "floating.expand", new Rectangle(20, 220, 160, 28), "\"click\"");
                for (int fi2 = 0; fi2 < 3; fi2++) Rect(sb, ref first, "floating.action" + fi2.ToString(), new Rectangle(20 + fi2 * 110, 260, 100, 28), "\"click\"");
            }
            try
            {
                List<TodoItem> rows = _store.Active();
                for (int i = 0; i < rows.Count && i < 40; i++)
                {
                    string id = "row." + rows[i].Id.ToString(CultureInfo.InvariantCulture);
                    int top = 148 + i * 58;
                    Rect(sb, ref first, id, new Rectangle(248, top, Math.Max(200, w - 248 - 200), 40), "\"click\"");
                    Rect(sb, ref first, id + ".circle", new Rectangle(248 + 12, top + 8, 24, 24), "\"click\"");
                }
            }
            catch { }
            return sb.Append("]}").ToString();
        }

        private static void Rect(StringBuilder sb, ref bool first, string id, Rectangle r, string actions)
        {
            if (!first) sb.Append(",");
            first = false;
            sb.Append("{\"id\":").Append(J(id));
            sb.Append(",\"type\":\"control\",\"rect\":[").Append(r.X).Append(",").Append(r.Y).Append(",").Append(r.Width).Append(",").Append(r.Height).Append("]");
            sb.Append(",\"enabled\":true,\"actions\":[").Append(actions).Append("]}");
        }

        private static FlatCheck FindCheck(Control root)
        {
            if (root == null) return null;
            FlatCheck fc = root as FlatCheck;
            if (fc != null) return fc;
            for (int i = 0; i < root.Controls.Count; i++)
            {
                FlatCheck r = FindCheck(root.Controls[i]);
                if (r != null) return r;
            }
            return null;
        }

        public bool FocusFirstCheck() { FlatCheck fc = FindCheck(Settings); if (fc == null) return false; fc.Focus(); return true; }
        public bool ToggleFirstCheck() { FlatCheck fc = FindCheck(Settings); if (fc == null) return false; fc.Checked = !fc.Checked; return true; }
        private static ButtonBase FindButton(Control root)
        {
            if (root == null) return null;
            ButtonBase b = root as ButtonBase;
            if (b != null) return b;
            for (int i = 0; i < root.Controls.Count; i++) { ButtonBase r = FindButton(root.Controls[i]); if (r != null) return r; }
            return null;
        }
        public bool BlurChecks()
        {
            if (Settings == null) return false;
            ButtonBase b = FindButton(Settings);
            if (b != null) { b.Focus(); return true; }
            Settings.ActiveControl = null;
            return true;
        }
        public bool ShowSettings() { if (Settings == null) return false; Settings.Show(); Settings.BringToFront(); return true; }
        public bool IsClickable(string id)
        {
            if (id == null) return false;
            if (id.StartsWith("rail.view."))
            {
                int idx = Array.IndexOf(Views, id.Substring(10));
                return idx >= 0;
            }
            if (id == "compose.title" || id == "compose.add" || id == "rail.search" || id == "status.theme") return true;
            if (id.StartsWith("settings.theme.") || id.StartsWith("settings.font.") || id == "settings.close") return true;
            if (id == "welcome.start") return true;
            if (id == "floating.expand" || id.StartsWith("floating.action")) return true;
            if (id.StartsWith("row."))
            {
                string rest = id.Substring(4);
                if (rest.EndsWith(".circle", StringComparison.Ordinal)) rest = rest.Substring(0, rest.Length - 7);
                long rowId;
                return long.TryParse(rest, out rowId);
            }
            return false;
        }

        public bool Click(string id)
        {
            MainForm m = Main;
            if (m == null || id == null) return false;
            if (id.StartsWith("rail.view."))
            {
                string key = id.Substring(10);
                int idx = Array.IndexOf(Views, key);
                if (idx < 0) return false;
                m.TestSetFilter(idx);
                return true;
            }
            if (id == "compose.title") { m.FocusTitle(); return true; }
            if (id == "compose.add") { m.TestAddTodoFromInput(_pendingTitle); _pendingTitle = ""; return true; }
            if (id == "rail.search") { m.TestSearch(""); return true; }
            if (id == "status.theme") { m.TestCycleTheme(); return true; }
            if (id.StartsWith("row."))
            {
                string rest = id.Substring(4);
                bool circle = rest.EndsWith(".circle", StringComparison.Ordinal);
                if (circle) rest = rest.Substring(0, rest.Length - 7);
                long rowId;
                if (!long.TryParse(rest, out rowId)) return false;
                m.TestSelectById(rowId);
                if (circle) m.TestToggleDone();
                return true;
            }
            if (id.StartsWith("settings.theme."))
            {
                string key = id.Substring("settings.theme.".Length);
                if (Settings != null) Settings.TestSetTheme(key);
                if (Main != null) Main.TestApplyThemeSetting();
                return true;
            }
            if (id.StartsWith("settings.font."))
            {
                string key2 = id.Substring("settings.font.".Length);
                if (Settings != null) Settings.TestSetFont(key2);
                if (Main != null) Main.TestApplyThemeSetting();
                return true;
            }
            if (id == "settings.close") { if (Settings != null) Settings.Close(); return true; }
            if (id == "welcome.start") { if (Welcome != null) Welcome.TestAccept(); return true; }
            if (id == "floating.expand") { if (Floating != null) { if (Floating.TestExpanded) Floating.TestCollapse(); else Floating.TestExpand(); } return true; }
            if (id.StartsWith("floating.action"))
            {
                int ai; if (Floating != null && int.TryParse(id.Substring("floating.action".Length), out ai)) { Floating.TestAction(ai); return true; }
                return false;
            }
            if (id.StartsWith("settings.") || id.StartsWith("welcome.") || id.StartsWith("floating."))
                return false;   // 见报告：这三类壳的稳定 id 本轮未接
            return false;
        }

        public void Type(string text) { _pendingTitle = text == null ? "" : text; }
        public void Key(string key)
        {
            if (key == null) return;
            string k = key.ToLowerInvariant();
            MainForm m = Main;
            if (m == null) return;
            if (k == "enter") { m.TestAddTodoFromInput(_pendingTitle); _pendingTitle = ""; }
            else if (k == "esc" || k == "escape") { _pendingTitle = ""; }
        }
        public void CycleTheme() { if (Main != null) Main.TestCycleTheme(); }
        public bool SetTheme(string key) { return Main != null && Main.TestSetTheme(key); }
        public bool BeginDelete(long id) { return Main != null && Main.TestBeginDelete(id); }
        public bool ConfirmDelete() { return Main != null && Main.TestConfirmDelete(); }
        public void CancelDelete() { if (Main != null) Main.TestCancelDelete(); }
        public void Action(string act) { }

        private static string J(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else sb.Append(c);
            }
            return sb.Append("\"").ToString();
        }
    }
}
