// FlatButton.cs — v5 文字按钮：全直角、无阴影、无渐变
// 层级规则（理论 §分组与层级）：一屏只允许 1 个实底主按钮；其余一律文字按钮/描边按钮。
// 状态：静止 / 悬停 / 按下 / 聚焦 / 禁用（禁用不只靠颜色，还降透明度并给出 Aux 文字）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Decanter.App
{
    public enum ButtonKind { Primary, Secondary, Danger, Icon }

    public class FlatButton : Button
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        public Theme Theme;
        public float Dpi = 1f;
        private bool _hover;
        private bool _press;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            Font = new Font(Theme.FontUI, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            Height = Theme.HControl;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private int S(float v) { return (int)Math.Round(v * Dpi); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _press = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _press = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            Rectangle r = new Rectangle(0, 0, Width, Height);
            Color bg = th.Paper;
            bool transparent = true;
            Color fg = th.SubText;
            Color edge = Color.Empty;   // 描边色（Primary 实底 / 次级描边用）

            if (!Enabled)
            {
                fg = th.Aux;
                if (Kind == ButtonKind.Primary) { bg = th.CardHi; transparent = false; fg = th.Aux; }
            }
            else if (Kind == ButtonKind.Primary)
            {
                transparent = false;
                bg = _press ? Theme.Mix(th.Accent, Color.Black, th.IsDark ? 0.22 : 0.14)
                     : (_hover ? Theme.Mix(th.Accent, Color.Black, th.IsDark ? 0.10 : 0.07) : th.Accent);
                fg = th.OnAccent;
            }
            else if (Kind == ButtonKind.Danger)
            {
                fg = _hover || _press ? th.Dark : th.Dark;
                if (_hover || _press) { transparent = false; bg = Theme.Alpha(th.Dark, _press ? 46 : 26); }
            }
            else if (Kind == ButtonKind.Icon)
            {
                fg = _hover || _press ? th.Accent : th.SubText;
                if (_hover || _press) { transparent = false; bg = th.CardHi; }
            }
            else
            {
                // 次级 = 文字按钮：悬停只给一层很低的底 + 一条黄铜下划线（不是实底，避免第二个主按钮）
                fg = _hover || _press ? th.Text : th.SubText;
                if (_hover || _press) { transparent = false; bg = th.CardHi; }
            }

            if (!transparent)
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, r);
            }

            // 次级文字按钮：常驻/悬停基线（1px），黄铜只出现在悬停与聚焦时
            if (Kind == ButtonKind.Secondary)
            {
                Color ul = Focused ? th.Accent : (_hover ? Theme.Alpha(th.Accent, 160) : Color.Empty);
                if (ul != Color.Empty)
                {
                    using (SolidBrush b = new SolidBrush(ul))
                        g.FillRectangle(b, new Rectangle(S(8), Height - Math.Max(1, S(1)), Math.Max(1, Width - S(16)), Math.Max(1, S(1))));
                }
            }

            // 聚焦：主按钮外描 2px 黄铜；文字按钮补一条 2px 黄铜下划线（焦点可见 ≥3:1）
            if (Focused && Enabled && Kind != ButtonKind.Icon)
            {
                if (Kind == ButtonKind.Primary)
                {
                    using (Pen p = new Pen(th.OnAccent, Math.Max(1, S(2))))
                        g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
                }
                else if (Kind != ButtonKind.Secondary)
                {
                    using (Pen p = new Pen(th.Accent, Math.Max(1, S(1))))
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(th.Accent))
                        g.FillRectangle(b, new Rectangle(S(8), Height - Math.Max(2, S(2)), Math.Max(1, Width - S(16)), Math.Max(2, S(2))));
                }
            }

            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
