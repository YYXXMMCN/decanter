// Program.cs — 入口（GUI / CLI 双模），单实例、托盘、提醒分级、任务栏闪烁、本地 HTTP
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public static class Program
    {
        private static Store _store;
        private static FloatingForm _floating;
        private static Trace _trace;
        private static SettingsForm _debugSettings;
        private static WelcomeForm _debugWelcome;
        private static MainForm _main;
        private static NotifyIcon _tray;
        private static System.Windows.Forms.Timer _reminderTimer;
        private static System.Windows.Forms.Timer _uiRefreshTimer;   // W6：Store.Changed → 100ms 合并刷新
        private static DebugPanelForm _debugPanel;
        private static ApiServer _api;
        private static FlashHost _flash;
        private static int _notifiedToday;
        private static string _notifiedDate = "";

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        private static void EnableDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                SetThreadDpiAwarenessContext(new IntPtr(-4));
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        private static string InstanceMutexName()
        {
            // one instance per data directory; isolated test homes can run alongside the user's app
            string key = Paths.DbPath().ToLowerInvariant();
            int h = 0;
            for (int i = 0; i < key.Length; i++) h = (h * 31 + key[i]) & 0x7fffffff;
            return "Local\\Decanter.v011." + h.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

[System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static void ActivateExistingInstance()
        {
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("Decanter"))
            {
                if (p.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(p.MainWindowHandle, 9);
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }

        private static void WriteStartupNote(string note)
        {
            try
            {
                string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "startup-notes.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + note + "\n");
            }
            catch { }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool cli = false;
            foreach (string s in args) if (s == "--cli") { cli = true; break; }
            if (!cli) EnableDpiAwareness();
            // 自检模式：输出布局/字号/悬浮窗几何/主题序列/种子 的 JSON 证据，不弹窗
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--debug-selftest") return DebugSelfCheck.Run(args);
                if (args[i] == "--selftest")
                {
                    string outPath = "selftest.json";
                    for (int j = 0; j < args.Length - 1; j++) if (args[j] == "--out") outPath = args[j + 1];
                    return Selftest.Run(outPath);
                }
            }
            if (cli)
            {
                AttachConsole(-1);
                return Cli.Run(args);
            }
            // 截图取证模式（交付用，不参与正常运行）：--shot <out.png> --state main|main-day|settings|float|float-expanded|empty|dpi200|welcome
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--shot")
                {
                    string outPath = (i + 1 < args.Length) ? args[i + 1] : "shot.png";
                    string st = "main";
                    string th = "night";
                    for (int k = 0; k < args.Length - 1; k++)
                    {
                        if (args[k] == "--state") st = args[k + 1];
                        if (args[k] == "--theme") th = args[k + 1];
                    }
                    return Shots.Run(outPath, st, th);
                }
            }

            bool created;
            using (Mutex mutex = new Mutex(true, InstanceMutexName(), out created))
            {
                if (!created)
                {
                    try { ActivateExistingInstance(); } catch { }
                    try { WriteStartupNote("already-running"); } catch { }
                    return 0;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs te)
            {
                try
                {
                    string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " UI " + te.Exception.GetType().Name + ": " + te.Exception.Message + Environment.NewLine + te.Exception.StackTrace + Environment.NewLine);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ee)
            {
                try
                {
                    string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    Exception ex = ee.ExceptionObject as Exception;
                    System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " DOMAIN " + (ex == null ? "?" : ex.GetType().Name + ": " + ex.Message) + Environment.NewLine + (ex == null ? "" : ex.StackTrace) + Environment.NewLine);
                }
                catch { }
            };
            // task-144: opt-in UI trace (never active unless DECANTER_TRACE=1)
            try
            {
                if (Environment.GetEnvironmentVariable("DECANTER_TRACE") == "1")
                {
                    // task-145: app-state sampler is created after the main window exists (see below)

                }
            }
            catch { }
            Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { _store = new Store(Paths.DbPath()); }
                catch (Exception dbex)
                {
                    try { NoticeForm.ShowNotice("数据文件打不开", "data\\todos.db 打不开（可能损坏）。原文件未改动，建议改名备份后再启动。\\n" + dbex.Message); }
                    catch { }
                    return 3;
                }
                DebugGate gate = null;
                bool wantDebug = false, wantControl = false; int debugPort = 0;
                for (int di = 0; di < args.Length; di++)
                {
                    if (args[di] == "--debug") wantDebug = true;
                    else if (args[di] == "--debug-control") { wantDebug = true; wantControl = true; }
                    else if (args[di] == "--debug-port" && di + 1 < args.Length) int.TryParse(args[di + 1], out debugPort);
                }
                InitSettings(_store);
                PurgeLegacyLogs();
                Strings.Lang = _store.GetSetting("lang", "zh");
                Win32Theme.SetAppMode(Theme.FromSetting(_store.GetSetting("theme", "system")).IsDark);

                // 首次启动：一屏「三步就会用」，看完关掉后写入 welcome_done，之后不再出现。
                // 只在正常 GUI 启动走到这里（--cli / --selftest / --shot 已在上方返回）；--ui=main 首启同样先看这一屏。
                if (!wantDebug && _store.GetSetting("welcome_done", "0") != "1")
                {
                    using (WelcomeForm w = new WelcomeForm(_store.GetSetting("theme", "system")))
                    {
                        w.ShowDialog();
                    }
                    _store.SetSetting("welcome_done", "1");
                }

                StartApi();
                _floating = new FloatingForm(_store);
                _main = new MainForm(_store, _floating);
                try
                {
                    if (Environment.GetEnvironmentVariable("DECANTER_TRACE") == "1")
                    {
                        _trace = new Trace(_main, _floating, _store);
                        _trace.Start();
                        Application.ApplicationExit += delegate { try { if (_trace != null) _trace.Stop(); } catch { } };
                    }
                }
                catch { }
                // debug actions run on the main form's UI thread; force its handle even while hidden
                IntPtr mainHandle = _main.Handle;
                if (wantDebug)
                {
                    gate = new DebugGate(_main, _store, debugPort > 0 ? debugPort : DebugGate.FreePort(), wantControl ? DebugGate.ControlMode : DebugGate.MonitorMode);
                    if (!gate.Start())
                    {
                        try
                        {
                            string ldir = System.IO.Path.Combine(Paths.Home(), "logs");
                            if (!System.IO.Directory.Exists(ldir)) System.IO.Directory.CreateDirectory(ldir);
                            System.IO.File.AppendAllText(System.IO.Path.Combine(ldir, "startup-notes.txt"),
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " debug-port-in-use: " + debugPort + "\n");
                        }
                        catch { }
                        return 4;   // port taken: exit non-zero, do not overwrite the existing endpoint
                    }
                    else
                    {
                        try
                        {
                            _debugSettings = new SettingsForm(_store, _floating);
                            _debugSettings.Owner = _main;
                            _debugWelcome = new WelcomeForm(_store.GetSetting("theme", "system"));
                            gate.Probe.AttachShells(_floating, _debugSettings, _debugWelcome);
                        }
                        catch { }
                        Application.ApplicationExit += delegate { if (gate != null) gate.Dispose(); };
                    }
                }
                _floating.OpenMain += delegate { UiLog("OpenMain fired"); try { _main.RefreshList(); _main.Show(); _main.Activate(); UiLog("main shown"); } catch (Exception ex) { UiLog("OpenMain error: " + ex.Message); } };
                _floating.ExitApp += delegate { Application.Exit(); };
                // W6：数据变更（UI 自身/API 同进程写）→ 100ms 合并刷新；2s 定时器保留作跨进程兜底
                _store.Changed += delegate { ScheduleUiRefresh(); };
                BuildTray();
                StartReminderTimer();
                foreach (string s in args)
                {
                    if (s == "--ui=main") { _main.RefreshList(); _main.Show(); _main.Activate(); }
                    if (s == "--ui=settings") { SettingsForm sf = new SettingsForm(_store, _floating); sf.Owner = _main; sf.Show(); }
                    if (s == "--debug-panel" && _debugPanel == null) { _debugPanel = new DebugPanelForm(_store, _api); _debugPanel.Show(); }
                }
                Application.ApplicationExit += delegate
                {
                    try { if (_api != null) _api.Dispose(); } catch { }
                    try { if (_tray != null) _tray.Dispose(); } catch { }
                    try { if (_store != null) _store.Dispose(); } catch { }
                };
                Application.Run(_floating);
            }
            return 0;
        }

        private static void UiLog(string msg)
        {
            try
            {
                string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "ui.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n", new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        // W6：Store.Changed（可能来自 API 线程）→ BeginInvoke 到 UI 线程，100ms 合并节流后刷新
        private static void ScheduleUiRefresh()
        {
            try
            {
                if (_floating == null || !_floating.IsHandleCreated) return;
                _floating.BeginInvoke((MethodInvoker)delegate
                {
                    if (_uiRefreshTimer == null)
                    {
                        _uiRefreshTimer = new System.Windows.Forms.Timer();
                        _uiRefreshTimer.Interval = 100;
                        _uiRefreshTimer.Tick += delegate { _uiRefreshTimer.Stop(); DoUiRefresh(); };
                    }
                    _uiRefreshTimer.Stop();
                    _uiRefreshTimer.Start();
                });
            }
            catch { }
        }

        private static void DoUiRefresh()
        {
            // 只读刷新：不夺焦、不动选中、不写编辑中字段（W3 会话自己保字段）
            try { if (_main != null && _main.Visible) _main.RefreshList(); } catch { }
            try { if (_floating != null && _floating.Visible) _floating.RefreshData(); } catch { }
        }

        // 旧版本日志可能含待办原文（隐私），启动时清理
        // 首次启动：8 条示例（真实生活细节，不是功能说明书）
        // 规则：仅当数据库【没有任何待办】时种入；时间偏移以首次启动时刻为基准并顺延（不许一装出来就过期）
        // note 只写进说明字段（只在主界面显示）；pinned / recurrence / remind 必须写对
        internal static void SeedDemoTodos(Store store)
        {
            try
            {
                if (store.GetSetting("demo_seeded", "0") == "1") return;
                List<TodoItem> existing = store.Active();
                List<TodoItem> done = store.Completed(1);
                if (existing.Count > 0 || done.Count > 0) { store.SetSetting("demo_seeded", "1"); return; }

                DateTime baseT = DateTime.Now;

                // 今天 23:59（已过则次日）
                DateTime today2359 = baseT.Date.AddHours(23).AddMinutes(59);
                if (today2359 <= baseT) today2359 = today2359.AddDays(1);
                // 今天 18:00（已过则次日）
                DateTime today1800 = baseT.Date.AddHours(18);
                if (today1800 <= baseT) today1800 = today1800.AddDays(1);
                // 本周日 23:59（已过则下周）
                int daysToSunday = ((int)DayOfWeek.Sunday - (int)baseT.DayOfWeek + 7) % 7;
                DateTime sunday2359 = baseT.Date.AddDays(daysToSunday).AddHours(23).AddMinutes(59);
                if (sunday2359 <= baseT) sunday2359 = sunday2359.AddDays(7);

                // 标题 ≤12 字；note 20–40 字，写用户自己的细节，不写功能说明
                // 覆盖：今天截止 ×2、本周日截止 ×2、提醒 ×1、每天循环 ×1、置顶 ×1、重要度三档、8 条都有说明
                AddSeed(store, "给妈妈打电话", "上周答应过要打的，拖到现在。问问腰还疼不疼，别又光顾着说别的。", 2, false, today2359, null, "");
                AddSeed(store, "交这个月的电费", "上个月 186，这个月开空调多，估计要超两百。交完把截图存进相册。", 3, false, today2359, today1800, "");
                AddSeed(store, "整理本周会议记录", "周一和周四两次会，重点在排期和人员调整；整理完先发群里对一遍。", 2, false, sunday2359, null, "");
                AddSeed(store, "每天喝够八杯水", "用桌上那个杯子算：一杯 300 毫升，上午两杯下午三杯，晚上两杯。", 1, false, null, null, "daily");
                AddSeed(store, "买猫粮", "猫粮快见底了，先买一袋应急；旧的密封桶记得刷干净晾着。", 3, true, null, null, "");
                AddSeed(store, "看一集纪录片", "存了快一个月的那部，今晚看一集就够了，不追进度。", 1, false, null, null, "");
                AddSeed(store, "预约牙医", "右下那颗补过的地方又开始敏感，冷热都酸；先问周末能不能约。", 2, false, null, null, "");
                AddSeed(store, "把书还回图书馆", "借的三本逾期四天了，罚款大概五块；先还掉，想看的回来再借。", 2, false, sunday2359, null, "");

                store.SetSetting("demo_seeded", "1");
                store.SetSetting("firstrun_seeded", "1");   // 只在"真种了示例"的空库首启里显示练手提示（W2a ①）
            }
            catch { }
        }

        private static void AddSeed(Store store, string title, string note, int imp, bool pinned, DateTime? due, DateTime? remind, string recurrence)
        {
            TodoItem t = new TodoItem();
            t.Title = title;
            t.Note = note;
            t.Importance = imp;
            t.Pinned = pinned;
            if (due.HasValue) t.DueUtc = TimeUtil.ToUtc(due.Value);
            if (remind.HasValue) t.RemindUtc = TimeUtil.ToUtc(remind.Value);
            t.Recurrence = recurrence;
            if (t.IsRecurring) t.RecurrenceNextUtc = Recurrence.NextUtc(recurrence, DateTime.Now);
            store.Add(t);
        }

        private static void PurgeLegacyLogs()
        {
            try
            {
                string logs = System.IO.Path.Combine(Paths.Home(), "logs");
                if (!System.IO.Directory.Exists(logs)) return;
                foreach (string f in System.IO.Directory.GetFiles(logs, "*.log"))
                {
                    try
                    {
                        string txt = System.IO.File.ReadAllText(f, System.Text.Encoding.UTF8);
                        if (txt.Contains("title=")) System.IO.File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static System.Drawing.Icon AppIcon()
        {
            try
            {
                using (System.IO.Stream s = typeof(Program).Assembly.GetManifestResourceStream("decanter.ico"))
                {
                    if (s != null) return new System.Drawing.Icon(s);
                }
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        internal static void InitSettings(Store store)
        {
            SetDefault(store, "theme", "system");   // 需求 #26：默认跟随系统；用户可在设置页改日/夜
            SetDefault(store, "firstrun", "0");
                            // task-148: official first run is an EMPTY list; demo data only with DECANTER_DEMO=1
                if (Environment.GetEnvironmentVariable("DECANTER_DEMO") == "1") SeedDemoTodos(store);
            SetDefault(store, "growth_on", "1");
            SetDefault(store, "growth_all", "0");
            SetDefault(store, "growth_days", "90");
            SetDefault(store, "remind_l1", "1");
            SetDefault(store, "remind_l2", "1");
            SetDefault(store, "remind_l3", "1");
            SetDefault(store, "flash", "1");
            SetDefault(store, "sound", "1");
            SetDefault(store, "daily_limit", "20");
            SetDefault(store, "http_port", "8756");
            if (string.IsNullOrEmpty(store.GetSetting("api_token", "")))
            {
                store.SetSetting("api_token", RandomHex(16));
            }
        }

        private static void SetDefault(Store store, string key, string value)
        {
            if (string.IsNullOrEmpty(store.GetSetting(key, ""))) store.SetSetting(key, value);
        }

        private static string RandomHex(int bytes)
        {
            byte[] b = new byte[bytes];
            new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(b);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static void StartApi()
        {
            int port = 8756;
            int.TryParse(_store.GetSetting("http_port", "8756"), out port);
            string token = _store.GetSetting("api_token", "");
            _api = new ApiServer(_store, port, token);
            _api.Start();
        }

        private static void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = AppIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = Strings.L("tray.tip");
            _tray.Visible = true;
            ContextMenu menu = new ContextMenu();
            menu.MenuItems.Add(Strings.L("tray.show"), delegate { _floating.Visible = true; _floating.RefreshData(); });
            menu.MenuItems.Add(Strings.L("tray.main"), delegate { _main.RefreshList(); _main.Show(); _main.Activate(); });
            menu.MenuItems.Add(Strings.L("tray.settings"), delegate { using (SettingsForm f = new SettingsForm(_store, _floating)) { f.ShowDialog(_main); } _main.RefreshList(); _floating.RefreshData(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(Strings.L("tray.exit"), delegate { Application.Exit(); });
            _tray.ContextMenuStrip = ThemeMenu.FromLegacy(Theme.FromSetting(_store.GetSetting("theme", "system")), menu);
            _tray.BalloonTipTitle = Strings.L("tray.tip");
            _tray.BalloonTipText = Strings.L("balloon.started");
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(2000);
            _tray.DoubleClick += delegate { _main.RefreshList(); _main.Show(); _main.Activate(); };
        }

        private static void StartReminderTimer()
        {
            _reminderTimer = new System.Windows.Forms.Timer();
            _reminderTimer.Interval = 15000;
            _reminderTimer.Tick += delegate { CheckReminders(); };
            _reminderTimer.Start();
        }

        private static void CheckReminders()
        {
            try
            {
                long now = TimeUtil.NowUtc();
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (today != _notifiedDate) { _notifiedDate = today; _notifiedToday = 0; }
                int dailyLimit = 20;
                int.TryParse(_store.GetSetting("daily_limit", "20"), out dailyLimit);
                if (_notifiedToday >= dailyLimit) return;

                bool growthAll = _store.GetSetting("growth_all", "0") == "1";
                bool growthOn = _store.GetSetting("growth_on", "1") == "1";
                foreach (TodoItem t in _store.Active())
                {
                    if (!t.RemindUtc.HasValue) continue;
                    if (t.RemindUtc.Value > now) continue;
                    string key = "notified:" + t.Id + ":" + t.RemindUtc.Value;
                    if (_store.GetSetting(key, "0") == "1") continue;

                    ScoreResult r = Scoring.Evaluate(t, DateTime.Now, Scoring.GrowthEnabled(t, growthAll, growthOn));
                    int level = r.Score >= 0.75 ? 3 : (r.Score >= 0.45 ? 2 : 1);
                    bool enabled = true;
                    if (level == 1 && _store.GetSetting("remind_l1", "1") != "1") enabled = false;
                    if (level == 2 && _store.GetSetting("remind_l2", "1") != "1") enabled = false;
                    if (level == 3 && _store.GetSetting("remind_l3", "1") != "1") enabled = false;
                    if (!enabled) { LogReminder(level, t, "skipped level disabled"); continue; }

                    // 只在真正送达后才标记，避免"关掉某档 → 该提醒被永久吞掉"
                    _store.SetSetting(key, "1");
                    _notifiedToday++;
                    LogReminder(level, t, "fire score=" + r.Score.ToString("0.###"));
                    Notify(level, t, r);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("reminder error: " + ex.Message);
            }
        }

        private static void LogReminder(int level, TodoItem t, string action)
        {
            try
            {
                string dir = System.IO.Path.Combine(Paths.Home(), "logs");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                // 隐私：日志只记 id 与等级，绝不写待办原文（审查员要求）
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\tlevel=" + level + "\tid=" + t.Id + "\t" + action;
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "reminder.log"), line + "\r\n", new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private static void Notify(int level, TodoItem t, ScoreResult r)
        {
            if (_tray != null)
            {
                _tray.BalloonTipTitle = level >= 3 ? Strings.L("balloon.important") : (level >= 2 ? Strings.L("balloon.notable") : Strings.L("balloon.reminder"));
                _tray.BalloonTipText = t.Title + (r.Overdue ? "（" + Strings.L("overdue.tag") + "）" : "");
                _tray.BalloonTipIcon = level >= 3 ? ToolTipIcon.Warning : ToolTipIcon.Info;
                _tray.ShowBalloonTip(level >= 3 ? 8000 : 4000);
            }
            if (_store.GetSetting("sound", "1") == "1")
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }
            if (level >= 2 && _floating != null)
            {
                _floating.Visible = true;
                _floating.RefreshData();
            }
            if (level >= 3 && _store.GetSetting("flash", "1") == "1")
            {
                FlashTaskbar(5);
            }
        }

        private static void FlashTaskbar(int times)
        {
            try
            {
                if (_flash == null) _flash = new FlashHost();
                _flash.Flash(times);
            }
            catch { }
        }

        // 临时任务栏宿主：产生一个最小化的任务栏按钮，用于 FlashWindowEx
        private sealed class FlashHost : Form
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct FLASHWINFO
            {
                public uint cbSize;
                public IntPtr hwnd;
                public uint dwFlags;
                public uint uCount;
                public uint dwTimeout;
            }
            [DllImport("user32.dll")]
            private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

            public FlashHost()
            {
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                ShowInTaskbar = true;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-32000, -32000);
                Size = new Size(120, 40);
                Text = "醒酒器";
                WindowState = FormWindowState.Minimized;
                Show();
            }

            public void Flash(int times)
            {
                FLASHWINFO f = new FLASHWINFO();
                f.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
                f.hwnd = Handle;
                f.dwFlags = 0x00000002 | 0x0000000C; // FLASHW_TRAY | FLASHW_TIMERNOFG
                f.uCount = (uint)times;
                f.dwTimeout = 0;
                FlashWindowEx(ref f);
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
                base.OnFormClosing(e);
            }
        }
    }
}
