// Store.cs — 数据层（SQLite 单文件，逻辑层，无 UI）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Decanter.Core
{
    public sealed class Store : IDisposable
    {
        private readonly SqliteDb _db;
        private readonly object _revLock = new object();
        private readonly List<string> _events = new List<string>();   // "HH:mm:ss kind"，最多 5 条，不含业务原文
        public event Action Changed;                                  // W6：数据变更（订阅方自行 BeginInvoke 合并）

        public Store(string dbPath)
        {
            string dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _db = new SqliteDb(dbPath);
            InitSchema();
        }

        // W6：rev 存 settings（无 schema），每次写操作自增；state/调试面板只暴露 rev 与事件类型
        public long Rev
        {
            get
            {
                long v;
                return long.TryParse(GetSetting("rev", "0"), out v) ? v : 0;
            }
        }

        public List<string> RecentEvents()
        {
            lock (_revLock) return new List<string>(_events);
        }

        private void RecordEvent(string kind)
        {
            lock (_revLock)
            {
                long rev = Rev + 1;
                WriteSetting("rev", rev.ToString(CultureInfo.InvariantCulture));
                _events.Add(DateTime.Now.ToString("HH:mm:ss") + " " + kind);
                while (_events.Count > 5) _events.RemoveAt(0);
            }
            Action h = Changed;
            if (h != null) { try { h(); } catch { } }
        }

        private void WriteSetting(string key, string value)
        {
            using (Stmt st = _db.Prepare("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value"))
            {
                st.Bind(1, key).Bind(2, value);
                st.Step();
            }
        }

        public SqliteDb Db { get { return _db; } }

        private void InitSchema()
        {
            _db.Exec(@"
CREATE TABLE IF NOT EXISTS todos(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  note TEXT NOT NULL DEFAULT '',
  importance INTEGER NOT NULL DEFAULT 2,
  due_utc INTEGER NULL,
  remind_utc INTEGER NULL,
  recurrence TEXT NOT NULL DEFAULT '',
  recurrence_next_utc INTEGER NULL,
  pinned INTEGER NOT NULL DEFAULT 0,
  done INTEGER NOT NULL DEFAULT 0,
  deleted INTEGER NOT NULL DEFAULT 0,
  created_utc INTEGER NOT NULL,
  updated_utc INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '');
CREATE INDEX IF NOT EXISTS idx_todos_active ON todos(deleted, done);
");
        }

        public string GetSetting(string key, string def)
        {
            using (Stmt st = _db.Prepare("SELECT value FROM settings WHERE key=?"))
            {
                st.Bind(1, key);
                if (st.Step()) return st.GetText(0);
            }
            return def;
        }

        public void SetSetting(string key, string value)
        {
            WriteSetting(key, value);
            if (key != "rev") RecordEvent("setting:" + key);
        }

        public long Add(TodoItem t)
        {
            long now = TimeUtil.NowUtc();
            if (t.CreatedUtc == 0) t.CreatedUtc = now;
            if (t.UpdatedUtc == 0) t.UpdatedUtc = now;   // 导入时保留原值
            using (Stmt st = _db.Prepare(@"INSERT INTO todos(title,note,importance,due_utc,remind_utc,recurrence,recurrence_next_utc,pinned,done,deleted,created_utc,updated_utc)
VALUES(?,?,?,?,?,?,?,?,?,0,?,?)"))
            {
                st.Bind(1, t.Title).Bind(2, t.Note).Bind(3, t.Importance);
                if (t.DueUtc.HasValue) st.Bind(4, t.DueUtc.Value); else st.BindNull(4);
                if (t.RemindUtc.HasValue) st.Bind(5, t.RemindUtc.Value); else st.BindNull(5);
                st.Bind(6, t.Recurrence ?? "");
                if (t.RecurrenceNextUtc.HasValue) st.Bind(7, t.RecurrenceNextUtc.Value); else st.BindNull(7);
                st.Bind(8, t.Pinned ? 1 : 0).Bind(9, t.Done ? 1 : 0);
                st.Bind(10, t.CreatedUtc).Bind(11, t.UpdatedUtc);
                st.Step();
            }
            long id = _db.LastInsertRowId();
            RecordEvent("add");
            return id;
        }

        public void Update(TodoItem t)
        {
            UpdateCore(t);
            RecordEvent("update");
        }

        private void UpdateCore(TodoItem t)
        {
            t.UpdatedUtc = TimeUtil.NowUtc();
            using (Stmt st = _db.Prepare(@"UPDATE todos SET title=?,note=?,importance=?,due_utc=?,remind_utc=?,recurrence=?,recurrence_next_utc=?,pinned=?,done=?,updated_utc=? WHERE id=?"))
            {
                st.Bind(1, t.Title).Bind(2, t.Note).Bind(3, t.Importance);
                if (t.DueUtc.HasValue) st.Bind(4, t.DueUtc.Value); else st.BindNull(4);
                if (t.RemindUtc.HasValue) st.Bind(5, t.RemindUtc.Value); else st.BindNull(5);
                st.Bind(6, t.Recurrence ?? "");
                if (t.RecurrenceNextUtc.HasValue) st.Bind(7, t.RecurrenceNextUtc.Value); else st.BindNull(7);
                st.Bind(8, t.Pinned ? 1 : 0).Bind(9, t.Done ? 1 : 0).Bind(10, t.UpdatedUtc).Bind(11, t.Id);
                st.Step();
            }
        }

        public void SoftDelete(long id)
        {
            using (Stmt st = _db.Prepare("UPDATE todos SET deleted=1,updated_utc=? WHERE id=?"))
            {
                st.Bind(1, TimeUtil.NowUtc()).Bind(2, id);
                st.Step();
            }
            RecordEvent("delete");
        }

        public TodoItem Get(long id)
        {
            using (Stmt st = _db.Prepare("SELECT * FROM todos WHERE id=?"))
            {
                st.Bind(1, id);
                if (st.Step()) return Map(st);
            }
            return null;
        }

        private static TodoItem Map(Stmt st)
        {
            TodoItem t = new TodoItem();
            t.Id = st.GetInt64(0);
            t.Title = st.GetText(1) ?? "";
            t.Note = st.GetText(2) ?? "";
            t.Importance = (int)st.GetInt64(3);
            t.DueUtc = st.GetLongOrNull(4);
            t.RemindUtc = st.GetLongOrNull(5);
            t.Recurrence = st.GetText(6) ?? "";
            t.RecurrenceNextUtc = st.GetLongOrNull(7);
            t.Pinned = st.GetInt64(8) != 0;
            t.Done = st.GetInt64(9) != 0;
            t.Deleted = st.GetInt64(10) != 0;
            t.CreatedUtc = st.GetInt64(11);
            t.UpdatedUtc = st.GetInt64(12);
            return t;
        }

        // W5：分组排序 —— 置顶 → 已过期 → 今天 → 本周 → 以后 → 无期；组内沿用 综合分→截止时间
        public static DateTime WeekEnd(DateTime now)
        {
            int days = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
            return now.Date.AddDays(days).AddHours(23).AddMinutes(59);
        }

        private static int DueGroup(TodoItem t, DateTime now, DateTime weekEnd)
        {
            if (!t.DueUtc.HasValue) return 5;             // 无期
            DateTime d = t.DueLocal.Value;
            if (d < now) return 1;                        // 已过期
            if (d.Date == now.Date) return 2;             // 今天
            if (d <= weekEnd) return 3;                   // 本周
            return 4;                                     // 以后
        }

        // 取活跃待办（未删未完成），按"置顶 > 已过期/今天/本周/以后/无期 > 综合分 > 截止时间"排序
        public List<TodoItem> Active()
        {
            return ActiveAt(DateTime.Now);
        }

        // W5 自检用：可注入"现在"的同一排序（生产路径仍走 Active()）
        public List<TodoItem> ActiveAt(DateTime now)
        {
            List<TodoItem> list = new List<TodoItem>();
            using (Stmt st = _db.Prepare("SELECT * FROM todos WHERE deleted=0 AND done=0"))
            {
                while (st.Step()) list.Add(Map(st));
            }
            bool growthAll = GetSetting("growth_all", "0") == "1";
            bool growthOn = GetSetting("growth_on", "1") == "1";
            DateTime weekEnd = WeekEnd(now);
            List<KeyValuePair<TodoItem, ScoreResult>> scored = new List<KeyValuePair<TodoItem, ScoreResult>>();
            foreach (TodoItem t in list)
            {
                bool g = Scoring.GrowthEnabled(t, growthAll, growthOn);
                scored.Add(new KeyValuePair<TodoItem, ScoreResult>(t, Scoring.Evaluate(t, now, g)));
            }
            scored.Sort(delegate(KeyValuePair<TodoItem, ScoreResult> a, KeyValuePair<TodoItem, ScoreResult> b)
            {
                if (a.Key.Pinned != b.Key.Pinned) return a.Key.Pinned ? -1 : 1;
                int ga = DueGroup(a.Key, now, weekEnd);
                int gb = DueGroup(b.Key, now, weekEnd);
                if (ga != gb) return ga.CompareTo(gb);
                int c = b.Value.Score.CompareTo(a.Value.Score);
                if (c != 0) return c;
                long ad = a.Key.DueUtc.HasValue ? a.Key.DueUtc.Value : long.MaxValue;
                long bd = b.Key.DueUtc.HasValue ? b.Key.DueUtc.Value : long.MaxValue;
                return ad.CompareTo(bd);
            });
            List<TodoItem> outList = new List<TodoItem>();
            foreach (KeyValuePair<TodoItem, ScoreResult> kv in scored) outList.Add(kv.Key);
            return outList;
        }

        public List<TodoItem> Completed(int limit)
        {
            List<TodoItem> list = new List<TodoItem>();
            using (Stmt st = _db.Prepare("SELECT * FROM todos WHERE deleted=0 AND done=1 ORDER BY updated_utc DESC LIMIT " + limit))
            {
                while (st.Step()) list.Add(Map(st));
            }
            return list;
        }

        public TodoItem Next()
        {
            List<TodoItem> a = Active();
            return a.Count > 0 ? a[0] : null;
        }

        // 完成：循环待办自动生成下一次
        public TodoItem Complete(long id, out bool spawnedNext)
        {
            spawnedNext = false;
            TodoItem t = Get(id);
            if (t == null || t.Done) return null;
            t.Done = true;
            t.UpdatedUtc = TimeUtil.NowUtc();
            UpdateCore(t);
            RecordEvent("complete");
            if (t.IsRecurring)
            {
                long? next = Recurrence.NextUtc(t.Recurrence, DateTime.Now);
                if (next.HasValue)
                {
                    TodoItem n = new TodoItem();
                    n.Title = t.Title; n.Note = t.Note; n.Importance = t.Importance;
                    n.Recurrence = t.Recurrence; n.RecurrenceNextUtc = next;
                    n.DueUtc = next; n.Pinned = t.Pinned;
                    if (t.RemindUtc.HasValue) n.RemindUtc = next;   // 提醒跟随下一次
                    Add(n);
                    spawnedNext = true;
                }
            }
            return t;
        }

        // W4：恢复已完成待办（done=0）。不生成/不追溯循环的下一次；recurrence_next_utc 原样保留。
        public bool Restore(long id)
        {
            TodoItem t = Get(id);
            if (t == null || !t.Done) return false;
            t.Done = false;
            t.UpdatedUtc = TimeUtil.NowUtc();
            UpdateCore(t);
            RecordEvent("restore");
            return true;
        }

        public int CountDone()
        {
            using (Stmt st = _db.Prepare("SELECT COUNT(*) FROM todos WHERE deleted=0 AND done=1"))
            {
                if (st.Step()) return (int)st.GetInt64(0);
            }
            return 0;
        }

        public int CountActive()
        {
            using (Stmt st = _db.Prepare("SELECT COUNT(*) FROM todos WHERE deleted=0 AND done=0"))
            {
                if (st.Step()) return (int)st.GetInt64(0);
            }
            return 0;
        }

        public string IntegrityCheck()
        {
            using (Stmt st = _db.Prepare("PRAGMA integrity_check"))
            {
                if (st.Step()) return st.GetText(0);
            }
            return "unknown";
        }

        public void Dispose() { _db.Dispose(); }
    }
}
