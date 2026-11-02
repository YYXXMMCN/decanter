// FlatNav.cs — v6 左索引轨的视图项：图标 + 文字 + 右侧计数；选中态用 2px 黄铜左标
// 依据：理论 U5（图标旁必须有文字标签）、M1 克制（不做胶囊底）、Q31（需要稳定的参照物）
using System;
using Ui = Decanter.App.Layout;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Decanter.App
{
    public class FlatNav : Control
    {
        public Theme Theme;
        public float Dpi = 1f;
        public string Label = "";
        public string IconName = "";
        private int _count = -1;
        public int Count { get { return _count; } set { _count = value; } }   // <0 不显示
        public bool Selected;
        public event EventHandler Clicked;

        private bool _hover;

        public FlatNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Ui.RailNavH;
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = new Font(Theme.FontUI, Ui.FNav, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private int S(float v) { return (int)Math.Round(v * (Dpi <= 0 ? 1f : Dpi)); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Clicked != null) { Clicked(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            if (Selected)
            {
                using (SolidBrush b = new SolidBrush(th.CardHi)) g.FillRectangle(b, 0, 0, Width, Height);
                using (SolidBrush b = new SolidBrush(th.Accent)) g.FillRectangle(b, 0, S(6), Math.Max(2, S(2)), Height - S(12));
            }
            else if (_hover)
            {
                using (SolidBrush b = new SolidBrush(th.Hover)) g.FillRectangle(b, 0, 0, Width, Height);
            }

            Color ic = Selected ? th.Accent : (_hover ? th.Text : th.SubText);
            int icon = S(16);
            Rectangle ir = new Rectangle(S(12), (Height - icon) / 2, icon, icon);
            Icons.Draw(g, IconName, ir, ic, Dpi);

            Color tc = Selected ? th.Text : (_hover ? th.Text : th.SubText);
            int labelX = S(12) + icon + S(10);
            int rightW = Count >= 0 ? S(28) : 0;
            TextRenderer.DrawText(g, Label, Font, new Rectangle(labelX, 0, Math.Max(S(20), Width - labelX - rightW - S(8)), Height), tc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (Count >= 0)
            {
                using (Font mf = new Font(Theme.FontMono, Math.Max(9, S(Ui.FMeta)), FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    TextRenderer.DrawText(g, Count.ToString(), mf, new Rectangle(Width - S(8) - rightW, 0, rightW, Height),
                        Selected ? th.Accent : th.Aux,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            if (Focused)
            {
                using (Pen p = new Pen(th.Accent, Math.Max(1, S(1))))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
