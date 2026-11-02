// WelcomeForm.cs — 首次启动一屏引导「三步就会用」
// 结构：自绘 48px 顶条（标题 + ×，空白可拖动）→ 一屏三步（01/02/03 + 小标题 + 一句人话）→ 64px 底栏（唯一实底主按钮「开始用」+ 右侧事实小字）
// 版式：640×420 逻辑 px，8 栅格；全直角、无阴影/渐变/纹理；黄铜只给主按钮与编号竖标
// DPI：构造函数不建控件；OnLoad 取真实 DPI 后 BuildUi，所有尺寸经 S() 落到像素（AutoScaleMode.None）
// 行为：无原生标题栏（FrameForm）、无第二屏、不强制引导；Esc / × / 「开始用」都能关掉
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Decanter.App
{
    public class WelcomeForm : FrameForm
    {
        // ---------- 逻辑尺寸（全部经 S() 乘 DPI） ----------
        private const int W = 640;              // 窗口宽
        private const int H = 420;              // 窗口高
        private const int PadX = 32;            // 左右页边
        private const int HeadH = 48;           // 自绘顶条
        private const int FootH = 64;           // 固定底栏
        private const int CtrlH = 32;           // 按钮高
        private const int BtnW = 88;            // 主按钮宽
        private const int StepTop = 48;         // 正文里第一步相对正文顶的 y
        private const int StepH = 64;           // 每步行高
        private const int StepGap = 16;         // 步间间距
        private const int BarW = 3;             // 编号竖标宽
        private const int BarH = 40;            // 编号竖标高
        private const int NumX = 43;            // 编号 x（竖标右侧 8px）
        private const int NumW = 28;            // 编号宽
        private const int TextX = 79;           // 小标题/说明 x
        private const int SubY = 12;            // 副标题 y
        private const int GlyphPx = 15;         // 窗口按钮 glyph 字号
        private const int MonoPx = 12;          // 编号（等宽小字）字号

        private readonly string _themeSetting;
        private Theme _theme;
        private float _dpi = 1f;

        // 窗框
        private Panel _head;
        private Panel _headLine;
        private Label _title;
        private FlatButton _btnX;
        private Panel _footer;
        private Panel _footLine;
        private FlatButton _btnStart;
        private Label _footNote;

        // 正文
        private Label _sub;
        private Label[] _stepTitle = new Label[3];
        private Label[] _stepDesc = new Label[3];
        private Font _numFont;

        private bool _built;

        public WelcomeForm() : this("system")
        {
        }

        public WelcomeForm(string themeSetting)
        {
            _themeSetting = themeSetting == null ? "system" : themeSetting;
            _theme = Theme.FromSetting(_themeSetting);
            Icon = Program.AppIcon();
            Text = Strings.L("welcome.title") + " · " + Strings.L("app.title");
            StartPosition = FormStartPosition.Manual;
            PlaceCentreNonPrimary();
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = _theme.Paper;
            ForeColor = _theme.Text;
        }

        // 固定尺寸窗体：不加可缩放边、不响应边缘命中（FrameForm 用这个开关）
        protected override bool Resizable { get { return false; } }

        private int S(float v) { return (int)Math.Round(v * _dpi); }

        // 1 逻辑 px 的线：按 DPI 取整，至少 1 设备 px
        private int Line1() { return Math.Max(1, (int)Math.Round(_dpi)); }

        private Font F(float px, FontStyle style)
        {
            return new Font(Theme.FontUI, Math.Max(8, S(px)), style, GraphicsUnit.Pixel);
        }

        // ---------- 生命周期 ----------
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float dpi = DpiHelper.ScaleOf(this);
            string force = Environment.GetEnvironmentVariable("DECANTER_FORCE_DPI");
            if (!string.IsNullOrEmpty(force))
            {
                float f;
                if (float.TryParse(force, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && f >= 0.5f && f <= 4f) dpi = f;
            }
            if (dpi <= 0.1f) dpi = 1f;
            _dpi = dpi;

            ClientSize = new Size(S(W), S(H));
            BuildUi();
            LayoutChrome();
            _built = true;
            Placement.CenterOnNonPrimary(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Placement.CenterOnNonPrimary(this);
            // 主按钮拿到初始焦点：Enter 可直接开始，Esc 随时关闭
            try { _btnStart.Focus(); } catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_built) return;
            LayoutChrome();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 建界面 ----------
        private void BuildUi()
        {
            SuspendLayout();
            _numFont = new Font(Theme.FontMono, Math.Max(9, S(MonoPx)), FontStyle.Regular, GraphicsUnit.Pixel);

            // ---- 自绘顶条：标题 + ×，空白可拖动 ----
            MouseEventHandler dragDown = delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) DragMove();
            };

            _head = new Panel();
            _head.BackColor = _theme.Paper;
            _head.MouseDown += dragDown;

            _title = MakeLabel(Strings.L("welcome.title"), Theme.FTitle, _theme.Text, FontStyle.Bold);
            _title.MouseDown += dragDown;
            _head.Controls.Add(_title);

            _btnX = NewButton("×", ButtonKind.Icon);
            _btnX.Font = F(GlyphPx, FontStyle.Regular);
            _btnX.TabStop = false;
            _btnX.AccessibleName = Strings.L("set.close");
            _btnX.Click += delegate { Close(); };
            _head.Controls.Add(_btnX);

            _headLine = new Panel();
            _headLine.BackColor = _theme.LineStrong;
            _headLine.MouseDown += dragDown;
            _head.Controls.Add(_headLine);
            Controls.Add(_head);

            // ---- 正文：副标题 + 三步（编号/竖标/图标由 OnPaint 画） ----
            _sub = MakeLabel(Strings.L("welcome.sub"), Theme.FAux, _theme.Aux, FontStyle.Regular);
            _sub.MouseDown += dragDown;
            Controls.Add(_sub);

            string[] keys = new string[] { "welcome.step1", "welcome.step2", "welcome.step3" };
            for (int i = 0; i < keys.Length; i++)
            {
                Label t = MakeLabel(Strings.L(keys[i] + ".title"), Theme.FTitle, _theme.Text, FontStyle.Bold);
                Label d = MakeLabel(Strings.L(keys[i] + ".desc"), Theme.FAux, _theme.SubText, FontStyle.Regular);
                t.MouseDown += dragDown;
                d.MouseDown += dragDown;
                Controls.Add(t);
                Controls.Add(d);
                _stepTitle[i] = t;
                _stepDesc[i] = d;
            }

            // ---- 固定底栏：唯一实底主按钮 + 右侧事实小字 ----
            _footer = new Panel();
            _footer.BackColor = _theme.Paper;
            _footer.MouseDown += dragDown;

            _footLine = new Panel();
            _footLine.BackColor = _theme.Line;
            _footLine.MouseDown += dragDown;
            _footer.Controls.Add(_footLine);

            _btnStart = NewButton(Strings.L("welcome.start"), ButtonKind.Primary);
            _btnStart.Click += delegate { Close(); };
            _footer.Controls.Add(_btnStart);

            _footNote = MakeLabel(Strings.L("welcome.footer"), Theme.FCaption, _theme.Aux, FontStyle.Regular);
            _footNote.TextAlign = ContentAlignment.MiddleRight;
            _footNote.MouseDown += dragDown;
            _footer.Controls.Add(_footNote);
            Controls.Add(_footer);

            AcceptButton = _btnStart;
            ResumeLayout(false);
            PerformLayout();
        }

        private Label MakeLabel(string text, float fontPx, Color color, FontStyle style)
        {
            Label l = new Label();
            l.Text = text == null ? "" : text;
            l.AutoSize = false;
            l.UseMnemonic = false;
            l.Font = F(fontPx, style);
            l.ForeColor = color;
            l.BackColor = _theme.Paper;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AccessibleName = l.Text;
            return l;
        }

        private FlatButton NewButton(string text, ButtonKind kind)
        {
            FlatButton b = new FlatButton();
            b.Kind = kind;
            b.Text = text;
            b.Theme = _theme;
            b.Dpi = _dpi;
            b.Font = F(Theme.FButton, FontStyle.Regular);
            b.AccessibleName = text;
            return b;
        }

        // ---------- 布局 ----------
        private void LayoutChrome()
        {
            if (_head == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 100 || h < 100) return;

            _head.SetBounds(0, 0, w, S(HeadH));
            _headLine.SetBounds(0, S(HeadH) - Line1(), w, Line1());
            _btnX.SetBounds(w - S(PadX) - S(CtrlH), (S(HeadH) - S(CtrlH)) / 2, S(CtrlH), S(CtrlH));
            _title.SetBounds(S(PadX), 0, Math.Max(S(80), w - S(PadX) * 2 - S(CtrlH) - S(16)), S(HeadH));

            _sub.SetBounds(S(PadX), S(HeadH) + S(SubY), Math.Max(S(120), w - S(PadX) * 2), S(18));
            int tx = S(TextX);
            int tw = Math.Max(S(120), w - tx - S(PadX + 16 + 8));
            for (int i = 0; i < 3; i++)
            {
                int y = S(HeadH) + S(StepTop + i * (StepH + StepGap));
                _stepTitle[i].SetBounds(tx, y + S(8), tw, S(24));
                _stepDesc[i].SetBounds(tx, y + S(40), tw, S(18));
            }

            _footer.SetBounds(0, h - S(FootH), w, S(FootH));
            _footLine.SetBounds(0, 0, w, Line1());
            _btnStart.SetBounds(S(PadX), (S(FootH) - S(CtrlH)) / 2, S(BtnW), S(CtrlH));
            _footNote.SetBounds(w - S(PadX) - S(300), (S(FootH) - S(CtrlH)) / 2, S(300), S(CtrlH));
        }

        // ---------- 绘制：编号（等宽小字）+ 黄铜竖标 + 结构线图标 ----------
        public void TestAccept() { Close(); }

        private void PlaceCentreNonPrimary()
        {
            Screen target = Screen.PrimaryScreen;
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) if (!all[i].Primary) { target = all[i]; break; }
            Rectangle wa = target.WorkingArea;
            Location = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_theme == null || _stepTitle[0] == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            string[] icons = new string[] { "plus", "bell", "all" };
            for (int i = 0; i < 3; i++)
            {
                int y = S(HeadH) + S(StepTop + i * (StepH + StepGap));
                // 编号竖标：全窗唯一的黄铜装饰（与主按钮同为黄铜）
                using (SolidBrush b = new SolidBrush(_theme.Accent))
                    g.FillRectangle(b, S(PadX), y + S(8), Math.Max(2, S(BarW)), S(BarH));
                // 01 / 02 / 03：等宽小字，颜色走辅助色（不单独承担信息）
                TextRenderer.DrawText(g, "0" + (i + 1), _numFont,
                    new Rectangle(S(NumX), y + S(8), S(NumW), S(24)), _theme.Aux,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                // 图标：结构线（金属色），只是给每步一个参照物，旁边永远有文字
                Rectangle ir = new Rectangle(Width - S(PadX) - S(16), y + S(12), S(16), S(16));
                Icons.Draw(g, icons[i], ir, _theme.Metal, _dpi);
            }
        }
    }
}
