// MainForm.cs — v6 主界面：左索引轨 + 右账本页
// 结构：左轨（字标/日期 · 视图+计数 · 搜索 · 设置/关于/主题）
//       右页（页眉 44：视图名+计数+窗控 → 书写区 96：输入+添加+值令牌+溢出 → 横线列表 → 状态栏 28/44）
// 依据：问卷坐标卡 v2（深色暖调/留白够用/层次/锐利/纸+金属/全直角/人话）
//       + 前端理论第二版（分组层级、网格、排版、空状态、效率）+ v1.7 判据（I3≤7、U2 组间≥组内+2档、M2 版心）
// 不改：Core 业务语义、数据结构、CLI/API、安装方式、exe 名。
using System;
using Ui = Decanter.App.Layout;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public class MainForm : FrameForm
    {
        private readonly Store _store;
        private readonly FloatingForm _floating;
        private Theme _theme;

        private TodoListView _list;
        private FlatField _input, _noteBox, _searchBox;
        private FlatChip _impChip, _repeatChip, _remindChip, _pinChip, _dueChip;
        private FlatNav[] _nav;
        private FlatButton _btnAdd, _btnDone, _btnPin, _btnDel, _btnDue, _btnEdit;
        private FlatButton _btnSettings, _btnAbout, _btnTheme, _btnMore;
        private FlatButton _btnCancelEdit, _btnNewEdit, _btnMin, _btnMax, _btnClose;
        private Label _brandTitle, _brandDate, _railSection, _searchLabel, _pageTitle, _pageCount, _hint, _defaultLine, _editBanner;
        private Panel _rail, _railLine, _topStrip, _compose, _status, _lineTop, _lineCompose, _lineStatus;
        private PictureBox _logo;
        private List<TodoItem> _rows = new List<TodoItem>();
        private CommandPalette _palette;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out CLIENTRECT rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out CLIENTRECT rect);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct CLIENTRECT { public int Left, Top, Right, Bottom; }

        public Theme CurrentTheme { get { return _theme; } }

        private readonly EditorSession _edit = new EditorSession();
        private int _filterIndex;
        private int _impIndex = 1;
        private int _repeatIndex;
        private int _remindIndex;
        private long _editingId;
        private bool _loading;
        private bool _titleChanged, _noteChanged, _impChanged, _repeatChanged, _remindChanged, _pinChanged;
        private bool _noteExpanded;
        private long? _dueUtc;
        private bool _dueChanged;
        private Timer _firstRunTimer;
        private DateTime _firstRunUntil = DateTime.MinValue;
        private bool _firstRunVisible;
        private bool _trayHintShown;
        private float _dpi = 1f;
        private bool _recalcGuard;
        private float _deviceDpi = 1f;
        private float _fontScale = 1f;

        public MainForm(Store store, FloatingForm floating)
        {
            _store = store;
            _floating = floating;
            _theme = ThemeOf(store);
            Text = Strings.L("float.header");
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(960, 620);   // task-135: only two sizes -- small 960x620 or fullscreen
            MinimumSize = new Size(Ui.MinWindowW, Ui.MinWindowH);   // task-127: derived floor
            BackColor = _theme.Paper;
            Font = new Font(Theme.FontUI, Ui.FBody);
            Icon = Program.AppIcon();

            BuildRail();
            BuildTop();
            BuildCompose();
            BuildList();
            BuildStatus();
            WireEvents();
            ApplyDpi(1f);
            ApplyThemeToAll(_theme);
            LayoutAll();
            ResizeEnded += delegate
            {
                // task-131: always lay out once more on mouse-up, then reset the throttle window
                if (_liveTick != null) _liveTick.Stop();
                _liveDirty = false;
                _liveCost.Reset(); _liveCost.Start();
                try { SuspendLayout(); LayoutAll(); } finally { ResumeLayout(true); }
                Invalidate(true);
                _liveCost.Stop(); _liveLastCostMs = _liveCost.ElapsedMilliseconds;
                try { ForceRedraw(); } catch { }   // task-132: force only after the drag ends
            };
            RefreshList();
            SetupFirstRunHint();
        }

        private Theme ThemeOf(Store store)
        {
            return Theme.FromSetting(store.GetSetting("theme", "system"));
        }

        private int S(float v) { return (int)Math.Round(v * _dpi); }
        private Font Px(float px) { return Px(px, FontStyle.Regular); }
        private Font Px(float px, FontStyle style)
        {
            return new Font(Theme.FontUI, Math.Max(8, S(px)), style, GraphicsUnit.Pixel);
        }
        private Font Mono(float px)
        {
            return new Font(Theme.FontMono, Math.Max(8, S(px)), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // ---------- 构建：索引轨 ----------
        private void BuildRail()
        {
            _rail = new Panel();
            _rail.BackColor = _theme.Paper;
            _logo = new PictureBox();
            _logo.SizeMode = PictureBoxSizeMode.Normal;
            _logo.BackColor = _theme.Paper;
            _logo.Paint += delegate(object s, PaintEventArgs pe)
            {
                pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pe.Graphics.Clear(_theme.Card);
                Icons.Draw(pe.Graphics, "decanter", new Rectangle(0, 0, _logo.Width, _logo.Height), _theme.Accent, _dpi);
            };
            _brandTitle = new Label();
            _brandTitle.Text = Strings.L("float.header");
            _brandTitle.AutoSize = false;
            _brandDate = new Label();
            _brandDate.AutoSize = false;
            _brandDate.TextAlign = ContentAlignment.MiddleLeft;

            Label section = new Label();
            section.Name = "rail.section";
            section.Text = Strings.L("rail.views");
            section.AutoSize = false;
            _railSection = section;

            string[] icons = new string[] { "all", "today", "week", "overdue", "done" };
            string[] labels = new string[] {
                Strings.L("main.filter.all"), Strings.L("main.filter.today"), Strings.L("main.filter.week"),
                Strings.L("main.filter.overdue"), Strings.L("main.filter.done") };
            _nav = new FlatNav[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                FlatNav n = new FlatNav();
                n.Label = labels[i];
                n.IconName = icons[i];
                n.Theme = _theme;
                n.Selected = i == 0;
                n.TabStop = true;
                int idx = i;
                n.Clicked += delegate { SetFilter(idx); };
                _nav[i] = n;
                _rail.Controls.Add(n);
            }

            _searchBox = new FlatField();
            _searchBox.Placeholder = Strings.L("main.search_hint");
            _searchLabel = new Label();
            _searchLabel.AutoSize = false;
            _searchLabel.Text = Strings.L("main.search");
            _searchLabel.ForeColor = _theme.Aux;

            _btnSettings = MakeButton(Strings.L("main.settings"), ButtonKind.Secondary);
            _btnAbout = MakeButton(Strings.L("main.about"), ButtonKind.Secondary);
            _btnTheme = MakeButton("主题", ButtonKind.Secondary);   // batch2 #4: fixed label, menu instead of cycle
            _railLine = new Panel();
            _railLine.BackColor = _theme.LineStrong;
            _rail.Controls.Add(_logo);
            _rail.Controls.Add(_brandTitle);
            _rail.Controls.Add(_brandDate);
            _rail.Controls.Add(section);
            _rail.Controls.Add(_searchLabel);
            _rail.Controls.Add(_searchBox);
            _rail.Controls.Add(_btnSettings);
            _rail.Controls.Add(_btnAbout);
            _rail.Controls.Add(_btnTheme);
            _rail.Controls.Add(_railLine);
            Controls.Add(_rail);
        }

        // ---------- 构建：页眉 ----------
        private void BuildTop()
        {
            _topStrip = new Panel();
            _pageTitle = new Label();
            _pageTitle.AutoSize = false;
            _pageTitle.TextAlign = ContentAlignment.MiddleLeft;
            _pageTitle.Text = Strings.L("main.filter.all");
            _pageCount = new Label();
            _pageCount.AutoSize = false;
            _pageCount.Visible = false;   // #9: header count removed; footer keeps the counts
            _pageCount.TextAlign = ContentAlignment.MiddleRight;
            _btnMin = MakeButton("–", ButtonKind.Icon); _btnMin.TabStop = false;
            _btnMax = MakeButton("□", ButtonKind.Icon); _btnMax.TabStop = false;
            _btnClose = MakeButton("×", ButtonKind.Icon); _btnClose.TabStop = false;
            _lineTop = new Panel();
            _lineTop.BackColor = _theme.LineStrong;
            _topStrip.Controls.Add(_pageTitle);
            _topStrip.Controls.Add(_pageCount);
            _topStrip.Controls.Add(_btnMin);
            _topStrip.Controls.Add(_btnMax);
            _topStrip.Controls.Add(_btnClose);
            _topStrip.Controls.Add(_lineTop);
            Controls.Add(_topStrip);
        }

        // ---------- 构建：书写区 ----------
        private void BuildCompose()
        {
            _compose = new Panel();
            _input = new FlatField();
            _input.TextChanged += delegate { SyncDraftRow(); };
            _input.Placeholder = Strings.L("main.input_hint");
            _btnAdd = MakeButton(Strings.L("main.add"), ButtonKind.Primary);
            _btnMore = MakeButton("⋯", ButtonKind.Icon);
            _btnMore.AccessibleName = Strings.L("main.overflow");

            _impChip = MakeChip("", "");
            _dueChip = MakeChip("◷", Strings.L("due.none"));
            _repeatChip = MakeChip("↻", Strings.L("rep.none"));
            _remindChip = MakeChip(Strings.L("field.remind"), Strings.L("remind.none_chip"));
            _remindChip.Visible = false;
            _pinChip = MakeChip(Strings.L("field.pinned"), "");
            _pinChip.Toggle = true;
            _pinChip.Visible = false;

            _noteBox = new FlatField();
            _noteBox.Placeholder = Strings.L("field.note_hint");
            _defaultLine = new Label();
            _defaultLine.AutoSize = false;
            _defaultLine.TextAlign = ContentAlignment.MiddleLeft;
            _defaultLine.Visible = false;
            _editBanner = new Label();
            _editBanner.AutoSize = false;
            _editBanner.TextAlign = ContentAlignment.MiddleLeft;
            _editBanner.Visible = false;
            _btnCancelEdit = MakeButton(Strings.L("edit.cancel"), ButtonKind.Secondary);
            _btnNewEdit = MakeButton(Strings.L("edit.new"), ButtonKind.Secondary);
            _btnCancelEdit.Visible = false;
            _btnNewEdit.Visible = false;
            _lineCompose = new Panel();
            _lineCompose.BackColor = _theme.LineStrong;

            _compose.Controls.Add(_input);
            _compose.Controls.Add(_btnAdd);
            _compose.Controls.Add(_btnMore);
            _compose.Controls.Add(_btnCancelEdit);
            _compose.Controls.Add(_btnNewEdit);
            _compose.Controls.Add(_impChip);
            _compose.Controls.Add(_dueChip);
            _compose.Controls.Add(_repeatChip);
            _compose.Controls.Add(_defaultLine);
            _compose.Controls.Add(_editBanner);
            _compose.Controls.Add(_noteBox);
            _compose.Controls.Add(_remindChip);
            _compose.Controls.Add(_pinChip);
            _compose.Controls.Add(_lineCompose);
            _noteBox.Visible = false;
            Controls.Add(_compose);
            UpdateDefaultLine();
        }

        private void BuildList()
        {
            _list = new TodoListView();
            _list.Theme = _theme;
            _list.Name = "todo.list";
            _list.AccessibleName = "todo.list";
            _list.AccessibleRole = AccessibleRole.List;
            Controls.Add(_list);
        }

        private void BuildStatus()
        {
            _status = new Panel();
            _lineStatus = new Panel();
            _lineStatus.BackColor = _theme.Line;
            _btnEdit = MakeButton(Strings.L("main.edit"), ButtonKind.Secondary);
            _btnDone = MakeButton(Strings.L("main.done"), ButtonKind.Secondary);
            _btnPin = MakeButton(Strings.L("main.pin"), ButtonKind.Secondary);
            _btnDue = MakeButton(Strings.L("main.due_menu"), ButtonKind.Secondary);
            _btnDel = MakeButton(Strings.L("main.delete"), ButtonKind.Danger);
            _btnEdit.Visible = false;
            _btnDone.Visible = false;
            _btnPin.Visible = false;
            _btnDue.Visible = false;
            _btnDel.Visible = false;
            _hint = new Label();
            _hint.AutoSize = false;
            _status.Controls.Add(_lineStatus);
            _status.Controls.Add(_btnEdit);
            _status.Controls.Add(_btnDone);
            _status.Controls.Add(_btnPin);
            _status.Controls.Add(_btnDue);
            _status.Controls.Add(_btnDel);
            _status.Controls.Add(_hint);
            Controls.Add(_status);
        }

        private Panel Line()
        {
            Panel p = new Panel();
            p.Height = 1;
            p.BackColor = _theme.Line;
            return p;
        }

        private FlatButton MakeButton(string text, ButtonKind kind)
        {
            FlatButton b = new FlatButton();
            b.Kind = kind;
            b.Text = text;
            b.Theme = _theme;
            return b;
        }

        private FlatChip MakeChip(string label, string value)
        {
            FlatChip c = new FlatChip();
            c.Label = label;
            c.Value = value;
            c.Theme = _theme;
            return c;
        }

        // ---------- DPI / 字号 ----------
        private static float FontFactorOf(string v)
        {
            if (v == "l") return 1.2f;
            if (v == "s") return 0.94f;
            return 1f;
        }

        private int FL(float basePx, float floorPx)
        {
            int v = (int)Math.Round(basePx * _fontScale);
            int f = (int)Math.Round(floorPx);
            return v < f ? f : v;
        }

        private void ApplyDpi(float dpi)
        {
            _dpi = dpi <= 0 ? 1f : dpi;
            _fontScale = FontFactorOf(_store.GetSetting("font_scale", "m"));
            Font = Px(FL(Ui.FBody, 15));
            _brandTitle.Font = Px(FL(Ui.FBrand, 15), FontStyle.Bold);
            _brandTitle.ForeColor = _theme.Text;
            _brandDate.Font = Mono(FL(Ui.FMeta, 12));
            _brandDate.ForeColor = _theme.Aux;
            if (_searchLabel != null) _searchLabel.Font = Px(FL(Ui.FMeta, 12));
            _pageTitle.Font = Px(FL(Ui.FPageTitle, 15), FontStyle.Bold);
            _pageTitle.ForeColor = _theme.Text;
            _pageCount.Font = Mono(FL(Ui.FMeta, 12));
            _pageCount.ForeColor = _theme.Aux;
            _hint.Font = Px(FL(Ui.FMeta, 12));
            _hint.ForeColor = _theme.Aux;
            if (_defaultLine != null) { _defaultLine.Font = Px(FL(Ui.FAux, 12)); _defaultLine.ForeColor = _theme.Aux; }
            if (_editBanner != null) { _editBanner.Font = Px(FL(Ui.FAux, 12)); _editBanner.ForeColor = _theme.Accent; }

            FlatButton[] btns = new FlatButton[] { _btnAdd, _btnDone, _btnPin, _btnDel, _btnDue, _btnEdit,
                _btnSettings, _btnAbout, _btnTheme, _btnMore, _btnCancelEdit, _btnNewEdit, _btnMin, _btnMax, _btnClose };
            for (int i = 0; i < btns.Length; i++)
            {
                if (btns[i] == null) continue;
                btns[i].Dpi = _dpi;
                btns[i].Font = Px(FL(Theme.FButton, 12));
            }
            FlatButton[] glyphs = new FlatButton[] { _btnMin, _btnMax, _btnClose, _btnMore };
            for (int i = 0; i < glyphs.Length; i++)
                if (glyphs[i] != null) glyphs[i].Font = Px(Theme.Hglyph);
            FlatChip[] chips = new FlatChip[] { _impChip, _dueChip, _repeatChip, _remindChip, _pinChip };
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] == null) continue;
                chips[i].Dpi = _dpi;
                chips[i].Font = Px(FL(Theme.FAux, 12));
            }
            if (_nav != null)
            {
                for (int i = 0; i < _nav.Length; i++)
                {
                    if (_nav[i] == null) continue;
                    _nav[i].Dpi = _dpi;
                    _nav[i].Font = Px(FL(Ui.FNav, 12));
                }
            }
            FlatField[] fields = new FlatField[] { _input, _noteBox, _searchBox };
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i] == null) continue;
                fields[i].Dpi = _dpi;
                fields[i].ApplyDpi(_dpi, FL(Ui.FBody, 15));
            }
            if (_list != null)
            {
                _list.FontScale = _fontScale;
                _list.SetDpi(_dpi);
            }
        }

        // ---------- #1 edge snapping (mouse position on drag end; multi-monitor aware) ----------
        protected override void OnDragFinished()
        {
            base.OnDragFinished();
            if (WindowState == FormWindowState.Maximized) return;
            try { SnapFromPoint(Cursor.Position); } catch { }
        }

        private void SnapFromPoint(Point p)
        {
            Screen sc = Screen.FromPoint(p);
            Rectangle wa = sc.WorkingArea;
            int edge = S(18);
            bool left = p.X <= wa.Left + edge;
            bool right = p.X >= wa.Right - edge;
            bool top = p.Y <= wa.Top + edge;
            bool topCenter = top && !left && !right && Math.Abs(p.X - (wa.Left + wa.Width / 2)) <= wa.Width / 4;
            // task-135: only top-center drag maximizes; half/quarter snapping is intentionally disabled
            // (the old half/quarter code path is kept in git history; main window now has only two sizes)
            if (topCenter) { WindowState = FormWindowState.Maximized; return; }
            return;
        }

        // 吸附目标可能小于正常最小尺寸（半屏/四分屏）；只对这一次临时降低下限，避免被 MinimumSize 回夹。
        private void SetSnapBounds(Rectangle r)
        {
            Size old = MinimumSize;
            MinimumSize = new Size(Math.Min(old.Width, Math.Max(480, r.Width)),
                                   Math.Min(old.Height, Math.Max(320, r.Height)));
            SetBounds(r.X, r.Y, r.Width, r.Height);
            if (r.Width >= old.Width && r.Height >= old.Height) MinimumSize = old;
        }

        /// <summary>Debug/automation entry: snap to a named edge without injecting mouse input.</summary>
        public void TestSnap(string where)
        {
            Screen sc = Screen.PrimaryScreen;
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) if (!all[i].Primary) { sc = all[i]; break; }
            Rectangle wa = sc.WorkingArea;
            if (where == "top") { WindowState = FormWindowState.Maximized; return; }
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            if (where == "tl") SetSnapBounds(new Rectangle(wa.Left, wa.Top, wa.Width / 2, wa.Height / 2));
            else if (where == "tr") SetSnapBounds(new Rectangle(wa.Left + wa.Width / 2, wa.Top, wa.Width / 2, wa.Height / 2));
            else if (where == "bl") SetSnapBounds(new Rectangle(wa.Left, wa.Top + wa.Height / 2, wa.Width / 2, wa.Height / 2));
            else if (where == "br") SetSnapBounds(new Rectangle(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2, wa.Width / 2, wa.Height / 2));
            else if (where == "left") SetSnapBounds(new Rectangle(wa.Left, wa.Top, wa.Width / 2, wa.Height));
            else if (where == "right") SetSnapBounds(new Rectangle(wa.Left + wa.Width / 2, wa.Top, wa.Width / 2, wa.Height));
        }

        private TextBox _draftEditor;

        private void EnsureDraftEditor()
        {
            if (_draftEditor == null)
            {
                _draftEditor = new TextBox();
                _draftEditor.BorderStyle = BorderStyle.None;
                _draftEditor.Font = new Font(Theme.FontUI, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
                _draftEditor.TextChanged += delegate { SyncDraftRow(); };
                _draftEditor.KeyDown += delegate(object s, KeyEventArgs ke)
                {
                    if (ke.KeyCode == Keys.Enter) { DraftCommit(); ke.SuppressKeyPress = true; }
                    else if (ke.KeyCode == Keys.Escape) { DraftCancel(); ke.SuppressKeyPress = true; }
                };
                _draftEditor.GotFocus += delegate { if (DraftIsPlaceholder) { _draftEditor.ForeColor = _theme.Text; _draftEditor.Text = ""; } };
                _draftEditor.LostFocus += delegate { if (string.IsNullOrEmpty(_draftEditor.Text)) ShowDraftPlaceholder(); };
                _draftEditor.KeyPress += delegate { if (DraftIsPlaceholder) { _draftEditor.ForeColor = _theme.Text; _draftEditor.Text = ""; } };
                _list.Controls.Add(_draftEditor);
            }
            Theme th = _theme;
            _draftEditor.BackColor = th.Card;
            _draftEditor.ForeColor = th.Text;
            int x = Math.Max(S(56), _list.Width / 14);
            _draftEditor.SetBounds(x, S(24), Math.Max(120, _list.Width - x - S(24)), S(28));
            _draftEditor.Visible = true;
            _draftEditor.BringToFront();
        }

        private const string DraftPlaceholder = "写下一件要做的事…";

        private void ShowDraftPlaceholder()
        {
            if (_draftEditor == null) return;
            _draftEditor.ForeColor = _theme.Aux;
            if (_draftEditor.Text != DraftPlaceholder) _draftEditor.Text = DraftPlaceholder;
        }

        private bool DraftIsPlaceholder { get { return _draftEditor != null && _draftEditor.Text == DraftPlaceholder; } }
        private string DraftText { get { if (_draftEditor != null && _draftEditor.Visible) return DraftIsPlaceholder ? "" : _draftEditor.Text; return _input == null ? "" : _input.Text; } }

        private void SetDraftEditorText(string t)
        {
            if (_draftEditor == null) return;
            _draftEditor.TextChanged -= null;
            _draftEditor.Text = t == null ? "" : t;
        }
        private void SetDraftButtons(bool drafting)
        {
            if (_btnAdd != null) _btnAdd.Text = drafting ? "保存" : Strings.L("main.add");
            if (_btnCancelEdit != null) _btnCancelEdit.Visible = drafting;
            if (_btnNewEdit != null) _btnNewEdit.Visible = false;
            if (_input != null) _input.Visible = !drafting;
            if (_noteBox != null) _noteBox.Visible = !drafting;
            if (drafting) { EnsureDraftEditor(); }
            else if (_draftEditor != null) { _draftEditor.Visible = false; }
            if (!drafting) HideDraftRow();
        }

        private void DraftOutsideHandler(object s, MouseEventArgs e)
        {
            if (_draftActive) DraftClickOutside();
        }

        private void AttachDraftOutside()
        {
            if (_list != null) { _list.MouseDown -= DraftOutsideHandler; _list.MouseDown += DraftOutsideHandler; }
            this.MouseDown -= DraftOutsideHandler; this.MouseDown += DraftOutsideHandler;
        }
        private TodoItem _draftRow;

        private void ShowDraftRow()
        {
            HideDraftRow();
            _draftRow = new TodoItem();
            _draftRow.Title = "";
            _draftRow.CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _draftRow.UpdatedUtc = _draftRow.CreatedUtc;
            _list.Items.Insert(0, _draftRow);
            _list.EditingIndex = 0;
            _list.Rebuild();
        }

        private void SyncDraftRow()
        {
            if (_draftRow == null) return;
            string t = DraftText;
            _draftRow.Title = t.Trim();   // empty is fine: the overlay carries the placeholder at the title position
            _draftRow.Note = _noteBox == null ? "" : _noteBox.Text;
            if (_list != null) { _list.Rebuild(); _list.Invalidate(); }
            _list.Invalidate();
        }

        private void HideDraftRow()
        {
            if (_draftRow != null) { try { _list.Items.Remove(_draftRow); } catch { } _draftRow = null; _list.Rebuild(); }
            if (_list != null) { _list.EditingIndex = -1; _list.Invalidate(); }
        }
        // ---------- batch4 #13: one draft component for add + edit (compose area) ----------
        private bool _draftActive;
        private bool _draftConfirm;
        public bool DraftActive { get { return _draftActive; } }
        public bool DraftConfirm { get { return _draftConfirm; } }

        public void BeginDraft()
        {
            _draftActive = true; _draftConfirm = false;
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; } finally { _loading = false; }
            ResetInputChips();
            _edit.BeginNew();
            _editingId = 0;
            ClearChangedFlags();
            if (_editBanner != null) _editBanner.Visible = false;
            if (_btnCancelEdit != null) _btnCancelEdit.Visible = true;
            if (_btnNewEdit != null) _btnNewEdit.Visible = false;
            UpdateHint();
            SetDraftButtons(true);
            ShowDraftRow();
            AttachDraftOutside();
            if (_draftEditor != null) { _draftEditor.Text = ""; _draftEditor.Focus(); ShowDraftPlaceholder(); }
            FocusTitle();
        }

        public bool BeginDraftSelected()
        {
            TodoItem t = _list == null ? null : _list.SelectedItem;
            if (t == null) { UpdateHint(); return false; }
            _draftActive = true; _draftConfirm = false;
            HideDraftRow();
            SetDraftButtons(true);
            if (_list != null) { _list.MouseDown -= DraftOutsideHandler; _list.MouseDown += DraftOutsideHandler; }
            _edit.BeginEdit(t);
            _editingId = t.Id;
            ClearChangedFlags();
            LoadSelected();
            if (_draftEditor != null) { _draftEditor.Text = _input.Text; _draftEditor.Focus(); }
            if (_editBanner != null) _editBanner.Visible = false;
            if (_btnCancelEdit != null) _btnCancelEdit.Visible = true;
            FocusTitle();
            return true;
        }

        public void SetDraftTitle(string title)
        {
            _loading = true;
            try { SetDraftEditorText(title == null ? "" : title); SyncDraftRow(); } finally { _loading = false; }
            MarkDirty();
        }

        public void SetDraftNote(string note)
        {
            _loading = true;
            try { _noteBox.Text = note == null ? "" : note; } finally { _loading = false; }
            MarkDirty();
        }

        /// <summary>Enter / click Save. Empty title => discard (no row, hint only, no layout jump).</summary>
        public bool DraftCommit()
        {
            string title = DraftText == null ? "" : DraftText.Trim();
            if (title.Length == 0)
            {
                UpdateHint();
                return false;
            }
            _loading = true;
            try { _input.Text = title; } finally { _loading = false; }
            if (_edit.IsEditing) SaveEdit(); else AddTodo();
            _draftActive = false; _draftConfirm = false;
            SetDraftButtons(false);
            UpdateHint();
            HideDraftRow();
            RefreshList();
            return true;
        }

        /// <summary>Esc: first call arms confirmation, second call discards.</summary>
        public int DraftCancel()
        {
            if (!_draftActive) return 0;
            string t0 = (DraftText == null ? "" : DraftText).Trim();
            bool emptyDraft = t0.Length == 0 && !_edit.IsEditing;
            if (!_draftConfirm && !emptyDraft)
            {
                _draftConfirm = true;
                if (_hint != null) _hint.Text = "再按 Esc 放弃草稿（Enter 保存）";
                return 1;
            }
            _draftActive = false; _draftConfirm = false;
            if (_edit.IsEditing) CancelEdit();
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; } finally { _loading = false; }
            ResetInputChips();
            if (_btnCancelEdit != null) _btnCancelEdit.Visible = false;
            UpdateHint();
            return 2;
        }

        /// <summary>Click outside: non-empty saves, empty discards.</summary>
        public bool DraftClickOutside()
        {
            if (!_draftActive) return false;
            string title = DraftText == null ? "" : DraftText.Trim();
            if (title.Length == 0) { DraftCancel(); DraftCancel(); return false; }
            return DraftCommit();
        }

        public string DraftTitle { get { return DraftText; } }
        // ---------- 布局（唯一真源：Layout） ----------
        public void LayoutAll()
        {
            if (_rail == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 200 || h < 160) return;

            int railW = S(Ui.RailW);
            _rail.SetBounds(0, 0, railW, h);
            _railLine.SetBounds(railW - 1, 0, 1, h);

            // 字标
            int padX = S(Ui.RailPadX);
            int logo = S(Ui.RailBrandLogo);
            _logo.SetBounds(padX, S(Ui.RailBrandY), logo, logo);
            _brandTitle.SetBounds(padX + logo + S(10), S(Ui.RailBrandY - 2), railW - padX * 2 - logo - S(10), S(22));
            _brandDate.SetBounds(padX + logo + S(10), S(Ui.RailBrandY + 20), railW - padX * 2 - logo - S(10), S(16));

            // 视图
            int sectionH = S(16);
            if (_railSection != null)
            {
                int measured = TextRenderer.MeasureText("视图", _railSection.Font).Height + S(2);
                if (measured > sectionH) sectionH = measured;
                _railSection.SetBounds(padX, S(Ui.RailSectionY), railW - padX * 2, sectionH);
                _railSection.ForeColor = _theme.Aux;
            }
            int navW = railW - padX * 2;
            int footY = h - S(Ui.RailFootGap + Ui.RailFootH);
            int navTop = S(Ui.RailNavTop);
            int navH = S(Ui.RailNavH);
            int navGap = S(Ui.RailNavGap);
            int navSpace = Math.Max(0, footY - navTop - S(8));
            int needed = _nav.Length * (navH + navGap);
            if (needed > navSpace)
            {
                navGap = Math.Max(0, S(2));
                navH = Math.Max(S(24), (navSpace - navGap * (_nav.Length - 1)) / Math.Max(1, _nav.Length));
            }
            int navBottom = navTop;
            for (int i = 0; i < _nav.Length; i++)
            {
                int y = navTop + i * (navH + navGap);
                _nav[i].SetBounds(padX, y, navW, navH);
                navBottom = y + navH;
            }

            // 搜索：空间不够时整块折叠，绝不让标签/框压住导航或底部按钮
            int searchY = h - S(Ui.RailFootGap + Ui.RailFootH + 8 + Ui.RailSearchH);
            bool showSearch = searchY - S(20) > navBottom + S(32);
            _searchLabel.Visible = showSearch;
            _searchBox.Visible = showSearch;
            if (showSearch)
            {
                _searchLabel.SetBounds(padX, searchY - S(18), navW, S(16));
                _searchBox.SetBounds(padX, searchY, navW, S(Ui.RailSearchH));
            }

            // 底部设置/关于/主题
            int bw = navW / 3;
            _btnSettings.SetBounds(padX, footY, bw, S(Ui.RailFootH));
            _btnAbout.SetBounds(padX + bw, footY, bw, S(Ui.RailFootH));
            _btnTheme.SetBounds(padX + bw * 2, footY, navW - bw * 2, S(Ui.RailFootH));

            // 页面
            int pageX = railW;
            int pageW = w - railW;
            int contentW = Math.Min(S(Ui.PageMaxW), pageW - S(Ui.PagePadX) * 2);
            if (contentW < S(320)) contentW = Math.Max(S(240), pageW - S(Ui.PagePadX) * 2);
            int contentX = pageX + (pageW - contentW) / 2;
            int right = contentX + contentW;

            int relX = contentX - pageX;
            int relRight = relX + contentW;
            _topStrip.SetBounds(pageX, 0, pageW, S(Ui.PageTopStripH));
            if (!_headerLineHooked)
            {
                _headerLineHooked = true;
                _topStrip.Paint += delegate(object hs, PaintEventArgs hp)
                {
                    // task-137: draw the full-width 1px header rule directly in the strip paint (Panel stacking used to leave gaps)
                    try
                    {
                        using (Pen pen = new Pen(_theme.LineStrong))
                            hp.Graphics.DrawLine(pen, 0, _topStrip.ClientSize.Height - 1, Math.Max(0, _topStrip.ClientSize.Width - 1), _topStrip.ClientSize.Height - 1);
                    }
                    catch { }
                };
            }
            _lineTop.Visible = false;
            _pageTitle.SetBounds(relX, 0, Math.Max(S(120), contentW - S(150)), S(Ui.PageTopStripH));
            int winReserve = S(32) * 3 + S(2) * 2 + S(8);
            _pageCount.SetBounds(pageW - S(8) - winReserve - S(12) - S(150), 0, S(150), S(Ui.PageTopStripH));

            int winW = S(32), winH = S(Theme.HWinBtn), winY = S(9);
            int bx = pageW - S(8);
            _btnClose.SetBounds(bx - winW, winY, winW, winH); bx -= winW + S(2);
            _btnMax.SetBounds(bx - winW, winY, winW, winH); bx -= winW + S(2);
            _btnMin.SetBounds(bx - winW, winY, winW, winH);

            // 书写区
            bool editing = _edit.IsEditing;
            int composeH = S(editing && _noteExpanded ? Ui.ComposeHNote : Ui.ComposeH);
            int row1Y = S(Ui.ComposeRowY), row1H = S(Ui.ComposeRowH);
            if (_draftActive) composeH = row1Y + row1H + S(8);
            _compose.SetBounds(pageX, S(Ui.PageTopStripH), pageW, composeH);
            _lineCompose.SetBounds(0, composeH - 1, pageW, 1);
            int addW = S(Ui.AddW), moreW = S(Ui.MoreW), ebW = S(Ui.EditBtnW);
            int cur = relRight;
            if (editing)
            {
                _btnNewEdit.SetBounds(cur - ebW, row1Y, ebW, row1H); cur -= ebW + S(8);
                _btnCancelEdit.SetBounds(cur - ebW, row1Y, ebW, row1H); cur -= ebW + S(8);
                _btnAdd.SetBounds(cur - addW, row1Y, addW, row1H); cur -= addW + S(8);
                _btnMore.Visible = false;
            }
            else
            {
                _btnAdd.SetBounds(cur - addW, row1Y, addW, row1H); cur -= addW + S(10);
                _btnMore.SetBounds(cur - moreW, row1Y, moreW, row1H); cur -= moreW + S(8);
            }
            _input.SetBounds(relX, row1Y, Math.Max(S(120), cur - S(8) - relX), row1H);
            if (_draftActive)
            {
                // compose 输入在草稿期隐藏；取消/保存固定在右侧，避免用编辑态残留坐标跑到左边
                _btnAdd.SetBounds(cur - addW, row1Y, addW, row1H);
                _btnCancelEdit.SetBounds(cur - addW - S(8) - ebW, row1Y, ebW, row1H);
                _btnNewEdit.Visible = false;
                _btnMore.Visible = false;
            }

            // 值令牌行：草稿期上移到原输入行，顶部不再留空
            int row2Y = _draftActive ? row1Y : S(Ui.TokenRowY), row2H = S(Ui.TokenRowH);
            int chipImpW = S(76), chipDueW = S(104), chipRepW = S(96);
            int cx = relX;
            _impChip.SetBounds(cx, row2Y, chipImpW, row2H); cx += chipImpW + S(4);
            _dueChip.SetBounds(cx, row2Y, chipDueW, row2H); cx += chipDueW + S(4);
            _repeatChip.SetBounds(cx, row2Y, chipRepW, row2H);
            int bannerX = cx + chipRepW + S(20);
            _editBanner.SetBounds(bannerX, row2Y, Math.Max(S(80), relRight - bannerX), row2H);
            _defaultLine.SetBounds(bannerX, row2Y, Math.Max(S(80), relRight - bannerX), row2H);
            _impChip.Visible = true;
            _dueChip.Visible = true;
            _repeatChip.Visible = true;
            _editBanner.Visible = editing;

            if (editing && _noteExpanded) _noteBox.SetBounds(relX, S(Ui.NoteRowY), contentW, S(28));
            else _noteBox.SetBounds(relX, row2Y, contentW, row2H);

            // 列表
            bool hasSel = _list != null && _list.SelectedItem != null;
            int statusH = (hasSel || _firstRunVisible) ? S(Ui.StatusSelH) : S(Ui.StatusH);
            int listTop = S(Ui.PageTopStripH) + composeH + S(Ui.PageGap);
            int listBottom = h - statusH - S(Ui.PageGap);
            int listH = Math.Max(S(80), listBottom - listTop);
            _list.SetBounds(contentX, listTop, contentW, listH);

            // 状态栏
            _status.SetBounds(pageX, h - statusH, pageW, statusH);
            _lineStatus.SetBounds(0, 0, pageW, 1);
            _lineStatus.Visible = hasSel;
            int sy = Math.Max(0, (statusH - S(28)) / 2);
            int fx = relX;
            int aw = S(Theme.HAction);
            _btnEdit.SetBounds(fx, sy, aw, S(28)); fx += aw + S(2);
            _btnDone.SetBounds(fx, sy, aw, S(28)); fx += aw + S(2);
            _btnPin.SetBounds(fx, sy, aw, S(28)); fx += aw + S(2);
            _btnDue.SetBounds(fx, sy, aw, S(28)); fx += aw + S(2);
            _btnDel.SetBounds(fx, sy, aw, S(28));
            if (hasSel) { _hint.TextAlign = ContentAlignment.MiddleRight; _hint.SetBounds(Math.Max(relX, relRight - S(300)), sy, S(300), S(28)); }
            else { _hint.TextAlign = ContentAlignment.MiddleLeft; _hint.SetBounds(relX, sy, S(300), S(28)); }
        }

        private long _segSuspend, _segRail, _segPage, _segCompose, _segList, _segStatus, _segTotal;   // task-134 segment timings
        private long _lastPaintMs;
        private Timer _liveTick; private bool _liveDirty; private long _liveIntervalMs = 16; private long _liveTicks; private long _liveLastCostMs; private readonly System.Diagnostics.Stopwatch _liveCost = new System.Diagnostics.Stopwatch();   // task-131 adaptive tick
        /// <summary>task-131: adaptive resize scheduler -- interval = clamp(lastLayoutCost*1.3, 8, 33) ms.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            base.OnPaint(e);
            sw.Stop();
            _lastPaintMs = sw.ElapsedMilliseconds;
        }

        /// <summary>task-133: fast shell-only layout for the drag tick (no text measuring, no list rebuild).</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void LayoutShellFast()
        {
            if (_rail == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w < 200 || h < 160) return;
            System.Diagnostics.Stopwatch seg = System.Diagnostics.Stopwatch.StartNew();
            SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);   // task-134: WM_SETREDRAW off -- suppress intermediate paints during the drag tick
            SuspendLayout();
            _rail.SuspendLayout(); if (_topStrip != null) _topStrip.SuspendLayout(); _compose.SuspendLayout(); if (_list != null) _list.SuspendLayout(); _status.SuspendLayout();
            _segSuspend = seg.ElapsedMilliseconds;
            int railW = S(Ui.RailW);
            _rail.SetBounds(0, 0, railW, h);
            _railLine.SetBounds(railW - 1, 0, 1, h);
            int padX = S(Ui.RailPadX), logo = S(Ui.RailBrandLogo);
            _logo.SetBounds(padX, S(Ui.RailBrandY), logo, logo);
            _brandTitle.SetBounds(padX + logo + S(10), S(Ui.RailBrandY - 2), railW - padX * 2 - logo - S(10), S(22));
            _brandDate.SetBounds(padX + logo + S(10), S(Ui.RailBrandY + 20), railW - padX * 2 - logo - S(10), S(16));
            if (_railSection != null) _railSection.SetBounds(padX, S(Ui.RailSectionY), railW - padX * 2, S(16));
            int navW = railW - padX * 2;
            int footY = h - S(Ui.RailFootGap + Ui.RailFootH);
            int navTop = S(Ui.RailNavTop);
            int navH = S(Ui.RailNavH);
            int navGap = S(Ui.RailNavGap);
            int navSpace = Math.Max(0, footY - navTop - S(8));
            int needed = _nav.Length * (navH + navGap);
            if (needed > navSpace)
            {
                navGap = Math.Max(0, S(2));
                navH = Math.Max(S(24), (navSpace - navGap * (_nav.Length - 1)) / Math.Max(1, _nav.Length));
            }
            int navBottom = navTop;
            for (int i = 0; i < _nav.Length; i++)
            {
                int y = navTop + i * (navH + navGap);
                _nav[i].SetBounds(padX, y, navW, navH);
                navBottom = y + navH;
            }
            int searchY = h - S(Ui.RailFootGap + Ui.RailFootH + 8 + Ui.RailSearchH);
            bool showSearch = searchY - S(20) > navBottom + S(32);
            _searchLabel.Visible = showSearch; _searchBox.Visible = showSearch;
            if (showSearch)
            {
                _searchLabel.SetBounds(padX, searchY - S(18), navW, S(16));
                _searchBox.SetBounds(padX, searchY, navW, S(Ui.RailSearchH));
            }
            int bw = navW / 3;
            _btnSettings.SetBounds(padX, footY, bw, S(Ui.RailFootH));
            _btnAbout.SetBounds(padX + bw, footY, bw, S(Ui.RailFootH));
            _btnTheme.SetBounds(padX + bw * 2, footY, navW - bw * 2, S(Ui.RailFootH));
            _segRail = seg.ElapsedMilliseconds - _segSuspend;
            int pageX = railW, pageW = w - railW;
            int contentW = Math.Min(S(Ui.PageMaxW), pageW - S(Ui.PagePadX) * 2);
            if (contentW < S(320)) contentW = Math.Max(S(240), pageW - S(Ui.PagePadX) * 2);
            int contentX = pageX + (pageW - contentW) / 2;
            int relX = contentX - pageX, relRight = relX + contentW;
            _topStrip.SetBounds(pageX, 0, pageW, S(Ui.PageTopStripH));
            if (!_headerLineHooked)
            {
                _headerLineHooked = true;
                _topStrip.Paint += delegate(object hs, PaintEventArgs hp)
                {
                    // task-137: draw the full-width 1px header rule directly in the strip paint (Panel stacking used to leave gaps)
                    try
                    {
                        using (Pen pen = new Pen(_theme.LineStrong))
                            hp.Graphics.DrawLine(pen, 0, _topStrip.ClientSize.Height - 1, Math.Max(0, _topStrip.ClientSize.Width - 1), _topStrip.ClientSize.Height - 1);
                    }
                    catch { }
                };
            }
            _lineTop.Visible = false;
            _pageTitle.SetBounds(relX, 0, Math.Max(S(120), contentW - S(150)), S(Ui.PageTopStripH));
            int winW = S(32), winH = S(Theme.HWinBtn), winY = S(9);
            int bx = pageW - S(8);
            _btnClose.SetBounds(bx - winW, winY, winW, winH); bx -= winW + S(2);
            _btnMax.SetBounds(bx - winW, winY, winW, winH); bx -= winW + S(2);
            _btnMin.SetBounds(bx - winW, winY, winW, winH);
            _segPage = seg.ElapsedMilliseconds - _segSuspend - _segRail;
            int composeH = S(Ui.ComposeH);
            if (_draftActive) composeH = S(Ui.ComposeRowY) + S(Ui.ComposeRowH) + S(8);
            _compose.SetBounds(pageX, S(Ui.PageTopStripH), pageW, composeH);
            _lineCompose.SetBounds(0, composeH - 1, pageW, 1);
            int row1Y = S(Ui.ComposeRowY), row1H = S(Ui.ComposeRowH);
            _btnAdd.SetBounds(relRight - S(Ui.AddW), row1Y, S(Ui.AddW), row1H);
            _btnMore.SetBounds(relRight - S(Ui.AddW) - S(10) - S(Ui.MoreW), row1Y, S(Ui.MoreW), row1H);
            _input.SetBounds(relX, row1Y, Math.Max(S(120), relRight - S(Ui.AddW) - S(10) - S(Ui.MoreW) - S(18) - relX), row1H);
            _segCompose = seg.ElapsedMilliseconds - _segSuspend - _segRail - _segPage;
            int statusH = S(Ui.StatusH);
            int listTop = S(Ui.PageTopStripH) + composeH + S(Ui.PageGap);
            int listBottom = h - statusH - S(Ui.PageGap);
            int listH = Math.Max(S(80), listBottom - listTop);
            _list.SetBounds(contentX, listTop, contentW, listH);
            _segList = seg.ElapsedMilliseconds - _segSuspend - _segRail - _segPage - _segCompose;
            _status.SetBounds(pageX, h - statusH, pageW, statusH);
            _lineStatus.SetBounds(0, 0, pageW, 1);
            _segStatus = seg.ElapsedMilliseconds - _segSuspend - _segRail - _segPage - _segCompose - _segList;
            _rail.ResumeLayout(false); if (_topStrip != null) _topStrip.ResumeLayout(false); _compose.ResumeLayout(false); if (_list != null) _list.ResumeLayout(false); _status.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
            SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero);   // WM_SETREDRAW on
            _segTotal = seg.ElapsedMilliseconds;
            Invalidate();
        }

        private void EnsureLiveTick()
        {
            if (_liveTick == null)
            {
                _liveTick = new Timer();
                _liveTick.Interval = (int)_liveIntervalMs;
                _liveTick.Tick += delegate { LiveTickHandler(); };
            }
            _liveTick.Interval = (int)_liveIntervalMs;
            if (!_liveTick.Enabled) _liveTick.Start();
        }

        private void LiveTickHandler()
        {
            if (!_liveDirty || !IsResizing)
            {
                if (_liveTick != null) _liveTick.Stop();
                if (!IsResizing) _liveDirty = false;
                return;
            }
            _liveDirty = false;
            _liveCost.Reset(); _liveCost.Start();
            SuspendLayout();
            try { LayoutShellFast(); }   // task-133: shell-only fast path during drag
            finally { ResumeLayout(true); }
            Invalidate(true);   // task-132: let WM_PAINT coalesce, no Update()/forced redraw in the drag tick
            _liveCost.Stop();
            _liveLastCostMs = _liveCost.ElapsedMilliseconds;
            _liveTicks++;
            long next = (long)(_liveLastCostMs * 1.3);
            if (next < 8) next = 8;
            if (next > 33) next = 33;
            _liveIntervalMs = next;
            if (_liveTick != null) _liveTick.Interval = (int)next;
        }

        public string LiveResizeStats() { return "ticks=" + _liveTicks + " layoutMs=" + _liveLastCostMs + " intervalMs=" + _liveIntervalMs + " dirty=" + _liveDirty + " segs: suspend=" + _segSuspend + " rail=" + _segRail + " page=" + _segPage + " compose=" + _segCompose + " list=" + _segList + " status=" + _segStatus + " total=" + _segTotal; }
        private bool _headerLineHooked;
        public int TraceViewIndex { get { return _filterIndex; } }
        public string TraceViewName { get { string[] names = new string[] { "全部", "今天", "本周", "过期", "已完成" }; return _filterIndex >= 0 && _filterIndex < names.Length ? names[_filterIndex] : ""; } }   // task-147   // task-137: header 1px line is painted, not a Panel
        protected override bool Resizable { get { return false; } }   // task-135: main window has only two sizes (960x620 / fullscreen)

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsResizing)
            {
                // task-131(adaptive): MouseMove only marks dirty; a single scheduler lays out at a cost-based interval
                _liveDirty = true;
                EnsureLiveTick();
                return;
            }
            try { LayoutAll(); } catch { }
        }

        // ---------- 事件 ----------
        private void WireEvents()
        {
            _edit.ConfirmLeave = delegate { return ConfirmLeaveDialog(); };

            _btnAdd.Click += delegate { if (_draftActive) DraftCommit(); else if (_edit.IsEditing) SaveEdit(); else AddTodo(); };
            _btnCancelEdit.Click += delegate { CancelEdit(); };
            _btnNewEdit.Click += delegate { StartNewFromEdit(); };
            _btnEdit.Click += delegate { LoadSelected(); };
            _btnDone.Click += delegate { ToggleDone(); };
            _btnPin.Click += delegate { TogglePin(); };
            _btnDue.Click += delegate { ShowDueMenuForSelected(); };
            _btnDel.Click += delegate { DeleteTodo(); };
            _btnSettings.Click += delegate { OpenSettings(); };
            _btnAbout.Click += delegate { ShowAbout(); };
            _btnTheme.Click += delegate { ShowThemeMenu(); };
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            _btnMax.Click += delegate { ToggleMaximize(); };
            _btnClose.Click += delegate { Close(); };
            EventHandler drag = delegate { RestoreAndDrag(); };
            MouseEventHandler dragDown = delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { RestoreAndDrag(); } };
            _topStrip.MouseDown += dragDown;
            _topStrip.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleMaximize(); };
            _pageTitle.MouseDown += dragDown;
            _pageTitle.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleMaximize(); };

            _input.Box.Name = "todo.title";
            _input.Box.AccessibleName = "todo.title";
            _input.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    if (ImeHelper.IsComposing(_input.Box)) return;
                    if (_edit.IsEditing) SaveEdit(); else AddTodo();
                    e.SuppressKeyPress = true;
                }
            };
            _input.Box.TextChanged += delegate { if (_loading) return; _titleChanged = true; MarkDirty(); };
            _noteBox.Box.TextChanged += delegate { if (_loading) return; _noteChanged = true; MarkDirty(); };
            _searchBox.Box.TextChanged += delegate { RefreshList(); };

            _impChip.Clicked += delegate { CycleImportance(); };
            _dueChip.Clicked += delegate { if (_edit.IsEditing) ShowDueMenuForSelected(); else ShowDueMenuDraft(); };
            _btnMore.Click += delegate { ShowMoreMenu(); };
            _repeatChip.Clicked += delegate { ShowRepeatMenu(); };
            _remindChip.Clicked += delegate { ShowRemindMenu(); };
            _pinChip.Clicked += delegate
            {
                _pinChip.ToggleChecked();
                _pinChanged = true;
                MarkDirty();
            };

            _list.SelectionChanged += delegate
            {
                UpdateHint();
                if (_edit.IsEditing && _list.SelectedIndex != FindRowIndex(_edit.Id)) DiscardEdit();
            };
            _list.SelectionChanging += delegate(object s, CancelEventArgs e) { if (!EnsureCanLeave()) e.Cancel = true; };
            _list.ItemActivated += delegate { LoadSelected(); };
            _list.CompletionRequested += delegate { ToggleDone(); };
            _list.RowRightClicked += delegate { ShowRowMenu(); };
            _list.EmptyActionRequested += delegate { RunEmptyAction(); };

            Timer sync = new Timer();
            sync.Interval = 2000;
            sync.Tick += delegate { if (Visible) RefreshListCore(false); };
            sync.Start();
        }

        private void RunEmptyAction()
        {
            if (_searchBox != null && _searchBox.Text.Trim().Length > 0)
            {
                _searchBox.Text = "";
                RefreshList();
                _input.Box.Focus();
                return;
            }
            if (_filterIndex != 0) SetFilter(0);
            _input.Box.Focus();
        }

        // ---------- 值令牌 / 默认值 ----------
        private static string[] ImpNames()
        {
            return new string[] { Strings.L("imp.low"), Strings.L("imp.mid"), Strings.L("imp.high") };
        }

        private static string ImpDot(int idx) { return idx == 0 ? "○" : (idx == 2 ? "●" : "◐"); }

        private void UpdateImpChip()
        {
            if (_impChip == null) return;
            _impChip.Label = ImpDot(_impIndex);
            _impChip.Value = ImpNames()[_impIndex];
            _impChip.Invalidate();
        }

        private void CycleImportance()
        {
            _impIndex = (_impIndex + 1) % 3;
            UpdateImpChip();
            UpdateDefaultLine();
            if (_edit.IsEditing) _impChanged = true;
            MarkDirty();
        }

        private void UpdateDefaultLine()
        {
            if (_defaultLine == null) return;
            UpdateImpChip();
            if (_dueChip != null) { _dueChip.Label = "◷"; _dueChip.Value = DueDraftLabel(_dueUtc); _dueChip.Invalidate(); }
            if (_repeatChip != null) { _repeatChip.Label = "↻"; _repeatChip.Value = RepeatChipLabel(_repeatIndex); _repeatChip.Invalidate(); }
            _defaultLine.Text = ImpNames()[_impIndex] + " · " + DueDraftLabel(_dueUtc) + " · " + RepeatChipLabel(_repeatIndex);
            _defaultLine.Invalidate();
        }

        private string RepeatChipLabel(int idx)
        {
            if (idx == 1) return Strings.L("rep.daily");
            if (idx == 2) return Strings.L("rep.weekly");
            if (idx == 3) return Strings.L("rep.monthly");
            return Strings.L("rep.none");
        }

        private static string DueDraftLabel(long? dueUtc)
        {
            if (!dueUtc.HasValue) return Strings.L("due.none");
            return TimeText.Due(TimeUtil.FromUtc(dueUtc.Value), DateTime.Now);
        }

        public static long? DuePresetUtc(int preset, DateTime now, DateTime? customLocal)
        {
            if (preset == 1) return TimeUtil.ToUtc(now.Date.AddHours(23).AddMinutes(59));
            if (preset == 2) return TimeUtil.ToUtc(now.Date.AddDays(1).AddHours(23).AddMinutes(59));
            if (preset == 3) return TimeUtil.ToUtc(Store.WeekEnd(now));
            if (preset == 4) return customLocal.HasValue ? (long?)TimeUtil.ToUtc(customLocal.Value) : null;
            return null;
        }

        private void ShowDueMenuDraft()
        {
            ContextMenu m = new ContextMenu();
            AddDueItem(m, Strings.L("due.menu.today"), 1, false);
            AddDueItem(m, Strings.L("due.menu.tomorrow"), 2, false);
            AddDueItem(m, Strings.L("due.menu.sunday"), 3, false);
            AddDueItem(m, Strings.L("due.menu.custom"), 4, false);
            m.MenuItems.Add("-");
            AddDueItem(m, Strings.L("due.none"), 0, false);
            Control anchor = _dueChip != null ? (Control)_dueChip : _input;
            ThemeMenu.ShowLegacy(anchor, new Point(0, anchor.Height), _theme, m);
        }

        private void ShowDueMenuForSelected()
        {
            ContextMenu m = new ContextMenu();
            AddDueItem(m, Strings.L("due.menu.today"), 1, true);
            AddDueItem(m, Strings.L("due.menu.tomorrow"), 2, true);
            AddDueItem(m, Strings.L("due.menu.sunday"), 3, true);
            AddDueItem(m, Strings.L("due.menu.custom"), 4, true);
            m.MenuItems.Add("-");
            AddDueItem(m, Strings.L("due.none"), 0, true);
            ThemeMenu.ShowLegacy(_list, _list.PointToClient(Cursor.Position), _theme, m);
        }

        private void AddDueItem(ContextMenu m, string text, int preset, bool applyToSelected)
        {
            m.MenuItems.Add(text, delegate
            {
                if (applyToSelected) ApplyDueToSelected(preset);
                else SetDraftDue(preset);
            });
        }

        private void SetDraftDue(int preset)
        {
            if (preset == 4)
            {
                DateTime? cur = _dueUtc.HasValue ? (DateTime?)TimeUtil.FromUtc(_dueUtc.Value) : null;
                DateTime? picked = ShowCustomDueDialog(cur);
                if (!picked.HasValue) return;
                _dueUtc = DuePresetUtc(4, DateTime.Now, picked);
            }
            else
            {
                _dueUtc = DuePresetUtc(preset, DateTime.Now, null);
            }
            if (_edit.IsEditing) { _dueChanged = true; MarkDirty(); }
            UpdateDefaultLine();
        }

        private void ApplyDueToSelected(int preset)
        {
            TodoItem sel = _list.SelectedItem;
            if (sel == null) return;
            long? due;
            if (preset == 4)
            {
                DateTime? cur = sel.DueUtc.HasValue ? (DateTime?)TimeUtil.FromUtc(sel.DueUtc.Value) : null;
                DateTime? picked = ShowCustomDueDialog(cur);
                if (!picked.HasValue) return;
                due = DuePresetUtc(4, DateTime.Now, picked);
            }
            else
            {
                due = DuePresetUtc(preset, DateTime.Now, null);
            }
            TodoItem t = _store.Get(sel.Id);
            if (t == null) return;
            if (_edit.IsEditing && _edit.Id == t.Id)
            {
                _dueUtc = due; _dueChanged = true; MarkDirty(); UpdateDefaultLine(); return;
            }
            t.DueUtc = due;
            _store.Update(t);
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
        }

        private DateTime? ShowCustomDueDialog(DateTime? initial)
        {
            DateTime init = initial.HasValue ? initial.Value : DateTime.Now.Date.AddHours(18);
            using (Form f = new Form())
            {
                f.Text = Strings.L("main.due_menu");
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.AutoScaleMode = AutoScaleMode.None;
                f.Font = Px(FL(Ui.FBody, 15));
                f.BackColor = _theme.Paper;
                f.ClientSize = new Size(S(320), S(170));
                DateTimePicker dpDate = new DateTimePicker();
                dpDate.Format = DateTimePickerFormat.Custom;
                dpDate.CustomFormat = "yyyy-MM-dd";
                dpDate.Value = init.Date;
                dpDate.SetBounds(S(24), S(24), S(272), S(28));
                f.Controls.Add(dpDate);
                DateTimePicker dpTime = new DateTimePicker();
                dpTime.Format = DateTimePickerFormat.Custom;
                dpTime.CustomFormat = "HH:mm";
                dpTime.ShowUpDown = true;
                dpTime.Value = init;
                dpTime.SetBounds(S(24), S(64), S(140), S(28));
                f.Controls.Add(dpTime);
                FlatButton ok = MakeButton(Strings.L("common.ok"), ButtonKind.Primary);
                ok.Dpi = _dpi; ok.Font = Px(FL(Theme.FButton, 12));
                ok.SetBounds(S(120), S(116), S(80), S(32));
                ok.DialogResult = DialogResult.OK;
                f.Controls.Add(ok);
                FlatButton cancel = MakeButton(Strings.L("edit.cancel"), ButtonKind.Secondary);
                cancel.Dpi = _dpi; cancel.Font = Px(FL(Theme.FButton, 12));
                cancel.SetBounds(S(208), S(116), S(80), S(32));
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return null;
                return dpDate.Value.Date.AddHours(dpTime.Value.Hour).AddMinutes(dpTime.Value.Minute);
            }
        }

        private void ShowMoreMenu()
        {
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(Strings.L("field.remind") + "：" + _remindChip.Value, delegate { ShowRemindMenu(); });
            MenuItem pin = m.MenuItems.Add(Strings.L("field.pinned"), delegate
            {
                _pinChip.ToggleChecked();
                _pinChanged = true;
                UpdateDefaultLine();
                MarkDirty();
            });
            pin.Checked = _pinChip.Checked;
            m.MenuItems.Add(Strings.L("field.repeat") + "：" + RepeatChipLabel(_repeatIndex), delegate { ShowRepeatMenu(); });
            m.MenuItems.Add(Strings.L("main.more_detail"), delegate { ToggleNoteField(); });
            m.MenuItems.Add("-");
            m.MenuItems.Add(Strings.L("main.showfloat"), delegate { _floating.Visible = true; _floating.RefreshData(); });
            m.MenuItems.Add(Strings.L("main.import"), delegate { DoImport(); });
            m.MenuItems.Add(Strings.L("main.export"), delegate { DoExport(); });
            ThemeMenu.ShowLegacy(_btnMore, new Point(0, _btnMore.Height), _theme, m);
        }

        private void ToggleNoteField()
        {
            _noteExpanded = !_noteExpanded;
            _noteBox.Visible = _noteExpanded;
            if (_noteExpanded) _noteBox.Box.Focus(); else _input.Box.Focus();
            LayoutAll();
        }

        private void ShowRepeatMenu()
        {
            string[] vals = new string[] { Strings.L("rep.none"), Strings.L("rep.daily"), Strings.L("rep.weekly"), Strings.L("rep.monthly") };
            ContextMenu m = MenuOf(vals, delegate(int i)
            {
                _repeatIndex = i;
                if (_edit.IsEditing) _repeatChanged = true;
                UpdateDefaultLine();
                MarkDirty();
            });
            ThemeMenu.ShowLegacy(_repeatChip, new Point(0, _repeatChip.Height), _theme, m);
        }

        private static string[] RemindValues()
        {
            return new string[] { Strings.L("remind.none"), Strings.L("remind.1h"), Strings.L("remind.today"), Strings.L("remind.tomorrow") };
        }

        private static string RemindChipLabel(int idx)
        {
            return idx == 0 ? Strings.L("remind.none_chip") : RemindValues()[idx];
        }

        private void ShowRemindMenu()
        {
            string[] vals = RemindValues();
            ContextMenu m = MenuOf(vals, delegate(int i)
            {
                _remindIndex = i;
                _remindChip.Value = RemindChipLabel(i);
                _remindChip.Invalidate();
                if (_edit.IsEditing) _remindChanged = true;
                MarkDirty();
            });
            ThemeMenu.ShowLegacy(_btnMore, new Point(0, _btnMore.Height), _theme, m);
        }

        private delegate void IntPick(int i);

        private static ContextMenu MenuOf(string[] items, IntPick pick)
        {
            ContextMenu m = new ContextMenu();
            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                m.MenuItems.Add(items[i], delegate { pick(idx); });
            }
            return m;
        }

        private void SetFilter(int idx)
        {
            if (!EnsureCanLeave()) return;
            if (_edit.IsEditing) DiscardEdit();
            _filterIndex = idx;
            for (int i = 0; i < _nav.Length; i++)
            {
                _nav[i].Selected = i == idx;
                _nav[i].Invalidate();
            }
            RefreshList();
        }

        // ---------- 编辑态 ----------
        private void MarkDirty()
        {
            if (_loading || !_edit.IsEditing) return;
            _edit.MarkDirty();
            RefreshEditBannerText();
        }

        private void ClearChangedFlags()
        {
            _titleChanged = false; _noteChanged = false; _impChanged = false;
            _repeatChanged = false; _remindChanged = false; _pinChanged = false; _dueChanged = false;
        }

        private int FindRowIndex(long id)
        {
            if (_list == null) return -1;
            for (int i = 0; i < _list.Items.Count; i++) if (_list.Items[i].Id == id) return i;
            return -1;
        }

        private void RefreshEditBannerText()
        {
            if (_editBanner == null) return;
            if (!_edit.IsEditing) { _editBanner.Text = ""; return; }
            string title = _input.Text.Trim();
            if (title.Length == 0 && _edit.Snapshot != null) title = _edit.Snapshot.Title;
            _editBanner.Text = Strings.L("edit.banner").Replace("{0}", title)
                + (_edit.Dirty ? "  ·  " + Strings.L("edit.dirty") : "");
            _editBanner.Invalidate();
        }

        private void UpdateEditUi()
        {
            if (_btnAdd == null) return;
            bool editing = _edit.IsEditing;
            _btnAdd.Text = editing ? Strings.L("main.save_edit") : Strings.L("main.add");
            _btnCancelEdit.Visible = editing;
            _btnNewEdit.Visible = editing;
            _btnMore.Visible = !editing;
            _editBanner.Visible = editing;
            if (!editing) { _noteBox.Visible = false; _noteExpanded = false; }
            RefreshEditBannerText();
            if (_list != null)
            {
                int idx = editing ? FindRowIndex(_edit.Id) : -1;
                _list.EditingIndex = idx;
                if (idx >= 0) _list.ScrollToRow(idx);
                _list.Invalidate();
            }
            LayoutAll();
        }

        private bool EnsureCanLeave()
        {
            return _edit.CanLeave();
        }

        private LeaveAction ConfirmLeaveDialog()
        {
            DialogResult r = MessageBox.Show(this, Strings.L("edit.leave_prompt"), Strings.L("app.title"),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
            if (r == DialogResult.Yes) { SaveEdit(); return LeaveAction.Save; }
            if (r == DialogResult.No) return LeaveAction.Discard;
            return LeaveAction.Stay;
        }

        private void SaveEdit()
        {
            if (!_edit.IsEditing) return;
            ApplyToSelected();
            _edit.End();
            _editingId = 0;
            ClearChangedFlags();
            UpdateEditUi();
            MarkFirstRunDone();
            // task-147: after saving an edit the compose must be empty again (next 添加 creates exactly one row)
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; } finally { _loading = false; }
            _noteExpanded = false;
            ResetInputChips();
        }

        private void CancelEdit()
        {
            if (!_edit.IsEditing) return;
            DiscardEdit();
        }

        private void DiscardEdit()
        {
            if (!_edit.IsEditing) return;
            _edit.End();
            _editingId = 0;
            ClearChangedFlags();
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; _noteBox.Visible = false; _noteExpanded = false; ResetInputChips(); }
            finally { _loading = false; }
            UpdateEditUi();
        }

        private void StartNewFromEdit()
        {
            if (!EnsureCanLeave()) return;
            _edit.BeginNew();
            _editingId = 0;
            ClearChangedFlags();
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; _noteBox.Visible = false; _noteExpanded = false; ResetInputChips(); }
            finally { _loading = false; }
            UpdateEditUi();
            _input.Box.Focus();
        }

        private void ShowRowMenu()
        {
            if (_edit.IsEditing && !EnsureCanLeave()) return;
            if (_edit.IsEditing) DiscardEdit();
            ContextMenu m = new ContextMenu();
            m.MenuItems.Add(Strings.L("main.edit_row"), delegate { LoadSelected(); });
            m.MenuItems.Add(Strings.L("main.due_menu"), delegate { ShowDueMenuForSelected(); });
            m.MenuItems.Add(Strings.L("field.pinned"), delegate { TogglePin(); });
            m.MenuItems.Add("-");
            m.MenuItems.Add(Strings.L("main.delete"), delegate { DeleteTodo(); });
            ThemeMenu.ShowLegacy(_list, _list.PointToClient(Cursor.Position), _theme, m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_draftActive)
            {
                if (keyData == Keys.Enter) { DraftCommit(); return true; }
                if (keyData == Keys.Escape && !ImeHelper.IsComposing(_input.Box)) { DraftCancel(); return true; }
                if (keyData == Keys.Tab) { if (_input.Box.Focused) { _noteBox.Box.Focus(); return true; } if (_noteBox.Box.Focused) { _input.Box.Focus(); return true; } }
            }
            if (keyData == (Keys.Control | Keys.K)) { OpenCommandPalette(); return true; }
            if (keyData == Keys.Escape && _edit.IsEditing)
            {
                if (!ImeHelper.IsComposing(_input.Box)) { CancelEdit(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RecalcLayout(bool fitWindow)
        {
            if (_recalcGuard) return;
            if (_brandTitle == null || _input == null || _list == null) return;   // constructor sets ClientSize before controls exist
            _recalcGuard = true;
            try
            {
                float device = _deviceDpi > 0 ? _deviceDpi : 1f;
                Rectangle wa = Placement.NonPrimaryWorkArea();
                float scale = device;
                if (fitWindow)
                {
                    float maxW = Math.Max(0.6f, (wa.Width - 80f) / Ui.DefaultW);
                    float maxH = Math.Max(0.6f, (wa.Height - 80f) / Ui.DefaultH);
                    scale = Math.Min(device, Math.Min(maxW, maxH));
                    if (scale < 0.6f) scale = 0.6f;
                    int cw = (int)Math.Round(Ui.DefaultW * scale);
                    int ch = (int)Math.Round(Ui.DefaultH * scale);
                    int mw = (int)Math.Round(Ui.MinW * scale);
                    int mh = (int)Math.Round(Ui.MinH * scale);
                    MinimumSize = new Size(Ui.MinWindowW, Ui.MinWindowH);   // task-127: derived floor
                    ClientSize = new Size(cw, ch);
                }
                // Window resize does not change the layout scale; LayoutAll() reflows the page
                // into the new client width. Only the DPI/work-area fit changes the scale.
                ApplyDpi(scale);
                LayoutAll();
            }
            finally { _recalcGuard = false; }
        }

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
            _deviceDpi = dpi <= 0 ? 1f : dpi;
            ApplyThemeToAll(_theme);
            RecalcLayout(true);
            RefreshList();
        }

        private bool _placedOnSecondary;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RecalcLayout(true);
            if (!_placedOnSecondary) { Placement.CenterOnNonPrimary(this); _placedOnSecondary = true; }
            SyncTitleBar(_theme.IsDark);
            try { ActiveControl = _input.Box; _input.Box.SelectionStart = 0; _input.Box.SelectionLength = 0; } catch { }
            FadeIn();
        }

        private void FadeIn()
        {
            Opacity = 0.0;
            Timer t = new Timer();
            t.Interval = 12;
            int step = 0;
            t.Tick += delegate
            {
                step++;
                double v = step / 10.0;
                if (v >= 1) { Opacity = 1.0; t.Stop(); t.Dispose(); return; }
                Opacity = v;
            };
            t.Start();
        }

        public void FocusTitle() { _input.Box.Focus(); }

        private void SyncTitleBar(bool dark)
        {
            Win32Theme.ApplyDarkTitleBar(Handle, dark);
        }

        // ---------- 主题 ----------
        private void ApplyThemeToAll(Theme th)
        {
            _theme = th;
            BackColor = th.Paper;
            if (_btnTheme != null) _btnTheme.Text = "主题";
            if (_rail != null) _rail.BackColor = th.Paper;
            if (_logo != null) _logo.BackColor = th.Paper;
            if (_railLine != null) _railLine.BackColor = th.LineStrong;
            if (_topStrip != null) _topStrip.BackColor = th.Paper;
            if (_compose != null) _compose.BackColor = th.Paper;
            if (_status != null) _status.BackColor = th.Paper;
            if (_lineTop != null) _lineTop.BackColor = th.LineStrong;
            if (_lineCompose != null) _lineCompose.BackColor = th.LineStrong;
            if (_lineStatus != null) _lineStatus.BackColor = th.Line;
            if (_brandTitle != null) _brandTitle.ForeColor = th.Text;
            if (_brandDate != null) _brandDate.ForeColor = th.Aux;
            if (_railSection != null) _railSection.ForeColor = th.Aux;
            if (_searchLabel != null) _searchLabel.ForeColor = th.Aux;
            if (_pageTitle != null) _pageTitle.ForeColor = th.Text;
            if (_pageCount != null) _pageCount.ForeColor = th.Aux;
            if (_hint != null) _hint.ForeColor = th.Aux;
            if (_defaultLine != null) _defaultLine.ForeColor = th.Aux;
            if (_editBanner != null) _editBanner.ForeColor = th.Accent;
            FlatField[] fields = new FlatField[] { _input, _noteBox, _searchBox };
            for (int i = 0; i < fields.Length; i++) if (fields[i] != null) fields[i].ApplyTheme(th);
            ThemeChildren(_rail, th);
            ThemeChildren(_topStrip, th);
            ThemeChildren(_compose, th);
            ThemeChildren(_status, th);
            if (_nav != null) for (int i = 0; i < _nav.Length; i++) { if (_nav[i] != null) { _nav[i].Theme = th; _nav[i].Invalidate(); } }
            if (_list != null) { _list.Theme = th; _list.Invalidate(); }
            if (IsHandleCreated) SyncTitleBar(th.IsDark);
            Win32Theme.SetAppMode(th.IsDark);
            Invalidate(true);
        }

        private void ThemeChildren(Control parent, Theme th)
        {
            if (parent == null) return;
            foreach (Control c in parent.Controls)
            {
                FlatButton b = c as FlatButton;
                if (b != null) { b.Theme = th; b.Invalidate(); continue; }
                Label l = c as Label;
                if (l != null) { if (l == _editBanner) l.ForeColor = th.Accent; else if (l == _brandTitle || l == _pageTitle) l.ForeColor = th.Text; else l.ForeColor = th.Aux; continue; }
                FlatChip ch = c as FlatChip;
                if (ch != null) { ch.Theme = th; ch.Invalidate(); continue; }
                FlatField ff = c as FlatField;
                if (ff != null) { ff.ApplyTheme(th); continue; }
            }
        }

        // batch2 #4/#5: one themed popup above the theme button; selects/persists immediately
        private void ShowThemeMenu()
        {
            string cur = _store.GetSetting("theme", "system");
            string[] labels = new string[] { "跟随系统", "v1 暖纸", "v6 暖黑", "v7 暖账本", "v8 石墨" };
            string[] keys = new string[] { "system", "v1", "v6", "v7", "v8" };
            bool[] checks = new bool[keys.Length];
            for (int i = 0; i < keys.Length; i++) checks[i] = keys[i] == cur;
            Action[] acts = new Action[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                acts[i] = delegate
                {
                    _store.SetSetting("theme", k);
                    MarkFirstRunDone();
                    ApplyThemeAnimated();
                };
            }
            ContextMenuStrip menu = ThemeMenu.Build(_theme, labels, checks, acts);
            ThemeMenu.ShowAbove(_btnTheme, menu);
        }
        private void CycleTheme()
        {
            string next = _theme.IsDark ? "day" : "night";
            _store.SetSetting("theme", next);
            MarkFirstRunDone();
            ApplyThemeAnimated();
        }

        private void ApplyThemeAnimated()
        {
            Theme from = Theme.Blend(_theme, _theme, 0.0);
            Theme to = ThemeOf(_store);
            Timer t = new Timer();
            t.Interval = 12;
            int step = 0;
            int total = 10;
            t.Tick += delegate
            {
                step++;
                double v = step / (double)total;
                if (v >= 1)
                {
                    ApplyThemeToAll(to);
                    t.Stop();
                    t.Dispose();
                    try { _floating.ReloadTheme(); } catch { }
                    return;
                }
                ApplyThemeToAll(Theme.Blend(from, to, v));
            };
            t.Start();
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(_store, _floating))
            {
                f.ShowDialog(this);
            }
            ApplyThemeAnimated();
            RefreshList();
            ApplyDpi(_dpi);
            LayoutAll();
        }

        // ---------- 首启提示 ----------
        private void SetupFirstRunHint()
        {
            if (_store.GetSetting("firstrun", "0") == "1") return;
            if (_store.GetSetting("firstrun_seeded", "0") != "1")
            {
                _firstRunVisible = false;
                try { _store.SetSetting("firstrun", "1"); } catch { }
                UpdateHint();
                return;
            }
            _firstRunUntil = DateTime.Now.AddSeconds(15);
            _firstRunVisible = true;
            _firstRunTimer = new Timer();
            _firstRunTimer.Interval = 1000;
            _firstRunTimer.Tick += delegate
            {
                if (!_firstRunVisible) { _firstRunTimer.Stop(); return; }
                if (DateTime.Now >= _firstRunUntil)
                {
                    _firstRunVisible = false;
                    _firstRunTimer.Stop();
                    MarkFirstRunDone();
                    UpdateHint();
                }
            };
            _firstRunTimer.Start();
            UpdateHint();
        }

        private void MarkFirstRunDone()
        {
            if (!_firstRunVisible && _store.GetSetting("firstrun", "0") == "1") return;
            _firstRunVisible = false;
            try { _store.SetSetting("firstrun", "1"); } catch { }
            UpdateHint();
        }

        // ---------- 列表 ----------
        public void RefreshList() { RefreshListCore(true); }

        private void RefreshListCore(bool updateHint)
        {
            long selId = 0;
            TodoItem sel = _list.SelectedItem;
            if (sel != null) selId = sel.Id;
            List<TodoItem> rows = BuildRows();
            bool growthAll = _store.GetSetting("growth_all", "0") == "1";
            bool growthOn = _store.GetSetting("growth_on", "1") == "1";
            string q = _searchBox == null ? "" : _searchBox.Text.Trim();
            if (q.Length > 0)
            {
                _list.EmptyText = Strings.L("main.empty.search").Replace("{0}", q);
                _list.EmptyAction = Strings.L("empty.action.clear");
            }
            else if (_filterIndex == 4)
            {
                _list.EmptyText = Strings.L("main.empty.done");
                _list.EmptyAction = Strings.L("empty.action.all");
            }
            else if (_filterIndex == 3)
            {
                _list.EmptyText = Strings.L("main.empty.overdue");
                _list.EmptyAction = Strings.L("empty.action.all");
            }
            else
            {
                _list.EmptyText = Strings.L("main.empty.active");
                _list.EmptyAction = Strings.L("empty.action");
            }
            _list.ShowSections = (_filterIndex != 4 && q.Length == 0);
            _rows = rows;
            _list.SetItems(rows, growthAll, growthOn);
            if (_draftRow != null)
            {
                _list.Items.Insert(0, _draftRow);
                _list.EditingIndex = 0;
                _list.Rebuild();
            }
            if (selId != 0)
            {
                for (int i = 0; i < rows.Count; i++) if (rows[i].Id == selId) { _list.SelectedIndex = i; break; }
            }
            UpdatePageTitle(q, rows.Count);
            UpdateFooterActions();
            UpdateCount();
            if (updateHint) UpdateHint();
        }

        private void UpdatePageTitle(string q, int count)
        {
            if (_pageTitle == null) return;
            string[] names = new string[] {
                Strings.L("main.filter.all"), Strings.L("main.filter.today"), Strings.L("main.filter.week"),
                Strings.L("main.filter.overdue"), Strings.L("main.filter.done") };
            if (q.Length > 0)
            {
                _pageTitle.Text = Strings.L("main.search_result").Replace("{0}", q).Replace("{1}", count.ToString());
            }
            else
            {
                _pageTitle.Text = names[_filterIndex];
            }
            _pageCount.Text = count + " 条";
        }

        private void UpdateCount()
        {
            if (_brandDate == null || _nav == null) return;
            DateTime now = DateTime.Now;
            DateTime todayEnd = now.Date.AddDays(1);
            DateTime weekEnd = Store.WeekEnd(now);
            int all = 0, today = 0, week = 0, overdue = 0;
            foreach (TodoItem t in _store.Active())
            {
                all++;
                if (!t.DueUtc.HasValue) continue;
                DateTime d = t.DueLocal.Value;
                if (d < now) overdue++;
                if (d < todayEnd) today++;
                if (d <= weekEnd) week++;
            }
            int done = _store.Completed(300).Count;
            _nav[0].Count = all; _nav[1].Count = today; _nav[2].Count = week; _nav[3].Count = overdue; _nav[4].Count = done;
            for (int i = 0; i < _nav.Length; i++) _nav[i].Invalidate();
            _brandDate.Text = now.ToString("MM-dd") + "  " + TimeText.Weekday(now);
            LayoutAll();
        }

        private void UpdateHint()
        {
            if (_hint == null) return;
            TodoItem sel = _list == null ? null : _list.SelectedItem;
            if (_firstRunVisible) _hint.Text = Strings.L("main.firstrun");
            else if (sel == null) _hint.Text = string.Format(Strings.L("main.status"), _list == null ? 0 : _list.Items.Count, _nav == null ? 0 : _nav[1].Count);
            else _hint.Text = string.Format(Strings.L("main.selected"), Short(sel.Title, 22));
            UpdateFooterActions();
            LayoutAll();
        }

        private static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        private void UpdateFooterActions()
        {
            if (_btnDone == null) return;
            TodoItem sel = _list == null ? null : _list.SelectedItem;
            bool has = sel != null;
            _btnEdit.Visible = has;
            _btnDone.Visible = has;
            _btnPin.Visible = has;
            _btnDue.Visible = has;
            _btnDel.Visible = has;
            if (has)
            {
                _btnDone.Text = sel.Done ? Strings.L("main.restore") : Strings.L("main.done");
                _btnPin.Text = Strings.L("main.pin");
                _hint.Text = string.Format(Strings.L("main.selected"), Short(sel.Title, 22));
            }
        }

        private List<TodoItem> BuildRows()
        {
            List<TodoItem> rows = new List<TodoItem>();
            if (_filterIndex == 4)
            {
                rows = _store.Completed(300);
            }
            else
            {
                DateTime now = DateTime.Now;
                DateTime todayEnd = now.Date.AddDays(1);
                DateTime weekEnd = Store.WeekEnd(now);
                foreach (TodoItem t in _store.Active())
                {
                    if (_filterIndex == 1)
                    {
                        if (!t.DueUtc.HasValue) continue;
                        DateTime d = t.DueLocal.Value;
                        if (d < now.Date || d >= todayEnd) continue;
                    }
                    else if (_filterIndex == 2)
                    {
                        if (!t.DueUtc.HasValue) continue;
                        if (t.DueLocal.Value > weekEnd) continue;
                    }
                    else if (_filterIndex == 3)
                    {
                        if (!t.DueUtc.HasValue) continue;
                        if (t.DueLocal.Value >= now) continue;
                    }
                    rows.Add(t);
                }
            }
            string q = _searchBox == null ? "" : _searchBox.Text.Trim();
            if (q.Length > 0)
            {
                List<TodoItem> filtered = new List<TodoItem>();
                foreach (TodoItem t in rows)
                {
                    if (t.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) { filtered.Add(t); continue; }
                    if (!string.IsNullOrEmpty(t.Note) && t.Note.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(t);
                }
                rows = filtered;
            }
            return rows;
        }

        // ---------- 增删改 ----------
        private void AddTodo()
        {
            string title = _input.Text.Trim();
            if (title.Length == 0) return;
            TodoItem t = new TodoItem();
            t.Title = title;
            t.Note = _noteBox.Text.Trim();
            t.Importance = _impIndex + 1;
            t.Pinned = _pinChip.Checked;
            long? rem = RemindFromPreset(_remindIndex);
            if (rem.HasValue) t.RemindUtc = rem.Value;
            if (_dueUtc.HasValue) t.DueUtc = _dueUtc.Value;
            t.Recurrence = RepeatValue(_repeatIndex);
            if (t.IsRecurring) t.RecurrenceNextUtc = Recurrence.NextUtc(t.Recurrence, DateTime.Now);
            _store.Add(t);
            _loading = true;
            try { _input.Text = ""; _noteBox.Text = ""; } finally { _loading = false; }
            _edit.BeginNew();
            _editingId = 0;
            ClearChangedFlags();
            ResetInputChips();
            UpdateEditUi();
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
        }

        private void ResetInputChips()
        {
            _impIndex = 1;
            _repeatIndex = 0;
            _remindIndex = 0;
            _remindChip.Value = Strings.L("remind.none_chip");
            _pinChip.Checked = false;
            _dueUtc = null; _dueChanged = false;
            UpdateDefaultLine();
            _impChip.Invalidate(); _repeatChip.Invalidate(); _remindChip.Invalidate(); _pinChip.Invalidate();
        }

        private static string RepeatValue(int idx)
        {
            if (idx == 1) return "daily";
            if (idx == 2) return "weekly";
            if (idx == 3) return "monthly";
            return "";
        }

        private static long? RemindFromPreset(int idx)
        {
            DateTime now = DateTime.Now;
            if (idx == 1) return TimeUtil.ToUtc(now.AddHours(1));
            if (idx == 2)
            {
                DateTime t = now.Date.AddHours(21);
                if (t <= now) t = t.AddDays(1);
                return TimeUtil.ToUtc(t);
            }
            if (idx == 3) return TimeUtil.ToUtc(now.Date.AddDays(1).AddHours(9));
            return null;
        }

        private void ApplyToSelected()
        {
            if (!_edit.IsEditing) return;
            TodoItem t = _store.Get(_edit.Id);
            if (t == null) { _edit.End(); _editingId = 0; UpdateEditUi(); return; }
            if (_titleChanged)
            {
                string title = _input.Text.Trim();
                if (title.Length > 0) t.Title = title;
            }
            if (_noteChanged) t.Note = _noteBox.Text.Trim();
            if (_impChanged) t.Importance = _impIndex + 1;
            if (_pinChanged) t.Pinned = _pinChip.Checked;
            if (_repeatChanged)
            {
                t.Recurrence = RepeatValue(_repeatIndex);
                t.RecurrenceNextUtc = t.IsRecurring ? Recurrence.NextUtc(t.Recurrence, DateTime.Now) : null;
            }
            if (_remindChanged) t.RemindUtc = RemindFromPreset(_remindIndex);
            if (_dueChanged) t.DueUtc = _dueUtc;
            _store.Update(t);
            ClearChangedFlags();
            _edit.Dirty = false;
            RefreshList();
            RefreshEditBannerText();
            try { _floating.RefreshData(); } catch { }
        }

        private void LoadSelected()
        {
            if (_edit.IsEditing && !EnsureCanLeave()) return;
            TodoItem t = _list.SelectedItem;
            if (t == null) { UpdateHint(); return; }
            _loading = true;
            try
            {
                _input.Text = t.Title;
                _noteBox.Text = t.Note == null ? "" : t.Note;
                _impIndex = Math.Max(0, Math.Min(2, t.Importance - 1));
                _pinChip.Checked = t.Pinned;
                int rIdx = 0;
                if (t.Recurrence == "daily") rIdx = 1;
                else if (t.Recurrence == "weekly") rIdx = 2;
                else if (t.Recurrence == "monthly") rIdx = 3;
                _repeatIndex = rIdx;
                _remindIndex = 0;
                _remindChip.Value = t.RemindUtc.HasValue ? TimeText.Remind(TimeUtil.FromUtc(t.RemindUtc.Value), DateTime.Now) : Strings.L("remind.none_chip");
                _dueUtc = t.DueUtc;
                _dueChanged = false;
                _edit.BeginEdit(t);
                _editingId = t.Id;
                ClearChangedFlags();
                UpdateDefaultLine();
                _impChip.Invalidate(); _repeatChip.Invalidate(); _remindChip.Invalidate(); _pinChip.Invalidate();
            }
            finally { _loading = false; }
            UpdateEditUi();
            LayoutAll();
            _input.Box.Focus();
            _input.Box.SelectionStart = _input.Text.Length;
        }

        private void ToggleDone()
        {
            if (_edit.IsEditing && !EnsureCanLeave()) return;
            TodoItem t = _list.SelectedItem;
            if (t == null) return;
            if (t.Done)
            {
                if (!_store.Restore(t.Id)) return;
            }
            else
            {
                bool spawned;
                _store.Complete(t.Id, out spawned);
            }
            if (_edit.IsEditing) { _edit.End(); _editingId = 0; UpdateEditUi(); }
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
        }

        private void TogglePin()
        {
            TodoItem t = _list.SelectedItem;
            if (t == null) return;
            if (_edit.IsEditing && _edit.Id == t.Id)
            {
                t.Pinned = !t.Pinned;
                _pinChip.Checked = t.Pinned;
                _pinChanged = true;
                MarkDirty();
                UpdateDefaultLine();
                return;
            }
            t.Pinned = !t.Pinned;
            _store.Update(t);
            if (_editingId == t.Id)
            {
                _loading = true;
                try { _pinChip.Checked = t.Pinned; } finally { _loading = false; }
            }
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
        }

        private void DeleteTodo()
        {
            TodoItem t = _list.SelectedItem;
            if (t == null) return;
            if (_edit.IsEditing && !EnsureCanLeave()) return;
            string confirm = Strings.L("msg.confirm_delete").Replace("{0}", t.Title);
            if (MessageBox.Show(this, confirm, Strings.L("app.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _store.SoftDelete(t.Id);
            if (_edit.IsEditing && _edit.Id == t.Id) { _edit.End(); _editingId = 0; ClearChangedFlags(); UpdateEditUi(); }
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
        }

        // ---- two-step delete surface (debug API): begin -> confirming, confirm -> soft delete, cancel ----
        private long _deleteId;
        public long ConfirmingId { get { return _deleteId; } }

        public bool TestBeginDelete(long id)
        {
            TodoItem t = _store.Get(id);
            if (t == null) return false;
            _deleteId = id;
            return true;
        }

        public bool TestConfirmDelete()
        {
            if (_deleteId == 0) return false;
            long id = _deleteId;
            _deleteId = 0;
            TodoItem t = _store.Get(id);
            if (t == null) return false;
            _store.SoftDelete(id);
            if (_edit.IsEditing && _edit.Id == id) { _edit.End(); _editingId = 0; ClearChangedFlags(); UpdateEditUi(); }
            MarkFirstRunDone();
            RefreshList();
            try { _floating.RefreshData(); } catch { }
            return true;
        }

        public void TestCancelDelete() { _deleteId = 0; }

        private void DoExport()
        {
            if (MessageBox.Show(this, Strings.L("main.export_warn"), Strings.L("app.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Decanter (*.decanter)|*.decanter|JSON (*.json)|*.json";
                d.FileName = "todos-" + DateTime.Now.ToString("yyyyMMdd") + ".decanter";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Journal.Export(_store, d.FileName);
                    MessageBox.Show(this, string.Format(Strings.L("main.exported"), d.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, Strings.L("main.export_failed") + ex.Message); }
            }
        }

        private void DoImport()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Decanter (*.decanter;*.json)|*.decanter;*.json|All files (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int skipped;
                    int count = Journal.Import(_store, d.FileName, out skipped);
                    RefreshList();
                    try { _floating.RefreshData(); } catch { }
                    MessageBox.Show(this, string.Format(Strings.L("main.imported"), count, skipped));
                }
                catch (Exception ex) { MessageBox.Show(this, Strings.L("main.import_failed") + ex.Message); }
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this, Strings.L("app.title") + " v0.11\r\n\r\n" +
                Strings.L("about.license") + "\r\n" +
                Strings.L("about.unsigned") + "\r\n" +
                Strings.L("about.data") + Paths.DataDir() + "\r\n" +
                Strings.L("about.disclaimer"),
                Strings.L("main.about"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!EnsureCanLeave()) { e.Cancel = true; return; }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    MessageBox.Show(this, Strings.Lang == "en"
                        ? "Decanter keeps running in the tray. Use tray menu -> Exit to quit."
                        : "醒酒器已缩到系统托盘继续运行。要退出请用托盘菜单 → 退出。");
                }
                return;
            }
            base.OnFormClosing(e);
        }

        // ---------- 供自检 ----------
        public void TestSetClientSize(int w, int h) { ClientSize = new Size(w, h); LayoutAll(); }

        public Dictionary<string, string> LayoutSnapshot()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["client"] = ClientSize.Width + "x" + ClientSize.Height;
            d["rail"] = Rect(_rail);
            d["railW"] = _rail == null ? "0" : _rail.Width.ToString();
            d["topStrip"] = Rect(_topStrip);
            d["compose"] = Rect(_compose);
            d["list"] = Rect(_list);
            d["status"] = Rect(_status);
            d["btnAdd"] = Rect(_btnAdd);
            d["btnMore"] = Rect(_btnMore);
            d["searchBox"] = Rect(_searchBox);
            d["pageTitle"] = Rect(_pageTitle);
            d["pageTitleText"] = _pageTitle == null ? "" : _pageTitle.Text;
            d["pageCount"] = _pageCount == null ? "" : _pageCount.Text;
            d["navAll"] = Rect(_nav == null ? null : _nav[0]);
            d["navToday"] = Rect(_nav == null ? null : _nav[1]);
            d["navWeek"] = Rect(_nav == null ? null : _nav[2]);
            d["navOverdue"] = Rect(_nav == null ? null : _nav[3]);
            d["navDone"] = Rect(_nav == null ? null : _nav[4]);
            d["navCountAll"] = _nav == null ? "" : _nav[0].Count.ToString();
            d["navCountToday"] = _nav == null ? "" : _nav[1].Count.ToString();
            d["defaultLine"] = Rect(_defaultLine);
            d["defaultText"] = _defaultLine == null ? "" : _defaultLine.Text;
            d["chipImpText"] = _impChip == null ? "" : _impChip.Value;
            d["btnDone"] = Rect(_btnDone);
            d["btnDoneText"] = _btnDone == null ? "" : _btnDone.Text;
            d["footerActionsVisible"] = _btnDone == null ? "0" : (_btnDone.Visible ? "1" : "0");
            d["btnCancelEdit"] = Rect(_btnCancelEdit);
            d["btnNewEdit"] = Rect(_btnNewEdit);
            d["editBanner"] = Rect(_editBanner);
            d["editBannerText"] = _editBanner == null ? "" : _editBanner.Text;
            d["editing"] = _edit.IsEditing ? "1" : "0";
            d["dirty"] = _edit.Dirty ? "1" : "0";
            d["editingId"] = _edit.Id.ToString();
            d["editingIndex"] = _list == null ? "-1" : _list.EditingIndexForTest().ToString();
            d["noteBox"] = Rect(_noteBox);
            d["chipImp"] = Rect(_impChip);
            d["chipDue"] = Rect(_dueChip);
            d["chipRepeat"] = Rect(_repeatChip);
            d["chipRemind"] = Rect(_remindChip);
            d["chipPin"] = Rect(_pinChip);
            d["listRows"] = _list == null ? "0" : _list.Items.Count.ToString();
            d["listSections"] = _list == null ? "0" : _list.SectionCountForTest().ToString();
            d["statusBottom"] = _status == null ? "" : (_status.Top + _status.Height).ToString();
            d["hint"] = _hint == null ? "" : _hint.Text;
            d["brandDate"] = _brandDate == null ? "" : _brandDate.Text;
            d["btnTheme"] = Rect(_btnTheme);
            d["btnEdit"] = Rect(_btnEdit);
            d["btnDue"] = Rect(_btnDue);
            return d;
        }

        private static string Rect(Control c)
        {
            if (c == null) return "";
            return c.Left + "," + c.Top + "," + c.Width + "," + c.Height;
        }

        public void TestReload() { RefreshList(); }
        public TodoListView ListView { get { return _list; } }
        public void TestCycleTheme() { CycleTheme(); }
        public void TestCycleThemeSync()
        {
            string next = _theme.IsDark ? "day" : "night";
            _store.SetSetting("theme", next);
            ApplyThemeToAll(ThemeOf(_store));
        }
        public string TestThemeSetting() { return _store.GetSetting("theme", "day"); }
        public void TestApplyThemeSetting() { ApplyThemeToAll(ThemeOf(_store)); }

        public bool TestSetTheme(string key)
        {
            if (key == null) return false;
            string k = key.Trim().ToLowerInvariant();
            if (k != "system" && k != "day" && k != "night" && k != "v1" && k != "v6" && k != "v7" && k != "v8") return false;
            _store.SetSetting("theme", k);
            ApplyThemeToAll(ThemeOf(_store));
            try { _floating.ReloadTheme(); } catch { }
            return true;
        }

        public void OpenCommandPalette()
        {
            if (_palette == null || _palette.IsDisposed) _palette = new CommandPalette(this);
            _palette.OpenPalette();
        }

        public void CmdSetFilter(int idx) { SetFilter(idx); }
        public void CmdFocusCompose() { try { _input.Box.Focus(); } catch { } }
        public void CmdFocusSearch() { try { _searchBox.Box.Focus(); } catch { } }
        public void CmdEditSelected() { if (_list != null && _list.SelectedIndex >= 0) LoadSelected(); }
        public void CmdToggleDone() { if (_list != null && _list.SelectedIndex >= 0) ToggleDone(); }
        public string CmdStatus = "";
        public long SelectedId { get { TodoItem it = _list == null ? null : _list.SelectedItem; return it == null ? 0 : it.Id; } }
        public bool HasSelection { get { TodoItem it = _list == null ? null : _list.SelectedItem; return it != null; } }
        public void CmdDeleteSelected()
        {
            TodoItem it = _list == null ? null : _list.SelectedItem;
            if (it == null) { CmdStatus = "no-selection"; return; }   // never a silent no-op
            CmdStatus = TestBeginDelete(it.Id) ? "began-confirm" : "begin-failed";
        }
        public void TestOpenThemeMenu() { ShowThemeMenu(); }
        public void CmdCycleTheme() { CycleTheme(); }
        public void CmdOpenSettings() { OpenSettings(); }
        public void CmdShowFloating() { _floating.Show(); _floating.RefreshData(); }
        public void CmdQuit() { Application.Exit(); }
        public void TestSelectIndex(int i) { _list.SelectedIndex = i; }
        public string TestNoteBox() { return _noteBox.Text; }

        // 真实输入路径：写标题 -> 添加 -> 搜索（自检用，不弹窗）
        public void TestAddTodoFromInput(string title)
        {
            _loading = true;
            try { _input.Text = title; } finally { _loading = false; }
            AddTodo();
        }
        public void TestSearch(string q)
        {
            _loading = true;
            try { _searchBox.Text = q; } finally { _loading = false; }
            RefreshList();
        }
        public int TestVisibleRows() { return _list == null ? 0 : _list.Items.Count; }

        public bool TestIsEditing() { return _edit.IsEditing; }
        public bool TestIsDirty() { return _edit.Dirty; }
        public long TestEditingId() { return _edit.Id; }
        public string TestEditBannerText() { return _editBanner == null ? "" : _editBanner.Text; }
        public string TestAddButtonText() { return _btnAdd == null ? "" : _btnAdd.Text; }
        public void TestBeginEditById(long id)
        {
            int idx = FindRowIndex(id);
            if (idx >= 0) { _list.SelectedIndex = idx; LoadSelected(); }
        }
        public void TestSetTitleText(string s)
        {
            _loading = true;
            try { _input.Text = s; } finally { _loading = false; }
            _titleChanged = true;
            MarkDirty();
        }
        public void TestSetImpIndex(int idx)
        {
            _impIndex = Math.Max(0, Math.Min(2, idx));
            UpdateImpChip();
            UpdateDefaultLine();
            if (_edit.IsEditing) _impChanged = true;
            MarkDirty();
        }
        public void TestSaveEdit() { SaveEdit(); }
        public void TestCancelEdit() { CancelEdit(); }
        public void TestToggleNote() { ToggleNoteField(); }

        public void TestSetFilter(int idx) { SetFilter(idx); }
        public bool TestSelectById(long id) { int idx = FindRowIndex(id); if (idx < 0) return false; _list.SelectedIndex = idx; return true; }
        public void TestToggleDone() { ToggleDone(); }
        public void TestApplyDuePreset(int preset) { ApplyDueToSelected(preset); }

        public float TestDpi() { return _dpi; }
        public void TestForceDpi(float dpi)
        {
            ApplyDpi(dpi);
            LayoutAll();
        }

        public void TestApplyFontScale(string v)
        {
            _store.SetSetting("font_scale", v);
            ApplyDpi(_dpi);
            LayoutAll();
            RefreshList();
        }

        public string TestFontInfo()
        {
            float ls = 0f;
            if (_list != null) ls = _list.FontScale;
            return "scale=" + _fontScale.ToString("0.##")
                + ";title=" + FL(Ui.FTitle, 15)
                + ";body=" + FL(Ui.FBody, 15)
                + ";meta=" + FL(Ui.FMeta, 12)
                + ";aux=" + FL(Ui.FAux, 12)
                + ";page=" + FL(Ui.FPageTitle, 15)
                + ";listScale=" + ls.ToString("0.##");
        }

        public int TestFontPx(string which)
        {
            if (which == "title") return FL(Ui.FTitle, 15);
            if (which == "body") return FL(Ui.FBody, 15);
            if (which == "meta") return FL(Ui.FMeta, 12);
            if (which == "aux") return FL(Ui.FAux, 12);
            return FL(Ui.FPageTitle, 15);
        }
    }
}