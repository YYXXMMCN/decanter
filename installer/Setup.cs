// Setup.cs — 醒酒器 0.11 安装器（单文件、双击即装、默认不提权）
// 用法：
//   双击                                        → 图形安装（默认 D:\醒酒器，不可写时回退 %LOCALAPPDATA%\Programs\Decanter）
//   --sandbox <root> [--silent] [--autostart 0|1] → 工作区合规测试：目标目录 root\program、开始菜单 root\StartMenu、
//                                                    注册表 root\registry.txt、自启 root\autostart.txt
//   --shot <png> --state install|success|error   → 只渲染并截图后退出
// 退出码：0 成功；1 失败/取消。
// C# 5 约束：不用字符串插值 / ?. / nameof / 自动属性初始化器。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Decanter.Setup
{
    internal class InstallEngine
    {
        public Layout L;
        public bool AutoStart;
        public Action<string> Progress;

        private SysOps _ops;

        public void Run()
        {
            _ops = new SysOps(L);

            Report("正在看看有没有开着的醒酒器…");
            if (ProcessCheck.IsDecanterRunning(L))
                throw new HumanException(HumanError.Running(L.InstallDir));

            Report("正在准备安装位置…");
            PrepareDir();

            Report("正在放程序文件…");
            ExtractPayload();

            if (L.Sandbox)
            {
                File.WriteAllText(L.MarkerTxt, L.Root, new UTF8Encoding(false));
            }
            else
            {
                // 真机安装不要留沙箱标记（万一目录是从别处拷来的）
                try { if (File.Exists(L.MarkerTxt)) File.Delete(L.MarkerTxt); }
                catch { }
            }

            Report("正在建开始菜单快捷方式…");
            CreateShortcuts();

            Report("正在登记卸载信息…");
            RegisterUninstall();

            Report("正在设置开机自启…");
            ApplyAutostart();

            Report("装好了。");
        }

        private void Report(string text)
        {
            if (Progress != null) Progress(text);
        }

        private void PrepareDir()
        {
            try
            {
                EnsureDirWritable(L.InstallDir);
            }
            catch (Exception ex)
            {
                // 真机：D 盘不可写时回退到 %LOCALAPPDATA%\Programs\Decanter
                if (!L.Sandbox && L.InstallDir.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase))
                {
                    string fallback = Layout.LocalFallbackDir();
                    try
                    {
                        L.InstallDir = fallback;
                        EnsureDirWritable(fallback);
                        return;
                    }
                    catch (Exception ex2)
                    {
                        throw new HumanException(HumanError.Map(ex2, "dir", fallback));
                    }
                }
                throw new HumanException(HumanError.Map(ex, "dir", L.InstallDir));
            }
        }

        private static void EnsureDirWritable(string dir)
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".decanter-write-probe.tmp");
            File.WriteAllText(probe, "ok", new UTF8Encoding(false));
            File.SetAttributes(probe, FileAttributes.Normal);
            File.Delete(probe);
        }

        private void ExtractPayload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream res = asm.GetManifestResourceStream("payload.zip");
            if (res == null)
                throw new HumanException(new HumanError(
                    "这个安装文件里没有找到程序内容。",
                    "安装包可能没下载完整，或者被改动过。",
                    "重新下载一次安装包再双击；不要从压缩包里直接双击临时文件。"));
            try
            {
                using (res)
                using (ZipArchive zip = new ZipArchive(res, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string rel = entry.FullName.Replace('/', '\\');
                        if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;
                        if (Path.IsPathRooted(rel)) continue;
                        string dest = Path.Combine(L.InstallDir, rel);
                        string parent = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            Directory.CreateDirectory(parent);
                        CopyEntry(entry, dest);
                    }
                }
            }
            catch (HumanException) { throw; }
            catch (Exception ex)
            {
                throw new HumanException(HumanError.Map(ex, "payload", L.InstallDir));
            }

            string[] required = new string[]
            {
                "Decanter.exe", "Decanter.exe.config", "sqlite3.dll", "Uninstall.exe", "README.txt"
            };
            foreach (string name in required)
            {
                string p = Path.Combine(L.InstallDir, name);
                if (!File.Exists(p) || new FileInfo(p).Length == 0)
                    throw new HumanException(HumanError.Map(new IOException("文件缺失"), "payload", L.InstallDir));
            }
        }

        private static void CopyEntry(ZipArchiveEntry entry, string dest)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    using (Stream src = entry.Open())
                    using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[81920];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (!(ex is IOException)) throw;
                    System.Threading.Thread.Sleep(120);
                }
            }
            if (last != null) throw last;
        }

        private void CreateShortcuts()
        {
            try
            {
                _ops.EnsureDir(L.StartMenuDir);
                _ops.CreateShortcut(Path.Combine(L.StartMenuDir, "醒酒器.lnk"),
                    L.ExePath, L.InstallDir, L.ExePath);
                _ops.CreateShortcut(Path.Combine(L.StartMenuDir, "卸载醒酒器.lnk"),
                    L.UninstallPath, L.InstallDir, L.ExePath);
            }
            catch (Exception ex)
            {
                throw new HumanException(HumanError.Map(ex, "shortcut", L.StartMenuDir));
            }
        }

        private void RegisterUninstall()
        {
            string[] files = new string[]
            {
                L.ExePath, Path.Combine(L.InstallDir, "Decanter.exe.config"),
                Path.Combine(L.InstallDir, "sqlite3.dll"), L.UninstallPath,
                Path.Combine(L.InstallDir, "README.txt")
            };
            try { _ops.RegisterUninstall(FileUtil.DirSizeKb(files)); }
            catch (Exception ex) { throw new HumanException(HumanError.Map(ex, "registry", L.InstallDir)); }
        }

        private void ApplyAutostart()
        {
            try
            {
                if (AutoStart) _ops.SetAutostart(true);
                else
                {
                    // 不勾选：清掉旧残留（失败不影响安装）
                    try { _ops.RemoveAutostart(); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (AutoStart) throw new HumanException(HumanError.Map(ex, "autostart", L.InstallDir));
            }
        }
    }

    internal static class InstallResultFile
    {
        public static void Write(Layout l, bool ok, bool autostart, HumanError err)
        {
            if (!l.Sandbox) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ok=" + (ok ? "true" : "false"));
                sb.AppendLine("install_dir=" + l.InstallDir);
                sb.AppendLine("start_menu=" + l.StartMenuDir);
                sb.AppendLine("registry=" + l.RegistryTxt);
                sb.AppendLine("autostart_file=" + l.AutostartTxt);
                sb.AppendLine("autostart=" + (autostart ? "1" : "0"));
                if (!ok && err != null)
                {
                    sb.AppendLine("what=" + err.What);
                    sb.AppendLine("why=" + err.Why);
                    sb.AppendLine("todo=" + err.Todo);
                }
                string path = Path.Combine(l.Root, "install-result.txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal class SetupForm : LedgerForm
    {
        private enum St { Install, Working, Error, Success }

        private Layout _layout;
        private bool _autostart;
        private St _state = St.Install;
        private string _status = "装好后会在这里显示每一步。";
        private HumanError _err;
        private Action _retry;
        private int _exitCode = 1;
        private bool _installed;

        private const int CancelX = 368;
        private const int PrimaryX = 476;

        public SetupForm(Layout layout) : base(640, 420)
        {
            _layout = layout;
            Text = "醒酒器 0.11";
            Rebuild();
        }

        public int ExitCode { get { return _exitCode; } }

        public void SetAutostartDefault(bool on) { _autostart = on; Invalidate(); }

        // 截图用：把界面摆到某个状态（不真的安装）
        public void PrepareState(string state)
        {
            if (state == "success")
            {
                _state = St.Success;
                _installed = true;
                _exitCode = 0;
                _status = "装好了。";
            }
            else if (state == "error")
            {
                _state = St.Error;
                _err = new HumanError(
                    "程序文件没全放进安装目录。",
                    "醒酒器可能正在运行，文件被占着；也可能是磁盘空间不够。",
                    "先退出醒酒器、确认磁盘还有空间，再点「重试」。");
                _retry = BeginInstall;
            }
            else if (state == "working")
            {
                _state = St.Working;
                _status = "正在放程序文件…";
            }
            Rebuild();
        }

        protected override void BuildZones()
        {
            UpdateTitle();
            if (_state == St.Install)
            {
                AddZone("toggle", new Rectangle(32, 212, 500, 48), delegate
                {
                    _autostart = !_autostart;
                    Invalidate();
                });
                AddZone("cancel", new Rectangle(CancelX, 352, 96, 36), delegate { CancelAll(); });
                AddZone("install", new Rectangle(PrimaryX, 352, 132, 36), delegate { BeginInstall(); });
            }
            else if (_state == St.Working)
            {
                Zone a = AddZone("cancel", new Rectangle(CancelX, 352, 96, 36), null);
                a.Enabled = false;
                Zone b = AddZone("install", new Rectangle(PrimaryX, 352, 132, 36), null);
                b.Enabled = false;
                Zone c = FindZone("close");
                if (c != null) c.Enabled = false;
            }
            else if (_state == St.Error)
            {
                AddZone("retry", new Rectangle(PrimaryX, 352, 132, 36), delegate { Retry(); });
                AddZone("cancel", new Rectangle(CancelX, 352, 96, 36), delegate { CancelAll(); });
            }
            else // Success
            {
                AddZone("done", new Rectangle(360, 352, 96, 36), delegate { FinishSuccess(); });
                AddZone("open", new Rectangle(464, 352, 144, 36), delegate { OpenApp(); });
            }
        }

        // 无边框窗口没有标题栏；标题只进任务栏/Alt+Tab，便于测试和读屏
        private void UpdateTitle()
        {
            if (_state == St.Install || _state == St.Working) Text = "醒酒器 0.11";
            else if (_state == St.Success) Text = "醒酒器 0.11 · 已安装";
            else Text = "醒酒器 0.11 · 需要重试";
        }

        protected override void OnPaintContent(Graphics g)
        {
            if (_state == St.Install || _state == St.Working) PaintInstall(g);
            else if (_state == St.Success) PaintSuccess(g);
            else PaintError(g);
        }

        private void PaintInstall(Graphics g)
        {
            Ui.DrawText(g, "醒酒器 0.11", FTitle, Ui.Ink, 32, 22, 400);
            Ui.DrawText(g, "一步装好。不用选路径，不用管理员权限。", FBody, Ui.Sub, 32, 64, 560);
            Ui.Fill(g, Ui.Line, 32, 98, 576, 1);

            Ui.DrawText(g, "装到这里", FSmall, Ui.Aux, 32, 112, 200);
            Ui.DrawText(g, _layout.InstallDir, FBodyStrong, Ui.Ink, 32, 134, 576);
            Ui.DrawText(g, "数据也放在这个目录的 data 文件夹里。卸载时可以带走，也可以留在原处。",
                FSmall, Ui.Aux, 32, 166, 576);

            // 开机自启（默认关）
            Zone toggle = FindZone("toggle");
            bool hover = toggle != null && toggle.Hover;
            Rectangle box = new Rectangle(32, 219, 18, 18);
            DrawCheckSquare(g, _autostart, box, hover);
            int labelW = Ui.Measure(g, "开机自启", FBody);
            Ui.DrawText(g, "开机自启", FBody, Ui.Ink, 60, 212, 200);
            Ui.DrawText(g, "（默认关）", FSmall, Ui.Aux, 60 + labelW + 2, 214, 140);
            Ui.DrawText(g, "想让醒酒器跟着开机才勾；不勾也能随时从开始菜单打开。", FSmall, Ui.Aux, 60, 240, 480);

            bool working = _state == St.Working;
            Ui.DrawText(g, _status, FSmall, working ? Ui.Brass : Ui.Aux, 32, 292, 576);
            Ui.DrawText(g, "支持 Windows 10 / 11 及后续；.NET Framework 4.8 由系统自带。",
                FSmall, Ui.Aux, 32, 320, 576);
            Ui.DrawText(g, "不需要管理员权限。", FSmall, Ui.Aux, 32, 358, 240);

            DrawButton(g, "cancel", new Rectangle(CancelX, 352, 96, 36), "取消", BtnSecondary);
            DrawButton(g, "install", new Rectangle(PrimaryX, 352, 132, 36), working ? "正在安装" : "安装", BtnPrimary);
        }

        private void PaintSuccess(Graphics g)
        {
            Ui.DrawText(g, "装好了", FTitle, Ui.Ink, 32, 22, 400);
            Ui.DrawText(g, "醒酒器已经在这台电脑上。", FBody, Ui.Sub, 32, 64, 560);
            Ui.Fill(g, Ui.Line, 32, 98, 576, 1);

            Ui.DrawText(g, "安装位置", FSmall, Ui.Aux, 32, 112, 200);
            Ui.DrawText(g, _layout.InstallDir, FBodyStrong, Ui.Ink, 32, 134, 576);
            Ui.DrawText(g, "从开始菜单「醒酒器」打开；数据存在安装目录的 data 里。", FSmall, Ui.Aux, 32, 166, 576);
            Ui.DrawText(g, "开机自启：" + (_autostart ? "开。下次开机会自己起来。" : "关。随时从开始菜单打开。"),
                FSmall, Ui.Aux, 32, 190, 576);

            Ui.Fill(g, Ui.Line, 32, 228, 576, 1);
            Ui.DrawText(g, "现在打开，或者点「完成」关掉这个窗口。", FSmall, Ui.Sub, 32, 248, 576);

            DrawButton(g, "done", new Rectangle(360, 352, 96, 36), "完成", BtnSecondary);
            DrawButton(g, "open", new Rectangle(464, 352, 144, 36), "打开醒酒器", BtnPrimary);
        }

        private void PaintError(Graphics g)
        {
            Ui.DrawText(g, "安装没完成", FTitle, Ui.Ink, 32, 22, 400);
            DrawThreeParts(g, 32, 84, 576, _err);
            DrawButton(g, "cancel", new Rectangle(CancelX, 352, 96, 36),
                _installed ? "关闭" : "取消", BtnSecondary);
            DrawButton(g, "retry", new Rectangle(PrimaryX, 352, 132, 36), "重试", BtnPrimary);
        }

        private void BeginInstall()
        {
            if (_state == St.Working) return;
            _state = St.Working;
            _status = "正在准备…";
            Rebuild();

            InstallEngine e = new InstallEngine();
            e.L = _layout;
            e.AutoStart = _autostart;
            e.Progress = delegate(string s)
            {
                _status = s;
                Application.DoEvents();
                Refresh();
            };
            try
            {
                e.Run();
                _installed = true;
                _exitCode = 0;
                _status = "装好了。";
                _state = St.Success;
            }
            catch (HumanException he)
            {
                _err = he.Info;
                _retry = BeginInstall;
                _state = St.Error;
                _exitCode = 1;
            }
            catch (Exception ex)
            {
                _err = HumanError.Map(ex, "files", _layout.InstallDir);
                _retry = BeginInstall;
                _state = St.Error;
                _exitCode = 1;
            }
            Rebuild();
        }

        private void Retry()
        {
            if (_retry == null) return;
            _retry();
        }

        private void OpenApp()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _layout.ExePath;
                psi.WorkingDirectory = _layout.InstallDir;
                psi.UseShellExecute = true;
                Process.Start(psi);
                _exitCode = 0;
                Close();
            }
            catch (Exception)
            {
                _err = new HumanError(
                    "醒酒器没能打开。",
                    "系统没能启动安装目录里的程序，可能被安全软件拦了一下。",
                    "点「重试」；也可以自己到安装目录双击 Decanter.exe。");
                _retry = OpenApp;
                _state = St.Error;
                Rebuild();
            }
        }

        private void FinishSuccess()
        {
            _exitCode = 0;
            Close();
        }

        private void CancelAll()
        {
            if (_state == St.Working) return;
            _exitCode = _installed ? 0 : 1;
            Close();
        }

        protected override void OnEscape()
        {
            if (_state == St.Working) return;
            CancelAll();
        }

        protected override void OnEnter()
        {
            if (_state == St.Working) return;
            if (_state == St.Install) BeginInstall();
            else if (_state == St.Error) Retry();
            else if (_state == St.Success) OpenApp();
        }

        protected override void OnCloseRequested()
        {
            if (_state == St.Working) return;
            if (_state == St.Success) { _exitCode = 0; Close(); return; }
            CancelAll();
        }
    }

    internal static class SetupProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CmdArgs a = CmdArgs.Parse(args);
            Layout layout;
            try
            {
                layout = a.Has("sandbox") ? Layout.FromSandbox(a.Get("sandbox", "")) : Layout.ForRealInstall();
            }
            catch (HumanException)
            {
                return 2; // 沙箱参数不合法（例如目录不在工作区）：不开界面、不写任何东西
            }

            if (a.Has("shot"))
            {
                SetupForm shotForm = new SetupForm(layout);
                shotForm.SetNoActivate();
                shotForm.PrepareState(a.Get("state", "install"));
                return Capture(shotForm, a.Get("shot", "install.png"));
            }

            bool autostart = a.Get("autostart", "0") == "1";

            if (a.Has("silent"))
            {
                InstallEngine engine = new InstallEngine();
                engine.L = layout;
                engine.AutoStart = autostart;
                try
                {
                    engine.Run();
                    InstallResultFile.Write(layout, true, autostart, null);
                    return 0;
                }
                catch (HumanException he)
                {
                    InstallResultFile.Write(layout, false, autostart, he.Info);
                    return 1;
                }
                catch (Exception ex)
                {
                    InstallResultFile.Write(layout, false, autostart,
                        HumanError.Map(ex, "files", layout.InstallDir));
                    return 1;
                }
            }

            SetupForm form = new SetupForm(layout);
            form.SetAutostartDefault(autostart);
            Application.Run(form);
            return form.ExitCode;
        }

        private static int Capture(LedgerForm f, string path)
        {
            try
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                Timer timer = new Timer();
                timer.Interval = 350;
                timer.Tick += delegate
                {
                    timer.Stop();
                    timer.Dispose();
                    try
                    {
                        Shot.Save(f, path);
                        Environment.Exit(0);
                    }
                    catch
                    {
                        Environment.Exit(3);
                    }
                };
                timer.Start();
                Application.Run(f);
                return 0;
            }
            catch
            {
                return 3;
            }
        }
    }
}
