// v0.11 App · Snapshot: PNG of a control (offscreen-safe; used by /debug/screenshot)
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Decanter.App
{
    public static class Snapshot
    {
        /// <summary>Render on the UI thread (DrawToBitmap must not run on the HTTP thread).</summary>
        public static byte[] PngOfOnUi(Control c)
        {
            if (c == null) return new byte[0];
            byte[] result = new byte[0];
            try
            {
                if (c.IsHandleCreated)
                {
                    c.Invoke((MethodInvoker)delegate { result = PngOf(c); });
                }
                else result = PngOf(c);
            }
            catch { }
            return result;
        }

        public static byte[] PngOf(Control c)
        {
            using (Bitmap bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }
}
