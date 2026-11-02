// v0.11 App · NoticeForm: small, focus-safe notice centred on a NON-primary screen (primary only if none)
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Decanter.App
{
    public sealed class NoticeForm : Form
    {
        private readonly string _title;
        private readonly string _body;
        private readonly Theme _theme;
        private Font _fTitle, _fBody, _fBtn;
        private Rectangle _okRect;

        public NoticeForm(string title, string body, Theme theme)
        {
            _title = title;
            _body = body;
            _theme = theme;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 220);
            StartPosition = FormStartPosition.Manual;
            _fTitle = new Font(Theme.FontUI, 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font(Theme.FontUI, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBtn = new Font(Theme.FontUI, 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            _okRect = new Rectangle(ClientSize.Width - 24 - 96, ClientSize.Height - 24 - 34, 96, 34);
            PlaceCentreNonPrimary();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public static void ShowNotice(string title, string body)
        {
            using (NoticeForm f = new NoticeForm(title, body, Theme.FromSetting("system")))
            {
                f.ShowDialog();
            }
        }

        public Rectangle OkRect() { return _okRect; }

        public void PlaceCentreNonPrimary()
        {
            Screen target = Screen.PrimaryScreen;
            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++) if (!all[i].Primary) { target = all[i]; break; }
            Rectangle wa = target.WorkingArea;
            Location = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(_theme.Paper)) g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(_theme.LineStrong)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (SolidBrush b = new SolidBrush(_theme.Text))
            {
                g.DrawString(_title, _fTitle, b, new RectangleF(24, 20, Width - 48, 26));
                g.DrawString(_body, _fBody, b, new RectangleF(24, 58, Width - 48, 100));
            }
            using (SolidBrush b = new SolidBrush(_theme.Accent)) g.FillRectangle(b, _okRect);
            using (SolidBrush b = new SolidBrush(_theme.OnAccent))
                g.DrawString("OK", _fBtn, b, new RectangleF(_okRect.X + 38, _okRect.Y + 8, 40, 20));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _okRect.Contains(e.Location)) Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.Enter) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
