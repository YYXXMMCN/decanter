// v0.11 App · Placement: physical-pixel placement on the first non-primary monitor.
// WinForms Screen.AllScreens can be unreliable under PerMonitorV2 + mixed DPI, so this
// enumerates monitors with Win32 and positions by physical window rect.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Decanter.App
{
    internal static class Placement
    {
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private static Rectangle SelectWorkArea()
        {
            List<Rectangle> work = new List<Rectangle>();
            Rectangle primary = Rectangle.Empty;
            MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data)
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(hMon, ref mi))
                {
                    Rectangle w = new Rectangle(mi.rcWork.Left, mi.rcWork.Top,
                        mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
                    work.Add(w);
                    if ((mi.dwFlags & MONITORINFOF_PRIMARY) != 0) primary = w;
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            for (int i = 0; i < work.Count; i++)
                if (work[i] != primary) return work[i];
            if (primary != Rectangle.Empty) return primary;
            return System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
        }

        private static Size WindowSize(Form f)
        {
            RECT r;
            if (f.IsHandleCreated && GetWindowRect(f.Handle, out r))
                return new Size(r.Right - r.Left, r.Bottom - r.Top);
            return f.Size;
        }

        private static void Move(Form f, int x, int y)
        {
            if (!f.IsHandleCreated) { f.Location = new Point(x, y); return; }
            SetWindowPos(f.Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public static void CenterOnNonPrimary(Form f)
        {
            Rectangle wa = SelectWorkArea();
            Size s = WindowSize(f);
            Move(f, wa.X + Math.Max(0, (wa.Width - s.Width) / 2),
                   wa.Y + Math.Max(0, (wa.Height - s.Height) / 2));
        }

        /// <summary>Centre a secondary window on its owner's main-window rect; fall back to non-primary screen.</summary>
        public static void CenterOnOwner(Form f, Form owner)
        {
            if (owner == null || owner.IsDisposed || !owner.Visible)
            {
                CenterOnNonPrimary(f);
                return;
            }
            Rectangle ob = owner.Bounds;
            Size s = WindowSize(f);
            Move(f, ob.X + (ob.Width - s.Width) / 2, ob.Y + (ob.Height - s.Height) / 2);
        }

        public static void BottomRightOnNonPrimary(Form f, int rightPad, int bottomPad)
        {
            Rectangle wa = SelectWorkArea();
            Size s = WindowSize(f);
            Move(f, wa.Right - s.Width - rightPad, wa.Bottom - s.Height - bottomPad);
        }

        public static bool IsPointOnNonPrimary(int x, int y)
        {
            Rectangle wa = SelectWorkArea();
            return wa.Contains(new Point(x, y));
        }

        public static Rectangle NonPrimaryWorkArea() { return SelectWorkArea(); }
    }
}
