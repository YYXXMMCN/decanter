// FlatChip.cs — v5 两种形态：
//   ① Radio=true → 筛选页签：纯文字 + 2px 黄铜下划线，不做胶囊底（层级更安静，一屏只有一个实底主按钮）
//   ② Toggle/普通 → 值令牌：文字 + 值，悬停给极低底，选中/开启用黄铜文字；可点区域 ≥28 逻辑 px
// 依据：理论 §分组与层级（相似/接近）、§控件状态（悬停与聚焦可区分）、M1 克制（无边框胶囊堆叠）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Decanter.App
{
    public class FlatChip : Control
    {
        public Theme Theme;
        public float Dpi = 1f;
        public string Label = "";
        public string Value = "";
        public bool Toggle;
        public bool Checked;
        public bool Radio;
        public bool Selected;
        public event EventHandler Clicked;

        private bool _hover;

        public FlatChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = new Font(Theme.FontUI, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            Height = Theme.HControl;
            Cursor = Cursors.Hand;
        }

        private int S(float v) { return (int)Math.Round(v * Dpi); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (Clicked != null) Clicked(this, EventArgs.Empty);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                if (Clicked != null) Clicked(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        public void ToggleChecked()
        {
            Checked = !Checked;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            bool on = Selected || (Toggle && Checked);
            if (Radio)
            {
                // 页签：当前项黄铜下划线（2px），其余只有文字；悬停给 1px 暗线提示可点
                Color tc = on ? th.Text : (_hover ? th.Text : th.SubText);
                TextRenderer.DrawText(g, Label, Font, new Rectangle(0, 0, Width, Height - S(3)), tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                int ulH = on ? Math.Max(2, S(2)) : Math.Max(1, S(1));
                Color ulC = on ? th.Accent : (_hover ? Theme.Alpha(th.Accent, 150) : Color.Empty);
                if (ulC != Color.Empty)
                {
                    using (SolidBrush b = new SolidBrush(ulC))
                        g.FillRectangle(b, new Rectangle(S(10), Height - ulH, Math.Max(1, Width - S(20)), ulH));
                }
                if (Focused)
                {
                    using (Pen p = new Pen(th.Accent, Math.Max(1, S(1))))
                        g.DrawRectangle(p, 0, S(2), Width - 1, Height - S(6));
                }
                return;
            }

            // 值令牌：Label（含符号）＋ Value；悬停垫一层卡面，开启/选中用黄铜文字
            if (_hover || on)
            {
                using (SolidBrush b = new SolidBrush(on ? th.CardHi : th.Hover)) g.FillRectangle(b, 0, 0, Width, Height);
            }
            Color lc = on ? th.Accent : (_hover ? th.Text : th.SubText);
            Color vc = on ? th.Accent : th.Text;
            string left = Label;
            string right = Value;
            if (Toggle && Checked && left.IndexOf("▲") < 0) left = "▲ " + left;

            int padX = S(8);
            int gap = right.Length > 0 ? S(6) : 0;
            Size lsz = TextRenderer.MeasureText(g, left, Font, Size.Empty, TextFormatFlags.NoPadding);
            int tw = Math.Max(10, Width - padX * 2 - gap);
            TextRenderer.DrawText(g, left, Font, new Rectangle(padX, 0, tw, Height), lc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (right.Length > 0)
            {
                int lx = padX + Math.Min(lsz.Width, tw) + gap;
                int rw = Math.Max(10, Width - lx - padX);
                TextRenderer.DrawText(g, right, Font, new Rectangle(lx, 0, rw, Height), vc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (Focused)
            {
                using (Pen p = new Pen(th.Accent, Math.Max(1, S(1))))
                    g.DrawRectangle(p, 0, S(2), Width - 1, Height - S(5));
            }
        }
    }
}
