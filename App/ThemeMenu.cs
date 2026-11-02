// v0.11 App · ThemeMenu: one themed popup menu for the whole app (dark card, metal border, hover, current item)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Decanter.App
{
    /// <summary>Custom renderer: card background, metal 1px border, accent hover, check mark for the current item.</summary>
    public sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme _theme;
        public ThemeMenuRenderer(Theme theme) : base(new ThemeColorTable(theme)) { _theme = theme; }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(_theme.Metal, FrameTokens.Outer))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle r = new Rectangle(2, e.Item.Bounds.Top, e.Item.Bounds.Width - 4, e.Item.Bounds.Height);
            bool hot = e.Item.Selected || e.Item.Pressed;
            using (SolidBrush b = new SolidBrush(hot ? _theme.CardHi : _theme.Card)) e.Graphics.FillRectangle(b, r);
            if (hot)
            {
                using (Pen p = new Pen(_theme.Accent, FrameTokens.Inner))
                    e.Graphics.DrawLine(p, r.Right - 2, r.Top + 2, r.Right - 2, r.Bottom - 2);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _theme.Text : _theme.Aux;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            using (SolidBrush b = new SolidBrush(_theme.Accent))
                e.Graphics.FillRectangle(b, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
        }
    }

    internal sealed class ThemeColorTable : ProfessionalColorTable
    {
        private readonly Theme _theme;
        public ThemeColorTable(Theme theme) { _theme = theme; }
        public override Color ToolStripDropDownBackground { get { return _theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return _theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return _theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return _theme.Card; } }
        public override Color MenuBorder { get { return _theme.Metal; } }
        public override Color MenuItemBorder { get { return _theme.Accent; } }
        public override Color MenuItemSelected { get { return _theme.CardHi; } }
        public override Color SeparatorDark { get { return _theme.LineStrong; } }
        public override Color SeparatorLight { get { return _theme.Line; } }
    }

    public static class ThemeMenu
    {
        /// <summary>Build a themed context menu; items are (label, checked, action).</summary>
        public static ContextMenuStrip Build(Theme theme, string[] labels, bool[] checks, Action[] actions)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ThemeMenuRenderer(theme);
            menu.BackColor = theme.Card;
            menu.ForeColor = theme.Text;
            menu.ShowImageMargin = true;
            menu.Padding = new Padding(0, FrameTokens.MenuPad, 0, FrameTokens.MenuPad);
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem item = new ToolStripMenuItem(labels[i]);
                item.Checked = checks != null && i < checks.Length && checks[i];
                item.ForeColor = theme.Text;
                item.BackColor = theme.Card;
                item.Padding = new Padding(4, 2, 4, 2);
                if (actions != null && i < actions.Length && actions[i] != null) item.Click += delegate { actions[idx](); };
                menu.Items.Add(item);
            }
            return menu;
        }

        /// <summary>Convert a legacy ContextMenu (MenuItem tree) into the themed ToolStrip menu.</summary>
        public static ContextMenuStrip FromLegacy(Theme theme, ContextMenu legacy)
        {
            ContextMenuStrip strip = new ContextMenuStrip();
            strip.Renderer = new ThemeMenuRenderer(theme);
            strip.BackColor = theme.Card;
            strip.ForeColor = theme.Text;
            strip.ShowImageMargin = true;
            strip.Padding = new Padding(0, FrameTokens.MenuPad, 0, FrameTokens.MenuPad);
            if (legacy != null) foreach (MenuItem mi in legacy.MenuItems) AddLegacy(strip, theme, mi);
            return strip;
        }

        private static void AddLegacy(ContextMenuStrip strip, Theme theme, MenuItem mi)
        {
            if (mi == null) return;
            if (mi.Text == "-")
            {
                strip.Items.Add(new ToolStripSeparator());
                return;
            }
            ToolStripMenuItem item = new ToolStripMenuItem(mi.Text);
            item.Checked = mi.Checked;
            item.Enabled = mi.Enabled;
            item.ForeColor = theme.Text;
            item.Padding = new Padding(4, 2, 4, 2);
            MenuItem src = mi;
            item.Click += delegate { src.PerformClick(); };
            strip.Items.Add(item);
        }

        /// <summary>Show a legacy ContextMenu as the themed popup at the same anchor/point.</summary>
        public static void ShowLegacy(Control anchor, Point pos, Theme theme, ContextMenu legacy)
        {
            ContextMenuStrip strip = FromLegacy(theme, legacy);
            strip.Show(anchor, pos);
            try { strip.Focus(); } catch { }
        }
        /// <summary>Show above the anchor control (never on the main screen if the anchor is on a secondary one).</summary>
        public static void ShowAbove(Control anchor, ContextMenuStrip menu)
        {
            if (anchor == null || menu == null) return;
            Size pref = menu.GetPreferredSize(Size.Empty);
            Point p = anchor.PointToScreen(new Point(0, -pref.Height - 4));
            menu.Show(p, ToolStripDropDownDirection.AboveRight);
            try { menu.Focus(); } catch { }
        }
    }
}
