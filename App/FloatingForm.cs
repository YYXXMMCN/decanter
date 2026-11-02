// FloatingForm.cs — v6 悬浮小牌（三态之一：收起 / 展开；主界面是第三态）
// 收起态：抬头「下一条」＋「N 条」计数，只显示 1 条；三行＝标题 / 元信息或说明 / 重要度或说明，点一下展开
// 展开态：竖长条横线本，按 12 行单位定高（2 高＋2 中＋2 低＝恰好 6 条），每条按重要度占 1/2/3 行，
//         行数=重要度、色段编码；装不下的条数在底部写「还有 N 条」
// 行语法与主界面账本一致：右列只放截止（过期=已过期，危险色）；循环/提醒做成元信息里的文字＋12px 小图标；
//         截止/提醒文案统一走 TimeText（本文件不再自造日期格式）
// 交互：点圆圈＝完成/恢复该条；单击空白＝展开/收起；双击＝进主界面；右键＝菜单；Esc＝退一层（收起态按 Esc 不消失）
// 材质：1px 金属外框＋内侧 1px 纸线＋左上一枚黄铜角标；全直角。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
/// <summary>task-99: pure lane allocation for the floating window (no drawing, unit-testable).
    /// Row units: high=3, mid=2, low=1. Pinned lane above, default lane below.</summary>
    internal static class LaneFlow
    {
        public static int NaturalRows(TodoItem t)
        {
            if (t == null) return 1;
            int imp = t.Importance;           // 1 low / 2 mid / 3 high
            if (imp >= 3) return 3;
            if (imp == 2) return 2;
            return 1;
        }

        public static int[] Compact(IList<TodoItem> pinned, IList<TodoItem> defaults, int budget)
        {
            int pc = pinned == null ? 0 : pinned.Count;
            int dc = defaults == null ? 0 : defaults.Count;
            int[] alloc = new int[pc + dc];
            int left = Math.Max(0, budget);
            int lastPinned = -1;
            for (int i = 0; i < pc && left > 0; i++)
            {
                int take = Math.Min(NaturalRows(pinned[i]), left);
                alloc[i] = take;
                left -= take;
                if (take > 0) lastPinned = i;
            }
            if (dc > 0)
            {
                for (int i = 0; i < dc && left > 0; i++) { alloc[pc + i] = 1; left--; }
            }
            else if (left > 0 && lastPinned >= 0)
            {
                alloc[lastPinned] += left;
            }
            return alloc;
        }

        public static int[] Expanded(IList<TodoItem> pinned, IList<TodoItem> defaults, int budget)
        {
            int pc = pinned == null ? 0 : pinned.Count;
            int dc = defaults == null ? 0 : defaults.Count;
            int[] alloc = new int[pc + dc];
            int left = Math.Max(0, budget);
            for (int i = 0; i < pc; i++) { int take = Math.Min(NaturalRows(pinned[i]), left); alloc[i] = take; left -= take; }
            for (int i = 0; i < dc; i++) { int take = Math.Min(NaturalRows(defaults[i]), left); alloc[pc + i] = take; left -= take; }
            return alloc;
        }

        public static string Describe(IList<TodoItem> pinned, IList<TodoItem> defaults, bool expanded, int compactBudget, int expandedBudget)
        {
            int[] a = expanded ? Expanded(pinned, defaults, expandedBudget) : Compact(pinned, defaults, compactBudget);
            int pc = pinned == null ? 0 : pinned.Count;
            StringBuilder sb = new StringBuilder("p:");
            for (int i = 0; i < pc; i++) { if (i > 0) sb.Append(','); sb.Append(a[i]); }
            sb.Append("|d:");
            int dc = defaults == null ? 0 : defaults.Count;
            for (int i = 0; i < dc; i++) { if (i > 0) sb.Append(','); sb.Append(a[pc + i]); }
            return sb.ToString();
        }
    }

    public class FloatingForm : Form
    {
        public const int CompactW = 320;
        public const int ExpandedW = 320;
        public const int HeaderH = 32;
        public const int SlotH = 36;                       // task-117: one line unit = 36 logical px (was 32)
        public const int CompactRows = 3;                  // 收起态 = 标准三行高（一条待办占满三行）
        public const int CompactContentH = SlotH * CompactRows;   // 96
        public const int BottomPad = 10;                   // 收起态底距
        public const int ExpandedBottomPad = 22;           // 展开态底距（放「还有 N 条」）
        public const int CompactH = HeaderH + CompactContentH + BottomPad;              // 138
        public const int ExpandedContentH = SlotH * 12;    // 12 行单位 = 384 ≈ 390
        public const int ExpandedH = HeaderH + ExpandedContentH + ExpandedBottomPad;    // 438
        public const int RailW = 3;
        public const int TextLeft = 34;
        public const int PadR = 12;
        public const int MaxCompactItems = 1;
        public const int MaxExpandedItems = 6;
        public const int AnimMs = 150;

        private readonly Store _store;
        private Theme _theme;
        private bool _expanded;
        private readonly Timer _timer;
        private Point _press;
        private Point _offset;
        private bool _dragging;
        // task-140: fixed rect, valid before the first paint (no dependency on OnPaint)
        private Rectangle HeaderBtnRect { get { return new Rectangle(S(6), 0, S(112), S(HeaderH)); } }
        private bool _hovering;
        private int _hoverRow = -1;
        private bool _hoverCircle;
        private float _dpi = 1f;
        private List<TodoItem> _items = new List<TodoItem>();
        private DateTime _lastClickAt = DateTime.MinValue;
        private Point _lastClickPos = Point.Empty;
        private DateTime _lastOpenMain = DateTime.MinValue;
        private int _anchorTop;
        public event EventHandler OpenMain;
        public event EventHandler ExitApp;

        private readonly List<RowHit> _rows = new List<RowHit>();

        private sealed class RowHit
        {
            public TodoItem Item;
            public Rectangle Rect;
            public Rectangle Circle;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        public FloatingForm(Store store)
        {
            _store = store;
            _theme = LoadTheme();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = _theme.Paper;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Icon = Program.AppIcon();
            _timer = new Timer();
            _timer.Interval = 2000;
            _timer.Tick += delegate { RefreshData(); };
            _timer.Start();
            // task-104: hide/close/drag -> jump straight to the current target (no animation)
            VisibleChanged += delegate { if (!Visible && _animItems != null) JumpToTarget(); };
            FormClosing += delegate { if (_animItems != null) JumpToTarget(); };
            RefreshData();
        }

        // ---- 行模型（两态共用；保留 P0 公开算法与常量名，便于自检） ----
        public static int CompactLineBudget { get { return CompactContentH / SlotH; } }     // 3（窗口高）
        public static int ExpandedLineBudget { get { return ExpandedContentH / SlotH; } }   // 12

        public static int LinesOf(int importance) { return importance >= 3 ? 3 : (importance <= 1 ? 1 : 2); }

        public static int FontPxOfLines(int lines) { return lines >= 3 ? 20 : (lines <= 1 ? 15 : 17); }

        public static int TitleFontOf(int importance) { return FontPxOfLines(LinesOf(importance)); }

        public static int RowPadV { get { return 2; } }
        public static int RowPadTotal { get { return 2 * RowPadV; } }

        // 标题只出文字：置顶改用 Icons 图钉（v6 起不再在标题里插 ▲ 字符）
        public static string TitleOf(TodoItem t)
        {
            if (t == null) return "";
            return t.Title == null ? "" : t.Title;
        }

        public static int CompactHeightOf(int importance) { return CompactH; }
        public static int CompactHeightForLines(int lines) { return CompactH; }

        // 贪心填满行预算：按优先级从上往下取，想要 3/2/1 行；余量不足时降级（最低 1 行）；上限 maxItems
        public static int[] AllocateByBudget(List<TodoItem> items, int maxItems, int lineBudget)
        {
            int n = items == null ? 0 : items.Count;
            if (n == 0 || lineBudget <= 0 || maxItems <= 0) return new int[0];
            List<int> lines = new List<int>();
            int remaining = lineBudget;
            for (int i = 0; i < n && i < maxItems; i++)
            {
                int want = LinesOf(items[i].Importance);
                int take = want < remaining ? want : remaining;
                if (take < 1) break;
                lines.Add(take);
                remaining -= take;
                if (remaining <= 0) break;
            }
            return lines.ToArray();
        }

        private static Rectangle NonPrimaryWorkArea()
        {
            Screen target = Screen.PrimaryScreen;
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) if (!all[i].Primary) { target = all[i]; break; }
            return target.WorkingArea;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW（系统给出的一层，不加自绘阴影）
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private Theme LoadTheme()
        {
            return Theme.FromSetting(_store.GetSetting("theme", "system"));
        }

        private float CurrentDpi()
        {
            try
            {
                if (IsHandleCreated)
                {
                    uint w = GetDpiForWindow(Handle);
                    if (w > 0) return w / 96f;
                }
                uint s = GetDpiForSystem();
                if (s > 0) return s / 96f;
            }
            catch { }
            return 1f;
        }

        private int S(float logical) { return (int)Math.Round(logical * _dpi); }

        private void RestorePosition()
        {
            Rectangle wa = Placement.NonPrimaryWorkArea();
            string saved = _store.GetSetting("pos", "");
            int x = wa.Right - S(CompactW) - S(24);
            int y = wa.Bottom - S(CompactH) - S(48);
            if (!string.IsNullOrEmpty(saved))
            {
                string[] parts = saved.Split(',');
                int sx, sy;
                if (parts.Length == 2 && int.TryParse(parts[0], out sx) && int.TryParse(parts[1], out sy)
                    && Placement.IsPointOnNonPrimary(sx + S(CompactW) / 2, sy + S(CompactH) / 2))
                { x = sx; y = sy; }
            }
            Location = new Point(x, y);
        }

        private void SavePosition()
        {
            try { _store.SetSetting("pos", Location.X + "," + Location.Y); } catch { }
        }

        /// <summary>task-113: single source of truth for the expanded height (adaptive, capped).</summary>
        private int ExpandedTargetHeight()
        {
            BuildRowLayout();
            int used = 0;
            for (int i = 0; i < _rows.Count; i++) used += _rows[i].Rect.Height;
            int want = S(HeaderH) + used + S(ExpandedBottomPad);   // task-138: no cap -- all rows visible
            if (want < S(CompactH)) want = S(CompactH);
            return want;
        }

        /// <summary>task-122: compact height = Header + actual visible row heights + 1px frame (window bottom == last row bottom).</summary>
        private int CompactTargetHeight()
        {
            BuildRowLayout();
            int used = 0;
            for (int i = 0; i < _rows.Count; i++) used += _rows[i].Rect.Height;
            if (used <= 0) used = S(SlotH);
            return S(HeaderH) + used + 1;   // task-124: strict bottom == last row bottom
        }

        private void ApplyCompactSize()
        {
            _expanded = false;
            int lockLeft = Left, lockTop = Top;
            Width = S(CompactW);
            Height = CompactTargetHeight();   // task-122: single source of truth
            Location = new Point(lockLeft, lockTop);
            BuildRowLayout();
            Invalidate();
        }

        private void ApplyExpandedSize()
        {
            _expanded = true;
            int lockLeft = Left, lockTop = Top;
            Width = S(CompactW);
            Height = ExpandedTargetHeight();   // task-113: adaptive target, never fixed ExpandedH
            Location = new Point(lockLeft, lockTop);
            BuildRowLayout();
            Invalidate();
        }

        // ---- 动画：真实时间 + ease-in-out；出现比消失略慢 ----
        private Timer _anim;
        private System.Diagnostics.Stopwatch _animSw;
        private int _fromX, _fromY, _fromW, _fromH, _toX, _toY, _toW, _toH;
        private bool _animExpand;

        private void AnimateTo(bool expand)
        {
            _animExpand = expand;
            if (expand)
            {
                if (!_expanded) _anchorTop = Top;
                _expanded = true;
            }
            _fromX = Left; _fromY = Top; _fromW = Width; _fromH = Height;

            int tw = S(expand ? ExpandedW : CompactW);
            int th = S(expand ? ExpandedH : CompactH);
            int ty = Top;
            if (expand)
            {
                try
                {
                    Rectangle wa = Screen.FromPoint(new Point(Left + tw / 2, Top + th / 2)).WorkingArea;
                    if (Top + th > wa.Bottom)
                    {
                        ty = Top + Height - th;
                        if (ty < wa.Top) ty = wa.Top;
                    }
                }
                catch { }
            }
            else
            {
                ty = _anchorTop;
                try
                {
                    Rectangle wa = Screen.FromPoint(new Point(Left + tw / 2, ty + th / 2)).WorkingArea;
                    if (ty + th > wa.Bottom) ty = wa.Bottom - th;
                    if (ty < wa.Top) ty = wa.Top;
                }
                catch { }
            }
            _toX = Left; _toY = ty; _toW = tw; _toH = th;

            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 10;
                _anim.Tick += delegate { AnimTick(); };
            }
            _anim2Sw = System.Diagnostics.Stopwatch.StartNew();
            AnimLog((expand ? "start expand" : "start collapse") + " at=" + DateTime.Now.ToString("HH:mm:ss.fff") + " tickMs=0");
            _anim.Start();
        }

        private void AnimLog(string msg)
        {
            try
            {
                string p = Environment.GetEnvironmentVariable("DECANTER_ANIM_LOG");
                if (string.IsNullOrEmpty(p)) return;
                System.IO.File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
            }
            catch { }
        }

        private void AnimTick()
        {
            try
            {
                if (_items == null || _items.Count == 0) { try { if (_anim != null) _anim.Stop(); } catch { } return; }
            double t = _anim2Sw.Elapsed.TotalMilliseconds / Anim2Ms;
            if (t > 1) t = 1;
            if (t < 0) t = 0;
            double e = t * t * (3 - 2 * t);
            int x = (int)Math.Round(_fromX + (_toX - _fromX) * e);
            int y = (int)Math.Round(_fromY + (_toY - _fromY) * e);
            int w = (int)Math.Round(_fromW + (_toW - _fromW) * e);
            int h = (int)Math.Round(_fromH + (_toH - _fromH) * e);
            if (t >= 1)
            {
                AnimLog("end at=" + DateTime.Now.ToString("HH:mm:ss.fff") + " swElapsed=" + ((int)_animSw.ElapsedMilliseconds) + "ms t=" + t.ToString("0.###"));
                x = _toX; y = _toY; w = _toW; h = _toH;
            }
            SetBounds(x, y, w, h);
            Invalidate();
            if (t >= 1)
            {
                _anim.Stop();
                if (!_animExpand) _expanded = false;
                BuildRowLayout();
                Invalidate();
            }
            }
            catch (Exception ex) { try { if (_anim != null) _anim.Stop(); if (_animTimer != null) _animTimer.Stop(); Anim2Log("legacy-animtick-exception " + ex.GetType().Name + ": " + ex.Message); } catch { } }
        }

        private bool _testItemsOverride;   // task-102: debug/test fixtures must not be overwritten by the 2s refresh timer

        public void RefreshData()
        {
            if (_testItemsOverride) { BuildRowLayout(); Invalidate(); return; }
            _items = _store.Active();
            if (_expanded) ApplyExpandedSize(); else ApplyCompactSize();
            Invalidate();
        }

        public void ReloadTheme()
        {
            _theme = LoadTheme();
            BackColor = _theme.Paper;
            Invalidate();
        }

        // task-105: scaled geometry for diag assertions
        public int ScaledExpandedH { get { return S(ExpandedH); } }
        public int ScaledCompactH { get { return S(CompactH); } }
        public int ScaledContentLimit { get { return S(HeaderH) + S(ExpandedContentH); } }

        public Rectangle GeometryForTest() { return new Rectangle(Left, Top, Width, Height); }

        /// <summary>task-99: lane allocation for tests/debug ("p:3,2|d:1,0").</summary>
        public string LaneGeometryForTest()
        {
            List<TodoItem> lp = new List<TodoItem>(); List<TodoItem> ld = new List<TodoItem>();
            if (_items != null) for (int i = 0; i < _items.Count; i++) { if (_items[i] != null && _items[i].Pinned) lp.Add(_items[i]); else ld.Add(_items[i]); }
            return LaneFlow.Describe(lp, ld, _expanded, CompactLineBudget, ExpandedLineBudget);
        }
        public bool TestExpanded { get { return _expanded; } }

        // ---- 行布局（绘制与命中共用一套坐标，避免"看得见点不到"） ----
        private void BuildRowLayout()
        {
            _rows.Clear();
            if (_items == null || _items.Count == 0) return;
            DateTime now = DateTime.Now;
            bool growthAll = _store.GetSetting("growth_all", "0") == "1";
            bool growthOn = _store.GetSetting("growth_on", "1") == "1";

            if (!_expanded)
            {
                // task-99: compact lanes -- pinned natural 3/2/1 first, then default one line each
                List<TodoItem> pin = new List<TodoItem>();
                List<TodoItem> def = new List<TodoItem>();
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i] != null && _items[i].Pinned) pin.Add(_items[i]); else def.Add(_items[i]);
                }
                int[] alloc = LaneFlow.Compact(pin, def, CompactLineBudget);
                int y0 = S(HeaderH);
                int idx = 0;
                for (int i = 0; i < pin.Count; i++, idx++)
                {
                    int hh = alloc[idx] * S(SlotH);
                    if (hh <= 0) continue;
                    Rectangle r0 = new Rectangle(0, y0, ClientSize.Width, hh);
                    RowHit h0 = new RowHit();
                    h0.Item = pin[i]; h0.Rect = r0; h0.Circle = new Rectangle(S(10), y0 + S(4), S(24), S(24));
                    _rows.Add(h0);
                    y0 += hh;
                }
                for (int i = 0; i < def.Count; i++, idx++)
                {
                    int hh = alloc[idx] * S(SlotH);
                    if (hh <= 0) continue;
                    Rectangle r0 = new Rectangle(0, y0, ClientSize.Width, hh);
                    RowHit h0 = new RowHit();
                    h0.Item = def[i]; h0.Rect = r0; h0.Circle = new Rectangle(S(10), y0 + S(4), S(24), S(24));
                    _rows.Add(h0);
                    y0 += hh;
                }
                return;
            }

            // task-103: expanded lanes, strictly clipped to the expanded content budget
            List<TodoItem> pinE = new List<TodoItem>();
            List<TodoItem> defE = new List<TodoItem>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null && _items[i].Pinned) pinE.Add(_items[i]); else defE.Add(_items[i]);
            }
            List<TodoItem> ordered = new List<TodoItem>(pinE);
            ordered.AddRange(defE);
            int[] expLines = LaneFlow.Expanded(pinE, defE, int.MaxValue);   // task-138: unlimited expanded rows
            int yy = S(HeaderH);
            int contentLimit = S(ExpandedContentH);
            for (int i = 0; i < expLines.Length && i < ordered.Count; i++)
            {
                int hh = expLines[i] * S(SlotH);
                if (hh <= 0) continue;
                Rectangle r = new Rectangle(0, yy, ClientSize.Width, hh);
                RowHit h = new RowHit();
                h.Item = ordered[i]; h.Rect = r; h.Circle = new Rectangle(S(10), yy + S(4), S(24), S(24));
                _rows.Add(h);
                yy += hh;
            }
        }

        private int HitRow(Point p)
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Rect.Contains(p)) return i;
            return -1;
        }

        private bool HitCircle(Point p)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Circle.Contains(p)) return true;
            return false;
        }

        // ---- 绘制 ----
        // task-111: cached fonts (no per-frame/per-row allocation) + continuous title px from row height
        private readonly Dictionary<int, Font> _fontCacheUI = new Dictionary<int, Font>();
        private readonly Dictionary<int, Font> _fontCacheMono = new Dictionary<int, Font>();

        private Font FontUIFor(int px)
        {
            px = Math.Max(8, Math.Min(48, px));
            Font f;
            if (!_fontCacheUI.TryGetValue(px, out f))
            {
                f = new Font(Theme.FontUI, px, FontStyle.Regular, GraphicsUnit.Pixel);
                _fontCacheUI[px] = f;
            }
            return f;
        }

        private Font FontMonoFor(int px)
        {
            px = Math.Max(8, Math.Min(48, px));
            Font f;
            if (!_fontCacheMono.TryGetValue(px, out f))
            {
                f = new Font(Theme.FontMono, px, FontStyle.Regular, GraphicsUnit.Pixel);
                _fontCacheMono[px] = f;
            }
            return f;
        }

        /// <summary>Task-111: 1 row = 15px, 2 rows = 17px, 3 rows = 20px, interpolated by the CURRENT row height (so a row grows its font continuously).</summary>
        private int FontPxForRowHeight(int rowH)
        {
            int baseH = Math.Max(1, S(SlotH));
            double slotsExact = rowH / (double)baseH;
            if (slotsExact < 1.0) slotsExact = 1.0;
            if (slotsExact > 3.0) slotsExact = 3.0;
            int px = (int)Math.Round(15.0 + (slotsExact - 1.0) * 2.5);   // 15 / 17.5 / 20
            return Math.Max(15, Math.Min(20, px));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            Theme th = _theme ?? Theme.Night();

            using (SolidBrush b = new SolidBrush(th.Card)) g.FillRectangle(b, 0, 0, W, H);
            // 金属外框 + 内侧纸线（双层框=金属包住纸；全直角）
            using (Pen mp = new Pen(th.Metal, Math.Max(1, S(1)))) g.DrawRectangle(mp, 0, 0, W - 1, H - 1);
            using (Pen lp = new Pen(th.Paper, Math.Max(1, S(FrameTokens.Inner)))) g.DrawRectangle(lp, S(2), S(2), W - S(5), H - S(5));
            // batch2b corners: 2px brass L-brackets in all four corners (FrameTokens)
            int arm = S(8), cw = Math.Max(1, S(FrameTokens.Corner));
            using (Pen cp = new Pen(th.Accent, cw))
            {
                g.DrawLine(cp, S(1), S(1), S(1) + arm, S(1)); g.DrawLine(cp, S(1), S(1), S(1), S(1) + arm);
                g.DrawLine(cp, W - S(1), S(1), W - S(1) - arm, S(1)); g.DrawLine(cp, W - S(1), S(1), W - S(1), S(1) + arm);
                g.DrawLine(cp, S(1), H - S(1), S(1) + arm, H - S(1)); g.DrawLine(cp, S(1), H - S(1), S(1), H - S(1) - arm);
                g.DrawLine(cp, W - S(1), H - S(1), W - S(1) - arm, H - S(1)); g.DrawLine(cp, W - S(1), H - S(1), W - S(1), H - S(1) - arm);
            }
            // 左上黄铜角标（一枚 L 形，10×10）
            using (Pen ap = new Pen(th.Accent, Math.Max(2, S(2))))
            {
                g.DrawLine(ap, S(2), S(2), S(12), S(2));
                g.DrawLine(ap, S(2), S(2), S(2), S(12));
            }

            bool growthAll = _store.GetSetting("growth_all", "0") == "1";
            bool growthOn = _store.GetSetting("growth_on", "1") == "1";

            // 抬头
            string header = Strings.L("float.title");   // batch2 #10: one stable title in both states
            using (Font hf = new Font(Theme.FontUI, S(12), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font mf = new Font(Theme.FontMono, S(11), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // task-139: clickable header button = decanter logo + "醒酒器"
                // task-141: silent button -- no hover/active styling at all (same pixels as normal)
                try { Icons.Draw(g, "decanter", new Rectangle(S(10), S(HeaderH / 2 - S(8)), S(16), S(16)), th.Metal, _dpi); }
                catch { }
                TextRenderer.DrawText(g, "醒酒器", hf, new Rectangle(S(30), 0, S(86), S(HeaderH)), th.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                // 计数用「N 条」：与底部「还有 N 条」同一套说法（英文只说数字）
                TextRenderer.DrawText(g, CountText(), mf, new Rectangle(W - S(112), 0, S(84), S(HeaderH)), th.Aux,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                // 拖动握把（三点竖排，只在悬停时出现）
                if (_hovering)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        using (SolidBrush sb = new SolidBrush(th.Metal))
                            g.FillRectangle(sb, W - S(18), S(HeaderH / 2 - 5 + k * 4), S(2), S(2));
                    }
                }
            }
            using (Pen hp = new Pen(th.Line, Math.Max(1, S(1)))) g.DrawLine(hp, 0, S(HeaderH), W, S(HeaderH));

            if (_items == null || _items.Count == 0)
            {
                using (Font ef = new Font(Theme.FontUI, S(15), FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, Strings.L("float.empty"), ef, new Rectangle(S(14), S(HeaderH), W - S(28), S(CompactContentH)), th.Aux,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                return;
            }

            DateTime now2 = DateTime.Now;
            for (int i = 0; i < _rows.Count; i++)
            {
                RowHit row = _rows[i];
                bool hover = (_hovering && i == _hoverRow);
                if (hover)
                {
                    using (SolidBrush hb = new SolidBrush(th.CardHi)) g.FillRectangle(hb, row.Rect);
                }
                bool done = row.Item.Done;
                ScoreResult sr = Scoring.Evaluate(row.Item, now2, Scoring.GrowthEnabled(row.Item, growthAll, growthOn));
                Color rail = Theme.ScoreColor(sr.Score, th.IsDark);
                if (done) rail = Theme.Mix(th.Card, rail, 0.5);
                using (SolidBrush rb = new SolidBrush(rail))
                    g.FillRectangle(rb, 0, row.Rect.Top + S(5), S(RailW), Math.Max(S(4), row.Rect.Height - S(10)));

                // 完成圈（点它=完成/恢复；颜色不全靠色，选中态另有形状）
                Color cc = (_hoverCircle && i == _hoverRow) ? th.Accent : Theme.Mix(th.Card, th.SubText, 0.65);
                if (done)
                {
                    using (SolidBrush b = new SolidBrush(th.Accent)) g.FillEllipse(b, row.Circle);
                    using (Pen p = new Pen(th.OnAccent, Math.Max(2, S(2))))
                    {
                        g.DrawLine(p, row.Circle.Left + S(5), row.Circle.Top + S(11), row.Circle.Left + S(9), row.Circle.Top + S(15));
                        g.DrawLine(p, row.Circle.Left + S(9), row.Circle.Top + S(15), row.Circle.Left + S(17), row.Circle.Top + S(6));
                    }
                }
                else
                {
                    using (Pen p = new Pen(cc, Math.Max(1, S(1.5f)))) g.DrawEllipse(p, row.Circle);
                }

                // task-100: slots always follow the allocated row height (1 row = title only, 2 = +meta, 3 = full)
                int slots = Math.Max(1, row.Rect.Height / Math.Max(1, S(SlotH)));
                int titlePx = 20;   // task-114: ALL rows use the 3-line title size
                Font tf = FontUIFor(S(titlePx));   // task-115: cache key is PHYSICAL px
                Font metaF = FontUIFor(S(12));
                Font monoF = FontMonoFor(S(11));
                {
                    int titleY = row.Rect.Top;
                    int titleH = S(SlotH);

                    // 右列：只放截止；过期=「已过期」＋危险色（与主界面账本同一套说法）
                    int rightW = 0;
                    string right = DueRight(row.Item, now2);
                    if (right.Length > 0)
                    {
                        Size rsz = TextRenderer.MeasureText(g, right, monoF, Size.Empty, TextFormatFlags.NoPadding);
                        rightW = rsz.Width;
                    }
                    int textRight = W - S(PadR) - (rightW > 0 ? rightW + S(10) : 0);

                    // 标题行：置顶=12px 图钉（文字语义在第三行「· 置顶」）；循环/提醒改用元信息文字，标题前不再放符号
                    int rowX = S(TextLeft) + S(24);
                    int tx = rowX;
                    if (row.Item.Pinned)
                    {
                        Icons.Draw(g, "pin", new Rectangle(tx, titleY + (titleH - S(12)) / 2, S(12), S(12)), th.Accent, _dpi);
                        tx += S(16);
                    }
                    int tw = Math.Max(S(20), textRight - tx);
                    TextRenderer.DrawText(g, TitleOf(row.Item), tf, new Rectangle(tx, titleY, tw, titleH),
                        done ? th.Aux : th.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    if (right.Length > 0)
                        TextRenderer.DrawText(g, right, monoF, new Rectangle(W - S(PadR) - rightW, titleY, rightW, titleH),
                            sr.Overdue ? th.Dark : th.SubText,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    // 第 2 行：元信息（循环/提醒：文字＋小图标）＋说明；不再重复写「截止」
                    if (slots >= 2)
                        DrawMetaLine(g, row.Item, now2, rowX, titleY + S(SlotH), S(SlotH), textRight - rowX, th, metaF, done);

                    // 第 3 行：重要度/置顶（不重复前两行已出现的信息）
                    if (slots >= 3)
                    {
                        string line3 = ImportanceLine(row.Item);
                        if (line3.Length > 0)
                            TextRenderer.DrawText(g, line3, metaF, new Rectangle(rowX, titleY + 2 * S(SlotH), textRight - rowX, S(SlotH)),
                                th.Aux, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    }
                }

                // 横线（横线本的横线）
                using (Pen rp = new Pen(th.Line, Math.Max(1, S(1))))
                    g.DrawLine(rp, S(TextLeft), row.Rect.Bottom - 1, W - S(PadR), row.Rect.Bottom - 1);
            }

            // 展开态底部：还有 N 条（若装不下）
            if (_expanded)
            {
                int shown = _rows.Count;
                int left = _items.Count - shown;
                string foot = left > 0 ? string.Format(Strings.L("float.more"), left) : "";
                if (foot.Length > 0)
                {
                    using (Font ff = new Font(Theme.FontMono, S(11), FontStyle.Regular, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, foot, ff, new Rectangle(S(TextLeft), H - S(ExpandedBottomPad), W - S(TextLeft) - S(PadR), S(16)), th.Aux,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        // ---- 行内文案（与主界面账本同一套语法；日期一律走 TimeText，不在本文件自造格式） ----

        private sealed class MetaPart
        {
            public string Icon;   // Icons 名称：repeat / bell；说明没有图标
            public string Text;
        }

        private static List<MetaPart> MetaParts(TodoItem t, DateTime now)
        {
            List<MetaPart> parts = new List<MetaPart>();
            if (t.IsRecurring)
            {
                MetaPart p = new MetaPart();
                p.Icon = "repeat";
                p.Text = Strings.L("field.repeat") + " " + RepeatName(t.Recurrence);
                parts.Add(p);
            }
            if (t.RemindUtc.HasValue)
            {
                MetaPart p = new MetaPart();
                p.Icon = "bell";
                p.Text = Strings.L("field.remind") + " " + TimeText.Remind(TimeUtil.FromUtc(t.RemindUtc.Value), now);
                parts.Add(p);
            }
            if (!string.IsNullOrEmpty(t.Note))
            {
                MetaPart p = new MetaPart();
                p.Icon = "";
                p.Text = t.Note;
                parts.Add(p);
            }
            return parts;
        }

        // 元信息纯文本（自检/复用；绘制走 DrawMetaLine，带小图标、且不写「截止」）
        private static string MetaLine(TodoItem t, DateTime now)
        {
            List<MetaPart> parts = MetaParts(t, now);
            List<string> texts = new List<string>();
            for (int i = 0; i < parts.Count; i++) texts.Add(parts[i].Text);
            return string.Join(" · ", texts.ToArray());
        }

        private static string RepeatName(string rule)
        {
            if (rule == "daily") return Strings.L("rep.daily");
            if (rule == "weekly") return Strings.L("rep.weekly");
            if (rule == "monthly") return Strings.L("rep.monthly");
            if (!string.IsNullOrEmpty(rule) && rule.StartsWith("every:")) return rule.Substring(6) + " 天";
            return rule == null ? "" : rule;
        }

        private static string ImportanceLine(TodoItem t)
        {
            string lv = t.Importance >= 3 ? Strings.L("imp.high") : (t.Importance <= 1 ? Strings.L("imp.low") : Strings.L("imp.mid"));
            string s = Strings.L("field.importance_full") + " " + lv;
            if (t.Pinned) s += " · " + Strings.L("field.pinned");
            return s;
        }

        // 右列：只放截止；过期写「已过期」，颜色由调用方按 sr.Overdue 给危险色
        private static string DueRight(TodoItem t, DateTime now)
        {
            if (!t.DueUtc.HasValue) return "";
            DateTime due = t.DueLocal.Value;
            if (due < now) return Strings.L("overdue.tag");
            return TimeText.Due(due, now);
        }

        private static int MeasureW(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            Size sz = TextRenderer.MeasureText(g, s, f, Size.Empty, TextFormatFlags.NoPadding);
            return sz.Width;
        }

        // 第 2 行：循环/提醒各配一枚 12px 小图标，说明接在后面；放不下时省略号收尾
        private void DrawMetaLine(Graphics g, TodoItem t, DateTime now, int x, int y, int lineH, int width, Theme th, Font metaF, bool done)
        {
            List<MetaPart> parts = MetaParts(t, now);
            if (parts.Count == 0 || width <= S(12)) return;
            Color mc = done ? th.Aux : th.SubText;
            string sep = " · ";
            int sepW = MeasureW(g, sep, metaF);
            int iconSize = S(12);
            int iconGap = S(4);
            int cx = x;
            bool first = true;
            for (int i = 0; i < parts.Count; i++)
            {
                MetaPart p = parts[i];
                int lead = first ? 0 : sepW;
                bool hasIcon = !string.IsNullOrEmpty(p.Icon);
                int iconW = hasIcon ? iconSize + iconGap : 0;
                int textW = MeasureW(g, p.Text, metaF);
                int avail = x + width - cx;
                if (lead + iconW + textW <= avail)
                {
                    if (!first)
                    {
                        TextRenderer.DrawText(g, sep, metaF, new Rectangle(cx, y, sepW + S(2), lineH), mc,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        cx += sepW;
                    }
                    if (hasIcon)
                    {
                        Icons.Draw(g, p.Icon, new Rectangle(cx, y + (lineH - iconSize) / 2, iconSize, iconSize), mc, _dpi);
                        cx += iconW;
                    }
                    TextRenderer.DrawText(g, p.Text, metaF, new Rectangle(cx, y, textW + S(2), lineH), mc,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    cx += textW;
                    first = false;
                }
                else
                {
                    // 行宽不够：本段压到剩余宽度里，文字用省略号收尾，不再挤下一段
                    if (!first && avail > sepW + S(10))
                    {
                        TextRenderer.DrawText(g, sep, metaF, new Rectangle(cx, y, sepW + S(2), lineH), mc,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        cx += sepW;
                    }
                    if (hasIcon && x + width - cx >= iconSize + iconGap)
                    {
                        Icons.Draw(g, p.Icon, new Rectangle(cx, y + (lineH - iconSize) / 2, iconSize, iconSize), mc, _dpi);
                        cx += iconW;
                    }
                    int rem = x + width - cx;
                    if (rem > S(10))
                        TextRenderer.DrawText(g, p.Text, metaF, new Rectangle(cx, y, rem, lineH), mc,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    return;
                }
            }
        }

        // 抬头计数：「N 条」（英文只说数字；无独立字符串键时按 float.more 的口语直接拼）
        private string CountText()
        {
            int n = _items == null ? 0 : _items.Count;
            if (Strings.Lang == "en") return n.ToString();
            return n.ToString() + " 条";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dpi = CurrentDpi();
            RestorePosition();
            if (_expanded) ApplyExpandedSize(); else ApplyCompactSize();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _dpi = CurrentDpi();
            if (_expanded) ApplyExpandedSize(); else ApplyCompactSize();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _dpi = CurrentDpi();
            if (_expanded) ApplyExpandedSize(); else ApplyCompactSize();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _expanded)
            {
                AnimateToLanes(false);   // task-109: always the two-lane animator (Esc)
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseEnter(EventArgs e) { _hovering = true; try { Point p = PointToClient(Cursor.Position); Cursor = HeaderBtnRect.Contains(p) ? Cursors.Hand : Cursors.Default; } catch { } Invalidate(); base.OnMouseEnter(e); }   // task-143
        protected override void OnMouseLeave(EventArgs e) { _hovering = false; _hoverRow = -1; _hoverCircle = false; Cursor = Cursors.Default; Invalidate(); base.OnMouseLeave(e); }   // task-142

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // task-143: recompute the cursor on EVERY move (not only when the hover row/circle changes)
            int idx = HitRow(e.Location);
            bool circle = HitCircle(e.Location);
            Cursor = (HeaderBtnRect.Contains(e.Location) || circle || idx >= 0) ? Cursors.Hand : Cursors.Default;
            if (e.Button == MouseButtons.Left && _press != Point.Empty)
            {
                if (Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) > S(4)) _dragging = true;
                if (_dragging) Location = PointToScreen(new Point(e.X, e.Y)) - new Size(_offset.X, _offset.Y);
                return;
            }
            if (idx != _hoverRow || circle != _hoverCircle)
            {
                _hoverRow = idx;
                _hoverCircle = circle;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _press = e.Location;
                _offset = new Point(e.X, e.Y);
                _dragging = false;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { ShowMenu(); return; }
            bool wasPress = _press != Point.Empty;
            _press = Point.Empty;
            if (_dragging) { _dragging = false; SavePosition(); return; }
            if (!wasPress) return;
            // task-140: header button check comes first (after drag, before rows/double-click/blank)
            if (HeaderBtnRect.Contains(e.Location))
            {
                if (OpenMain != null && (DateTime.Now - _lastOpenMain).TotalMilliseconds > 600)
                {
                    _lastOpenMain = DateTime.Now;
                    OpenMain(this, EventArgs.Empty);
                }
                return;
            }

            // 点圆圈：完成/恢复该条（小牌与主界面功能对等）
            int idx = HitRow(e.Location);
            if (idx >= 0 && _rows[idx].Circle.Contains(e.Location))
            {
                TodoItem t = _rows[idx].Item;
                if (t.Done) { _store.Restore(t.Id); }
                else { bool spawned; _store.Complete(t.Id, out spawned); }
                RefreshData();
                return;
            }

            if (_expanded && idx >= 0)
            {
                // 展开态点条目 = 去主界面处理；点空白 = 收起
                if (OpenMain != null && (DateTime.Now - _lastOpenMain).TotalMilliseconds > 600)
                {
                    _lastOpenMain = DateTime.Now;
                    OpenMain(this, EventArgs.Empty);
                }
                return;
            }
            AnimateToLanes(!_expanded);   // task-109: real click must use the two-lane animator
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // task-139: double-click no longer opens the main window (logo click is the only entry)
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Visible = false; return; }
            base.OnFormClosing(e);
        }

        private void ShowMenu()
        {
            int idx = HitRow(PointToClient(Cursor.Position));
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(Strings.L("menu.done_next"), delegate { CompleteNext(); });
            if (idx >= 0 && _rows.Count > idx)
            {
                TodoItem t = _rows[idx].Item;
                m.MenuItems.Add(t.Done ? Strings.L("main.restore") : Strings.L("main.done"),
                    delegate { if (t.Done) _store.Restore(t.Id); else { bool sp; _store.Complete(t.Id, out sp); } RefreshData(); });
                m.MenuItems.Add(Strings.L("main.edit_row"), delegate { if (OpenMain != null) OpenMain(this, EventArgs.Empty); });
            }
            m.MenuItems.Add(Strings.L("menu.manage"), delegate { if (OpenMain != null) OpenMain(this, EventArgs.Empty); });
            m.MenuItems.Add(Strings.L("menu.refresh"), delegate { RefreshData(); });
            m.MenuItems.Add("-");
            m.MenuItems.Add(Strings.L("menu.hide"), delegate { Visible = false; });
            m.MenuItems.Add(Strings.L("menu.exit"), delegate { if (ExitApp != null) ExitApp(this, EventArgs.Empty); });
            m.Show(this, PointToClient(Cursor.Position));
        }

        private void CompleteNext()
        {
            TodoItem t = _store.Next();
            if (t == null) return;
            bool spawned;
            _store.Complete(t.Id, out spawned);
            RefreshData();
        }

        // ---- 自检用 ----
        public void TestAction(int idx)
        {
            if (idx == 1) { ApplyCompactSize(); return; }
            if (idx == 2) { if (OpenMain != null) OpenMain(this, EventArgs.Empty); return; }
            if (idx == 0 && _items != null)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Done) continue;
                    bool sp; _store.Complete(_items[i].Id, out sp); break;
                }
                RefreshData();
            }
        }

        // ---------- task-101: 160ms ease-out, two-lane per-row height animation ----------
        private List<TodoItem> _animItems;
        private int[] _animFromPx;
        private int[] _animToPx;
        private Timer _animTimer;
        private System.Diagnostics.Stopwatch _anim2Sw;
        private bool _animTargetExpanded;

        // task-104: animation log -> logs\float-anim.log
        private static readonly object _animLogLock = new object();

        private void Anim2Log(string s)
        {
            // task-147: anim log is opt-in only (Decanter normally writes no UI logs)
            if (Environment.GetEnvironmentVariable("DECANTER_ANIM_LOG") != "1") return;
            try
            {
                string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                lock (_animLogLock)
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "float-anim.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
            }
            catch { }
        }

        private void Anim2LogTargets(string tag, bool expanded)
        {
            if (_animItems == null) return;
            StringBuilder sb = new StringBuilder(tag + " expanded=" + expanded + " rows=" + _animItems.Count + " ");
            for (int i = 0; i < _animItems.Count; i++) sb.Append("[" + _animFromPx[i] + "->" + _animToPx[i] + "]");
            Anim2Log(sb.ToString());
        }


        private const int Anim2Ms = 170;   // task-111: 160-180ms window

        private static double EaseOut2(double x) { double u = 1.0 - x; return 1.0 - u * u * u; }

        private List<TodoItem> OrderedItems()
        {
            List<TodoItem> pin = new List<TodoItem>(), def = new List<TodoItem>();
            if (_items != null) for (int i = 0; i < _items.Count; i++) { if (_items[i] != null && _items[i].Pinned) pin.Add(_items[i]); else def.Add(_items[i]); }
            List<TodoItem> ordered = new List<TodoItem>(pin); ordered.AddRange(def);
            return ordered;
        }

        private int CurrentPxOf(TodoItem it)
        {
            if (_rows != null) for (int i = 0; i < _rows.Count; i++) if (_rows[i].Item == it) return _rows[i].Rect.Height;
            return 0;
        }

        // ===== task-123: two-phase tunnel animation (Phase A suffix clipped, Phase B prefix compressed) =====
        private int[] _tunNat, _tunCmp;
        private int _tunPrefix, _tunExpH, _tunPrefixH, _tunCmpH;
        private int _tunPhase;            // 0=idle 1=A 2=B (collapse order A->B; expand order B->A)
        private bool _tunExpand;
        private int _tunMsA, _tunMsB;

        private void TunnelStart(bool expanded)
        {
            List<TodoItem> ordered = OrderedItems();
            List<TodoItem> pin = new List<TodoItem>(), def = new List<TodoItem>();
            for (int i = 0; i < ordered.Count; i++) { if (ordered[i] != null && ordered[i].Pinned) pin.Add(ordered[i]); else def.Add(ordered[i]); }
            int[] aExp = LaneFlow.Expanded(pin, def, int.MaxValue);   // task-138: tunnel target = all rows
            int[] aCmp = LaneFlow.Compact(pin, def, CompactLineBudget);
            _animItems = ordered;
            _tunNat = new int[ordered.Count];
            _tunCmp = new int[ordered.Count];
            int sumNat = 0, sumCmp = 0; _tunPrefix = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                _tunNat[i] = Math.Max(0, aExp[i]) * S(SlotH);
                _tunCmp[i] = Math.Max(0, aCmp[i]) * S(SlotH);
                sumNat += _tunNat[i];
                if (_tunCmp[i] > 0) { sumCmp += _tunCmp[i]; _tunPrefix = i + 1; }
            }
            int prefixNat = 0;
            for (int i = 0; i < _tunPrefix; i++) prefixNat += _tunNat[i];
            _tunExpH = S(HeaderH) + sumNat + S(ExpandedBottomPad);
            _tunCmpH = S(HeaderH) + sumCmp + 1;   // task-125: same formula as CompactTargetHeight() (physical 1px)
            _tunPrefixH = S(HeaderH) + prefixNat + 1;   // task-125: keep the H-1 bottom rule at the end of Phase A
            int dA = Math.Max(0, _tunExpH - _tunPrefixH);
            int dB = Math.Max(0, _tunPrefixH - _tunCmpH);
            int total = 170;
            if (dA + dB <= 0) { _tunMsA = 40; _tunMsB = 40; }
            else
            {
                _tunMsA = (int)(total * (double)dA / (dA + dB));
                _tunMsA = Math.Max(40, Math.Min(total - 40, _tunMsA));
                _tunMsB = Math.Max(40, total - _tunMsA);
            }
            _tunExpand = expanded;
            _expanded = expanded;
            _animTargetExpanded = expanded;
            int lockLeft = Left, lockTop = Top;
            Width = S(CompactW);
            Height = expanded ? _tunCmpH : _tunExpH;   // start from current state
            Location = new Point(lockLeft, lockTop);
            if (_animTimer == null)
            {
                _animTimer = new Timer();
                _animTimer.Interval = 16;
                _animTimer.Tick += delegate { TunnelTick(); };
            }
            _anim2Sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_animTimer.Enabled) _animTimer.Start();
            Anim2Log("tun-start expand=" + expanded + " timer=" + (_animTimer.Enabled ? "reuse" : "new") + " nat=" + sumNat + " cmp=" + sumCmp + " prefix=" + _tunPrefix + " expH=" + _tunExpH + " prefixH=" + _tunPrefixH + " cmpH=" + _tunCmpH + " msA=" + _tunMsA + " msB=" + _tunMsB);
            TunnelApply();
        }

        private void TunnelTick()
        {
            if (_anim2Sw == null) { if (_animTimer != null) _animTimer.Stop(); return; }
            double ms = _anim2Sw.Elapsed.TotalMilliseconds;
            if (ms >= _tunMsA + _tunMsB)
            {
                _animTimer.Stop();
                if (_tunExpand) ApplyExpandedSize(); else ApplyCompactSize();
                Anim2Log("tun-end expand=" + _tunExpand + " H=" + Height + " targetH=" + (_tunExpand ? _tunExpH : _tunCmpH));
                return;
            }
            TunnelApply();
        }

        private static double EaseOut3(double x) { if (x < 0) x = 0; if (x > 1) x = 1; double u = 1 - x; return 1 - u * u * u; }

        private void TunnelApply()
        {
            if (_animItems == null || _tunNat == null) return;
            double ms = _anim2Sw.Elapsed.TotalMilliseconds;
            int lockLeft = Left, lockTop = Top;
            int n = _animItems.Count;
            int[] h = new int[n];
            int newH; int phase;
            if (!_tunExpand)
            {
                if (ms < _tunMsA)
                {
                    phase = 1; double e = EaseOut3(ms / Math.Max(1, _tunMsA));
                    for (int i = 0; i < n; i++) h[i] = _tunNat[i];            // Phase A: suffix only clipped, heights untouched
                    newH = (int)Math.Round(_tunExpH + (_tunPrefixH - _tunExpH) * e);
                }
                else
                {
                    phase = 2; double e = EaseOut3((ms - _tunMsA) / Math.Max(1, _tunMsB));
                    for (int i = 0; i < _tunPrefix; i++) h[i] = (int)Math.Round(_tunNat[i] + (_tunCmp[i] - _tunNat[i]) * e);
                    int used = 0; for (int i = 0; i < _tunPrefix; i++) used += h[i];
                    newH = S(HeaderH) + used + 1;   // task-124: strict lastBottom == H-1 (physical 1px, no S())
                }
            }
            else
            {
                if (ms < _tunMsB)
                {
                    phase = 2; double e = EaseOut3(ms / Math.Max(1, _tunMsB));
                    for (int i = 0; i < _tunPrefix; i++) h[i] = (int)Math.Round(_tunCmp[i] + (_tunNat[i] - _tunCmp[i]) * e);
                    int used = 0; for (int i = 0; i < _tunPrefix; i++) used += h[i];
                    newH = S(HeaderH) + used + 1;   // task-124: strict lastBottom == H-1
                }
                else
                {
                    phase = 1; double e = EaseOut3((ms - _tunMsB) / Math.Max(1, _tunMsA));
                    for (int i = 0; i < n; i++) h[i] = _tunNat[i];
                    newH = (int)Math.Round(_tunPrefixH + (_tunExpH - _tunPrefixH) * e);
                }
            }
            _rows.Clear();
            int y = S(HeaderH); int lastBottom = 0;
            for (int i = 0; i < n; i++)
            {
                if (h[i] <= 0) continue;
                RowHit rh = new RowHit();
                rh.Item = _animItems[i];
                rh.Rect = new Rectangle(0, y, ClientSize.Width, h[i]);
                rh.Circle = new Rectangle(S(10), y + S(4), S(24), Math.Max(S(12), Math.Min(S(24), h[i] - S(8))));
                _rows.Add(rh); lastBottom = y + h[i]; y += h[i];
            }
            SuspendLayout();
            try { Height = newH; Location = new Point(lockLeft, lockTop); }
            finally { ResumeLayout(); }
            _tunPhase = phase;
            StringBuilder sb = new StringBuilder();
            sb.Append("tun p=").Append(phase).Append(" H=").Append(Height).Append(" rows=").Append(_rows.Count).Append(" lastBottom=").Append(lastBottom).Append(" hs=");
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(","); sb.Append(h[i]); }
            Anim2Log(sb.ToString());
            Invalidate();
        }

        private void AnimateToLanes(bool expanded)
        {
            if (!Visible || !IsHandleCreated) { _expanded = expanded; JumpToTarget(); return; }
            TunnelStart(expanded);
        }

        private void AnimTick2()
        {
            try
            {
            if (_anim2Sw == null) { if (_animTimer != null) _animTimer.Stop(); return; }
            double t = _anim2Sw.Elapsed.TotalMilliseconds / Anim2Ms;
            if (t >= 1.0)
            {
                _animTimer.Stop();
                if (_animTargetExpanded) ApplyExpandedSize(); else ApplyCompactSize();
                Anim2Log("end expanded=" + _animTargetExpanded + " h=" + Height + " targetH=" + (_animTargetExpanded ? ExpandedTargetHeight() : CompactTargetHeight()));
                return;
            }
            AnimApply(EaseOut2(t));
            }
            catch (Exception ex) { try { if (_anim != null) _anim.Stop(); if (_animTimer != null) _animTimer.Stop(); Anim2Log("animtick2-exception " + ex.GetType().Name + ": " + ex.Message); } catch { } }
        }

        private void AnimApply(double e)
        {
            try
            {
            if (_animItems == null) return;
            int total = 0;
            List<int> px = new List<int>();
            for (int i = 0; i < _animItems.Count; i++)
            {
                int h = (int)Math.Round(_animFromPx[i] + (_animToPx[i] - _animFromPx[i]) * e);
                if (h < 0) h = 0;
                px.Add(h);
                total += h;
            }
            _rows.Clear();
            int y = S(HeaderH);
            for (int i = 0; i < _animItems.Count; i++)
            {
                int h = px[i];
                if (h <= 0) continue;
                RowHit rh = new RowHit();
                rh.Item = _animItems[i];
                rh.Rect = new Rectangle(0, y, ClientSize.Width, h);
                rh.Circle = new Rectangle(S(10), y + S(4), S(24), Math.Max(S(12), Math.Min(S(24), h - S(8))));
                _rows.Add(rh);
                y += h;
            }
            int bottomPad = _animTargetExpanded ? S(ExpandedBottomPad) : S(FrameTokens.Outer);   // task-122: compact pad == 1px frame
            int animCap = _animTargetExpanded ? ExpandedTargetHeight() : S(CompactH);   // task-113
            int newH = S(HeaderH) + total + bottomPad;
            Anim2Log("frame h=" + (S(HeaderH) + total + bottomPad) + " target=" + (_animTargetExpanded ? "exp" : "compact") + " rows=" + _rows.Count + " mrow=" + (S(HeaderH)) + " pad=" + bottomPad);
                if (newH < S(HeaderH) + S(SlotH)) newH = S(HeaderH) + S(SlotH);   // task-118: only keep above the first row; target CompactH is applied at the end
            if (newH > animCap) newH = animCap;
            int lockLeft = Left, lockTop = Top;
            SuspendLayout();
            try
            {
                Width = S(CompactW);   // task-110: width never changes
                Height = newH;
                Location = new Point(lockLeft, lockTop);   // task-110: top-left locked
            }
            finally { ResumeLayout(); }
            Invalidate();
            }
            catch (Exception ex) { try { if (_anim != null) _anim.Stop(); if (_animTimer != null) _animTimer.Stop(); Anim2Log("animapply-exception " + ex.GetType().Name + ": " + ex.Message); } catch { } }
        }

        /// <summary>Jump straight to the current target (drag / hidden / system close).</summary>
        public void JumpToTarget()
        {
            Anim2Log("jump expanded=" + _expanded + " (anim cancelled)");
            if (_animTimer != null) _animTimer.Stop();
            _animItems = null;
            if (_expanded) ApplyExpandedSize(); else ApplyCompactSize();
            Invalidate();
        }

        public void SettleForTest() { JumpToTarget(); }
        public void TestExpand() { AnimateToLanes(true); }
        public void TestCollapse() { AnimateToLanes(false); }
        public void TestSetItems(List<TodoItem> items) { _testItemsOverride = true; _expanded = false; _items = items; ApplyCompactSize(); }
        public void TestClearItemsOverride() { _testItemsOverride = false; }
        public List<TodoItem> TestItems() { return _items; }
        public Rectangle TestRowRect(int i) { BuildRowLayout(); return i >= 0 && i < _rows.Count ? _rows[i].Rect : new Rectangle(); }
        public Rectangle TestCircleRect(int i) { BuildRowLayout(); return i >= 0 && i < _rows.Count ? _rows[i].Circle : new Rectangle(); }
        public int TestShownCount() { BuildRowLayout(); return _rows.Count; }
    }
}