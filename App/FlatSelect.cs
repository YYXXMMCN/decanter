// FlatSelect.cs — v5 下拉：下划线式（无框、金属底线 + ▾），与输入框同一套形态；
// 聚焦底线 2px 黄铜；菜单仍用系统 ContextMenu（不自造弹层，减少焦点/键盘风险）。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Decanter.App
{
    public class FlatSelect : Control
    {
        public Theme Theme;
        public float Dpi = 1f;
        public string[] Options = new string[0];
        public int SelectedIndex;
        public event EventHandler SelectedIndexChanged;
        private bool _hover;
        private bool _open;

        public FlatSelect()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = new Font(Theme.FontUI, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            Height = 32;
            Cursor = Cursors.Hand;
        }

        public override string Text
        {
            get { return (SelectedIndex >= 0 && SelectedIndex < Options.Length) ? Options[SelectedIndex] : ""; }
            set { }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _open = false; Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            ShowMenu();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) { ShowMenu(); e.Handled = true; }
            base.OnKeyUp(e);
        }

        private void ShowMenu()
        {
            // batch2 #5: every FlatSelect dropdown now uses the unified ThemeMenu (dark card / metal border / hover / current)
            bool[] checks = new bool[Options.Length];
            Action[] acts = new Action[Options.Length];
            for (int i = 0; i < Options.Length; i++)
            {
                int idx = i;
                checks[i] = i == SelectedIndex;
                acts[i] = delegate
                {
                    SelectedIndex = idx;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                };
            }
            ContextMenuStrip menu = ThemeMenu.Build(Theme ?? Theme.Night(), Options, checks, acts);
            _open = true;
            ThemeMenu.ShowAbove(this, menu);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            int pw = Focused ? Math.Max(2, S(2)) : Math.Max(1, S(1));
            Color lc = Focused ? th.Accent : (_hover ? th.Metal : th.Line);
            using (SolidBrush b = new SolidBrush(lc))
                g.FillRectangle(b, new Rectangle(0, Height - pw, Width, pw));

            TextRenderer.DrawText(g, Text, Font, new Rectangle(2, 0, Width - S(28), Height), th.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "▾", Font, new Rectangle(Width - S(24), 0, S(20), Height), _hover || _open ? th.Accent : th.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private int S(float v) { return (int)Math.Round(v * (Dpi <= 0 ? 1f : Dpi)); }
    }
}
