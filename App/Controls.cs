// Controls.cs — v5 基础控件：FlatField（下划线输入框）+ FlatCheck（自绘勾选）
// 形态：全直角；输入框「无框、只有一条金属底线」，聚焦时底线变 2px 黄铜（焦点与悬停必须可区分，理论 §控件状态）
// 占位提示：用真实文本＋_ph 标记（P0 验证过的做法，高 DPI 不会被裁）；聚焦时清空
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Decanter.App
{
    public class FlatField : Panel
    {
        public readonly TextBox Box;
        public Theme Theme;
        public float Dpi = 1f;
        private bool _focused;
        private bool _hover;
        private string _placeholder = "";
        private bool _ph;

        public FlatField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Box = new TextBox();
            Box.BorderStyle = BorderStyle.None;
            Box.Font = new Font(Theme.FontUI, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            Box.BackColor = Color.Black;
            Box.ForeColor = Color.White;
            Box.GotFocus += delegate
            {
                _focused = true;
                Invalidate();
            };
            Box.LostFocus += delegate
            {
                _focused = false;
                if (Box.Text.Length == 0) ShowPlaceholderIfIdle();
                Invalidate();
            };
            Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                // 聚焦时仍显示占位提示；第一次"真正输入"才清空提示（方向键/回车/Esc/Tab 不算）
                if (_ph && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu
                    && e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Up && e.KeyCode != Keys.Down
                    && e.KeyCode != Keys.Home && e.KeyCode != Keys.End && e.KeyCode != Keys.Enter
                    && e.KeyCode != Keys.Escape && e.KeyCode != Keys.Tab)
                {
                    _ph = false;
                    Box.Text = "";
                    Box.ForeColor = Theme != null ? Theme.Text : Color.Black;
                }
            };
            Box.MouseEnter += delegate { _hover = true; Invalidate(); };
            Box.MouseLeave += delegate { _hover = false; Invalidate(); };
            Controls.Add(Box);
            Height = 32;
        }

        private int S(float v) { return (int)Math.Round(v * Dpi); }

        public override string Text
        {
            get { return _ph ? "" : Box.Text; }
            set { SetTextSilent(value); }
        }

        public bool ReadOnly
        {
            get { return Box.ReadOnly; }
            set { Box.ReadOnly = value; Invalidate(); }
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set { _placeholder = value == null ? "" : value; ShowPlaceholderIfIdle(); }
        }

        public void ApplyDpi(float dpi, float fontPx)
        {
            Dpi = dpi <= 0 ? 1f : dpi;
            try { Box.Font = new Font(Theme.FontUI, Math.Max(9, S(fontPx)), FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { }
            LayoutBox();
        }

        private void LayoutBox()
        {
            int pad = S(2);
            // 单行 TextBox 的高度必须跟着字号走，否则高 DPI 下字被裁掉（P0 遗留坑）
            int h = Math.Max(S(18), Box.Font.Height + S(6));
            Box.SetBounds(pad, (Height - h) / 2, Math.Max(10, Width - pad * 2), h);
            Invalidate();
        }

        private void ShowPlaceholderIfIdle()
        {
            if (_ph) return;
            if (_placeholder.Length == 0) return;
            _ph = true;
            Box.Text = _placeholder;
            Box.ForeColor = Theme != null ? Theme.Aux : Color.Gray;
            try { Box.SelectionStart = 0; Box.SelectionLength = 0; } catch { }   // 不把占位提示变成整段选中（防蓝底）

        }

        private void SetTextSilent(string v)
        {
            _ph = false;
            Box.Text = v == null ? "" : v;
            Box.ForeColor = Theme != null ? Theme.Text : Color.Black;
            if (Box.Text.Length == 0) ShowPlaceholderIfIdle();   // 清空后提示回来（聚焦时也显示）
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Box == null) return;
            LayoutBox();
        }

        public void ApplyTheme(Theme th)
        {
            Theme = th;
            Box.BackColor = th.Card;
            Box.ForeColor = _ph ? th.Aux : th.Text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            // 一层很低的底面：把输入区和纸场区分开（只差一点明度，不做边框/阴影/圆角）
            using (SolidBrush b = new SolidBrush(Box.ReadOnly ? th.CardHi : th.Card))
                g.FillRectangle(b, new Rectangle(0, 0, Width, Math.Max(0, Height - 1)));

            int pw = _focused ? Math.Max(2, S(2)) : Math.Max(1, S(1));
            Color lc = _focused ? th.Accent : (_hover ? th.Metal : th.Line);
            using (SolidBrush b = new SolidBrush(lc))
                g.FillRectangle(b, new Rectangle(0, Height - pw, Width, pw));
        }
    }

    // 自绘勾选框：16×16 方形 + 文字；选中黄铜实底 + 墨色勾；聚焦加外描边（≥3:1）
    public class FlatCheck : CheckBox
    {
        public Theme Theme;
        public float Dpi = 1f;
        private bool _hover;

        public FlatCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Height = Theme.HControl;
            Font = new Font(Theme.FontUI, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            ForeColor = Color.White;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        private int S(float v) { return (int)Math.Round(v * Dpi); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            int box = S(16);
            int y = (Height - box) / 2;
            Rectangle r = new Rectangle(S(2), y, box, box);
            if (Checked)
            {
                using (SolidBrush b = new SolidBrush(th.Accent)) g.FillRectangle(b, r);
                using (Pen p = new Pen(th.OnAccent, Math.Max(2, S(2))))
                {
                    g.DrawLine(p, r.Left + S(4), r.Top + S(8), r.Left + S(7), r.Top + S(11));
                    g.DrawLine(p, r.Left + S(7), r.Top + S(11), r.Left + S(12), r.Top + S(5));
                }
            }
            else
            {
                using (Pen p = new Pen(_hover ? th.Metal : th.LineStrong, Math.Max(1, S(1))))
                    g.DrawRectangle(p, r.Left, r.Top, r.Width - 1, r.Height - 1);
            }
            if (Focused)
            {
                using (Pen p = new Pen(th.Accent, Math.Max(1, S(1))))
                {
                    // #6: keep the focus ring INSIDE the control bounds (it used to be clipped at left/top)
                    int o = S(2);
                    int fx = Math.Max(1, r.Left - o);
                    int fy = Math.Max(1, r.Top - o);
                    int fw = Math.Min(Width - fx - 1, r.Width + 2 * o);
                    int fh = Math.Min(Height - fy - 1, r.Height + 2 * o);
                    if (fw > 2 && fh > 2) g.DrawRectangle(p, fx, fy, fw, fh);
                }
            }
            Color fg = Enabled ? th.Text : th.Aux;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(box + S(12), 0, Math.Max(10, Width - box - S(12)), Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }
}
