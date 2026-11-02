// DebugPanelForm.cs — W6 调试模式：只读状态面板（独立入口 --debug-panel；不显示待办原文/token）
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public class DebugPanelForm : Form
    {
        private readonly Store _store;
        private readonly ApiServer _api;
        private readonly Label _lblPort, _lblRev, _lblCounts, _lblEvents, _lblDb;
        private readonly Timer _timer;

        public DebugPanelForm(Store store, ApiServer api)
        {
            _store = store;
            _api = api;
            Text = "醒酒器 · 调试面板（只读）";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ApplyTheme();
            ClientSize = new Size(520, 300);
            _lblPort = MakeLabel(16, 16);
            _lblRev = MakeLabel(16, 48);
            _lblCounts = MakeLabel(16, 80);
            _lblDb = MakeLabel(16, 112);
            _lblEvents = MakeLabel(16, 150);
            _lblEvents.Size = new Size(480, 120);
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { RefreshPanel(); };
            _timer.Start();
            RefreshPanel();
        }

        private void ApplyTheme()
        {
            Theme th = _store != null && _store.GetSetting("theme", "day") == "night" ? Theme.Night() : Theme.Day();
            BackColor = th.Paper;
            ForeColor = th.Text;
        }

        private Label MakeLabel(int x, int y)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Location = new Point(x, y);
            Controls.Add(l);
            return l;
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return "（无）";
            if (token.Length <= 6) return "…" + token.Substring(token.Length - 1);
            return token.Substring(0, 3) + "…" + token.Substring(token.Length - 2);
        }

        private void RefreshPanel()
        {
            try { ApplyTheme(); } catch { }
            try
            {
                _lblPort.Text = "API：" + (_api == null ? "未启动" : ("127.0.0.1:" + _api.Port + "  token=" + MaskToken(_api.Token)));
            }
            catch { _lblPort.Text = "API：读取失败"; }
            try { _lblRev.Text = "rev：" + _store.Rev; } catch { _lblRev.Text = "rev：读取失败"; }
            try { _lblCounts.Text = "计数：未完成 " + _store.CountActive() + " 条 / 已完成 " + _store.CountDone() + " 条"; }
            catch { _lblCounts.Text = "计数：读取失败"; }
            try
            {
                string dbPath = Paths.DbPath();
                long size = 0;
                try { FileInfo fi = new FileInfo(dbPath); if (fi.Exists) size = fi.Length; } catch { }
                _lblDb.Text = "数据库：" + dbPath + "（" + (size / 1024) + " KB）";
            }
            catch { _lblDb.Text = "数据库：读取失败"; }
            try
            {
                System.Collections.Generic.List<string> evs = _store.RecentEvents();
                _lblEvents.Text = "最近 5 条事件（仅类型/时间，无原文）：\r\n" + string.Join("\r\n", evs.ToArray());
            }
            catch { _lblEvents.Text = "最近事件：读取失败"; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
