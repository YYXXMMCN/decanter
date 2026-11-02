// DpiHelper.cs — 统一处理 WinForms 在高 DPI 下的缩放（4.8 的 DeviceDpi 不可靠）
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Decanter.App
{
    public static class DpiHelper
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static float ScaleOf(Control c)
        {
            try
            {
                if (c.IsHandleCreated)
                {
                    uint d = GetDpiForWindow(c.Handle);
                    if (d > 0) return d / 96f;
                }
            }
            catch { }
            return 1f;
        }

        public static void ScaleForm(Form f, int designWidth, int designHeight)
        {
            float dpi = ScaleOf(f);
            if (dpi > 1.01f)
            {
                f.Scale(new SizeF(dpi, dpi));
            }
            f.ClientSize = new Size((int)Math.Round(designWidth * dpi), (int)Math.Round(designHeight * dpi));
            if (dpi > 1.01f) TuneComboBoxes(f, dpi);
        }

        private static void TuneComboBoxes(Control parent, float dpi)
        {
            foreach (Control c in parent.Controls)
            {
                ComboBox cb = c as ComboBox;
                if (cb != null && !cb.DroppedDown)
                {
                    try { cb.Font = new Font(cb.Font.FontFamily, 9f * dpi, cb.Font.Style, GraphicsUnit.Point); } catch { }
                }
                if (c.HasChildren) TuneComboBoxes(c, dpi);
            }
        }

        private static void ScaleFonts(Control parent, float dpi)
        {
            foreach (Control c in parent.Controls)
            {
                try
                {
                    Font f = c.Font;
                    if (f != null)
                    {
                        c.Font = new Font(f.FontFamily, f.SizeInPoints * dpi, f.Style, GraphicsUnit.Point);
                    }
                }
                catch { }
                if (c.HasChildren) ScaleFonts(c, dpi);
            }
        }
    }
}
