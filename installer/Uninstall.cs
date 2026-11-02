// Uninstall.cs — 醒酒器 0.11 卸载器（单文件、无控制台、逐项报告、退出后自删）
// 用法：
//   双击                                                   → 图形卸载（读自身目录作为安装目录；有 sandbox.txt 时按沙箱走）
//   --sandbox <root> --silent --choice keep|export|delete [--export-dir <dir>] → 合规测试
//   --shot <png> --state uninstall|uninstall-confirm|uninstall-done|error   → 只渲染并截图后退出
// 退出码：0 成功；1 失败/取消。
// C# 5 约束：不用字符串插值 / ?. / nameof / 自动属性初始化器。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Decanter.Setup
{
    internal enum UninstallChoice { Keep, Export, Delete }

    internal class UninstallEngine
    {
        public Layout L;
        public UninstallChoice Choice = UninstallChoice.Keep;
        public string ExportDir;
        public Action<string> Progress;
        public List<StepResult> Results = new List<StepResult>();
        public bool DataOk;
        public string RenamedSelf;

        private SysOps _ops;
        private string _selfPath;

        public bool RemoveInstallDir
        {
            get
            {
                if (Choice != UninstallChoice.Keep) return DataOk;
                // 保留数据：data 还在就不删目录；没有数据则空目录也要清掉
                return !Directory.Exists(L.DataDir);
            }
        }

        public void Run()
        {
            _ops = new SysOps(L);
            _selfPath = Application.ExecutablePath;

            Report("正在看看醒酒器有没有在运行…");
            if (ProcessCheck.IsDecanterRunning(L))
                throw new HumanException(HumanError.Running(L.InstallDir));
            Results.Add(new StepResult("检查运行中的醒酒器", true, "没有在运行"));

            Report("正在清理开始菜单…");
            Results.Add(Guard("开始菜单", delegate { return RemoveStartMenu(); }));

            Report("正在清理卸载登记…");
            Results.Add(Guard("卸载登记", delegate { return RemoveUninstallEntry(); }));

            Report("正在清理开机自启…");
            Results.Add(Guard("开机自启", delegate { return RemoveAutostart(); }));

            Report(Choice == UninstallChoice.Keep ? "正在保留数据…"
                : (Choice == UninstallChoice.Export ? "正在导出数据…" : "正在删除数据…"));
            StepResult dataStep = Guard("数据", delegate { return HandleData(); });
            Results.Add(dataStep);

            Report("正在删程序文件…");
            Results.Add(Guard("程序文件", delegate { return RemoveProgramFiles(); }));

            // 不能删正在运行的自己：先改名，原路径空出来；真正的删除交给退出后的 cmd
            RenamedSelf = SelfClean.TryRenameSelf(_selfPath);
            Results.Add(new StepResult("卸载程序自身", true,
                RenamedSelf != null ? "已移出原位置，退出后删除" : "退出后删除"));

            Report("正在收尾…");
            Results.Add(Guard("安装目录", delegate { return FinishDirectory(); }));
        }

        private void Report(string text)
        {
            if (Progress != null) Progress(text);
        }

        private StepResult Guard(string name, Func<bool> action)
        {
            try
            {
                bool ok = action();
                return new StepResult(name, ok, ok ? "" : "没清干净");
            }
            catch (HumanException) { throw; }
            catch (Exception ex)
            {
                return new StepResult(name, false, ShortReason(ex));
            }
        }

        private bool RemoveStartMenu()
        {
            if (!Directory.Exists(L.StartMenuDir)) return true;
            FileUtil.DeleteDirIfExists(L.StartMenuDir);
            return !Directory.Exists(L.StartMenuDir);
        }

        private bool RemoveUninstallEntry()
        {
            _ops.RemoveUninstallEntry();
            return !_ops.UninstallEntryExists();
        }

        private bool RemoveAutostart()
        {
            _ops.RemoveAutostart();
            return !_ops.AutostartExists();
        }

        private bool HandleData()
        {
            if (Choice == UninstallChoice.Keep)
            {
                DataOk = true;
                return true;
            }

            if (Choice == UninstallChoice.Export)
            {
                if (!Directory.Exists(L.DataDir))
                {
                    DataOk = true;
                    return true;
                }
                if (string.IsNullOrEmpty(ExportDir)) ExportDir = DefaultExportDir(L, false);
                if (IsInside(ExportDir, L.InstallDir))
                    throw new HumanException(new HumanError(
                        "导出文件夹不能放在安装目录里。",
                        "卸载会把安装目录一起清掉，数据副本也会没。",
                        "换一个安装目录外面的文件夹，比如桌面。"));
                try
                {
                    FileUtil.CopyDirectory(L.DataDir, ExportDir);
                }
                catch (Exception ex)
                {
                    throw new HumanException(HumanError.Map(ex, "export", ExportDir));
                }
                if (!VerifyExport(L.DataDir, ExportDir))
                    throw new HumanException(new HumanError(
                        "数据导出后核对不上，所以没有删原数据。",
                        "复制过程中可能有文件没写完，或者文件被占用。",
                        "换一个文件夹再试；也可以先改成「保留数据」，装回来后再手动备份。"));
                FileUtil.DeleteDirIfExists(L.DataDir);
                DataOk = !Directory.Exists(L.DataDir);
                return DataOk;
            }

            // Delete
            FileUtil.DeleteDirIfExists(L.DataDir);
            DataOk = !Directory.Exists(L.DataDir);
            return DataOk;
        }

        private bool RemoveProgramFiles()
        {
            bool okAll = true;
            foreach (string f in Directory.GetFiles(L.InstallDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(f, _selfPath)) continue;
                if (RenamedSelf != null && SamePath(f, RenamedSelf)) continue;
                try { FileUtil.DeleteFileIfExists(f); }
                catch { okAll = false; }
            }
            foreach (string d in Directory.GetDirectories(L.InstallDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(d, L.DataDir)) continue;
                try { FileUtil.DeleteDirIfExists(d); }
                catch { okAll = false; }
            }
            foreach (string f in Directory.GetFiles(L.InstallDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(f, _selfPath)) continue;
                if (RenamedSelf != null && SamePath(f, RenamedSelf)) continue;
                okAll = false;
            }
            foreach (string d in Directory.GetDirectories(L.InstallDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (SamePath(d, L.DataDir)) continue;
                okAll = false;
            }
            return okAll;
        }

        private bool FinishDirectory()
        {
            if (!Directory.Exists(L.InstallDir)) return true;
            if (Choice == UninstallChoice.Keep)
            {
                // 保留数据 → 目录留着（这是对的，不是残留）；没有 data 目录也算正常
                return true;
            }
            // 非保留：目录里只剩正在运行的卸载程序，退出后由 cmd rmdir
            return true;
        }

        public static string DefaultExportDir(Layout l, bool realDefault)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            string name = "醒酒器数据导出-" + stamp;
            if (l != null && l.Sandbox)
                return Path.Combine(l.Root, "exported", name);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), name);
        }

        private static bool VerifyExport(string src, string dst)
        {
            if (!Directory.Exists(src)) return true;
            foreach (string dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(src.TrimEnd('\\').Length).TrimStart('\\');
                if (!Directory.Exists(Path.Combine(dst, rel))) return false;
            }
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.TrimEnd('\\').Length).TrimStart('\\');
                string mirror = Path.Combine(dst, rel);
                if (!File.Exists(mirror)) return false;
                if (new FileInfo(file).Length != new FileInfo(mirror).Length) return false;
            }
            return true;
        }

        private static bool IsInside(string child, string parent)
        {
            string c = Path.GetFullPath(child).TrimEnd('\\');
            string p = Path.GetFullPath(parent).TrimEnd('\\');
            return c.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                   c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return Path.GetFullPath(a).TrimEnd('\\')
                    .Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string ShortReason(Exception ex)
        {
            string m = ex.Message != null ? ex.Message.ToLowerInvariant() : "";
            if (m.IndexOf("being used") >= 0 || m.IndexOf("正由另一进程使用") >= 0 ||
                m.IndexOf("另一个程序") >= 0 || m.IndexOf("被占用") >= 0 || m.IndexOf("sharing violation") >= 0)
                return "文件被占用";
            if (ex is UnauthorizedAccessException || m.IndexOf("access is denied") >= 0 ||
                m.IndexOf("拒绝访问") >= 0 || m.IndexOf("没有权限") >= 0)
                return "没有权限";
            if (m.IndexOf("not enough space") >= 0 || m.IndexOf("磁盘空间") >= 0)
                return "磁盘空间不够";
            return "没成功";
        }
    }

    internal class UninstallForm : LedgerForm
    {
        private enum St { Choice, Confirm, Working, Done, Error }

        private Layout _layout;
        private UninstallChoice _choice = UninstallChoice.Keep;
        private string _exportDir;
        private St _state = St.Choice;
        private string _status = "";
        private List<StepResult> _results = new List<StepResult>();
        private HumanError _err;
        private Action _retry;
        private int _exitCode = 1;
        private bool _cleanupScheduled;
        private UninstallEngine _engine;

        private int _y1 = 122;
        private int _y2 = 182;
        private int _y3 = 242;

        public UninstallForm(Layout layout) : base(560, 460)
        {
            _layout = layout;
            _exportDir = UninstallEngine.DefaultExportDir(layout, true);
            Text = "卸载醒酒器";
            Rebuild();
        }

        public int ExitCode { get { return _exitCode; } }

        public void PrepareState(string state)
        {
            if (state == "uninstall-confirm")
            {
                _state = St.Confirm;
            }
            else if (state == "uninstall-done" || state == "success")
            {
                _state = St.Done;
                _results = SampleResults();
                _exitCode = 0;
            }
            else if (state == "error")
            {
                _state = St.Error;
                _err = HumanError.Running(_layout.InstallDir);
                _retry = RunUninstall;
            }
            Rebuild();
        }

        private List<StepResult> SampleResults()
        {
            List<StepResult> list = new List<StepResult>();
            list.Add(new StepResult("检查运行中的醒酒器", true, "没有在运行"));
            list.Add(new StepResult("开始菜单", true, "已清理"));
            list.Add(new StepResult("卸载登记", true, "已清理"));
            list.Add(new StepResult("开机自启", true, "已清除"));
            list.Add(new StepResult("数据", true, "保留在安装目录的 data 里"));
            list.Add(new StepResult("程序文件", true, "已清理"));
            list.Add(new StepResult("卸载程序自身", true, "退出后删除"));
            list.Add(new StepResult("安装目录", true, "保留：data 还在"));
            return list;
        }

        private void LayoutOptions()
        {
            _y1 = 122;
            _y2 = _y1 + 56 + 4;
            int h2 = _choice == UninstallChoice.Export ? 92 : 56;
            _y3 = _y2 + h2 + 4;
        }

        protected override void BuildZones()
        {
            UpdateTitle();
            LayoutOptions();
            if (_state == St.Choice)
            {
                AddZone("opt1", new Rectangle(24, _y1, 512, 56), delegate { Pick(UninstallChoice.Keep); });
                AddZone("opt2", new Rectangle(24, _y2, 512,
                    _choice == UninstallChoice.Export ? 92 : 56), delegate { Pick(UninstallChoice.Export); });
                AddZone("opt3", new Rectangle(24, _y3, 512, 56), delegate { Pick(UninstallChoice.Delete); });
                if (_choice == UninstallChoice.Export)
                {
                    AddZone("choose", new Rectangle(452, _y2 + 52, 64, 26), delegate { ChooseFolder(); });
                }
                AddZone("uninstall", new Rectangle(400, 396, 132, 36), delegate { StartUninstall(); });
                AddZone("cancel", new Rectangle(300, 396, 96, 36), delegate { CancelAll(); });
            }
            else if (_state == St.Confirm)
            {
                AddZone("back", new Rectangle(400, 396, 132, 36), delegate { BackToChoice(); });
                AddZone("really", new Rectangle(280, 396, 112, 36), delegate { RunUninstall(); });
            }
            else if (_state == St.Working)
            {
                Zone z = AddZone("uninstall", new Rectangle(400, 396, 132, 36), null);
                z.Enabled = false;
                Zone c = FindZone("close");
                if (c != null) c.Enabled = false;
            }
            else if (_state == St.Done)
            {
                AddZone("finish", new Rectangle(400, 396, 132, 36), delegate { Finish(); });
            }
            else // Error
            {
                AddZone("retry", new Rectangle(400, 396, 132, 36), delegate { Retry(); });
                AddZone("cancel", new Rectangle(300, 396, 96, 36), delegate { CancelAll(); });
            }
        }

        // 无边框窗口没有标题栏；标题只进任务栏/Alt+Tab，便于测试和读屏
        private void UpdateTitle()
        {
            if (_state == St.Confirm) Text = "卸载醒酒器 · 确认删除数据";
            else if (_state == St.Done) Text = "卸载醒酒器 · 已完成";
            else if (_state == St.Error) Text = "卸载醒酒器 · 需要重试";
            else Text = "卸载醒酒器";
        }

        protected override void OnPaintContent(Graphics g)
        {
            if (_state == St.Choice || _state == St.Working) PaintChoice(g);
            else if (_state == St.Confirm) PaintConfirm(g);
            else if (_state == St.Done) PaintDone(g);
            else PaintError(g);
        }

        private void PaintChoice(Graphics g)
        {
            LayoutOptions();
            Ui.DrawText(g, "卸载醒酒器", FTitle, Ui.Ink, 28, 20, 300);
            Ui.DrawText(g, "安装位置：" + _layout.InstallDir, FSmall, Ui.Aux, 28, 58, 504);
            Ui.Fill(g, Ui.Line, 28, 84, 504, 1);
            Ui.DrawText(g, "数据怎么办？", FHead, Ui.Ink, 28, 96, 300);

            DrawOption(g, "opt1", _y1, 56, UninstallChoice.Keep, "保留数据",
                "只删程序。data 留在原处，下次装回来还在。", false);
            DrawOption(g, "opt2", _y2, _choice == UninstallChoice.Export ? 92 : 56,
                UninstallChoice.Export, "导出数据",
                "先复制一份 data 到桌面或你选的文件夹，再删程序和数据。", false);
            if (_choice == UninstallChoice.Export)
            {
                Ui.DrawText(g, _exportDir, FSmall, Ui.Ink, 56, _y2 + 54, 380);
                DrawButton(g, "choose", new Rectangle(452, _y2 + 52, 64, 26), "选择…", BtnSecondary);
            }
            DrawOption(g, "opt3", _y3, 56,
                UninstallChoice.Delete, "删除数据",
                "连 todos.db 和 data 一起删。删掉就找不回来。", true);

            bool working = _state == St.Working;
            if (working) Ui.DrawText(g, _status, FSmall, Ui.Brass, 28, 350, 504);
            else Ui.DrawText(g, "卸载会清掉程序、开始菜单入口和开机项；数据按上面选的办。", FSmall, Ui.Aux, 28, 350, 504);

            DrawButton(g, "cancel", new Rectangle(300, 396, 96, 36), "取消", BtnSecondary);
            DrawButton(g, "uninstall", new Rectangle(400, 396, 132, 36),
                working ? "正在卸载" : "卸载", BtnPrimary);
        }

        private void DrawOption(Graphics g, string id, int y, int h, UninstallChoice value,
            string title, string desc, bool danger)
        {
            Zone z = FindZone(id);
            bool hover = z != null && z.Hover && _state == St.Choice;
            if (hover) Ui.Fill(g, Ui.Hover, 24, y, 512, h);
            bool selected = _choice == value;
            Rectangle box = new Rectangle(32, y + 8, 16, 16);
            DrawRadioSquare(g, selected, box, hover);
            Color titleColor = danger ? Ui.Danger : (selected ? Ui.Ink : Ui.Sub);
            Ui.DrawText(g, title, FBodyStrong, titleColor, 56, y + 4, 300);
            Ui.DrawText(g, desc, FSmall, Ui.Aux, 56, y + 28, 460);
        }

        private void PaintConfirm(Graphics g)
        {
            Ui.DrawText(g, "删掉数据？", FTitle, Ui.Ink, 28, 20, 300);
            Ui.Fill(g, Ui.Danger, 28, 76, 48, 2);
            Ui.DrawWrapped(g, "会删除 todos.db 和 data 下所有文件，不能恢复。", FBody, Ui.Ink, 28, 94, 504);
            Ui.DrawWrapped(g, "程序也会一起卸载。如果只是暂时不用，点「返回」，改成保留或导出数据。",
                FSmall, Ui.Aux, 28, 136, 504);

            DrawButton(g, "really", new Rectangle(280, 396, 112, 36), "仍要删除", BtnDanger);
            DrawButton(g, "back", new Rectangle(400, 396, 132, 36), "返回", BtnPrimary);
        }

        private void PaintDone(Graphics g)
        {
            Ui.DrawText(g, "卸载完成", FTitle, Ui.Ink, 28, 20, 300);
            Ui.DrawText(g, "下面逐项列出这次实际的结果。", FSmall, Ui.Aux, 28, 58, 504);
            Ui.Fill(g, Ui.Line, 28, 82, 504, 1);

            int y = 94;
            int lineH = 27;
            for (int i = 0; i < _results.Count; i++)
            {
                StepResult r = _results[i];
                int ry = y + i * lineH;
                if (ry > 316) break;
                Ui.DrawText(g, r.Ok ? "✓" : "✗", FBodyStrong,
                    r.Ok ? Ui.OkColor : Ui.Danger, 28, ry, 22);
                Ui.DrawText(g, r.Name, FBody, Ui.Ink, 52, ry, 180);
                Ui.DrawText(g, r.Detail, FSmall, r.Ok ? Ui.Aux : Ui.Danger, 234, ry + 2, 296);
            }

            int noteY = y + Math.Min(_results.Count, 8) * lineH + 6;
            if (noteY < 340)
            {
                if (_choice == UninstallChoice.Keep)
                    Ui.DrawText(g, "数据留在安装目录的 data 里；想彻底删掉，重新安装后再卸载并选「删除数据」。",
                        FSmall, Ui.Aux, 28, noteY, 504);
            }
            Ui.DrawText(g, "这个窗口关掉后，卸载程序会自己删掉，不弹命令行窗口。", FSmall, Ui.Aux, 28, 366, 504);

            DrawButton(g, "finish", new Rectangle(400, 396, 132, 36), "完成", BtnPrimary);
        }

        private void PaintError(Graphics g)
        {
            Ui.DrawText(g, "卸载没完成", FTitle, Ui.Ink, 28, 20, 300);
            DrawThreeParts(g, 28, 84, 504, _err);
            DrawButton(g, "cancel", new Rectangle(300, 396, 96, 36), "取消", BtnSecondary);
            DrawButton(g, "retry", new Rectangle(400, 396, 132, 36), "重试", BtnPrimary);
        }

        private void Pick(UninstallChoice value)
        {
            if (_state != St.Choice) return;
            _choice = value;
            Rebuild();
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选一个放数据副本的文件夹（会在里面新建「醒酒器数据导出-时间」）";
                dlg.ShowNewFolderButton = true;
                try
                {
                    string parent = Path.GetDirectoryName(_exportDir);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) dlg.SelectedPath = parent;
                    else dlg.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    _exportDir = Path.Combine(dlg.SelectedPath,
                        "醒酒器数据导出-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
                    _choice = UninstallChoice.Export;
                    Rebuild();
                }
            }
        }

        private void StartUninstall()
        {
            if (_choice == UninstallChoice.Delete)
            {
                _state = St.Confirm;
                Rebuild();
                return;
            }
            RunUninstall();
        }

        private void RunUninstall()
        {
            if (_state == St.Working) return;
            _state = St.Working;
            _status = "正在准备…";
            Rebuild();

            UninstallEngine engine = new UninstallEngine();
            engine.L = _layout;
            engine.Choice = _choice;
            engine.ExportDir = _exportDir;
            engine.Progress = delegate(string s)
            {
                _status = s;
                Application.DoEvents();
                Refresh();
            };
            try
            {
                engine.Run();
                _engine = engine;
                _results = engine.Results;
                _exitCode = 0;
                for (int i = 0; i < _results.Count; i++) if (!_results[i].Ok) _exitCode = 1;
                _status = "卸载完成。";
                _state = St.Done;
            }
            catch (HumanException he)
            {
                _err = he.Info;
                _retry = RunUninstall;
                _state = St.Error;
                _exitCode = 1;
            }
            catch (Exception ex)
            {
                _err = HumanError.Map(ex, "delete", _layout.InstallDir);
                _retry = RunUninstall;
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

        private void BackToChoice()
        {
            _state = St.Choice;
            Rebuild();
        }

        private void CancelAll()
        {
            if (_state == St.Working) return;
            _exitCode = 1;
            Close();
        }

        private void Finish()
        {
            ScheduleCleanup();
            Close();
        }

        private void ScheduleCleanup()
        {
            if (_cleanupScheduled) return;
            bool removeDir = _engine != null && _engine.RemoveInstallDir;
            string renamed = _engine != null ? _engine.RenamedSelf : null;
            SelfClean.Schedule(Application.ExecutablePath, _layout.InstallDir, removeDir, renamed);
            _cleanupScheduled = true;
        }

        protected override void OnEscape()
        {
            if (_state == St.Working) return;
            if (_state == St.Confirm) { BackToChoice(); return; }
            if (_state == St.Done) { Finish(); return; }
            CancelAll();
        }

        protected override void OnEnter()
        {
            if (_state == St.Working) return;
            if (_state == St.Choice) StartUninstall();
            else if (_state == St.Confirm) BackToChoice();
            else if (_state == St.Done) Finish();
            else Retry();
        }

        protected override void OnCloseRequested()
        {
            if (_state == St.Working) return;
            if (_state == St.Done) { Finish(); return; }
            CancelAll();
        }
    }

    internal static class UninstallProgram
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
                layout = ResolveLayout(a);
            }
            catch (HumanException)
            {
                return 2;
            }

            if (a.Has("shot"))
            {
                // 截图只展示默认安装位置，不读真实目录（不写任何东西）
                if (!layout.Sandbox) layout.InstallDir = @"D:\醒酒器";
                UninstallForm shotForm = new UninstallForm(layout);
                shotForm.SetNoActivate();
                shotForm.PrepareState(a.Get("state", "uninstall"));
                return Capture(shotForm, a.Get("shot", "uninstall.png"));
            }

            if (a.Has("silent"))
            {
                return RunSilent(layout, a);
            }

            UninstallForm form = new UninstallForm(layout);
            Application.Run(form);
            return form.ExitCode;
        }

        private static Layout ResolveLayout(CmdArgs a)
        {
            if (a.Has("sandbox")) return Layout.FromSandbox(a.Get("sandbox", ""));

            string selfDir = Path.GetDirectoryName(Application.ExecutablePath);
            string marker = Path.Combine(selfDir, "sandbox.txt");
            if (File.Exists(marker))
            {
                string root = null;
                try { root = File.ReadAllText(marker).Trim(); }
                catch { }
                if (!string.IsNullOrEmpty(root))
                    return Layout.FromSandboxMarker(root, selfDir);
            }
            return Layout.ForRealUninstall(selfDir);
        }

        private static int RunSilent(Layout layout, CmdArgs a)
        {
            UninstallChoice choice = ParseChoice(a.Get("choice", "keep"));
            string exportDir = null;
            if (a.Has("export-dir") && !string.IsNullOrEmpty(a.Get("export-dir", "")))
                exportDir = Path.GetFullPath(a.Get("export-dir", ""));
            else if (choice == UninstallChoice.Export)
                exportDir = UninstallEngine.DefaultExportDir(layout, false);

            UninstallEngine engine = new UninstallEngine();
            engine.L = layout;
            engine.Choice = choice;
            engine.ExportDir = exportDir;
            try
            {
                engine.Run();
                string cleanupCmd = SelfClean.Schedule(Application.ExecutablePath, layout.InstallDir,
                    engine.RemoveInstallDir, engine.RenamedSelf);
                WriteResult(layout, choice, exportDir, engine.Results, null, engine.RemoveInstallDir, cleanupCmd);
                bool anyFail = false;
                for (int i = 0; i < engine.Results.Count; i++) if (!engine.Results[i].Ok) anyFail = true;
                return anyFail ? 1 : 0;
            }
            catch (HumanException he)
            {
                WriteResult(layout, choice, exportDir, null, he.Info, false, null);
                return 1;
            }
            catch (Exception ex)
            {
                WriteResult(layout, choice, exportDir, null,
                    HumanError.Map(ex, "delete", layout.InstallDir), false, null);
                return 1;
            }
        }

        private static UninstallChoice ParseChoice(string s)
        {
            if (string.Equals(s, "export", StringComparison.OrdinalIgnoreCase)) return UninstallChoice.Export;
            if (string.Equals(s, "delete", StringComparison.OrdinalIgnoreCase)) return UninstallChoice.Delete;
            return UninstallChoice.Keep;
        }

        private static void WriteResult(Layout layout, UninstallChoice choice, string exportDir,
            List<StepResult> results, HumanError err, bool removeDir, string cleanupCmd)
        {
            if (!layout.Sandbox) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ok=" + (err == null ? "true" : "false"));
                sb.AppendLine("choice=" + ChoiceName(choice));
                if (!string.IsNullOrEmpty(exportDir)) sb.AppendLine("export_dir=" + exportDir);
                sb.AppendLine("remove_install_dir=" + (removeDir ? "1" : "0"));
                sb.AppendLine("install_dir=" + layout.InstallDir);
                bool cleanupOk = cleanupCmd != null;
                sb.AppendLine("cleanup=" + (cleanupOk ? "ok" : "failed"));
                sb.AppendLine("cleanup_cmd=" + (cleanupOk ? cleanupCmd : "-"));
                sb.AppendLine("--");
                if (results != null)
                {
                    for (int i = 0; i < results.Count; i++)
                    {
                        StepResult r = results[i];
                        string line = (r.Ok ? "✓ " : "✗ ") + r.Name;
                        if (!string.IsNullOrEmpty(r.Detail)) line += " · " + r.Detail;
                        sb.AppendLine(line);
                    }
                }
                if (err != null)
                {
                    sb.AppendLine("what=" + err.What);
                    sb.AppendLine("why=" + err.Why);
                    sb.AppendLine("todo=" + err.Todo);
                }
                File.WriteAllText(Path.Combine(layout.Root, "uninstall-result.txt"),
                    sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static string ChoiceName(UninstallChoice c)
        {
            if (c == UninstallChoice.Export) return "export";
            if (c == UninstallChoice.Delete) return "delete";
            return "keep";
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
