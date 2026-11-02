// SetupUi.cs — 安装器/卸载器共用：暖黑账本视觉（与主程序 Theme.Night 同一套色阶/字号）、
// 自绘无边框窗框、直角按钮/勾选/单选框、错误三段式、沙箱与真机两套落点、自删除、截图。
// 说明：
//   * 字体尺寸用 GraphicsUnit.Pixel，对齐主程序的「逻辑 px」字号层；
//   * 全直角、无阴影、无渐变、无纹理；一屏只有一个黄铜实底主按钮；
//   * 兼容 C# 5（不用字符串插值 / ?. / nameof / 自动属性初始化器；事件用 delegate）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Decanter.Setup
{
    // ---------------- 色阶与绘制小工具（对应 Theme.Night 的语义名） ----------------
    internal static class Ui
    {
        public const string FontUI = "Microsoft YaHei UI";

        public static readonly Color Paper = Hex("#15110D");   // 场：暖黑
        public static readonly Color Card = Hex("#1D1813");    // 面：纸面
        public static readonly Color CardHi = Hex("#251E17");  // 抬高面
        public static readonly Color Line = Hex("#2E2820");    // 1px 结构线
        public static readonly Color LineStrong = Hex("#3D362B");
        public static readonly Color Metal = Hex("#7A705F");   // 金属结构线
        public static readonly Color Ink = Hex("#EFE6D4");     // 墨
        public static readonly Color Sub = Hex("#B3A58C");     // 次要
        public static readonly Color Aux = Hex("#9C8E78");     // 辅助
        public static readonly Color Brass = Hex("#D89A4E");   // 黄铜：主操作
        public static readonly Color Danger = Hex("#E4795F");  // 危险
        public static readonly Color OkColor = Hex("#8FAE86"); // 成功
        public static readonly Color Hover = Hex("#211B15");
        public static readonly Color Selected = Hex("#2A231B");

        public static Color Hex(string s) { return ColorTranslator.FromHtml(s); }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c); }

        public static Color Mix(Color a, Color b, int pct)
        {
            int p = Math.Max(0, Math.Min(100, pct));
            return Color.FromArgb(
                (a.R * (100 - p) + b.R * p) / 100,
                (a.G * (100 - p) + b.G * p) / 100,
                (a.B * (100 - p) + b.B * p) / 100);
        }

        public static void Fill(Graphics g, Color c, Rectangle r) { Fill(g, c, r.X, r.Y, r.Width, r.Height); }

        public static void Fill(Graphics g, Color c, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
        }

        // 1px 直角描边（用 FillRectangle 保证像素对齐、不发虚）
        public static void Frame(Graphics g, Color c, Rectangle r) { Frame(g, c, r.X, r.Y, r.Width, r.Height); }

        public static void Frame(Graphics g, Color c, int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillRectangle(b, x, y, w, 1);
                g.FillRectangle(b, x, y + h - 1, w, 1);
                g.FillRectangle(b, x, y, 1, h);
                g.FillRectangle(b, x + w - 1, y, 1, h);
            }
        }

        public static int Measure(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        public static int LineHeight(Graphics g, Font f)
        {
            return (int)Math.Ceiling(f.GetHeight(g)) + 4;
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, int x, int y, int w)
        {
            int h = (int)Math.Ceiling(f.GetHeight(g)) + 2;
            TextRenderer.DrawText(g, s, f, new Rectangle(x, y, w, h), c,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static void DrawTextRight(Graphics g, string s, Font f, Color c, int x, int y, int w, int h)
        {
            TextRenderer.DrawText(g, s, f, new Rectangle(x, y, w, h), c,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static List<string> Wrap(Graphics g, string text, Font f, int maxWidth)
        {
            List<string> lines = new List<string>();
            if (text == null) text = "";
            string[] paras = text.Replace("\r\n", "\n").Split('\n');
            foreach (string para in paras)
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                StringBuilder cur = new StringBuilder();
                foreach (char ch in para)
                {
                    string cand = cur.ToString() + ch;
                    if (cur.Length > 0 && Measure(g, cand, f) > maxWidth)
                    {
                        lines.Add(cur.ToString());
                        cur.Length = 0;
                    }
                    cur.Append(ch);
                }
                if (cur.Length > 0) lines.Add(cur.ToString());
            }
            return lines;
        }

        public static int DrawWrapped(Graphics g, string text, Font f, Color c, int x, int y, int maxWidth)
        {
            List<string> lines = Wrap(g, text, f, maxWidth);
            int lh = LineHeight(g, f);
            for (int i = 0; i < lines.Count; i++) DrawText(g, lines[i], f, c, x, y + i * lh, maxWidth);
            return lines.Count * lh;
        }
    }

    // ---------------- 可点区域（自绘控件的命中盒） ----------------
    internal class Zone
    {
        public string Id = "";
        public Rectangle Rect;
        public bool Enabled = true;
        public bool Hover;
        public bool Pressed;
        public Action Click;
    }

    // ---------------- 自绘无边框窗框：拖动 + × + Esc/Enter + 命中 ----------------
    internal class LedgerForm : Form
    {
        protected readonly List<Zone> Zones = new List<Zone>();
        protected Font FTitle;
        protected Font FHead;
        protected Font FBody;
        protected Font FBodyStrong;
        protected Font FSmall;
        protected Font FSmallStrong;
        protected Font FButton;
        private Zone _hot;
        private Zone _down;
        private bool _noActivate;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        protected LedgerForm(int w, int h)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(w, h);
            BackColor = Ui.Paper;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            KeyPreview = true;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = "醒酒器";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FTitle = Mk(22f, FontStyle.Bold);
            FHead = Mk(15f, FontStyle.Bold);
            FBody = Mk(15f, FontStyle.Regular);
            FBodyStrong = Mk(14f, FontStyle.Bold);
            FSmall = Mk(12f, FontStyle.Regular);
            FSmallStrong = Mk(12f, FontStyle.Bold);
            FButton = Mk(13f, FontStyle.Regular);
        }

        private static Font Mk(float px, FontStyle st)
        {
            return new Font(Ui.FontUI, px, st, GraphicsUnit.Pixel);
        }

        // 截图时用：不抢焦点
        public void SetNoActivate() { _noActivate = true; }
        protected override bool ShowWithoutActivation { get { return _noActivate; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (FTitle != null) FTitle.Dispose();
                if (FHead != null) FHead.Dispose();
                if (FBody != null) FBody.Dispose();
                if (FBodyStrong != null) FBodyStrong.Dispose();
                if (FSmall != null) FSmall.Dispose();
                if (FSmallStrong != null) FSmallStrong.Dispose();
                if (FButton != null) FButton.Dispose();
            }
            base.Dispose(disposing);
        }

        protected Zone AddZone(string id, Rectangle rect, Action click)
        {
            Zone z = new Zone();
            z.Id = id;
            z.Rect = rect;
            z.Click = click;
            Zones.Add(z);
            return z;
        }

        protected Zone FindZone(string id)
        {
            for (int i = 0; i < Zones.Count; i++) if (Zones[i].Id == id) return Zones[i];
            return null;
        }

        // 清空并重建命中盒：× + 各屏自己的控件
        protected void Rebuild()
        {
            Zones.Clear();
            Zone close = new Zone();
            close.Id = "close";
            close.Rect = new Rectangle(Width - 44, 10, 30, 30);
            close.Click = delegate { OnCloseRequested(); };
            Zones.Add(close);
            BuildZones();
            Invalidate();
        }

        protected virtual void BuildZones() { }

        private Zone ZoneAt(Point p)
        {
            for (int i = Zones.Count - 1; i >= 0; i--)
            {
                Zone z = Zones[i];
                if (z.Enabled && z.Rect.Contains(p)) return z;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Zone z = ZoneAt(e.Location);
            if (z != null)
            {
                _down = z;
                z.Pressed = true;
                Invalidate();
                return;
            }
            // 空白处按下 → 拖动窗口（用系统命中，拖动跟手）
            try
            {
                ReleaseCapture();
                SendMessage(Handle, 0x00A1, 2, 0); // WM_NCLBUTTONDOWN / HTCAPTION
            }
            catch { }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_down != null && !_down.Rect.Contains(e.Location))
            {
                _down.Pressed = false;
                _down = null;
            }
            Zone z = ZoneAt(e.Location);
            if (z != _hot)
            {
                if (_hot != null) _hot.Hover = false;
                _hot = z;
                if (_hot != null) _hot.Hover = true;
                Cursor = (z != null) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Zone down = _down;
            _down = null;
            if (down != null)
            {
                down.Pressed = false;
                Invalidate();
                Zone z = ZoneAt(e.Location);
                if (z == down && down.Enabled && down.Click != null) down.Click();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != null) { _hot.Hover = false; _hot = null; Cursor = Cursors.Default; Invalidate(); }
            if (_down != null) { _down.Pressed = false; _down = null; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.Handled = true; OnEscape(); }
            else if (e.KeyCode == Keys.Enter) { e.Handled = true; OnEnter(); }
        }

        protected virtual void OnEscape() { }
        protected virtual void OnEnter() { }
        protected virtual void OnCloseRequested() { Close(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            using (SolidBrush b = new SolidBrush(Ui.Paper)) g.FillRectangle(b, ClientRectangle);
            OnPaintContent(g);
            // 窗框：1px 结构线 + 右上角自绘 ×
            Ui.Frame(g, Ui.Line, 0, 0, Width, Height);
            Zone close = FindZone("close");
            if (close != null)
            {
                Color c = (close.Hover || close.Pressed) && close.Enabled ? Ui.Brass : (close.Enabled ? Ui.Sub : Ui.LineStrong);
                int cx = close.Rect.X + close.Rect.Width / 2;
                int cy = close.Rect.Y + close.Rect.Height / 2;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(c, 1.4f))
                {
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx - 5, cy + 5, cx + 5, cy - 5);
                }
                g.SmoothingMode = SmoothingMode.None;
            }
        }

        protected virtual void OnPaintContent(Graphics g) { }

        // ---------------- 自绘控件 ----------------
        protected const int BtnSecondary = 0;
        protected const int BtnPrimary = 1;
        protected const int BtnDanger = 2;

        protected void DrawButton(Graphics g, string id, Rectangle r, string text, int kind)
        {
            Zone z = FindZone(id);
            bool enabled = z == null || z.Enabled;
            bool hover = z != null && z.Enabled && (z.Hover || z.Pressed);
            bool pressed = z != null && z.Pressed;
            Color fg = Ui.Sub;
            if (kind == BtnPrimary)
            {
                Color bg = !enabled ? Ui.CardHi
                    : (pressed ? Ui.Mix(Ui.Brass, Color.Black, 22)
                       : (hover ? Ui.Mix(Ui.Brass, Color.Black, 10) : Ui.Brass));
                fg = enabled ? Ui.Paper : Ui.Aux;
                Ui.Fill(g, bg, r);
            }
            else if (kind == BtnDanger)
            {
                fg = enabled ? Ui.Danger : Ui.Aux;
                if (hover) Ui.Fill(g, Ui.Alpha(Ui.Danger, pressed ? 48 : 26), r);
                if (enabled) Ui.Frame(g, Ui.Alpha(Ui.Danger, hover ? 180 : 96), r);
            }
            else
            {
                fg = !enabled ? Ui.Aux : (hover ? Ui.Ink : Ui.Sub);
                if (hover) Ui.Fill(g, Ui.CardHi, r);
                Color ul = !enabled ? Color.Empty : (pressed ? Ui.Brass : (hover ? Ui.Alpha(Ui.Brass, 160) : Color.Empty));
                if (ul != Color.Empty) Ui.Fill(g, ul, r.X + 8, r.Y + r.Height - 2, r.Width - 16, 1);
            }
            TextRenderer.DrawText(g, text, FButton, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected void DrawCheckSquare(Graphics g, bool on, Rectangle box, bool hover)
        {
            Ui.Fill(g, on ? Ui.Brass : Ui.Paper, box);
            Ui.Frame(g, on ? Ui.Brass : (hover ? Ui.Metal : Ui.Metal), box);
            if (on)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Ui.Paper, 2f))
                {
                    g.DrawLine(p, box.X + 4, box.Y + 9, box.X + 8, box.Y + 13);
                    g.DrawLine(p, box.X + 8, box.Y + 13, box.X + 14, box.Y + 5);
                }
                g.SmoothingMode = SmoothingMode.None;
            }
        }

        protected void DrawRadioSquare(Graphics g, bool on, Rectangle box, bool hover)
        {
            Ui.Frame(g, on ? Ui.Brass : (hover ? Ui.Metal : Ui.LineStrong), box);
            if (on)
            {
                Ui.Fill(g, Ui.Brass, box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
            }
        }

        // 错误三段式：发生了什么 / 可能原因 / 怎么办（人类语言，不出现错误码）
        protected int DrawThreeParts(Graphics g, int x, int y, int width, HumanError err)
        {
            int cur = y;
            cur = DrawPart(g, "发生了什么", err.What, x, cur, width);
            cur += 14;
            cur = DrawPart(g, "可能原因", err.Why, x, cur, width);
            cur += 14;
            cur = DrawPart(g, "怎么办", err.Todo, x, cur, width);
            return cur;
        }

        private int DrawPart(Graphics g, string label, string body, int x, int y, int width)
        {
            Ui.DrawText(g, label, FSmallStrong, Ui.Aux, x, y, width);
            int bodyY = y + 18;
            int h = Ui.DrawWrapped(g, body, FBody, Ui.Ink, x, bodyY, width);
            return bodyY + h;
        }
    }

    // ---------------- 人话错误 ----------------
    internal class HumanError
    {
        public string What = "";
        public string Why = "";
        public string Todo = "";

        public HumanError() { }

        public HumanError(string what, string why, string todo)
        {
            What = what;
            Why = why;
            Todo = todo;
        }

        public static HumanError Running(string dir)
        {
            return new HumanError(
                "醒酒器还在运行，文件正被它占着。",
                "程序没退出去之前，安装/卸载程序没法替换或删除它自己的文件。",
                "切到醒酒器，右键任务栏托盘里的图标选「退出」，再点「重试」。");
        }

        public static HumanError Map(Exception ex, string stage, string path)
        {
            bool inUse = Contains(ex, "being used", "正由另一进程使用", "另一个程序", "sharing violation", "被占用");
            bool noSpace = Contains(ex, "not enough space", "磁盘空间不足", "There is not enough space", "空间不够");
            bool denied = (ex is UnauthorizedAccessException) || Contains(ex, "access is denied", "拒绝访问", "没有权限");
            if (stage == "running") return Running(path);
            if (stage == "payload")
            {
                return new HumanError(
                    "安装包里的程序文件读不出来。",
                    "安装包可能没下载完整，或者被安全软件扫描时锁住了。",
                    "重新下载一次安装包，放到别的文件夹里再双击；然后点「重试」。");
            }
            if (stage == "export")
            {
                return new HumanError(
                    "数据没能复制到你选的位置。",
                    noSpace ? "目标磁盘可能已经满了。" : "那个文件夹可能没有写权限，或者被安全软件拦住了。",
                    "换一个文件夹（比如桌面）再试，或者返回去改成「保留数据」。");
            }
            if (stage == "shortcut")
            {
                return new HumanError(
                    "开始菜单里的两个快捷方式没建出来。",
                    "系统里的快捷方式组件没有响应，或者被安全软件拦住了。",
                    "点「重试」。如果一直不行，程序文件已经放好了，可以直接到安装目录运行 Decanter.exe。");
            }
            if (stage == "registry")
            {
                return new HumanError(
                    "系统的「添加或删除程序」里没登记上这一项。",
                    denied ? "当前账户不允许写这项注册表。" : "注册表写入被安全软件拦住了。",
                    "点「重试」。程序本身已经装好，不影响使用。");
            }
            if (stage == "autostart")
            {
                return new HumanError(
                    "开机自启没设上。",
                    denied ? "当前账户不允许写启动项。" : "启动项被安全软件拦住了。",
                    "点「重试」；也可以先不勾开机自启，装完从开始菜单打开。");
            }
            if (stage == "delete")
            {
                return new HumanError(
                    "有程序文件没删掉。",
                    inUse ? "醒酒器可能又启动了，或者文件被别的程序占着。" : "文件可能被安全软件保护着。",
                    "退出醒酒器再点「重试」；还不行就重启电脑后再卸载一次。");
            }
            if (stage == "dir")
            {
                return new HumanError(
                    "没能在 " + path + " 建安装目录。",
                    noSpace ? "磁盘可能已经满了。" : (denied ? "这个位置不允许当前账户写入。" : "这个位置可能被安全软件看住了，或者磁盘有问题。"),
                    "点「重试」；如果还是不行，换一台磁盘有空间、权限正常的电脑再装。");
            }
            return new HumanError(
                "程序文件没全放进安装目录。",
                inUse ? "醒酒器可能正在运行，文件被占着。" : (noSpace ? "磁盘可能已经满了。" : "写入被安全软件拦住了，或这个位置不允许写。"),
                "先退出醒酒器、确认磁盘还有空间，再点「重试」。");
        }

        private static bool Contains(Exception ex, params string[] keys)
        {
            if (ex == null || ex.Message == null) return false;
            string m = ex.Message.ToLowerInvariant();
            foreach (string k in keys) if (m.IndexOf(k.ToLowerInvariant()) >= 0) return true;
            return false;
        }
    }

    internal class HumanException : Exception
    {
        public HumanError Info;

        public HumanException(HumanError info) : base(info.What)
        {
            Info = info;
        }
    }

    // ---------------- 命令行 ----------------
    internal class CmdArgs
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static CmdArgs Parse(string[] args)
        {
            CmdArgs a = new CmdArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string s = args[i];
                if (s == null || !s.StartsWith("--")) continue;
                string key = s.Substring(2);
                if (i + 1 < args.Length && args[i + 1] != null && !args[i + 1].StartsWith("--"))
                {
                    a._values[key] = args[i + 1];
                    i++;
                }
                else a._values[key] = "1";
            }
            return a;
        }

        public bool Has(string name) { return _values.ContainsKey(name); }

        public string Get(string name, string fallback)
        {
            string v;
            if (_values.TryGetValue(name, out v)) return v;
            return fallback;
        }
    }

    // ---------------- 落点：真机 / 沙箱 ----------------
    internal class Layout
    {
        public bool Sandbox;
        public string Root = "";
        public string InstallDir = "";
        public string StartMenuDir = "";
        public string RegistryTxt = "";
        public string AutostartTxt = "";
        public string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Decanter";
        public string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public string ExePath { get { return Path.Combine(InstallDir, "Decanter.exe"); } }
        public string UninstallPath { get { return Path.Combine(InstallDir, "Uninstall.exe"); } }
        public string DataDir { get { return Path.Combine(InstallDir, "data"); } }
        public string MarkerTxt { get { return Path.Combine(InstallDir, "sandbox.txt"); } }

        // 沙箱根目录必须在当前工作目录内，防止测试误写真实路径
        public static string GuardSandboxRoot(string root)
        {
            if (string.IsNullOrEmpty(root))
                throw new HumanException(new HumanError("沙箱测试缺少目录参数。", "命令里 --sandbox 后面没有给目录。", "补上目录再试，例如 --sandbox run\\installer-test-a。"));
            string cwd = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd('\\');
            string abs = Path.GetFullPath(root).TrimEnd('\\');
            bool inside = abs.Equals(cwd, StringComparison.OrdinalIgnoreCase) ||
                          abs.StartsWith(cwd + "\\", StringComparison.OrdinalIgnoreCase);
            if (!inside)
            {
                throw new HumanException(new HumanError(
                    "沙箱目录不在当前工作目录里，已拒绝。",
                    "测试模式只允许把落点放在本工作区里，避免误写真实系统。",
                    "把 --sandbox 的目录改成当前工作目录下的子目录，例如 run\\installer-test-a。"));
            }
            return abs;
        }

        public static Layout FromSandbox(string root)
        {
            string abs = GuardSandboxRoot(root);
            Layout l = new Layout();
            l.Sandbox = true;
            l.Root = abs;
            l.InstallDir = Path.Combine(abs, "program");
            l.StartMenuDir = Path.Combine(abs, "StartMenu");
            l.RegistryTxt = Path.Combine(abs, "registry.txt");
            l.AutostartTxt = Path.Combine(abs, "autostart.txt");
            Directory.CreateDirectory(abs);
            return l;
        }

        // 卸载器：本目录里有 sandbox.txt 时，按记录的根目录继续走沙箱
        public static Layout FromSandboxMarker(string root, string installDir)
        {
            string abs = GuardSandboxRoot(root);
            Layout l = new Layout();
            l.Sandbox = true;
            l.Root = abs;
            l.InstallDir = installDir;
            l.StartMenuDir = Path.Combine(abs, "StartMenu");
            l.RegistryTxt = Path.Combine(abs, "registry.txt");
            l.AutostartTxt = Path.Combine(abs, "autostart.txt");
            return l;
        }

        // 真机安装：默认 D:\醒酒器；D 盘不存在时回退 %LOCALAPPDATA%\Programs\Decanter
        public static Layout ForRealInstall()
        {
            Layout l = new Layout();
            l.Sandbox = false;
            l.InstallDir = DefaultInstallDir();
            l.StartMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "醒酒器");
            return l;
        }

        public static string DefaultInstallDir()
        {
            string preferred = @"D:\醒酒器";
            try { if (Directory.Exists("D:\\")) return preferred; }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Decanter");
        }

        public static string LocalFallbackDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Decanter");
        }

        // 真机卸载：安装目录就是 exe 自己所在的目录
        public static Layout ForRealUninstall(string installDir)
        {
            Layout l = new Layout();
            l.Sandbox = false;
            l.InstallDir = installDir;
            l.StartMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "醒酒器");
            return l;
        }
    }

    // ---------------- 系统落点操作（真机 = 注册表/COM；沙箱 = 等价文本文件） ----------------
    internal class SysOps
    {
        public Layout L;

        public SysOps(Layout layout) { L = layout; }

        public void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        // 写 .lnk：用 WScript.Shell 的晚绑定，避免编译期 COM 引用
        public void CreateShortcut(string lnkPath, string target, string workDir, string iconPath)
        {
            string parent = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("WScript.Shell 不可用");
            object ws = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, ws, new object[] { lnkPath });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { iconPath + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            if (!File.Exists(lnkPath)) throw new IOException("快捷方式没有写成功");
        }

        public bool AutostartExists()
        {
            if (L.Sandbox) return File.Exists(L.AutostartTxt);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(L.RunKeyPath, false))
                {
                    if (k == null) return false;
                    return k.GetValue("Decanter") != null;
                }
            }
            catch { return false; }
        }

        public void SetAutostart(bool on)
        {
            if (L.Sandbox)
            {
                if (on)
                {
                    Directory.CreateDirectory(L.Root);
                    File.WriteAllText(L.AutostartTxt, "Decanter=\"" + L.ExePath + "\"\r\n", new UTF8Encoding(false));
                }
                else
                {
                    if (File.Exists(L.AutostartTxt)) File.Delete(L.AutostartTxt);
                }
                return;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(L.RunKeyPath))
            {
                if (on) k.SetValue("Decanter", "\"" + L.ExePath + "\"", RegistryValueKind.String);
                else k.DeleteValue("Decanter", false);
            }
        }

        public void RemoveAutostart()
        {
            try { SetAutostart(false); }
            catch
            {
                if (!L.Sandbox) throw;
            }
        }

        public void RegisterUninstall(int sizeKb)
        {
            if (L.Sandbox)
            {
                Directory.CreateDirectory(L.Root);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[" + L.RegistryKeyPath + "]");
                sb.AppendLine("DisplayName=醒酒器 Decanter");
                sb.AppendLine("DisplayVersion=0.11");
                sb.AppendLine("Publisher=CiviWineHouse Studio");
                sb.AppendLine("InstallLocation=" + L.InstallDir);
                sb.AppendLine("DisplayIcon=" + L.ExePath + ",0");
                sb.AppendLine("UninstallString=\"" + L.UninstallPath + "\"");
                sb.AppendLine("EstimatedSize=" + sizeKb.ToString());
                sb.AppendLine("NoModify=1");
                sb.AppendLine("NoRepair=1");
                File.WriteAllText(L.RegistryTxt, sb.ToString(), new UTF8Encoding(false));
                return;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(L.RegistryKeyPath))
            {
                k.SetValue("DisplayName", "醒酒器 Decanter", RegistryValueKind.String);
                k.SetValue("DisplayVersion", "0.11", RegistryValueKind.String);
                k.SetValue("Publisher", "CiviWineHouse Studio", RegistryValueKind.String);
                k.SetValue("InstallLocation", L.InstallDir, RegistryValueKind.String);
                k.SetValue("DisplayIcon", L.ExePath + ",0", RegistryValueKind.String);
                k.SetValue("UninstallString", "\"" + L.UninstallPath + "\"", RegistryValueKind.String);
                k.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public bool UninstallEntryExists()
        {
            if (L.Sandbox) return File.Exists(L.RegistryTxt);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(L.RegistryKeyPath, false))
                    return k != null;
            }
            catch { return false; }
        }

        public void RemoveUninstallEntry()
        {
            if (L.Sandbox)
            {
                if (File.Exists(L.RegistryTxt)) File.Delete(L.RegistryTxt);
                return;
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(L.RegistryKeyPath, false); }
            catch (ArgumentException) { }
        }
    }

    // ---------------- 进程检查：只拦本安装目录里的醒酒器；真机下见到醒酒器就拦（更稳） ----------------
    internal static class ProcessCheck
    {
        public static bool IsDecanterRunning(Layout l)
        {
            Process[] list;
            try { list = Process.GetProcessesByName("Decanter"); }
            catch { return false; }
            try
            {
                foreach (Process p in list)
                {
                    try
                    {
                        if (!l.Sandbox) return true;
                        string path = null;
                        try { path = p.MainModule.FileName; }
                        catch { }
                        if (path != null && path.StartsWith(l.InstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { if (!l.Sandbox) return true; }
                }
            }
            finally
            {
                foreach (Process p in list) { try { p.Dispose(); } catch { } }
            }
            return false;
        }
    }

    // ---------------- 卸载逐项结果 ----------------
    internal class StepResult
    {
        public string Name = "";
        public bool Ok;
        public string Detail = "";

        public StepResult() { }

        public StepResult(string name, bool ok, string detail)
        {
            Name = name;
            Ok = ok;
            Detail = detail;
        }
    }

    // ---------------- 退出后自删除（cmd 放 %TEMP%，不弹窗、不留在程序目录） ----------------
    internal static class SelfClean
    {
        // 先把正在运行的 Uninstall.exe 改个名（Windows 允许重命名运行中的 exe，不允许删除），
        // 原路径立刻空出来，避免「刚卸完又马上重装」时撞上还没删掉的卸载程序。
        public static string TryRenameSelf(string selfPath)
        {
            try
            {
                string tmp = Path.Combine(Path.GetDirectoryName(selfPath),
                    "uninstall-leaving-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                File.Move(selfPath, tmp);
                return tmp;
            }
            catch { return null; }
        }

        public static string Schedule(string selfPath, string installDir, bool removeInstallDir, string renamedSelf)
        {
            string target = renamedSelf != null ? renamedSelf : selfPath;
            string body = BuildScript(target, installDir, removeInstallDir);
            string[] dirs = new string[]
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                installDir,
                Directory.GetCurrentDirectory()
            };
            string lastError = null;
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                if (string.IsNullOrEmpty(dir)) continue;
                string cmdPath = Path.Combine(dir,
                    "decanter-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".cmd");
                try
                {
                    File.WriteAllText(cmdPath, body, Encoding.Default);
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "cmd.exe";
                    psi.Arguments = "/c \"\"" + cmdPath + "\"\"";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(psi);
                    return cmdPath;
                }
                catch (Exception ex)
                {
                    lastError = ex.ToString();
                    // 受限环境可能不许写 %TEMP%（例如被安全策略锁住）：换下一个落点
                    try { if (File.Exists(cmdPath)) File.Delete(cmdPath); }
                    catch { }
                }
            }
            try
            {
                File.WriteAllText(Path.Combine(installDir, "uninstall-cleanup-error.txt"),
                    lastError == null ? "no writable script dir" : lastError, new UTF8Encoding(false));
            }
            catch { }

            // 最后兜底：不落脚本文件，直接用 cmd /c 里带等待的删除命令
            try
            {
                string inline = "ping 127.0.0.1 -n 3 >nul 2>&1 & del /f /q \"" + target + "\" >nul 2>&1";
                if (removeInstallDir)
                    inline += " & rmdir /s /q \"" + installDir + "\" >nul 2>&1";
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"" + inline + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
                return "inline";
            }
            catch { return null; }
        }

        private static string BuildScript(string target, string installDir, bool removeInstallDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal enableextensions");
            sb.AppendLine("set /a tries=0");
            sb.AppendLine(":retry");
            sb.AppendLine("set /a tries+=1");
            // timeout 需要控制台输入句柄；没有时立刻返回错误，所以补一个 ping 兜底等待
            sb.AppendLine("timeout /t 2 /nobreak >nul 2>&1");
            sb.AppendLine("ping -n 2 127.0.0.1 >nul 2>&1");
            sb.AppendLine("del /f /q \"" + target + "\" >nul 2>&1");
            sb.AppendLine("if not exist \"" + target + "\" goto gone");
            sb.AppendLine("if %tries% lss 6 goto retry");
            sb.AppendLine(":gone");
            if (removeInstallDir)
            {
                sb.AppendLine("rmdir /s /q \"" + installDir + "\" >nul 2>&1");
            }
            sb.AppendLine("(goto) 2>nul & del /f /q \"%~f0\" >nul 2>&1");
            return sb.ToString();
        }
    }

    // ---------------- 截图（只画自己的窗口） ----------------
    internal static class Shot
    {
        public static void Save(Form f, string outPath)
        {
            string full = Path.GetFullPath(outPath);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(full, ImageFormat.Png);
            }
        }
    }

    // ---------------- 文件小工具 ----------------
    internal static class FileUtil
    {
        public static int DirSizeKb(string[] files)
        {
            long sum = 0;
            foreach (string f in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Exists) sum += fi.Length;
                }
                catch { }
            }
            int kb = (int)(sum / 1024);
            return kb < 1 ? 1 : kb;
        }

        public static void CopyDirectory(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (string d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = d.Substring(src.TrimEnd('\\').Length).TrimStart('\\');
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.TrimEnd('\\').Length).TrimStart('\\');
                string dest = Path.Combine(dst, rel);
                string parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                CopyWithRetry(f, dest);
            }
        }

        public static void CopyWithRetry(string src, string dst)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using (FileStream from = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (FileStream to = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[81920];
                        int n;
                        while ((n = from.Read(buf, 0, buf.Length)) > 0) to.Write(buf, 0, n);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!(ex is IOException)) throw;
                    System.Threading.Thread.Sleep(120);
                }
            }
            if (last != null) throw last;
        }

        public static void DeleteFileIfExists(string path)
        {
            if (!File.Exists(path)) return;
            Exception last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!(ex is IOException)) throw;
                    System.Threading.Thread.Sleep(120);
                }
            }
            if (last != null) throw last;
        }

        public static void DeleteDirIfExists(string dir)
        {
            if (!Directory.Exists(dir)) return;
            Exception last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Directory.Delete(dir, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!(ex is IOException)) throw;
                    System.Threading.Thread.Sleep(120);
                }
            }
            if (last != null) throw last;
        }
    }
}
