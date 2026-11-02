// TodoListView.cs — v6 列表：一页横线本（左装订线、段头、右日期列、行数=重要度）
// 视觉：无卡片、无圆角；整页横线；左侧 3px 重要度色段（连续取色）；完成圈是唯一语义圆形；
//       标题统一 16px（字号不读业务字段）；右列只放截止时间（等宽、右对齐），循环/提醒/成长放元信息；
// 信息：行数=重要度（1/2/3 行）；色段＋行数＋符号三通道冗余（需求 #30）；过期用危险色并写"已过期"。
// 分组：置顶 / 进行中 两个段头（组间 24px + 1px 强线；组内行间 1px 淡线），排序仍由 Store 决定，不重排。
// 交互：整行选中；圆圈=完成/恢复；双击=编辑；右键=行菜单；滚轮整行翻页；滚动条 3px overlay。
using System;
using Ui = Decanter.App.Layout;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public class TodoListView : Control
    {
        public Theme Theme;
        public List<TodoItem> Items = new List<TodoItem>();
        public string EmptyText = "";
        public string EmptyAction = "";
        public int EditingIndex = -1;
        public bool ShowSections = true;
        public event EventHandler SelectionChanged;
        public event EventHandler<CancelEventArgs> SelectionChanging;
        public event EventHandler ItemActivated;
        public event EventHandler CompletionRequested;
        public event EventHandler RowRightClicked;
        public event EventHandler EmptyActionRequested;

        // 行模型常量（单一来源 Layout；自检直接引用这些名字）
        public const int TitleLineH = Ui.TitleLineH;
        public const int MetaLineH = Ui.MetaLineH;
        public const int RowPadY = Ui.RowPadY;
        public const int CardPad = Ui.ListPadY;
        public const int RailW = Ui.RailBarW;
        public const int CircleVisual = Ui.CircleVisual;
        public const int CircleHit = Ui.CircleHit;

        public float FontScale = 1f;

        private float _dpi = 1f;
        private float _dpiOverride = 0f;
        private int _selected = -1;
        private int _hover = -1;
        private bool _hoverCircle;
        private int _firstRow;
        private bool _dragging;
        private int _dragY0;
        private int _dragFirst0;
        private readonly List<int> _tops = new List<int>();
        private readonly List<int> _heights = new List<int>();
        private readonly List<int> _sectionTops = new List<int>();
        private readonly List<string> _sectionLabels = new List<string>();
        private bool _growthAll, _growthOn;
        private Rectangle _emptyAction;

        public TodoListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = new Font(Theme.FontUI, Ui.FBody, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private int S(float v) { return (int)Math.Round(v * (DpiSafe())); }
        private float DpiSafe() { return _dpi <= 0 ? 1f : _dpi; }

        public void SetDpi(float dpi)
        {
            _dpiOverride = dpi > 0 ? dpi : 0f;
            _dpi = dpi > 0 ? dpi : DeviceDpiHint();
            Rebuild();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dpi = _dpiOverride > 0 ? _dpiOverride : DeviceDpiHint();
            Rebuild();
        }

        private float DeviceDpiHint()
        {
            try
            {
                if (IsHandleCreated)
                {
                    uint d = GetDpiForWindow(Handle);
                    if (d > 0) return d / 96f;
                }
            }
            catch { }
            return 1f;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr h);

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < -1 || value >= Items.Count) return;
                if (_selected == value) return;
                _selected = value;
                EnsureVisible(_selected);
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public TodoItem SelectedItem
        {
            get { return (_selected >= 0 && _selected < Items.Count) ? Items[_selected] : null; }
        }

        public void SetItems(List<TodoItem> items, bool growthAll, bool growthOn)
        {
            Items = items == null ? new List<TodoItem>() : items;
            _growthAll = growthAll;
            _growthOn = growthOn;
            if (_selected >= Items.Count) _selected = Items.Count - 1;
            _firstRow = 0;
            Rebuild();
        }

        public List<int> RowTopsForTest() { return new List<int>(_tops); }
        public List<int> RowHeightsForTest() { return new List<int>(_heights); }
        public int FirstRowForTest() { return _firstRow; }
        public int EditingIndexForTest() { return EditingIndex; }
        public void ScrollToBottomForTest() { SetFirstRow(MaxFirstRow()); }
        public void ScrollToRow(int idx) { EnsureVisible(idx); Invalidate(); }
        public int SectionCountForTest() { return _sectionTops.Count; }

        public Rectangle RowRectForTest(int i)
        {
            if (i < 0 || i >= _tops.Count) return new Rectangle();
            return new Rectangle(0, S(CardPad) + _tops[i] - ScrollTop(), Width, _heights[i]);
        }

        public static int MaxLines(int importance)
        {
            if (importance >= 3) return 3;
            if (importance <= 1) return 1;
            return 2;
        }

        public static string[] WrapNote(Graphics g, string text, Font f, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 10) return new string[0];
            int n1 = FitChars(g, f, text, width);
            if (n1 <= 0) return new string[0];
            if (n1 >= text.Length) return new string[] { text };
            string first = text.Substring(0, n1).TrimEnd();
            string rest = text.Substring(n1).TrimStart();
            if (rest.Length == 0) return new string[] { first };
            int n2 = FitChars(g, f, rest, width);
            string second;
            if (n2 >= rest.Length) second = rest;
            else second = rest.Substring(0, Math.Max(1, n2)).TrimEnd() + "…";
            return new string[] { first, second };
        }

        private static int FitChars(Graphics g, Font f, string s, int width)
        {
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                string sub = s.Substring(0, mid);
                Size sz = TextRenderer.MeasureText(g, sub, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (sz.Width <= width) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private ScoreResult ScoreOf(TodoItem t)
        {
            return Scoring.Evaluate(t, DateTime.Now, Scoring.GrowthEnabled(t, _growthAll, _growthOn));
        }

        private string MetaALine(TodoItem t, ScoreResult r, string[] noteLines)
        {
            List<string> parts = new List<string>();
            if (t.IsRecurring) parts.Add(Strings.L("field.repeat") + " " + RepeatName(t.Recurrence));
            if (t.RemindUtc.HasValue) parts.Add(Strings.L("field.remind") + " " + TimeText.Remind(TimeUtil.FromUtc(t.RemindUtc.Value), DateTime.Now));
            if (r.Growth > 0.004) parts.Add(Strings.L("growth.label"));
            if (noteLines.Length > 0 && noteLines[0].Length > 0) parts.Add(noteLines[0]);
            return string.Join(" · ", parts.ToArray());
        }

        private string MetaBLine(TodoItem t, string[] noteLines)
        {
            if (noteLines.Length > 1 && noteLines[1].Length > 0) return noteLines[1];
            if (t.Done) return Strings.L("done.at") + " " + TimeText.DoneAt(TimeUtil.FromUtc(t.UpdatedUtc));
            return "";
        }

        private static string RepeatName(string rule)
        {
            if (rule == "daily") return Strings.L("rep.daily");
            if (rule == "weekly") return Strings.L("rep.weekly");
            if (rule == "monthly") return Strings.L("rep.monthly");
            if (!string.IsNullOrEmpty(rule) && rule.StartsWith("every:")) return rule.Substring(6) + " 天";
            return rule == null ? "" : rule;
        }

        private int MeasureRow(Graphics g, TodoItem t, int metaWidth, Font metaFont, out string[] noteLines, out string metaA, out string metaB)
        {
            noteLines = WrapNote(g, t.Note, metaFont, metaWidth);
            ScoreResult r = ScoreOf(t);
            metaA = "";
            metaB = "";
            int lines = 1;
            if (t.Importance >= 2)
            {
                metaA = MetaALine(t, r, noteLines);
                if (metaA.Length > 0) lines++;
            }
            if (t.Importance >= 3)
            {
                metaB = MetaBLine(t, noteLines);
                if (metaB.Length > 0) lines++;
            }
            if (lines > MaxLines(t.Importance)) lines = MaxLines(t.Importance);
            int metaCount = lines - 1;
            return S(TitleLineH) + metaCount * S(MetaLineH) + 2 * S(RowPadY);
        }

        public void Rebuild()
        {
            _tops.Clear();
            _heights.Clear();
            _sectionTops.Clear();
            _sectionLabels.Clear();
            if (Width <= 0 || Height <= 0) return;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                MeasureAll(g, false);
                bool scroll = TotalHeight() > InnerHeight();
                if (scroll) MeasureAll(g, true);
            }
            SetFirstRow(_firstRow);
            EnsureVisible(_selected);
            Invalidate();
        }

        private void MeasureAll(Graphics g, bool scroll)
        {
            _tops.Clear();
            _heights.Clear();
            _sectionTops.Clear();
            _sectionLabels.Clear();
            int contentRight = Width - S(Ui.RightEdge) - (scroll ? S(Ui.ScrollW) + S(4) : 0);
            int titleRight = contentRight - S(Ui.RightColW) - S(Ui.TextGap);
            int metaWidth = Math.Max(S(80), titleRight - S(Ui.TextX));
            int y = S(CardPad);
            bool hasPinned = false;
            foreach (TodoItem t in Items) if (t.Pinned) { hasPinned = true; break; }
            bool firstSection = true;
            using (Font metaFont = new Font(Theme.FontUI, S(FL(Theme.FCaption, 12)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    TodoItem t = Items[i];
                    if (ShowSections && hasPinned)
                    {
                        bool newGroup = false;
                        string label = null;
                        if (i == 0 && t.Pinned) { newGroup = true; label = Strings.L("main.section.pinned"); }
                        else if (i > 0 && !t.Pinned && Items[i - 1].Pinned) { newGroup = true; label = Strings.L("main.section.active"); }
                        if (newGroup)
                        {
                            if (!firstSection) y += S(Ui.SectionGap);
                            _sectionTops.Add(y);
                            _sectionLabels.Add(label);
                            y += S(Ui.SectionH) + S(Ui.SectionAfter);
                            firstSection = false;
                        }
                    }
                    string[] noteLines; string metaA, metaB;
                    int h = MeasureRow(g, t, metaWidth, metaFont, out noteLines, out metaA, out metaB);
                    _tops.Add(y);
                    _heights.Add(h);
                    y += h;
                }
            }
        }

        private int TotalHeight()
        {
            if (_tops.Count == 0) return 0;
            return _tops[_tops.Count - 1] + _heights[_heights.Count - 1] + S(CardPad);
        }

        private int InnerHeight() { return Math.Max(S(40), Height - 2 * S(CardPad)); }
        private bool ScrollVisible() { return TotalHeight() > InnerHeight(); }

        private int VisibleCountFrom(int first)
        {
            int n = 0;
            int used = 0;
            for (int i = first; i < _heights.Count; i++)
            {
                if (used + _heights[i] > InnerHeight()) break;
                used += _heights[i];
                n++;
            }
            return n;
        }

        private int MaxFirstRow()
        {
            if (_heights.Count == 0) return 0;
            int total = TotalHeight();
            if (total <= InnerHeight()) return 0;
            int first = 0;
            for (int k = 0; k < _heights.Count; k++)
            {
                if (total - _tops[k] <= InnerHeight()) { first = k; break; }
                first = k;
            }
            return first;
        }

        private void SetFirstRow(int v)
        {
            int max = MaxFirstRow();
            if (v < 0) v = 0;
            if (v > max) v = max;
            _firstRow = v;
        }

        private int ScrollTop() { return _firstRow <= 0 ? 0 : (_firstRow < _tops.Count ? _tops[_firstRow] : 0); }

        private void EnsureVisible(int idx)
        {
            if (idx < 0 || idx >= _tops.Count) return;
            if (!ScrollVisible()) { _firstRow = 0; return; }
            if (idx < _firstRow) { SetFirstRow(idx); return; }
            int bottom = _tops[idx] + _heights[idx];
            int k = _firstRow;
            while (k < idx && bottom - ScrollTopOf(k) > InnerHeight()) k++;
            SetFirstRow(k);
        }

        private int ScrollTopOf(int firstRow) { return firstRow <= 0 ? 0 : (firstRow < _tops.Count ? _tops[firstRow] : 0); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Rebuild();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!ScrollVisible()) return;
            SetFirstRow(_firstRow - Math.Sign(e.Delta));
            Invalidate();
        }

        private int HitTest(int y)
        {
            int top = ScrollTop();
            int yy = y - S(CardPad) + top;
            for (int i = 0; i < _tops.Count; i++)
            {
                if (yy >= _tops[i] && yy < _tops[i] + _heights[i])
                {
                    if (i < _firstRow) return -1;
                    if (_tops[i] + _heights[i] - top > InnerHeight()) return -1;
                    return i;
                }
            }
            return -1;
        }

        private Rectangle CircleRect(int idx)
        {
            int y = S(CardPad) + _tops[idx] - ScrollTop();
            int cy = y + S(RowPadY) + (S(TitleLineH) - S(CircleVisual)) / 2;
            return new Rectangle(S(Ui.CircleX), cy, S(CircleVisual), S(CircleVisual));
        }

        private bool InCircle(int x, int y, int idx)
        {
            Rectangle c = CircleRect(idx);
            Rectangle hot = new Rectangle(c.Left - S((CircleHit - CircleVisual) / 2), c.Top - S((CircleHit - CircleVisual) / 2), S(CircleHit), S(CircleHit));
            return hot.Contains(x, y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                int max = MaxFirstRow();
                if (max > 0)
                {
                    int delta = (e.Y - _dragY0);
                    int step = delta * Math.Max(1, max) / Math.Max(1, Height);
                    SetFirstRow(_dragFirst0 + step);
                    Invalidate();
                }
                return;
            }
            if (Items.Count == 0)
            {
                Cursor = _emptyAction.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
                return;
            }
            int idx = HitTest(e.Y);
            bool circle = idx >= 0 && InCircle(e.X, e.Y, idx);
            if (idx != _hover || circle != _hoverCircle)
            {
                _hover = idx;
                _hoverCircle = circle;
                Invalidate();
            }
            if (ScrollVisible() && e.X >= Width - S(Ui.RightEdge) - S(Ui.ScrollW) - S(4))
                Cursor = Cursors.SizeNS;
            else
                Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            _hoverCircle = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Items.Count == 0)
            {
                if (_emptyAction.Contains(e.Location) && EmptyActionRequested != null) EmptyActionRequested(this, EventArgs.Empty);
                return;
            }
            if (ScrollVisible() && e.X >= Width - S(Ui.RightEdge) - S(Ui.ScrollW) - S(4))
            {
                _dragging = true;
                _dragY0 = e.Y;
                _dragFirst0 = _firstRow;
                return;
            }
            int idx = HitTest(e.Y);
            if (idx < 0) return;
            if (e.Clicks == 1 && InCircle(e.X, e.Y, idx))
            {
                if (!TrySelect(idx)) return;
                if (CompletionRequested != null) CompletionRequested(this, EventArgs.Empty);
                return;
            }
            TrySelect(idx);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.Y);
                if (idx >= 0 && TrySelect(idx) && RowRightClicked != null) RowRightClicked(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int idx = HitTest(e.Y);
            if (idx < 0) return;
            if (InCircle(e.X, e.Y, idx)) return;
            if (!TrySelect(idx)) return;
            if (ItemActivated != null) ItemActivated(this, EventArgs.Empty);
        }

        private bool TrySelect(int idx)
        {
            if (idx < 0) return false;
            if (SelectionChanging != null)
            {
                CancelEventArgs ce = new CancelEventArgs();
                SelectionChanging(this, ce);
                if (ce.Cancel) return false;
            }
            SelectedIndex = idx;
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme ?? Theme.Night();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : th.Paper);

            int contentRight = Width - S(Ui.RightEdge) - (ScrollVisible() ? S(Ui.ScrollW) + S(6) : 0);
            int rightColLeft = contentRight - S(Ui.RightColW);
            int titleRight = rightColLeft - S(Ui.TextGap);
            int textX = S(Ui.TextX);

            // 装订线：页边一条 1px 金属竖线；横线从它右侧起
            using (SolidBrush mb = new SolidBrush(th.Metal))
                g.FillRectangle(mb, new Rectangle(S(Ui.BindX), 0, Math.Max(1, S(1)), Height));

            if (Items.Count == 0)
            {
                DrawEmpty(g, th, textX, contentRight);
                return;
            }

            int top = ScrollTop();
            using (Font metaFont = new Font(Theme.FontUI, S(FL(Theme.FCaption, 12)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font sectionFont = new Font(Theme.FontUI, S(FL(Theme.FCaption, 12)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font monoFont = new Font(Theme.FontMono, Math.Max(9, S(FL(Theme.FCaption, 12))), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                // 段头（置顶 / 进行中）
                for (int si = 0; si < _sectionTops.Count; si++)
                {
                    int sy = S(CardPad) + _sectionTops[si] - top;
                    if (sy + S(Ui.SectionH) < 0 || sy > Height) continue;
                    TextRenderer.DrawText(g, _sectionLabels[si], sectionFont, new Rectangle(textX, sy, contentRight - textX, S(Ui.SectionH)), th.SubText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    using (Pen lp = new Pen(th.LineStrong, Math.Max(1, S(1))))
                        g.DrawLine(lp, S(Ui.BindX), sy + S(Ui.SectionH) - 1, contentRight, sy + S(Ui.SectionH) - 1);
                }

                DateTime now = DateTime.Now;
                for (int i = _firstRow; i < Items.Count; i++)
                {
                    int y = S(CardPad) + _tops[i] - top;
                    int h = _heights[i];
                    if (y + h < 0) continue;
                    if (y > Height) break;
                    TodoItem t = Items[i];
                    bool done = t.Done;
                    ScoreResult r = ScoreOf(t);
                    Color rail = Theme.ScoreColor(r.Score, th.IsDark);
                    if (done) rail = Theme.Mix(th.Card, rail, 0.45);

                    int rowL = S(Ui.BindX);
                    int rowW = contentRight - rowL;

                    if (i == _selected) { using (SolidBrush b = new SolidBrush(th.Selected)) g.FillRectangle(b, rowL, y, rowW, h); }
                    else if (i == _hover) { using (SolidBrush b = new SolidBrush(th.Hover)) g.FillRectangle(b, rowL, y, rowW, h); }
                    if (i == EditingIndex)
                    {
                        using (SolidBrush b = new SolidBrush(th.Accent))
                            g.FillRectangle(b, rowL, y, Math.Max(2, S(2)), Math.Max(2, h));
                    }

                    // 重要度色段
                    using (SolidBrush rb = new SolidBrush(rail))
                        g.FillRectangle(rb, S(Ui.BarX), y + S(6), Math.Max(2, S(RailW)), Math.Max(4, h - S(12)));

                    // 完成圈
                    Rectangle circle = CircleRect(i);
                    if (done)
                    {
                        using (SolidBrush b = new SolidBrush(th.Accent)) g.FillEllipse(b, circle);
                        using (Pen p = new Pen(th.OnAccent, Math.Max(2, S(2))))
                        {
                            int q = S(6);
                            g.DrawLine(p, circle.Left + q, circle.Top + S(12), circle.Left + S(10), circle.Top + S(16));
                            g.DrawLine(p, circle.Left + S(10), circle.Top + S(16), circle.Left + S(18), circle.Top + q);
                        }
                    }
                    else
                    {
                        Color cc = (_hoverCircle && i == _hover) ? th.Accent : Theme.Mix(th.Paper, th.SubText, 0.62);
                        using (Pen p = new Pen(cc, Math.Max(1, S(1.5f)))) g.DrawEllipse(p, circle);
                    }

                    int titleY = y + S(RowPadY);
                    int titleX = textX;
                    // 置顶：画一枚 12px 小图钉，不抢标题
                    if (t.Pinned)
                    {
                        Icons.Draw(g, "pin", new Rectangle(titleX, titleY + (S(TitleLineH) - S(12)) / 2, S(12), S(12)), th.Accent, DpiSafe());
                        titleX += S(18);
                    }

                    // 右列：只放截止（过期写"已过期"，危险色）
                    string rightText = "";
                    bool warn = false;
                    if (t.DueUtc.HasValue)
                    {
                        DateTime due = t.DueLocal.Value;
                        if (due < now) { warn = true; rightText = Strings.L("overdue.tag"); }
                        else rightText = TimeText.Due(due, now);
                    }
                    if (rightText.Length > 0)
                    {
                        TextRenderer.DrawText(g, rightText, monoFont, new Rectangle(rightColLeft, titleY, S(Ui.RightColW), S(TitleLineH)),
                            warn ? th.Dark : th.SubText,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }

                    int titleW = Math.Max(S(20), titleRight - titleX);
                    Color titleColor = done ? th.Aux : th.Text;
                    FontStyle titleStyle = done ? FontStyle.Strikeout : FontStyle.Regular;
                    using (Font tf = new Font(Theme.FontUI, S(FL(Theme.FTitle, 15)), titleStyle, GraphicsUnit.Pixel))
                    {
                        TextRenderer.DrawText(g, t.Title, tf, new Rectangle(titleX, titleY, titleW, S(TitleLineH)), titleColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    }

                    // 元信息：第二/三行
                    string[] noteLines;
                    string metaA, metaB;
                    MeasureRow(g, t, titleRight - textX, metaFont, out noteLines, out metaA, out metaB);
                    int my = titleY + S(TitleLineH);
                    if (metaA.Length > 0)
                    {
                        TextRenderer.DrawText(g, metaA, metaFont, new Rectangle(textX, my, titleRight - textX, S(MetaLineH)),
                            done ? th.Aux : th.SubText,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                        my += S(MetaLineH);
                    }
                    if (metaB.Length > 0)
                    {
                        TextRenderer.DrawText(g, metaB, metaFont, new Rectangle(textX, my, titleRight - textX, S(MetaLineH)),
                            th.Aux,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    }

                    // 行线：组内 1px 淡线，贯通到右侧；选中行加深
                    using (Pen lp = new Pen(i == _selected ? th.LineStrong : th.Line, Math.Max(1, S(1))))
                        g.DrawLine(lp, S(Ui.BindX), y + h - 1, contentRight, y + h - 1);
                    // 装订缝合点
                    using (Pen sp = new Pen(th.Metal, Math.Max(1, S(1))))
                        g.DrawLine(sp, 0, y + h - 1, S(Ui.BindX), y + h - 1);
                }
            }

            // 装订线最后重画：选中/悬停填充从 BindX 起，会盖住它
            using (SolidBrush mb2 = new SolidBrush(th.Metal))
                g.FillRectangle(mb2, new Rectangle(S(Ui.BindX), 0, Math.Max(1, S(1)), Height));

            // 滚动条：3px overlay
            if (ScrollVisible())
            {
                int trackX = Width - S(Ui.ScrollW);
                int trackTop = S(CardPad);
                int trackH = Height - 2 * S(CardPad);
                int max = MaxFirstRow();
                int thumbH = Math.Max(S(18), max > 0 ? trackH * Math.Max(1, VisibleCountFrom(_firstRow)) / Math.Max(1, Items.Count) : trackH);
                int thumbTop = trackTop;
                if (max > 0) thumbTop = trackTop + (trackH - thumbH) * _firstRow / max;
                using (SolidBrush tb = new SolidBrush(Theme.Alpha(th.Metal, 165)))
                    g.FillRectangle(tb, new Rectangle(trackX, thumbTop, Math.Max(3, S(4)), thumbH));
            }
        }

        private void DrawEmpty(Graphics g, Theme th, int textX, int contentRight)
        {
            string text = string.IsNullOrEmpty(EmptyText) ? Strings.L("main.empty.active") : EmptyText;
            string title = text;
            string hint = "";
            int nl = text.IndexOf('\n');
            if (nl >= 0)
            {
                title = text.Substring(0, nl);
                hint = text.Substring(nl + 1);
            }
            if (hint.Length == 0) hint = Strings.L("empty.hint.generic");

            int left = textX;
            int right = contentRight;

            // 空页横线：淡淡的 10 条，按 32 逻辑 px 的账本行距铺满
            using (Pen rp = new Pen(Theme.Alpha(th.Line, 150), Math.Max(1, S(1))))
            {
                for (int i = 0; i < 10; i++)
                {
                    int y = S(CardPad) + S(56) + i * S(32);
                    if (y < Height - S(CardPad)) g.DrawLine(rp, S(Ui.BindX), y, right, y);
                }
            }

            int baseY = Math.Max(S(40), S(CardPad) + S(18));
            using (SolidBrush b = new SolidBrush(th.Accent))
                g.FillRectangle(b, left, baseY, Math.Max(2, S(2)), S(18));

            using (Font tf = new Font(Theme.FontUI, S(FL(Theme.FTitle, 15)), FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font hf = new Font(Theme.FontUI, S(FL(Theme.FAux, 12)), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(g, title, tf, new Rectangle(left + S(14), baseY, right - left - S(14), S(26)), th.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                int hy = baseY + S(34);
                TextRenderer.DrawText(g, hint, hf, new Rectangle(left + S(14), hy, Math.Max(S(80), right - left - S(14)), S(44)), th.Aux,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

                string action = string.IsNullOrEmpty(EmptyAction) ? Strings.L("empty.action") : EmptyAction;
                Size asz = TextRenderer.MeasureText(g, action, hf, Size.Empty, TextFormatFlags.NoPadding);
                int aw = asz.Width + S(4);
                int ay = hy + S(52);
                _emptyAction = new Rectangle(left + S(14), ay, aw + S(8), S(30));
                bool hot = _emptyAction.Contains(PointToClient(Cursor.Position));
                Color ac = hot ? th.Accent : th.Text;
                TextRenderer.DrawText(g, action, hf, new Rectangle(left + S(14), ay, aw, S(30)), ac,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (SolidBrush b = new SolidBrush(ac))
                    g.FillRectangle(b, new Rectangle(left + S(14), ay + S(26), Math.Max(1, aw), Math.Max(1, S(2))));
            }
        }

        private string RightText(TodoItem t, DateTime now, out bool warn)
        {
            warn = false;
            if (!t.DueUtc.HasValue) return "";
            DateTime due = t.DueLocal.Value;
            if (due < now) { warn = true; return Strings.L("overdue.tag"); }
            return TimeText.Due(due, now);
        }

        public string RightTextForTest(TodoItem t, DateTime now)
        {
            bool w;
            return RightText(t, now, out w);
        }

        private int FL(float basePx, float floorPx)
        {
            int v = (int)Math.Round(basePx * FontScale);
            int f = (int)Math.Round(floorPx);
            return v < f ? f : v;
        }
    }

    public static class FloatCompat
    {
        public static Color ScoreColor(double score, Theme th)
        {
            return Theme.ScoreColor(score, th.IsDark);
        }

        public static string WrapLines(Graphics g, string text, Font font, int width, int maxLines, int lineH)
        {
            if (string.IsNullOrEmpty(text) || maxLines <= 0) return "";
            int maxH = lineH * maxLines;
            string s = text;
            while (s.Length > 0 && TextRenderer.MeasureText(g, s, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height > maxH)
                s = s.Substring(0, s.Length - 1);
            if (s.Length < text.Length)
            {
                while (s.Length > 0 && TextRenderer.MeasureText(g, s.TrimEnd() + "…", font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height > maxH)
                    s = s.Substring(0, s.Length - 1);
                s = s.TrimEnd() + "…";
            }
            return s;
        }
    }
}
