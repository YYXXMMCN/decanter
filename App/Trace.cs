// v0.11 App · Trace: opt-in APP-STATE trace (DECANTER_TRACE=1) -> <DECANTER_HOME>\logs\app-trace.jsonl
// Samples app state every 500ms and writes ONLY when something changed. Never records titles/notes/typed text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public sealed class Trace : IDisposable
    {
        private readonly object _lock = new object();
        private readonly int _pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        private readonly string _path;
        private readonly Timer _timer;
        private readonly MainForm _main;
        private readonly FloatingForm _floating;
        private readonly Store _store;
        private string _last = "";

        public Trace(MainForm main, FloatingForm floating, Store store)
        {
            _main = main; _floating = floating; _store = store;
            string dir = Path.Combine(Paths.Home(), "logs");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            _path = Path.Combine(dir, "app-trace.jsonl");
            _timer = new Timer();
            _timer.Interval = 500;
            _timer.Tick += delegate { Sample(); };
        }

        public void Start()
        {
            try
            {
                Write("{\"ts\":\"" + Ts() + "\",\"pid\":" + _pid + ",\"event\":\"start\"}");
                Sample();
                _timer.Start();
            }
            catch { }
        }

        public void Stop()
        {
            try { _timer.Stop(); Write("{\"ts\":\"" + Ts() + "\",\"pid\":" + _pid + ",\"event\":\"exit\"}"); } catch { }
        }

        public void Dispose()
        {
            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
        }

        private static string Ts() { return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"); }

        private void Write(string line)
        {
            try { lock (_lock) File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        private void Sample()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"ts\":\"").Append(Ts()).Append("\",\"pid\":").Append(_pid);
                AppendMain(sb);
                AppendCounts(sb);
                AppendSettings(sb);
                AppendFloating(sb);
                sb.Append("}");
                string line = sb.ToString();
                if (line != _last) { _last = line; Write(line); }
            }
            catch { }
        }

        private void AppendMain(StringBuilder sb)
        {
            if (_main == null) return;
            try
            {
                sb.Append(",\"main\":{\"visible\":").Append(_main.Visible ? "true" : "false");
                sb.Append(",\"windowState\":").Append((int)_main.WindowState);
                sb.Append(",\"bounds\":[").Append(_main.Left).Append(",").Append(_main.Top).Append(",").Append(_main.Width).Append(",").Append(_main.Height).Append("]");
                sb.Append(",\"client\":[").Append(_main.ClientSize.Width).Append(",").Append(_main.ClientSize.Height).Append("]");
                sb.Append(",\"view\":").Append(_main.TraceViewIndex);
                sb.Append(",\"viewName\":\"").Append(_main.TraceViewName).Append("\"");
                sb.Append("}");
            }
            catch { }
        }

        private void AppendCounts(StringBuilder sb)
        {
            try
            {
                if (_store == null) return;
                List<TodoItem> act = _store.Active();
                int active = _store.CountActive();   // task-150: Active() already filters done, so count via Store APIs
                int done = _store.CountDone();
                int total = active + done;
                int pinned = 0, overdue = 0;
                DateTime now = DateTime.Now;
                if (act != null)
                {
                    for (int i = 0; i < act.Count; i++)
                    {
                        TodoItem t = act[i];
                        if (t == null) continue;
                        total++;
                        if (t.Done) done++;
                        else
                        {
                            active++;
                            if (t.Pinned) pinned++;
                            if (t.DueUtc.HasValue && DateTimeOffset.FromUnixTimeMilliseconds(t.DueUtc.Value).LocalDateTime < now) overdue++;
                        }
                    }
                }
                sb.Append(",\"items\":{\"total\":").Append(total).Append(",\"active\":").Append(active).Append(",\"done\":").Append(done)
                  .Append(",\"pinned\":").Append(pinned).Append(",\"overdue\":").Append(overdue).Append("}");
            }
            catch { }
        }

        private void AppendSettings(StringBuilder sb)
        {
            try
            {
                bool settingsOpen = false;
                FormCollection open = Application.OpenForms;
                for (int i = 0; i < open.Count; i++) if (open[i] is SettingsForm) settingsOpen = true;
                sb.Append(",\"settingsOpen\":").Append(settingsOpen ? "true" : "false");
                string theme = _store == null ? "" : _store.GetSetting("theme", "system");
                string font = _store == null ? "" : _store.GetSetting("font_scale", "m");
                sb.Append(",\"theme\":\"").Append(theme).Append("\",\"font\":\"").Append(font).Append("\"");
            }
            catch { }
        }

        private void AppendFloating(StringBuilder sb)
        {
            if (_floating == null) return;
            try
            {
                sb.Append(",\"floating\":{\"visible\":").Append(_floating.Visible ? "true" : "false");
                sb.Append(",\"expanded\":").Append(_floating.TestExpanded ? "true" : "false");
                sb.Append(",\"height\":").Append(_floating.Height);
                sb.Append(",\"rows\":").Append(_floating.TestShownCount());
                sb.Append(",\"topmost\":").Append(_floating.TopMost ? "true" : "false");
                sb.Append(",\"bounds\":[").Append(_floating.Left).Append(",").Append(_floating.Top).Append("]");
                sb.Append("}");
            }
            catch { }
        }
    }
}
