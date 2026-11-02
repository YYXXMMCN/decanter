// Shots.cs — 交付截图模式（只用于出样张/交付证据，不参与正常运行）
// 用法：Decanter.exe --shot <out.png> --state main|main-day|settings|float|float-expanded|empty|dpi200|welcome
// 说明：为取证而短时显示窗口，截图后自动退出；数据目录由 DECANTER_HOME 隔离（绝不碰真实库）。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public static class Shots
    {
        public static int Run(string outPath, string state, string themeSetting)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Store store = new Store(Paths.DbPath());
                bool empty = state == "empty";
                if (empty) store.SetSetting("demo_seeded", "1");   // 阻止示例种子，得到真空态
                Program.InitSettings(store);
                store.SetSetting("theme", themeSetting);
                Win32Theme.SetAppMode(themeSetting == "night");
                store.SetSetting("firstrun", "1");
                Strings.Lang = store.GetSetting("lang", "zh");

                if (state == "welcome")
                {
                    // 首启引导：只构造 WelcomeForm，不构造主窗/浮窗
                    WelcomeForm wf = new WelcomeForm(themeSetting);
                    wf.Show();
                    CaptureLater(wf, outPath, 900);
                    Application.Run(wf);
                    return 0;
                }

                FloatingForm floating = new FloatingForm(store);

                if (state == "settings" || state == "settings-bottom")
                {
                    store.SetSetting("api_token", "test-token-show-only");   // 截图用：不把真实令牌写进交付图
                    SettingsForm sf = new SettingsForm(store, floating);
                    sf.Show();
                    if (state == "settings-bottom")
                    {
                        Timer st = new Timer();
                        st.Interval = 400;
                        st.Tick += delegate { st.Stop(); st.Dispose(); sf.TestScrollContent(2000); };
                        st.Start();
                    }
                    CaptureLater(sf, outPath, 1200);
                    Application.Run(sf);
                    return 0;                }

                if (state == "float" || state == "float-expanded")
                {
                    floating.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 380, 160);
                    floating.Show();
                    floating.RefreshData();
                    if (state == "float-expanded") floating.TestExpand();
                    CaptureLater(floating, outPath, 900);
                    Application.Run(floating);
                    return 0;
                }

                MainForm main = new MainForm(store, floating);
                floating.OpenMain += delegate { main.RefreshList(); main.Show(); main.Activate(); };
                floating.ExitApp += delegate { Application.Exit(); };
                main.Show();
                main.RefreshList();
                if (state == "main-day") { store.SetSetting("theme", "day"); main.TestApplyThemeSetting(); }
                float sc = main.TestDpi();
                if (state == "wide") main.TestSetClientSize((int)Math.Round(1600 * sc), (int)Math.Round(900 * sc));
                if (state == "narrow") main.TestSetClientSize((int)Math.Round(880 * sc), (int)Math.Round(560 * sc));
                if (state == "dpi200") { main.TestForceDpi(2f); main.TestSetClientSize(Layout.DefaultW * 2, Layout.DefaultH * 2); }
                if (state == "empty") main.RefreshList();
                if (state == "search") { main.TestSearch("猫粮"); }
                if (state == "selected") { main.RefreshList(); main.TestSelectIndex(1); }
                if (state == "editing")
                {
                    main.RefreshList();
                    main.TestSelectIndex(1);
                    TodoItem first = main.ListView.Items.Count > 1 ? main.ListView.Items[1] : (store.Active().Count > 0 ? store.Active()[0] : null);
                    if (first != null) main.TestBeginEditById(first.Id);
                }
                CaptureLater(main, outPath, 1000);
                Application.Run(main);
                return 0;
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(outPath + ".error.txt", ex.ToString()); } catch { }
                return 3;
            }
        }

        private static void CaptureLater(Form f, string outPath, int delayMs)
        {
            Timer t = new Timer();
            t.Interval = delayMs;
            t.Tick += delegate
            {
                t.Stop();
                try { Capture(f, outPath); } catch (Exception ex) { try { System.IO.File.WriteAllText(outPath + ".error.txt", ex.ToString()); } catch { } }
                // 直接退出进程：MainForm/FloatingForm 的关闭是「隐藏到托盘」，用 Close() 不会结束消息循环
                Environment.Exit(0);
            };
            t.Start();
        }

        private static void Capture(Form f, string outPath)
        {
            // 用 DrawToBitmap 只画自己的窗口：避免把桌面上别的窗口（甚至 DSH 的浮层）一起截进来
            Bitmap bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            string dir = System.IO.Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            bmp.Save(outPath, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
