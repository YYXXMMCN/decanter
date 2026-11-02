// v0.11 App · DebugSelfCheck: --debug-selftest, real HTTP against the gate, then exit
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public static class DebugSelfCheck
    {
        public static int Run(string[] args)
        {
            string outPath = "debug-selftest.json";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--out") outPath = args[i + 1];

            List<string> notes = new List<string>();
            bool all = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = false;   // the debug gate runs on a worker; this harness serializes it
            DebugGate.DirectUi = false;
            DebugGate.QueueUi = true;

            Store store = new Store(Paths.DbPath());
            InitSettings(store);
            try { TodoItem seed = new TodoItem(); seed.Title = "delete-me"; store.Add(seed); } catch { }
            FloatingForm floating = new FloatingForm(store);
            MainForm main = new MainForm(store, floating);
            main.StartPosition = FormStartPosition.Manual;
            main.Location = new System.Drawing.Point(-30000, -30000);
            main.ShowInTaskbar = false;
            main.Show();
            Application.DoEvents();

            int port = DebugGate.FreePort();
            DebugGate gate = new DebugGate(main, store, port, DebugGate.ControlMode);
            bool started = gate.Start();
            notes.Add((started ? "PASS" : "FAIL") + " gate start port=" + port.ToString(CultureInfo.InvariantCulture));
            all = all && started;

            string b = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            string tok = gate.Token;
            try
            {
                string state = Get(b + "/debug/state?token=" + tok);
                bool stateOk = state != null && state.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0 && state.IndexOf("\"theme\"") >= 0;
                notes.Add((stateOk ? "PASS" : "FAIL") + " GET /debug/state");
                all = all && stateOk;

                string tree = Get(b + "/debug/tree?token=" + tok);
                bool treeOk = tree != null && tree.IndexOf("rail.view.today") >= 0 && tree.IndexOf("compose.title") >= 0;
                notes.Add((treeOk ? "PASS" : "FAIL") + " GET /debug/tree (stable ids)");
                all = all && treeOk;

                int getWrite = Status(b + "/debug/click?token=" + tok, "GET", null);
                notes.Add((getWrite == 405 ? "PASS" : "FAIL") + " GET write -> " + getWrite);
                all = all && getWrite == 405;

                DebugGate mon = new DebugGate(main, store, DebugGate.FreePort(), DebugGate.MonitorMode);
                bool monOk = mon.Start();
                int monPost = monOk ? Status("http://127.0.0.1:" + mon.Port.ToString(CultureInfo.InvariantCulture) + "/debug/click?token=" + mon.Token, "POST", "{\"id\":\"rail.view.today\"}") : -1;
                notes.Add((monPost == 403 ? "PASS" : "FAIL") + " monitor POST -> " + monPost);
                all = all && monPost == 403;
                mon.Dispose();

                int bad = Status(b + "/debug/state?token=deadbeef", "GET", null);
                notes.Add((bad == 401 ? "PASS" : "FAIL") + " bad token -> " + bad);
                all = all && bad == 401;

                int click = Status(b + "/debug/click?token=" + tok, "POST", "{\"id\":\"rail.view.today\"}");
                notes.Add((click == 200 ? "PASS" : "FAIL") + " POST click rail.view.today");
                all = all && click == 200;

                int theme = Status(b + "/debug/theme?token=" + tok, "POST", "{}");
                notes.Add((theme == 200 ? "PASS" : "FAIL") + " POST theme");
                all = all && theme == 200;

                // two-step delete over HTTP: reset view to all, seed a row, delete -> confirming, cancel -> false,
                // delete+confirm -> row gone
                Status(b + "/debug/click?token=" + tok, "POST", "{\"id\":\"rail.view.all\"}");
                Status(b + "/debug/type?token=" + tok, "POST", "{\"text\":\"delete-me\"}");
                Status(b + "/debug/click?token=" + tok, "POST", "{\"id\":\"compose.add\"}");
                string treeAll = Get(b + "/debug/tree?token=" + tok);
                long delId = FirstRowId(treeAll);
                bool seedOk = delId > 0;
                notes.Add((seedOk ? "PASS" : "FAIL") + " seed row id=" + delId.ToString(CultureInfo.InvariantCulture));
                all = all && seedOk;
                if (delId > 0)
                {
                    Status(b + "/debug/delete?token=" + tok, "POST", "{\"id\":\"" + delId.ToString(CultureInfo.InvariantCulture) + "\"}");
                    string st1 = Get(b + "/debug/state?token=" + tok);
                    bool conf1 = st1 != null && st1.IndexOf("\"confirming\":true", StringComparison.Ordinal) >= 0;
                    notes.Add((conf1 ? "PASS" : "FAIL") + " delete -> confirming=true");
                    Status(b + "/debug/cancel?token=" + tok, "POST", "{}");
                    string st2 = Get(b + "/debug/state?token=" + tok);
                    bool conf2 = st2 != null && st2.IndexOf("\"confirming\":false", StringComparison.Ordinal) >= 0;
                    notes.Add((conf2 ? "PASS" : "FAIL") + " cancel -> confirming=false");
                    Status(b + "/debug/delete?token=" + tok, "POST", "{\"id\":\"" + delId.ToString(CultureInfo.InvariantCulture) + "\"}");
                    Status(b + "/debug/confirm?token=" + tok, "POST", "{}");
                    string treeAfter = Get(b + "/debug/tree?token=" + tok);
                    bool gone = treeAfter != null && treeAfter.IndexOf("\"row." + delId.ToString(CultureInfo.InvariantCulture) + "\"", StringComparison.Ordinal) < 0;
                    notes.Add((gone ? "PASS" : "FAIL") + " delete+confirm -> row gone");
                    all = all && conf1 && conf2 && gone;
                }
                byte[] png = null;
                Thread shotThread = new Thread(delegate () { png = GetBytes(b + "/debug/screenshot?token=" + tok); });
                shotThread.IsBackground = true;
                shotThread.Start();
                DateTime shotUntil = DateTime.UtcNow.AddSeconds(5);
                while (shotThread.IsAlive && DateTime.UtcNow < shotUntil)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                bool shotOk = png != null && png.Length > 1000;
                notes.Add((shotOk ? "PASS" : "FAIL") + " screenshot bytes=" + (png == null ? 0 : png.Length).ToString(CultureInfo.InvariantCulture));
                all = all && shotOk;
            }
            catch (Exception ex)
            {
                notes.Add("FAIL exception " + ex.GetType().Name + ": " + ex.Message);
                all = false;
            }

            gate.Dispose();
            main.Hide();
            try { main.Dispose(); } catch { }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"suite\":\"decanter-v011-debug\",\"allPassed\":").Append(all ? "true" : "false").Append(",\"checks\":[");
            for (int i = 0; i < notes.Count; i++) { if (i > 0) sb.Append(","); sb.Append("\"").Append(notes[i].Replace("\"", "'")).Append("\""); }
            sb.Append("]}");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("DEBUG SELFTEST " + (all ? "OK" : "FAILED") + " -> " + outPath);
            Environment.Exit(all ? 0 : 1);
            return all ? 0 : 1;
        }

        private static void InitSettings(Store store)
        {
            if (store.GetSetting("theme", "") == "") store.SetSetting("theme", "system");
            if (store.GetSetting("lang", "") == "") store.SetSetting("lang", "zh");
        }

        private static long FirstRowId(string tree)
        {
            if (tree == null) return 0;
            int at = tree.IndexOf("\"id\":\"row.", StringComparison.Ordinal);
            if (at < 0) return 0;
            int start = at + 10;   // after: "id":"row.
            int end = start;
            while (end < tree.Length && char.IsDigit(tree[end])) end++;
            long v; return long.TryParse(tree.Substring(start, end - start), out v) ? v : 0;
        }

        private static void Pump()
        {
            if (!DebugGate.QueueUi) return;
            Application.DoEvents();
            Thread.Sleep(20);
        }

        private static string Get(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = null; req.Timeout = 3000;
                string text;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd();
                Pump();
                return text;
            }
            catch { return null; }
        }

        private static byte[] GetBytes(string url)
        {
            try { using (WebClient wc = new WebClient()) { wc.Proxy = null; return wc.DownloadData(url); } }
            catch { return null; }
        }

        private static int Status(string url, string method, string body)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = null; req.Method = method; req.Timeout = 3000;
                if (body != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(body);
                    req.ContentType = "application/json"; req.ContentLength = b.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)res.StatusCode;
                    Pump();
                    return code;
                }
            }
            catch (WebException we)
            {
                if (we.Response != null) return (int)((HttpWebResponse)we.Response).StatusCode;
                return -1;
            }
        }
    }
}
