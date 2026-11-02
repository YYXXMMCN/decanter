// Journal.cs — 数据导出/导入（.decanter = JSON + schema_version），逻辑层
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Decanter.Core
{
    public static class Journal
    {
        public static int Export(Store store, string path)
        {
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
            return rows.Count;
        }

        public static int Import(Store store, string path)
        {
            int skipped;
            return Import(store, path, out skipped);
        }

        public static int Import(Store store, string path, out int skipped)
        {
            skipped = 0;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object ver;
            if (!root.TryGetValue("schema_version", out ver) || Convert.ToInt32(ver) != 1)
                throw new Exception("schema_version 不支持（只支持 1）");
            object todosObj;
            if (!root.TryGetValue("todos", out todosObj)) throw new Exception("缺少 todos");
            System.Collections.IEnumerable list = todosObj as System.Collections.IEnumerable;
            int n = 0;
            if (list != null)
                foreach (object o in list)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    TodoItem t = new TodoItem();
                    t.Title = d.ContainsKey("title") ? Convert.ToString(d["title"]) : "";
                    if (string.IsNullOrEmpty(t.Title)) continue;
                    t.Note = d.ContainsKey("note") ? Convert.ToString(d["note"]) : "";
                    t.Importance = d.ContainsKey("importance") ? Convert.ToInt32(d["importance"]) : 2;
                    t.DueUtc = d.ContainsKey("due_utc") && d["due_utc"] != null ? (long?)Convert.ToInt64(d["due_utc"]) : null;
                    t.RemindUtc = d.ContainsKey("remind_utc") && d["remind_utc"] != null ? (long?)Convert.ToInt64(d["remind_utc"]) : null;
                    t.Recurrence = d.ContainsKey("recurrence") ? Convert.ToString(d["recurrence"]) : "";
                    t.Pinned = d.ContainsKey("pinned") && Convert.ToBoolean(d["pinned"]);
                    t.Done = d.ContainsKey("done") && Convert.ToBoolean(d["done"]);
                    t.CreatedUtc = d.ContainsKey("created_utc") && d["created_utc"] != null ? Convert.ToInt64(d["created_utc"]) : 0;
                    t.UpdatedUtc = d.ContainsKey("updated_utc") && d["updated_utc"] != null ? Convert.ToInt64(d["updated_utc"]) : 0;
                    t.RecurrenceNextUtc = d.ContainsKey("recurrence_next_utc") && d["recurrence_next_utc"] != null ? (long?)Convert.ToInt64(d["recurrence_next_utc"]) : null;
                    // 去重：同 title + due + created 视为同一条
                    if (Exists(store, t)) { skipped++; continue; }
                    store.Add(t);
                    n++;
                }
            return n;
        }

        private static bool Exists(Store store, TodoItem t)
        {
            using (Stmt st = store.Db.Prepare("SELECT COUNT(*) FROM todos WHERE deleted=0 AND title=? AND IFNULL(due_utc,-1)=IFNULL(?,-1) AND created_utc=?"))
            {
                st.Bind(1, t.Title);
                if (t.DueUtc.HasValue) st.Bind(2, t.DueUtc.Value); else st.BindNull(2);
                st.Bind(3, t.CreatedUtc);
                if (st.Step()) return st.GetInt64(0) > 0;
            }
            return false;
        }
    }
}
