// SettingsForm.cs — v6 设置页：自绘 48px 顶条 + 两栏表单 + 56px 固定底栏
// 结构：顶条（标题 + ×，空白可拖动）→ 可滚动内容（外观 / 成长 / 提醒 / 数据与接口 / 关于）…
//        …→ 固定底栏（保存实底主按钮 + 关闭文字按钮）
// 版式：左标签列 160、右控件列到右边距、左右页边 32、行高 40、分组标题 12px + 1px 线、组间额外 16
// 令牌：字号只取 Theme 令牌（FTitle/FBody/FAux/FCaption）；颜色只取 Theme 字段；全直角、无阴影、无渐变
// DPI：构造函数不建控件；OnLoad 取真实 DPI 后 BuildUi，所有尺寸经 S() 落到像素
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public class SettingsForm : FrameForm
    {
        // ---------- 逻辑尺寸（全部经 S() 乘 DPI） ----------
        private const int W = 720;              // 窗口宽
        private const int HMax = 640;           // 窗口高上限
        private const int HMin = 520;           // 窗口高下限
        private const int PadX = 32;            // 左右页边
        private const int HeadH = 48;           // 自绘顶条
        private const int FootH = 56;           // 固定底栏
        private const int RowH = 40;            // 表单行高
        private const int CtrlH = 32;           // 控件高
        private const int LabelW = 160;         // 左标签列
        private const int ColGap = 24;          // 标签列到控件列
        private const int GroupHeadH = 42;      // 分组标题（文字 + 线）占高
        private const int GroupGapExtra = 16;   // 组间额外间距
        private const int ContentTop = 16;      // 内容顶部留白
        private const int ContentBottom = 24;   // 内容底部留白
        private const int BtnCopyW = 64;        // 复制按钮宽
        private const int BtnSaveW = 88;        // 保存按钮宽
        private const int BtnCloseW = 72;       // 关闭按钮宽
        private const int BtnOpenW = 160;       // 打开数据目录按钮宽

        private enum RowKind
        {
            Header,   // 分组标题 + 下方 1px 线
            Row,      // 标准行：左标签 + 右控件（可带第二个控件）
            Wrap      // 通栏说明 / 关于条目，可换行
        }

        private sealed class RowSpec
        {
            public RowKind Kind;
            public Control Label;
            public Control Ctrl;
            public Control Ctrl2;
            public int CtrlW;       // 0 = 撑满控件列
            public int CtrlW2;
            public int GapAfter;    // 行后额外间距（逻辑 px）
        }

        private readonly Store _store;
        private Theme _theme;
        private float _dpi = 1f;
        private int _designH = 640;

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // 窗框
        private Panel _head;
        private Label _title;
        private FlatButton _btnX;
        private Panel _headLine;
        private Panel _content;
        private Panel _footer;
        private Panel _footLine;
        private Panel _vbar;
        private bool _vbarDrag;
        private int _vbarDragY;
        private int _vbarDragVal;
        private int _vbarMax;
        private int _vbarBarH;
        private FlatButton _btnSave;
        private FlatButton _btnClose;

        // 设置控件
        private FlatSelect _themeSel;
        private FlatSelect _langSel;
        private FlatSelect _fontSel;
        private FlatCheck _growthOn;
        private FlatCheck _growthAll;
        private FlatField _growthDays;
        private FlatCheck _l1;
        private FlatCheck _l2;
        private FlatCheck _l3;
        private FlatCheck _sound;
        private FlatCheck _flash;
        private FlatField _dailyLimit;
        private FlatField _port;
        private FlatField _token;

        // 版式行模型（BuildUi 记录，LayoutPass 统一摆放）
        private readonly List<RowSpec> _rows = new List<RowSpec>();
        private bool _layingOut;

        public SettingsForm(Store store, FloatingForm floating)
        {
            _store = store;
            _theme = Theme.FromSetting(store.GetSetting("theme", "system"));
            Icon = Program.AppIcon();
            Text = Strings.L("set.title") + " · " + Strings.L("app.title");
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = _theme.Paper;
            ForeColor = _theme.Text;
        }

        // 固定尺寸窗体：不加可缩放边、不响应边缘命中（FrameForm 用这个开关）
        protected override bool Resizable { get { return false; } }

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        private int S(float v) { return (int)Math.Round(v * _dpi); }

        // 1 逻辑 px 的线：按 DPI 取整，至少 1 设备 px
        private int Line1() { return Math.Max(1, (int)Math.Round(_dpi)); }

        private Font F(float px, FontStyle style)
        {
            return new Font(Theme.FontUI, S(px), style, GraphicsUnit.Pixel);
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
                if (float.TryParse(force, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) && f >= 0.5f && f <= 4f) dpi = f;
            }
            if (dpi <= 0.1f) dpi = 1f;
            _dpi = dpi;

            // 高 = min(640, 工作区高 - 80)，下不低过 520
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int logicalWaH = (int)Math.Round(wa.Height / _dpi);
            int h = logicalWaH - 80;
            if (h > HMax) h = HMax;
            if (h < HMin) h = HMin;
            _designH = h;

            ClientSize = new Size(S(W), S(h));
            BuildUi();
            LayoutChrome();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Placement.CenterOnOwner(this, Owner);
            Win32Theme.DarkenScrollbars(_content);
            try { if (_content.IsHandleCreated) ShowScrollBar(_content.Handle, 1, false); } catch { }
            UpdateVBar();
            FadeIn();
        }

        private void FitToActualClient()
        {
            try
            {
                RECT cr;
                if (!GetWindowRect(Handle, out cr)) return;
                int aw = cr.Right - cr.Left, ah = cr.Bottom - cr.Top;
                int cw = ClientSize.Width, ch = ClientSize.Height;
                if (aw <= 0 || ah <= 0 || cw <= 0 || ch <= 0) return;
                float fit = Math.Min(aw / (float)cw, ah / (float)ch);
                if (fit < 0.98f && fit > 0.5f)
                {
                    _dpi = Math.Max(0.75f, _dpi * fit);
                    ClientSize = new Size(S(W), S(_designH));
                    LayoutChrome();
                }
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 建界面 ----------
        private void BuildUi()
        {
            SuspendLayout();

            // ---- 自绘顶条 ----
            _head = new Panel();
            _head.BackColor = _theme.Paper;
            MouseEventHandler dragDown = delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) DragMove();
            };
            _head.MouseDown += dragDown;

            _title = MakeLabel(Strings.L("set.title"), Theme.FTitle, _theme.Text, true);
            _title.AccessibleName = Strings.L("set.title");
            _title.MouseDown += dragDown;
            _head.Controls.Add(_title);

            _btnX = new FlatButton();
            _btnX.Kind = ButtonKind.Icon;
            _btnX.Text = "×";
            _btnX.Theme = _theme;
            _btnX.Dpi = _dpi;
            _btnX.Font = F(Theme.FBody, FontStyle.Regular);
            _btnX.AccessibleName = Strings.L("set.close");
            _btnX.Click += delegate { Close(); };
            _head.Controls.Add(_btnX);

            _headLine = new Panel();
            _headLine.BackColor = _theme.Line;
            _headLine.MouseDown += dragDown;
            _head.Controls.Add(_headLine);
            Controls.Add(_head);

            // ---- 固定底栏 ----
            _footer = new Panel();
            _footer.BackColor = _theme.Paper;

            _footLine = new Panel();
            _footLine.BackColor = _theme.LineStrong;
            _footer.Controls.Add(_footLine);

            _btnSave = NewButton(Strings.L("set.save"), ButtonKind.Primary);
            _btnSave.Click += delegate { SaveAll(); };
            _footer.Controls.Add(_btnSave);

            _btnClose = NewButton(Strings.L("set.close"), ButtonKind.Secondary);
            _btnClose.Click += delegate { Close(); };
            _footer.Controls.Add(_btnClose);
            Controls.Add(_footer);

            // ---- 可滚动内容 ----
            _content = new Panel();
            _content.AutoScroll = true;
            _content.BackColor = _theme.Card;   // batch2 #3: content sits on the raised card, not the paper
            _content.ClientSizeChanged += delegate { LayoutContent(); };
            _content.Resize += delegate { LayoutContent(); };
            _content.Scroll += delegate { UpdateVBar(); };
            _content.HandleCreated += delegate { try { ShowScrollBar(_content.Handle, 1, false); } catch { } };
            Controls.Add(_content);
            // batch2 #3: 1px Accent rule at the very top of the content card (visual separator from the header)
            _content.Paint += delegate(object s0, PaintEventArgs pe0)
            {
                using (SolidBrush ab = new SolidBrush(_theme.Accent)) pe0.Graphics.FillRectangle(ab, 0, 0, _content.Width, Math.Max(1, S(FrameTokens.Outer)));
            };
            _vbar = new Panel();
            _vbar.BackColor = _theme.Metal;
            _vbar.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                _vbarDrag = true;
                _vbarDragY = Cursor.Position.Y;
                _vbarDragVal = _content.VerticalScroll.Value;
            };
            _vbar.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!_vbarDrag) return;
                int trackH = Math.Max(1, _content.ClientSize.Height - _vbarBarH);
                int dy = Cursor.Position.Y - _vbarDragY;
                int v = _vbarDragVal + (_vbarMax <= 0 ? 0 : dy * _vbarMax / trackH);
                if (v < 0) v = 0;
                if (v > _vbarMax) v = _vbarMax;
                _content.AutoScrollPosition = new Point(0, v);
                UpdateVBar();
            };
            _vbar.MouseUp += delegate { _vbarDrag = false; };
            Controls.Add(_vbar);
            _vbar.BringToFront();

            BuildSections();

            ResumeLayout(false);
            PerformLayout();
        }

        private void BuildSections()
        {
            // ---- 外观 ----
            AddGroup(Strings.Lang == "en" ? "Appearance" : "外观");

            _themeSel = new FlatSelect();
            _themeSel.Options = new string[] { Strings.L("set.theme.auto"), "v6 暖黑", "v7 暖账本", "v8 石墨", "v1 暖纸" };
            string th = _store.GetSetting("theme", "system");
            int thIdx = th == "v6" || th == "night" ? 1 : (th == "v7" || th == "day" ? 2 : (th == "v8" || th == "graphite" ? 3 : (th == "v1" ? 4 : 0)));
            _themeSel.SelectedIndex = thIdx;
            AddSelectRow(Strings.L("set.theme"), _themeSel, Strings.L("set.theme"));

            _langSel = new FlatSelect();
            _langSel.Options = new string[] { "中文", "English" };
            _langSel.SelectedIndex = _store.GetSetting("lang", "zh") == "en" ? 1 : 0;
            AddSelectRow(Strings.L("set.lang"), _langSel, Strings.L("set.lang"));

            _fontSel = new FlatSelect();
            _fontSel.Options = new string[] { Strings.L("set.font.s"), Strings.L("set.font.m"), Strings.L("set.font.l") };
            string fontSetting = _store.GetSetting("font_scale", "m");
            _fontSel.SelectedIndex = fontSetting == "l" ? 2 : (fontSetting == "s" ? 0 : 1);
            AddSelectRow(Strings.L("set.font"), _fontSel, Strings.L("set.font"));
            EndGroup();

            // ---- 成长 ----
            AddGroup(Strings.L("set.growth"));

            _growthOn = NewCheck();
            _growthOn.Checked = _store.GetSetting("growth_on", "1") == "1";
            AddCheckRow(Strings.L("set.growth_on"), _growthOn, Strings.L("set.growth_on"));

            _growthAll = NewCheck();
            _growthAll.Checked = _store.GetSetting("growth_all", "0") == "1";
            AddCheckRow(Strings.L("set.growth_all"), _growthAll, Strings.L("set.growth_all"));

            AddWrap(Strings.Lang == "en"
                ? "Includes recurring and reminded todos. Older ones grow more prominent; completing ends growth."
                : "含循环/有提醒的待办；开启后，待办会随时间逐渐变醒目，完成即停止成长。", 4);

            _growthDays = NewField();
            _growthDays.Text = _store.GetSetting("growth_days", "90");
            AddFieldRow(Strings.L("set.growth_days"), _growthDays, 120, Strings.L("set.growth_days"));
            EndGroup();

            // ---- 提醒 ----
            AddGroup(Strings.L("set.remind"));

            _l1 = NewCheck();
            _l1.Checked = _store.GetSetting("remind_l1", "1") == "1";
            AddCheckRow(Strings.L("set.remind_l1"), _l1, Strings.L("set.remind_l1"));

            _l2 = NewCheck();
            _l2.Checked = _store.GetSetting("remind_l2", "1") == "1";
            AddCheckRow(Strings.L("set.remind_l2"), _l2, Strings.L("set.remind_l2"));

            _l3 = NewCheck();
            _l3.Checked = _store.GetSetting("remind_l3", "1") == "1";
            AddCheckRow(Strings.L("set.remind_l3"), _l3, Strings.L("set.remind_l3"));

            _sound = NewCheck();
            _sound.Checked = _store.GetSetting("sound", "1") == "1";
            AddCheckRow(Strings.L("set.sound"), _sound, Strings.L("set.sound"));

            _flash = NewCheck();
            _flash.Checked = _store.GetSetting("flash", "1") == "1";
            AddCheckRow(Strings.L("set.flash"), _flash, Strings.L("set.flash"));

            _dailyLimit = NewField();
            _dailyLimit.Text = _store.GetSetting("daily_limit", "20");
            AddFieldRow(Strings.L("set.daily_limit"), _dailyLimit, 120, Strings.L("set.daily_limit"));

            AddWrap(Strings.L("set.level_hint"), 0);
            EndGroup();

            // ---- 数据与接口 ----
            AddGroup(Strings.Lang == "en" ? "Data & API" : "数据与接口");

            _port = NewField();
            _port.Text = _store.GetSetting("http_port", "8756");
            AddFieldRow(Strings.L("set.http_port"), _port, 120, Strings.L("set.http_port"));

            _token = NewField();
            _token.ReadOnly = true;
            _token.Text = _store.GetSetting("api_token", "");
            _token.AccessibleName = Strings.L("set.api_token");
            _token.Box.AccessibleName = Strings.L("set.api_token");
            FlatButton copy = NewButton(Strings.L("set.copy"), ButtonKind.Secondary);
            copy.Click += delegate { CopyToken(); };
            AddRow(Strings.L("set.api_token"), _token, 0, copy, BtnCopyW);

            FlatButton open = NewButton(Strings.L("set.open_data"), ButtonKind.Secondary);
            open.Click += delegate { OpenDataDir(); };
            AddRow(null, open, BtnOpenW, null, 0);

            AddWrap(Strings.L("set.restart_hint"), 0);
            EndGroup();

            // ---- 关于 ----
            AddGroup(Strings.L("main.about"));

            AddWrap(Strings.L("app.title") + " v0.11", 6);
            AddWrap(Strings.L("about.license"), 6);
            AddWrap(Strings.L("about.unsigned"), 6);
            AddWrap(Strings.L("about.data") + Paths.DataDir(), 6);
            AddWrap(Strings.L("about.disclaimer"), 0);
            EndGroup();
        }

        // ---------- 控件工厂 ----------
        private Label MakeLabel(string text, float fontPx, Color color, bool bold)
        {
            Label l = new Label();
            l.Text = text == null ? "" : text;
            l.AutoSize = false;
            l.UseMnemonic = false;
            l.Font = F(fontPx, bold ? FontStyle.Bold : FontStyle.Regular);
            l.ForeColor = color;
            l.BackColor = _theme.Paper;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AccessibleName = l.Text;
            return l;
        }

        private FlatCheck NewCheck()
        {
            FlatCheck c = new FlatCheck();
            c.Theme = _theme;
            c.Dpi = _dpi;
            c.Font = F(Theme.FBody, FontStyle.Regular);
            c.Text = "";
            return c;
        }

        private FlatField NewField()
        {
            FlatField f = new FlatField();
            f.ApplyDpi(_dpi, Theme.FBody);
            f.ApplyTheme(_theme);
            return f;
        }

        private FlatButton NewButton(string text, ButtonKind kind)
        {
            FlatButton b = new FlatButton();
            b.Kind = kind;
            b.Text = text;
            b.Theme = _theme;
            b.Dpi = _dpi;
            b.Font = F(Theme.FAux, FontStyle.Regular);
            b.AccessibleName = text;
            return b;
        }

        // ---------- 行模型 ----------
        private void AddGroup(string title)
        {
            Label l = MakeLabel(title, Theme.FCaption, _theme.Aux, false);
            l.BackColor = _theme.Card;
            Panel line = new Panel();
            line.BackColor = _theme.Line;
            _content.Controls.Add(l);
            _content.Controls.Add(line);
            RowSpec r = new RowSpec();
            r.Kind = RowKind.Header;
            r.Label = l;
            r.Ctrl = line;
            _rows.Add(r);
        }

        private void EndGroup()
        {
            if (_rows.Count == 0) return;
            _rows[_rows.Count - 1].GapAfter = GroupGapExtra;
        }

        private void AddRow(string labelText, Control ctrl, int ctrlW, Control ctrl2, int ctrlW2)
        {
            RowSpec r = new RowSpec();
            r.Kind = RowKind.Row;
            r.CtrlW = ctrlW;
            r.CtrlW2 = ctrlW2;
            if (!string.IsNullOrEmpty(labelText))
            {
                Label l = MakeLabel(labelText, Theme.FAux, _theme.SubText, false);
                l.BackColor = _theme.Card;
                r.Label = l;
                _content.Controls.Add(l);
            }
            if (ctrl != null)
            {
                if (string.IsNullOrEmpty(ctrl.AccessibleName)) ctrl.AccessibleName = labelText;
                r.Ctrl = ctrl;
                _content.Controls.Add(ctrl);
            }
            if (ctrl2 != null)
            {
                if (string.IsNullOrEmpty(ctrl2.AccessibleName)) ctrl2.AccessibleName = labelText;
                r.Ctrl2 = ctrl2;
                _content.Controls.Add(ctrl2);
            }
            _rows.Add(r);
        }

        // 开关行：开关画在右控件列（自绘框），左标签可点切换
        private void AddCheckRow(string labelText, FlatCheck cb, string accessName)
        {
            cb.AccessibleName = accessName;
            AddRow(labelText, cb, 0, null, 0);
            Label l = _rows[_rows.Count - 1].Label as Label;
            if (l != null)
            {
                l.Cursor = Cursors.Hand;
                l.Click += delegate { cb.Checked = !cb.Checked; };
            }
        }

        private void AddSelectRow(string labelText, FlatSelect sel, string accessName)
        {
            sel.Theme = _theme;
            sel.Dpi = _dpi;
            sel.Font = F(Theme.FBody, FontStyle.Regular);
            sel.AccessibleName = accessName;
            AddRow(labelText, sel, 0, null, 0);
        }

        private void AddFieldRow(string labelText, FlatField f, int ctrlW, string accessName)
        {
            f.AccessibleName = accessName;
            if (f.Box != null) f.Box.AccessibleName = accessName;
            AddRow(labelText, f, ctrlW, null, 0);
        }

        // 通栏说明 / 关于条目：宽度撑满版心，按实际宽度换行
        private void AddWrap(string text, int gapAfter)
        {
            Label l = MakeLabel(text, Theme.FAux, _theme.Aux, false);
            l.BackColor = _theme.Card;
            l.TextAlign = ContentAlignment.TopLeft;
            _content.Controls.Add(l);
            RowSpec r = new RowSpec();
            r.Kind = RowKind.Wrap;
            r.Label = l;
            r.GapAfter = gapAfter;
            _rows.Add(r);
        }

        // ---------- 窗框布局 ----------
        private void LayoutChrome()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _head.SetBounds(0, 0, w, S(HeadH));
            int xw = S(32);
            int xh = S(32);
            _btnX.SetBounds(w - S(PadX) - xw, (S(HeadH) - xh) / 2, xw, xh);
            _title.SetBounds(S(PadX), 0, Math.Max(S(80), w - S(PadX) * 2 - xw - S(16)), S(HeadH));
            _headLine.SetBounds(0, S(HeadH) - Line1(), w, Line1());

            _footer.SetBounds(0, h - S(FootH), w, S(FootH));
            _footLine.SetBounds(0, 0, w, Line1());
            int by = (S(FootH) - S(CtrlH)) / 2;
            _btnSave.SetBounds(w - S(PadX) - S(BtnSaveW), by, S(BtnSaveW), S(CtrlH));
            _btnClose.SetBounds(w - S(PadX) - S(BtnSaveW) - S(8) - S(BtnCloseW), by, S(BtnCloseW), S(CtrlH));

            int contentH = h - S(HeadH) - S(FootH);
            if (contentH < S(120)) contentH = S(120);
            _content.SetBounds(0, S(HeadH), w, contentH);
            LayoutContent();
            UpdateVBar();
        }

        // 自检/截图用：滚到指定位置（不依赖鼠标滚轮）
        public void TestSetTheme(string key) { _store.SetSetting("theme", key); }
        public void TestSetFont(string s) { _store.SetSetting("font_scale", s); }

        public void TestScrollContent(int y)
        {
            if (_content == null) return;
            _content.AutoScrollPosition = new Point(0, Math.Max(0, y));
            UpdateVBar();
            _content.Invalidate(true);
        }

        // 自绘 3px 竖向滚动指示（系统滚动条已隐藏；滚轮/键盘仍由 AutoScroll 处理）
        private void UpdateVBar()
        {
            if (_vbar == null || _content == null) return;
            int viewH = _content.ClientSize.Height;
            int total = Math.Max(_content.AutoScrollMinSize.Height, _content.DisplayRectangle.Height);
            bool need = total > viewH + S(2);
            _vbar.Visible = need;
            if (!need) return;
            int max = Math.Max(1, total - viewH);
            int val = _content.VerticalScroll.Value;
            if (val < 0) val = 0;
            if (val > max) val = max;
            int barH = Math.Max(S(18), viewH * viewH / Math.Max(1, total));
            int top = _content.Top + (viewH - barH) * val / max;
            _vbarMax = max;
            _vbarBarH = barH;
            _vbar.BackColor = _theme.Metal;
            _vbar.SetBounds(_content.Left + _content.Width - S(3), top, S(3), barH);
            _vbar.BringToFront();
        }

        // 内容重排：滚动条出现/消失会改变客户区宽，按真实客户区宽重算，保证右列不被滚动条盖住
        private void LayoutContent()
        {
            if (_content == null || _layingOut) return;
            if (_content.ClientSize.Width <= 0) return;
            _layingOut = true;
            try
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    int before = _content.ClientSize.Width;
                    LayoutPass();
                    if (_content.ClientSize.Width == before) break;
                }
            }
            finally
            {
                _layingOut = false;
            }
        }

        private void LayoutPass()
        {
            int width = _content.ClientSize.Width;
            // 内容宽 = 客户区宽 - 2 × 页边（客户区宽已排除竖滚动条，右列不会被盖住）
            int innerW = width - S(PadX) * 2;
            int labelW = S(LabelW);
            int ctrlX = S(PadX) + labelW + S(ColGap);
            int ctrlW = innerW - labelW - S(ColGap);
            if (ctrlW < S(140)) ctrlW = S(140);

            int y = S(ContentTop);
            for (int i = 0; i < _rows.Count; i++)
            {
                RowSpec r = _rows[i];
                int rowH;
                if (r.Kind == RowKind.Header)
                {
                    if (r.Label != null) r.Label.SetBounds(S(PadX), y, innerW, S(20));
                    if (r.Ctrl != null) r.Ctrl.SetBounds(S(PadX), y + S(26), innerW, Line1());
                    rowH = S(GroupHeadH);
                }
                else if (r.Kind == RowKind.Wrap)
                {
                    int wrapH = MeasureWrap(r.Label, innerW);
                    if (r.Label != null) r.Label.SetBounds(S(PadX), y, innerW, wrapH);
                    rowH = wrapH;
                }
                else
                {
                    int ch = S(CtrlH);
                    int cy = y + (S(RowH) - ch) / 2;
                    if (r.Label != null) r.Label.SetBounds(S(PadX), y, labelW, S(RowH));
                    if (r.Ctrl != null)
                    {
                        if (r.Ctrl2 != null)
                        {
                            int w2 = S(r.CtrlW2);
                            int gap = S(12);
                            int w1 = ctrlW - w2 - gap;
                            if (w1 < S(80)) w1 = S(80);
                            r.Ctrl.SetBounds(ctrlX, cy, w1, ch);
                            r.Ctrl2.SetBounds(ctrlX + w1 + gap, cy, w2, ch);
                        }
                        else
                        {
                            int cw = r.CtrlW > 0 ? Math.Min(S(r.CtrlW), ctrlW) : ctrlW;
                            r.Ctrl.SetBounds(ctrlX, cy, cw, ch);
                        }
                    }
                    rowH = S(RowH);
                }
                y += rowH + S(r.GapAfter);
            }
            _content.AutoScrollMinSize = new Size(0, y + S(ContentBottom));
        }

        private int MeasureWrap(Control c, int width)
        {
            if (c == null) return 0;
            Size sz = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(10, width), 100000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(S(18), sz.Height + S(3));
        }

        // ---------- 动作 ----------
        private void CopyToken()
        {
            try
            {
                if (_token.Text.Length > 0) Clipboard.SetText(_token.Text);
            }
            catch { }
        }

        private void OpenDataDir()
        {
            try { Process.Start("explorer.exe", Paths.DataDir()); } catch { }
        }

        private void FadeIn()
        {
            Opacity = 0.0;
            Timer t = new Timer();
            t.Interval = 12;
            int step = 0;
            t.Tick += delegate
            {
                if (IsDisposed || Disposing) { t.Stop(); t.Dispose(); return; }
                step++;
                double v = step / 8.0;
                if (v >= 1) { Opacity = 1.0; t.Stop(); t.Dispose(); return; }
                Opacity = v;
            };
            t.Start();
        }

        // ---------- 保存（键与行为保持 v5 原样） ----------
        private void SaveAll()
        {
            int ti = _themeSel.SelectedIndex;
            string th = ti == 1 ? "v6" : (ti == 2 ? "v7" : (ti == 3 ? "v8" : (ti == 4 ? "v1" : "system")));
            _store.SetSetting("theme", th);
            _store.SetSetting("lang", _langSel.SelectedIndex == 1 ? "en" : "zh");
            _store.SetSetting("font_scale", _fontSel.SelectedIndex == 2 ? "l" : (_fontSel.SelectedIndex == 0 ? "s" : "m"));
            _store.SetSetting("growth_on", _growthOn.Checked ? "1" : "0");
            _store.SetSetting("growth_all", _growthAll.Checked ? "1" : "0");
            _store.SetSetting("growth_days", _growthDays.Text.Trim());
            _store.SetSetting("remind_l1", _l1.Checked ? "1" : "0");
            _store.SetSetting("remind_l2", _l2.Checked ? "1" : "0");
            _store.SetSetting("remind_l3", _l3.Checked ? "1" : "0");
            _store.SetSetting("sound", _sound.Checked ? "1" : "0");
            _store.SetSetting("flash", _flash.Checked ? "1" : "0");
            _store.SetSetting("daily_limit", _dailyLimit.Text.Trim());
            _store.SetSetting("http_port", _port.Text.Trim());
            Strings.Lang = _store.GetSetting("lang", "zh");
            MessageBox.Show(this, Strings.Lang == "en" ? "Saved. Some labels apply after restart." : "已保存。部分文字重启后完全生效。");
            Close();
        }
    }
}
