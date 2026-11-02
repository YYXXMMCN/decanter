// v0.11 App · CommandPalette: Ctrl+K quick commands (v8 pattern, v6 visuals)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Decanter.App
{
    public sealed class CommandPalette : Form
    {
        private sealed class Cmd
        {
            public string Label;
            public string Hint;
            public Action Run;
        }

        private readonly MainForm _owner;
        private readonly TextBox _input;
        private readonly ListBox _list;
        private readonly List<Cmd> _all = new List<Cmd>();
        private readonly List<Cmd> _shown = new List<Cmd>();
        private Control _previousFocus;
        private bool _closing;

        public CommandPalette(MainForm owner)
        {
            _owner = owner;
            Text = "命令";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(560, 392);
            Theme th = owner.CurrentTheme;
            BackColor = th.Paper;
            ForeColor = th.Text;

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.FixedSingle;
            _input.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
            _input.BackColor = th.Card;
            _input.ForeColor = th.Text;
            _input.SetBounds(16, 14, ClientSize.Width - 32, 30);
            _input.TextChanged += delegate { Filter(); };
            _input.KeyDown += InputKeyDown;
            Controls.Add(_input);

            _list = new ListBox();
            _list.BorderStyle = BorderStyle.None;
            _list.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            _list.BackColor = th.Paper;
            _list.ForeColor = th.Text;
            _list.SetBounds(16, 54, ClientSize.Width - 32, ClientSize.Height - 70);
            _list.DoubleClick += delegate { RunSelected(); };
            _list.KeyDown += ListKeyDown;
            Controls.Add(_list);

            BuildCommands();
        }

        private void BuildCommands()
        {
            Add("视图：全部", "全部待办", delegate { _owner.CmdSetFilter(0); });
            Add("视图：今天", "今天到期", delegate { _owner.CmdSetFilter(1); });
            Add("视图：本周", "本周到期", delegate { _owner.CmdSetFilter(2); });
            Add("视图：过期", "已过期", delegate { _owner.CmdSetFilter(3); });
            Add("视图：已完成", "已完成", delegate { _owner.CmdSetFilter(4); });
            Add("写一条", "聚焦书写区", delegate { _owner.CmdFocusCompose(); });
            Add("搜索", "聚焦搜索", delegate { _owner.CmdFocusSearch(); });
            Add("编辑选中", "读取到书写区", delegate { _owner.CmdEditSelected(); });
            Add("完成/恢复选中", "切换完成", delegate { _owner.CmdToggleDone(); });
            Add("删除选中", "两步确认的第一下", delegate { _owner.CmdDeleteSelected(); });
            Add("切换主题", "v1/v6/v7/v8 循环", delegate { _owner.CmdCycleTheme(); });
            Add("设置", "打开设置", delegate { _owner.CmdOpenSettings(); });
            Add("显示浮窗", "副屏右下", delegate { _owner.CmdShowFloating(); });
            Add("退出", "关闭醒酒器", delegate { _owner.CmdQuit(); });
        }

        private void Add(string label, string hint, Action run)
        {
            Cmd c = new Cmd();
            c.Label = label;
            c.Hint = hint;
            c.Run = run;
            _all.Add(c);
        }

        public void OpenPalette()
        {
            Rectangle ob = _owner.Bounds;
            Location = new Point(ob.X + Math.Max(0, (ob.Width - Width) / 2), ob.Y + Math.Max(0, (ob.Height - Height) / 2));
            _previousFocus = _owner.ActiveControl;
            _closing = false;
            _input.Text = "";
            _list.Items.Clear();
            Filter();
            Show(_owner);
            _input.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!_closing) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _closing = true;
            try
            {
                _owner.Activate();
                if (_previousFocus != null && !_previousFocus.IsDisposed) _previousFocus.Focus();
            }
            catch { }
        }

        private void Filter()
        {
            string q = _input.Text == null ? "" : _input.Text.Trim();
            _shown.Clear();
            _list.Items.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                Cmd c = _all[i];
                if (q.Length == 0 || c.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || c.Hint.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _shown.Add(c);
                    _list.Items.Add(c.Label + "    " + c.Hint);
                }
            }
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { RunSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { MoveSelection(1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { MoveSelection(-1); e.Handled = true; }
        }

        private void ListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { RunSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        private void MoveSelection(int delta)
        {
            if (_shown.Count == 0) return;
            int i = _list.SelectedIndex + delta;
            if (i < 0) i = _shown.Count - 1;
            if (i >= _shown.Count) i = 0;
            _list.SelectedIndex = i;
        }

        private void RunSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _shown.Count) { Close(); return; }
            Cmd c = _shown[i];
            Close();
            try { c.Run(); } catch { }
        }
    }
}
