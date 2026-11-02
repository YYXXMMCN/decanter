// FrameForm.cs — v5 自绘窗框基类：无边框 + 可拖动 + 八向可缩放 + 最大化贴合工作区 + CS_DROPSHADOW
// 目的：系统标题栏在 Win11 上会把整个深色账本拉回 WinForms 默认白条；改为自绘后窗框与内容同一套材质。
// 保留系统行为：任务栏按钮、Alt+F4、Win+方向键（HTCAPTION）、双击标题栏最大化/还原、最小/最大/关闭按钮。
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Decanter.App
{
/// <summary>子控件铺满客户区时会把 WM_NCHITTEST 吃掉（尤其是底部状态栏），
    /// 这里在消息过滤层统一换算成窗口边缘命中，保证四边四角可拖。</summary>
    internal sealed class ResizeHitFilter : IMessageFilter
    {
        private readonly FrameForm _owner;
        public ResizeHitFilter(FrameForm owner) { _owner = owner; }
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x0084) return false;   // WM_NCHITTEST
            try
            {
                if (_owner == null || _owner.IsDisposed || !_owner.IsHandleCreated) return false;
                long lp = (long)m.LParam;
                Point screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                int hit = _owner.ScreenEdgeHit(screen);
                if (hit == 0) return false;      // HTCLIENT -> let the child handle it
                m.Result = (IntPtr)hit;
                return true;
            }
            catch { return false; }
        }
    }

    public class FrameForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_NCLBUTTONDOWN = 0x00A1;

        private Panel[] _resizeGrips;
        private const int GripW = 6;
        private int _resizeCode;
        private Point _resizeStart;
        private RECT _resizeStartRect;
        private bool _resizing;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int CS_DROPSHADOW = 0x00020000;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        public FrameForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
        }

        // 子控件铺满客户区时，窗体的 WM_NCHITTEST 边缘会被子控件吃掉；
        // 在边缘放透明抓手，mousedown 时把命中码交回 DefWindowProc 进入系统缩放循环。
        private sealed class GripPanel : Panel
        {
            public GripPanel()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                TabStop = false;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (Resizable) InstallResizeGrips();
        }

        private void InstallResizeGrips()
        {
            if (_resizeGrips != null) return;
            int[] codes = new int[] { HTLEFT, HTRIGHT, HTTOP, HTBOTTOM, HTTOPLEFT, HTTOPRIGHT, HTBOTTOMLEFT, HTBOTTOMRIGHT };
            Cursor[] cursors = new Cursor[] { Cursors.SizeWE, Cursors.SizeWE, Cursors.SizeNS, Cursors.SizeNS,
                Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeNWSE };
            _resizeGrips = new Panel[codes.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                GripPanel g = new GripPanel();
                g.Tag = codes[i];
                g.Cursor = cursors[i];
                g.MouseDown += delegate(object sender, MouseEventArgs me)
                {
                    if (me.Button != MouseButtons.Left) return;
                    Panel p = sender as Panel;
                    if (p == null) return;
                    _resizeCode = (int)p.Tag;
                    _resizeStart = Cursor.Position;
                    GetWindowRect(Handle, out _resizeStartRect);
                    _resizing = true;
                    p.Capture = true;
                };
                g.MouseMove += delegate(object sender, MouseEventArgs me)
                {
                    if (!_resizing) return;
                    ResizeFromMouse(Cursor.Position);
                };
                g.MouseUp += delegate(object sender, MouseEventArgs me)
                {
                    _resizing = false;
            try { if (ResizeEnded != null) ResizeEnded(this, EventArgs.Empty); } catch { }   // task-130
                    Panel p = sender as Panel;
                    if (p != null) p.Capture = false;
                };
                Controls.Add(g);
                _resizeGrips[i] = g;
            }
            LayoutResizeGrips();
        }

        private void ResizeFromMouse(Point screen)
        {
            if (!_resizing) return;
            int dx = screen.X - _resizeStart.X;
            int dy = screen.Y - _resizeStart.Y;
            int x = _resizeStartRect.Left, y = _resizeStartRect.Top;
            int w = _resizeStartRect.Right - _resizeStartRect.Left;
            int h = _resizeStartRect.Bottom - _resizeStartRect.Top;
            int minW = Math.Max(480, MinimumSize.Width);
            int minH = Math.Max(320, MinimumSize.Height);
            if (_resizeCode == HTLEFT || _resizeCode == HTTOPLEFT || _resizeCode == HTBOTTOMLEFT)
            {
                x += dx; w -= dx;
                if (w < minW) { x = _resizeStartRect.Right - minW; w = minW; }
            }
            if (_resizeCode == HTRIGHT || _resizeCode == HTTOPRIGHT || _resizeCode == HTBOTTOMRIGHT)
            {
                w += dx;
                if (w < minW) w = minW;
            }
            if (_resizeCode == HTTOP || _resizeCode == HTTOPLEFT || _resizeCode == HTTOPRIGHT)
            {
                y += dy; h -= dy;
                if (h < minH) { y = _resizeStartRect.Bottom - minH; h = minH; }
            }
            if (_resizeCode == HTBOTTOM || _resizeCode == HTBOTTOMLEFT || _resizeCode == HTBOTTOMRIGHT)
            {
                h += dy;
                if (h < minH) h = minH;
            }
            // task-131: drag only changes bounds; MainForm owns the (throttled) layout
            Bounds = new Rectangle(x, y, w, h);
            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0200 | 0x0400); // INVALIDATE|ERASE|ALLCHILDREN|UPDATENOW|ERASENOW|FRAME
            Refresh();
            Update();
        }

        // task-129: programmatic drag-resize entry (same code path as real grip drag), for frame-by-frame capture
        public void TestResizeBegin(int code)
        {
            _resizeCode = code;
            _resizeStart = Cursor.Position;
            GetWindowRect(Handle, out _resizeStartRect);
            _resizing = true;
        }
        public void TestResizeTo(int totalDx, int totalDy)
        {
            ResizeFromMouse(new Point(_resizeStart.X + totalDx, _resizeStart.Y + totalDy));
        }
        public void TestResizeEnd() { _resizing = false; try { if (ResizeEnded != null) ResizeEnded(this, EventArgs.Empty); } catch { } }

        private void LayoutResizeGrips()
        {
            if (_resizeGrips == null) return;
            if (WindowState == FormWindowState.Maximized) { for (int gi = 0; gi < _resizeGrips.Length; gi++) _resizeGrips[gi].Visible = false; return; }   // task-127: hide grips while maximized
            for (int gi2 = 0; gi2 < _resizeGrips.Length; gi2++) _resizeGrips[gi2].Visible = true;
            int w = ClientSize.Width, h = ClientSize.Height, e = GripW;
            if (w <= e * 2 || h <= e * 2) return;
            // edges
            _resizeGrips[0].Bounds = new Rectangle(0, e, e, h - e * 2);
            _resizeGrips[1].Bounds = new Rectangle(w - e, e, e, h - e * 2);
            _resizeGrips[2].Bounds = new Rectangle(e, 0, w - e * 2, e);
            _resizeGrips[3].Bounds = new Rectangle(e, h - e, w - e * 2, e);
            // corners
            _resizeGrips[4].Bounds = new Rectangle(0, 0, e, e);
            _resizeGrips[5].Bounds = new Rectangle(w - e, 0, e, e);
            _resizeGrips[6].Bounds = new Rectangle(0, h - e, e, e);
            _resizeGrips[7].Bounds = new Rectangle(w - e, h - e, e, e);
            for (int i = 0; i < _resizeGrips.Length; i++) _resizeGrips[i].BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutResizeGrips();
        }

        // 固定尺寸窗体（设置页）覆盖为 false：不加可缩放边、不响应边缘命中
        protected virtual bool Resizable { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Pure popup: no caption/thickframe/min/max bits, so DWM has no non-client
                // frame or title to paint when the taskbar activates the window.
                cp.Style &= ~(WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
                cp.Style |= unchecked((int)0x80000000);          // WS_POPUP
                cp.ExStyle |= 0x00040000;        // WS_EX_APPWINDOW (keep taskbar button)
                cp.ClassStyle &= ~CS_DROPSHADOW;
                return cp;
            }
        }

        public void DragMove()
        {
            try { ReleaseCapture(); SendMessage(Handle, 0x00A1, HTCAPTION, 0); } catch { }
        }

        /// <summary>Maximized windows: restore to normal and continue moving under the cursor (browser behavior).</summary>
        public void RestoreAndDrag()
        {
            if (WindowState != FormWindowState.Maximized) { DragMove(); return; }
            Rectangle mb = Bounds;
            Rectangle rb = RestoreBounds;
            if (rb.Width < 200 || rb.Height < 150) rb = new Rectangle(mb.Left + 40, mb.Top + 40, 1120, 720);
            Point mp = Cursor.Position;
            double rx = mb.Width > 0 ? (mp.X - mb.Left) / (double)mb.Width : 0.5;
            double ry = mb.Height > 0 ? (mp.Y - mb.Top) / (double)mb.Height : 0.08;
            if (rx < 0) rx = 0; if (rx > 1) rx = 1;
            if (ry < 0) ry = 0; if (ry > 1) ry = 1;
            WindowState = FormWindowState.Normal;
            Bounds = rb;
            Location = new Point(mp.X - (int)Math.Round(rb.Width * rx), mp.Y - (int)Math.Round(rb.Height * ry));
            DragMove();
        }

        public void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private ResizeHitFilter _hitFilter;

        /// <summary>0=HTCLIENT else the HT* code for this physical screen point.</summary>
        public bool IsResizing { get { return _resizing; } }   // task-130

        /// <summary>task-132: forced repaint only on resize end (not every drag tick).</summary>
        public void ForceRedraw()
        {
            try { RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0200 | 0x0400); } catch { }
            Invalidate(true);
            Update();
        }
        public event EventHandler ResizeEnded;   // task-130: fired once after the drag ends

        public int ScreenEdgeHit(Point screen)
        {
            if (!Resizable || !IsHandleCreated) return 0;
            if (WindowState == FormWindowState.Maximized) return 0;   // task-127: maximized window must not be resizable
            RECT wr;
            if (!GetWindowRect(Handle, out wr)) return 0;
            int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
            if (w <= 0 || h <= 0) return 0;
            int x = screen.X - wr.Left, y = screen.Y - wr.Top;
            if (x < 0 || y < 0 || x >= w || y >= h) return 0;
            int edge = Math.Max(6, (int)Math.Round(8 * (DeviceDpi / 96f)));
            bool left = x <= edge, right = x >= w - edge;
            bool top = y <= edge, bottom = y >= h - edge;
            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        /// <summary>0=HTCLIENT else the HT* code for this client point.</summary>
        public int EdgeHit(Point c)
        {
            if (!Resizable) return 0;
            if (WindowState == FormWindowState.Maximized) return 0;   // task-127
            int edge = Math.Max(6, (int)Math.Round(8 * (DeviceDpi / 96f)));
            bool left = c.X <= edge, right = c.X >= ClientSize.Width - edge;
            bool top = c.Y <= edge, bottom = c.Y >= ClientSize.Height - edge;
            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try   // task-130: opt-in composition test for the user (default off)
            {
                if (Environment.GetEnvironmentVariable("DECANTER_COMPOSITED") == "1")
                {
                    int ex = GetWindowLong(Handle, -20);
                    SetWindowLong(Handle, -20, ex | 0x02000000);   // WS_EX_COMPOSITED
                }
            }
            catch { }
            if (_hitFilter == null) { _hitFilter = new ResizeHitFilter(this); Application.AddMessageFilter(_hitFilter); }
            try { int policy = 1; DwmSetWindowAttribute(Handle, 2, ref policy, 4); } catch { } // DWMNCRP_DISABLED
            try { int border = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(Handle, 34, ref border, 4); } catch { } // DWMWA_COLOR_NONE
            try { int corner = 1; DwmSetWindowAttribute(Handle, 33, ref corner, 4); } catch { } // DWMWCP_DONOTROUND
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _hitFilter != null) { Application.RemoveMessageFilter(_hitFilter); _hitFilter = null; }
            base.Dispose(disposing);
        }


        protected virtual void OnDragFinished() { }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0232) { base.WndProc(ref m); try { OnDragFinished(); } catch { } return; }   // WM_EXITSIZEMOVE
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // 去掉非客户区：客户区填满整窗（否则无边框仍会留 1px 系统边）
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCACTIVATE)
            {
                base.WndProc(ref m);
                return;
            }
            if (m.Msg == WM_GETMINMAXINFO)
            {
                try
                {
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    IntPtr mon = MonitorFromWindow(Handle, 2 /*MONITOR_DEFAULTTONEAREST*/);
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(mon, ref mi))
                    {
                        RECT w = mi.rcWork;
                        mmi.ptMaxPosition.X = w.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = w.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = w.Right - w.Left;
                        mmi.ptMaxSize.Y = w.Bottom - w.Top;
                        mmi.ptMaxTrackSize.X = w.Right - w.Left;
                        mmi.ptMaxTrackSize.Y = w.Bottom - w.Top;
                        Marshal.StructureToPtr(mmi, m.LParam, true);
                    }
                }
                catch { }
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (!Resizable) return;
                if ((int)m.Result != HTCLIENT) return;
                int pX = (short)((long)m.LParam);
                int pY = (short)(((long)m.LParam) >> 16);
                int r = ScreenEdgeHit(new Point(pX, pY));
                if (r != 0) { m.Result = (IntPtr)r; return; }
                // 顶栏空白区交给宿主决定：拖动或窗口按钮由宿主自行处理（见 MainForm/SettingsForm 的标题栏事件）
                return;
            }
            base.WndProc(ref m);
        }
    }
}
