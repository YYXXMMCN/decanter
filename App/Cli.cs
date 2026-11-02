// Cli.cs — 命令行接口（AI 与人都能用；逻辑层，不依赖 UI）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Decanter.Core;

namespace Decanter.App
{
    public static class Paths
    {
        public static string Home()
        {
            string h = Environment.GetEnvironmentVariable("DECANTER_HOME");
            if (!string.IsNullOrEmpty(h)) return h;
            // 总裁要求：数据放程序目录（安装目录\data），便携可带走
            return System.AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }
        public static string DataDir() { return Path.Combine(Home(), "data"); }
        public static string DbPath() { return Path.Combine(DataDir(), "todos.db"); }
    }

    public static class Cli
    {
        public static int Run(string[] args)
        {
            List<string> a = new List<string>();
            foreach (string s in args) if (s != "--cli") a.Add(s);
            string cmd = a.Count > 0 ? a[0] : "help";
            try
            {
                using (Store store = new Store(Paths.DbPath()))
                {
                    if (cmd == "init") { Console.WriteLine("db: " + Paths.DbPath()); Console.WriteLine("ok"); return 0; }
                    if (cmd == "add") return CmdAdd(store, a);
                    if (cmd == "list") return CmdList(store, a);
                    if (cmd == "next") return CmdNext(store);
                    if (cmd == "done") return CmdDone(store, a);
                    if (cmd == "restore") return CmdRestore(store, a);
                    if (cmd == "pin") return CmdPin(store, a);
                    if (cmd == "del") return CmdDel(store, a);
                    if (cmd == "edit") return CmdEdit(store, a);
                    if (cmd == "export") return CmdExport(store, a);
                    if (cmd == "import") return CmdImport(store, a);
                    if (cmd == "db-check") { Console.WriteLine(store.IntegrityCheck()); return 0; }
                    if (cmd == "settings") return CmdSettings(store);
                    if (cmd == "set") return CmdSet(store, a);
                    if (cmd == "api-token") { Console.WriteLine(store.GetSetting("api_token", "")); return 0; }
                    if (cmd == "seed") return CmdSeed(store);
                    Console.WriteLine("用法: Decanter.exe --cli <init|add [--note x]|list|next|done|restore <id>|pin|del|edit <id> --note x|export|import|db-check|seed>");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR " + ex.Message);
                return 2;
            }
        }

        private static string Arg(List<string> a, string name, string def)
        {
            for (int i = 0; i < a.Count - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
        private static bool Flag(List<string> a, string name) { return a.Contains(name); }

        private static int CmdAdd(Store store, List<string> a)
        {
            string title = null;
            for (int i = 1; i < a.Count; i++)
            {
                if (!a[i].StartsWith("--")) { title = a[i]; break; }
            }
            if (string.IsNullOrEmpty(title)) { Console.Error.WriteLine("缺少标题"); return 2; }
            TodoItem t = new TodoItem();
            t.Title = title;
            int imp;
            if (int.TryParse(Arg(a, "--imp", "2"), out imp)) t.Importance = Math.Max(1, Math.Min(3, imp));
            string due = Arg(a, "--due", null);
            if (!string.IsNullOrEmpty(due)) t.DueUtc = ParseLocal(due);
            string remind = Arg(a, "--remind", null);
            if (!string.IsNullOrEmpty(remind)) t.RemindUtc = ParseLocal(remind);
            t.Recurrence = Arg(a, "--repeat", "");
            if (t.IsRecurring) t.RecurrenceNextUtc = Recurrence.NextUtc(t.Recurrence, DateTime.Now);
            t.Pinned = Flag(a, "--pin");
            string note = Arg(a, "--note", null);
            if (note != null) t.Note = note;
            long id = store.Add(t);
            Console.WriteLine("added id=" + id);
            return 0;
        }

        private static long ParseLocal(string s)
        {
            DateTime dt;
            string[] fmts = new string[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "MM-dd HH:mm" };
            if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return TimeUtil.ToUtc(dt);
            if (DateTime.TryParse(s, out dt)) return TimeUtil.ToUtc(dt);
            throw new Exception("无法解析时间: " + s);
        }

        private static int CmdList(Store store, List<string> a)
        {
            List<TodoItem> list = store.Active();
            bool json = Flag(a, "--json");
            DateTime now = DateTime.Now;
            bool growthAll = store.GetSetting("growth_all", "0") == "1";
            bool growthOn = store.GetSetting("growth_on", "1") == "1";
            if (json)
            {
                List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
                foreach (TodoItem t in list)
                {
                    ScoreResult r = Scoring.Evaluate(t, now, Scoring.GrowthEnabled(t, growthAll, growthOn));
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["id"] = t.Id; d["title"] = t.Title; d["importance"] = t.Importance;
                    d["tier"] = r.Tier; d["lines"] = r.Lines; d["score"] = Math.Round(r.Score, 4);
                    d["growth"] = Math.Round(r.Growth, 4); d["overdue"] = r.Overdue;
                    d["pinned"] = t.Pinned; d["recurring"] = t.IsRecurring; d["note"] = t.Note;
                    d["due"] = t.DueLocal.HasValue ? t.DueLocal.Value.ToString("yyyy-MM-dd HH:mm") : null;
                    rows.Add(d);
                }
                Console.WriteLine(new JavaScriptSerializer().Serialize(rows));
            }
            else
            {
                foreach (TodoItem t in list)
                {
                    ScoreResult r = Scoring.Evaluate(t, now, Scoring.GrowthEnabled(t, growthAll, growthOn));
                    string mark = t.Pinned ? "*" : " ";
                    string due = t.DueLocal.HasValue ? t.DueLocal.Value.ToString("MM-dd HH:mm") : "";
                    Console.WriteLine(string.Format("{0} [{1}] {2,-40} {3:0.00} {4} {5} {6}", mark, r.Tier, Trunc(t.Title, 40), r.Score, due, t.Recurrence, Trunc(t.Note, 30)));
                }
                if (list.Count == 0) Console.WriteLine("(空)");
            }
            return 0;
        }

        private static string Trunc(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        private static int CmdNext(Store store)
        {
            TodoItem t = store.Next();
            if (t == null) { Console.WriteLine("(空)"); return 0; }
            Console.WriteLine(t.Title);
            return 0;
        }

        private static int CmdDone(Store store, List<string> a)
        {
            long id;
            if (a.Count < 2 || !long.TryParse(a[1], out id)) { Console.Error.WriteLine("用法: done <id>"); return 2; }
            bool spawned;
            TodoItem t = store.Complete(id, out spawned);
            if (t == null) { Console.Error.WriteLine("找不到或已完成"); return 2; }
            Console.WriteLine("done id=" + id + (spawned ? " (已生成下一次)" : ""));
            return 0;
        }

        private static int CmdRestore(Store store, List<string> a)
        {
            long id;
            if (a.Count < 2 || !long.TryParse(a[1], out id)) { Console.Error.WriteLine("用法: restore <id>"); return 2; }
            if (!store.Restore(id)) { Console.Error.WriteLine("找不到或未完成: " + id); return 2; }
            Console.WriteLine("ok");
            return 0;
        }

        private static int CmdPin(Store store, List<string> a)
        {
            long id;
            if (a.Count < 2 || !long.TryParse(a[1], out id)) return 2;
            TodoItem t = store.Get(id);
            if (t == null) return 2;
            t.Pinned = !t.Pinned;
            store.Update(t);
            Console.WriteLine("pin=" + t.Pinned);
            return 0;
        }

        private static int CmdDel(Store store, List<string> a)
        {
            long id;
            if (a.Count < 2 || !long.TryParse(a[1], out id)) return 2;
            store.SoftDelete(id);
            Console.WriteLine("deleted id=" + id);
            return 0;
        }

        private static int CmdEdit(Store store, List<string> a)
        {
            long id;
            if (a.Count < 2 || !long.TryParse(a[1], out id)) { Console.Error.WriteLine("用法: edit <id> [--title x] [--imp n] [--due t] [--remind t] [--repeat r] [--pin 0|1]"); return 2; }
            TodoItem t = store.Get(id);
            if (t == null) { Console.Error.WriteLine("未找到该待办"); return 2; }
            string title = Arg(a, "--title", null);
            if (!string.IsNullOrEmpty(title)) t.Title = title;
            string imp = Arg(a, "--imp", null);
            int iv;
            if (imp != null && int.TryParse(imp, out iv)) t.Importance = Math.Max(1, Math.Min(3, iv));
            string due = Arg(a, "--due", null);
            if (due != null) t.DueUtc = due.Length == 0 ? (long?)null : ParseLocal(due);
            string remind = Arg(a, "--remind", null);
            if (remind != null) t.RemindUtc = remind.Length == 0 ? (long?)null : ParseLocal(remind);
            string repeat = Arg(a, "--repeat", null);
            if (repeat != null) { t.Recurrence = repeat; t.RecurrenceNextUtc = t.IsRecurring ? Recurrence.NextUtc(repeat, DateTime.Now) : null; }
            string note = Arg(a, "--note", null);
            if (note != null) t.Note = note;
            string pin = Arg(a, "--pin", null);
            if (pin != null) t.Pinned = pin == "1" || pin.ToLowerInvariant() == "true";
            store.Update(t);
            Console.WriteLine("edited id=" + id);
            return 0;
        }

        private static int CmdExport(Store store, List<string> a)
        {
            string path = a.Count >= 2 ? a[1] : Path.Combine(Environment.CurrentDirectory, "todos.decanter");
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schema_version"] = 1;
            root["exported_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            using (Stmt st = store.Db.Prepare("SELECT id,title,note,importance,due_utc,remind_utc,recurrence,recurrence_next_utc,pinned,done,created_utc,updated_utc FROM todos WHERE deleted=0"))
            {
                while (st.Step())
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["id"] = st.GetInt64(0); d["title"] = st.GetText(1); d["note"] = st.GetText(2);
                    d["importance"] = st.GetInt64(3);
                    d["due_utc"] = st.GetLongOrNull(4); d["remind_utc"] = st.GetLongOrNull(5);
                    d["recurrence"] = st.GetText(6); d["recurrence_next_utc"] = st.GetLongOrNull(7);
                    d["pinned"] = st.GetInt64(8) != 0; d["done"] = st.GetInt64(9) != 0;
                    d["created_utc"] = st.GetInt64(10); d["updated_utc"] = st.GetInt64(11);
                    rows.Add(d);
                }
            }
            root["todos"] = rows;
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(root), new UTF8Encoding(false));
            Console.WriteLine("exported " + rows.Count + " → " + path);
            Console.WriteLine("note: export contains todo text (possibly sensitive), keep it safe.");
            return 0;
        }

        private static int CmdImport(Store store, List<string> a)
        {
            string path = a.Count >= 2 ? a[1] : null;
            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("找不到文件"); return 2; }
            int skipped;
            int n = Journal.Import(store, path, out skipped);
            Console.WriteLine("imported " + n + " (skipped duplicates " + skipped + ")");
            return 0;
        }

        private static int CmdSettings(Store store)
        {
            using (Stmt st = store.Db.Prepare("SELECT key,value FROM settings ORDER BY key"))
            {
                while (st.Step()) Console.WriteLine(st.GetText(0) + "=" + st.GetText(1));
            }
            return 0;
        }

        private static int CmdSet(Store store, List<string> a)
        {
            if (a.Count < 3) { Console.Error.WriteLine("用法: set <key> <value>"); return 2; }
            store.SetSetting(a[1], a[2]);
            Console.WriteLine("set " + a[1] + "=" + a[2]);
            return 0;
        }

        private static int CmdSeed(Store store)
        {
            string[] titles = new string[] { "回复客户邮件", "写周报", "预约体检", "买牛奶", "整理桌面", "看完那篇论文" };
            int[] imps = new int[] { 3, 2, 2, 1, 1, 2 };
            for (int i = 0; i < titles.Length; i++)
            {
                TodoItem t = new TodoItem();
                t.Title = titles[i];
                t.Importance = imps[i];
                if (i % 2 == 0) t.DueLocal = DateTime.Now.AddHours(6 + i * 6);
                store.Add(t);
            }
            Console.WriteLine("seeded " + titles.Length);
            return 0;
        }
    }
}
