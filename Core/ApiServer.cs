// ApiServer.cs — 本地 HTTP 接口（AI 接口）：仅 127.0.0.1 + token 鉴权。
// 逻辑层（无 UI 引用）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Decanter.Core
{
    public sealed class ApiServer : IDisposable
    {
        private readonly Store _store;
        private readonly int _port;
        private readonly string _token;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        private const int MaxBodyBytes = 65536;

        // W6：可写设置白名单（未知 key 返回 400；api_token 不允许经 API 改）
        private static readonly string[] AllowedSettingKeys = new string[] {
            "theme", "lang", "font_scale", "growth_on", "growth_all", "growth_days",
            "remind_l1", "remind_l2", "remind_l3", "flash", "sound", "daily_limit", "http_port"
        };

        public static bool IsAllowedSettingKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < AllowedSettingKeys.Length; i++) if (AllowedSettingKeys[i] == key) return true;
            return false;
        }

        // W6：/api/edit 允许 patch 的字段（id/ids/token 为协议字段）
        private static readonly string[] AllowedEditFields = new string[] {
            "id", "ids", "token", "title", "note", "importance", "imp", "due", "remind", "repeat", "pin"
        };

        public static bool IsAllowedEditField(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < AllowedEditFields.Length; i++) if (AllowedEditFields[i] == key) return true;
            return false;
        }

        // W6：只读状态 JSON（rev/计数/最近事件；不含任何待办原文与 token）
        public static string StateJson(Store store, long since)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"rev\":" + store.Rev);
            sb.Append(",\"since\":" + since);
            sb.Append(",\"counts\":{\"active\":" + store.CountActive() + ",\"done\":" + store.CountDone() + "}");
            sb.Append(",\"lastEvents\":[");
            List<string> evs = store.RecentEvents();
            for (int i = 0; i < evs.Count; i++)
            {
                string e = evs[i] ?? "";
                int sp = e.IndexOf(' ');
                string at = sp > 0 ? e.Substring(0, sp) : "";
                string kind = sp > 0 ? e.Substring(sp + 1) : e;
                if (i > 0) sb.Append(",");
                sb.Append("{\"at\":\"" + JsonEscape(at) + "\",\"kind\":\"" + JsonEscape(kind) + "\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
        public int Port { get { return _port; } }
        public string Token { get { return _token; } }

        public ApiServer(Store store, int port, string token)
        {
            _store = store;
            _port = port;
            _token = token;
        }

        public void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("api start failed: " + ex.Message);
            }
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate(object o) { Handle((TcpClient)o); }, client);
                }
                catch (Exception) { if (!_running) return; }
            }
        }

        private void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    client.ReceiveTimeout = 5000;
                    string request = ReadHeaders(ns);
                    if (request == null) return;
                    string[] lines = request.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    string[] first = lines[0].Split(' ');
                    string method = first.Length > 0 ? first[0] : "";
                    string path = first.Length > 1 ? first[1] : "/";
                    string body = "";
                    int cl = 0;
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(), out cl);
                    }
                    string q = "";
                    int qi = path.IndexOf('?');
                    if (qi >= 0) { q = path.Substring(qi + 1); path = path.Substring(0, qi); }
                    Dictionary<string, string> query = ParseQuery(q);
                    Dictionary<string, string> headers = new Dictionary<string, string>();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int ci = lines[i].IndexOf(':');
                        if (ci > 0) headers[lines[i].Substring(0, ci).Trim().ToLowerInvariant()] = lines[i].Substring(ci + 1).Trim();
                    }

                    string token = null;
                    if (headers.ContainsKey("x-decanter-token")) token = headers["x-decanter-token"];
                    else if (query.ContainsKey("token")) token = query["token"];
                    if (method == "OPTIONS") { Respond(ns, 204, ""); return; }
                    if (!TokenEquals(token, _token)) { Respond(ns, 401, "{\"error\":\"unauthorized\"}"); return; }
                    if (!IsLocal(client)) { Respond(ns, 403, "{\"error\":\"local only\"}"); return; }
                    // 纵深防御：Host/Origin 必须是本机（防 DNS rebinding / CSRF）
                    string host = headers.ContainsKey("host") ? headers["host"] : "";
                    if (!(host.StartsWith("127.0.0.1") || host.StartsWith("localhost") || host.StartsWith("[::1]"))) { Respond(ns, 403, "{\"error\":\"bad host\"}"); return; }
                    string origin = headers.ContainsKey("origin") ? headers["origin"] : "";
                    if (origin.Length > 0 && !(origin.StartsWith("http://127.0.0.1") || origin.StartsWith("http://localhost"))) { Respond(ns, 403, "{\"error\":\"bad origin\"}"); return; }
                    // 鉴权通过后才读 body，且有硬上限（防未认证内存分配）
                    if (cl > MaxBodyBytes) { Respond(ns, 413, "{\"error\":\"payload too large\"}"); return; }
                    if (cl > 0)
                    {
                        byte[] buf = new byte[cl];
                        int read = 0;
                        while (read < cl) { int n = ns.Read(buf, read, cl - read); if (n <= 0) break; read += n; }
                        body = Encoding.UTF8.GetString(buf, 0, read);
                    }

                    Route(ns, method, path, query, body);
                }
            }
            catch (Exception)
            {
                // 单个请求失败不影响服务
            }
        }

        private static bool TokenEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool IsLocal(TcpClient c)
        {
            try
            {
                IPEndPoint ep = c.Client.RemoteEndPoint as IPEndPoint;
                return ep != null && IPAddress.IsLoopback(ep.Address);
            }
            catch { return false; }
        }

        private static string ReadHeaders(NetworkStream ns)
        {
            MemoryStream ms = new MemoryStream();
            byte[] one = new byte[1];
            int matched = 0;
            while (ms.Length < 16384)
            {
                int n = ns.Read(one, 0, 1);
                if (n <= 0) break;
                ms.WriteByte(one[0]);
                if ((matched == 0 || matched == 2) && one[0] == '\r') matched++;
                else if ((matched == 1 || matched == 3) && one[0] == '\n') matched++;
                else matched = 0;
                if (matched >= 4) break;
            }
            return ms.Length == 0 ? null : Encoding.UTF8.GetString(ms.ToArray());
        }

        private static Dictionary<string, string> ParseQuery(string q)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(q)) return d;
            foreach (string part in q.Split('&'))
            {
                int i = part.IndexOf('=');
                if (i <= 0) continue;
                d[Uri.UnescapeDataString(part.Substring(0, i))] = Uri.UnescapeDataString(part.Substring(i + 1));
            }
            return d;
        }

        private void Route(NetworkStream ns, string method, string path, Dictionary<string, string> query, string body)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            Dictionary<string, object> data = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(body))
            {
                try { data = ser.Deserialize<Dictionary<string, object>>(body) ?? new Dictionary<string, object>(); }
                catch { Respond(ns, 400, "{\"error\":\"bad json\"}"); return; }
            }
            string v;
            if (path == "/api/ping") { Respond(ns, 200, "{\"ok\":true,\"app\":\"Decanter\",\"schema\":1}"); return; }
            if (path == "/api/next")
            {
                TodoItem t = _store.Next();
                Respond(ns, 200, t == null ? "{\"todo\":null}" : "{\"todo\":" + TodoJson(t) + "}");
                return;
            }
            if (path == "/api/list")
            {
                StringBuilder sb = new StringBuilder("[");
                bool growthAll = _store.GetSetting("growth_all", "0") == "1";
                bool growthOn = _store.GetSetting("growth_on", "1") == "1";
                DateTime now = DateTime.Now;
                int n = 0;
                foreach (TodoItem t in _store.Active())
                {
                    ScoreResult r = Scoring.Evaluate(t, now, Scoring.GrowthEnabled(t, growthAll, growthOn));
                    if (n++ > 0) sb.Append(",");
                    sb.Append(ItemJson(t, r));
                }
                sb.Append("]");
                Respond(ns, 200, "{\"todos\":" + sb.ToString() + "}");
                return;
            }
            if (path == "/api/add" && method == "POST")
            {
                string title = Get(query, data, "title");
                if (string.IsNullOrEmpty(title)) { Respond(ns, 400, "{\"error\":\"title required\"}"); return; }
                TodoItem t = new TodoItem();
                t.Title = title;
                int imp;
                if (int.TryParse(Get(query, data, "importance") ?? Get(query, data, "imp"), out imp)) t.Importance = Math.Max(1, Math.Min(3, imp));
                string due = Get(query, data, "due");
                if (!string.IsNullOrEmpty(due)) t.DueUtc = ParseLocal(due);
                string remind = Get(query, data, "remind");
                if (!string.IsNullOrEmpty(remind)) t.RemindUtc = ParseLocal(remind);
                t.Recurrence = Get(query, data, "repeat") ?? "";
                if (t.IsRecurring) t.RecurrenceNextUtc = Recurrence.NextUtc(t.Recurrence, DateTime.Now);
                t.Pinned = Get(query, data, "pin") == "1";
                long id = _store.Add(t);
                Respond(ns, 200, "{\"ok\":true,\"id\":" + id + "}");
                return;
            }
            if ((path == "/api/done" || path == "/api/pin" || path == "/api/del") && method == "POST")
            {
                long id;
                if (!long.TryParse(Get(query, data, "id") ?? Get(query, data, "ids"), out id)) { Respond(ns, 400, "{\"error\":\"id required\"}"); return; }
                if (path == "/api/done") { bool sp; _store.Complete(id, out sp); Respond(ns, 200, "{\"ok\":true,\"spawned\":" + (sp ? "true" : "false") + "}"); return; }
                if (path == "/api/del") { _store.SoftDelete(id); Respond(ns, 200, "{\"ok\":true}"); return; }
                TodoItem t2 = _store.Get(id);
                if (t2 == null) { Respond(ns, 404, "{\"error\":\"not found\"}"); return; }
                t2.Pinned = !t2.Pinned;
                _store.Update(t2);
                Respond(ns, 200, "{\"ok\":true,\"pinned\":" + (t2.Pinned ? "true" : "false") + "}");
                return;
            }
            if (path == "/api/restore" && method == "POST")
            {
                // W4：只加不改；恢复已完成待办（done→0），不生成循环下一次
                long rid;
                if (!long.TryParse(Get(query, data, "id"), out rid)) { Respond(ns, 400, "{\"error\":\"id required\"}"); return; }
                if (!_store.Restore(rid)) { Respond(ns, 404, "{\"error\":\"not found or not done\"}"); return; }
                Respond(ns, 200, "{\"ok\":true}");
                return;
            }
            // W6：patch 语义（只改传入字段；未知字段 400；无效 id 400/404）
            if (path == "/api/edit" && method == "POST")
            {
                long id;
                if (!long.TryParse(Get(query, data, "id"), out id)) { Respond(ns, 400, "{\"error\":\"id required\"}"); return; }
                if (query != null)
                {
                    foreach (string k in query.Keys)
                    {
                        if (!IsAllowedEditField(k)) { Respond(ns, 400, "{\"error\":\"unknown field: " + JsonEscape(k) + "\"}"); return; }
                    }
                }
                if (data != null)
                {
                    foreach (string k in data.Keys)
                    {
                        if (!IsAllowedEditField(k)) { Respond(ns, 400, "{\"error\":\"unknown field: " + JsonEscape(k) + "\"}"); return; }
                    }
                }
                TodoItem t = _store.Get(id);
                if (t == null) { Respond(ns, 404, "{\"error\":\"not found\"}"); return; }
                try
                {
                    if (Has(query, data, "title"))
                    {
                        string title = (Get(query, data, "title") ?? "").Trim();
                        if (title.Length == 0) { Respond(ns, 400, "{\"error\":\"title cannot be empty\"}"); return; }
                        t.Title = title;
                    }
                    if (Has(query, data, "note")) t.Note = Get(query, data, "note") ?? "";
                    if (Has(query, data, "importance") || Has(query, data, "imp"))
                    {
                        int imp;
                        string s = Get(query, data, "importance") ?? Get(query, data, "imp");
                        if (!int.TryParse(s, out imp)) { Respond(ns, 400, "{\"error\":\"bad importance\"}"); return; }
                        t.Importance = Math.Max(1, Math.Min(3, imp));
                    }
                    if (Has(query, data, "due"))
                    {
                        string due = Get(query, data, "due");
                        t.DueUtc = string.IsNullOrEmpty(due) ? (long?)null : (long?)ParseLocal(due);
                    }
                    if (Has(query, data, "remind"))
                    {
                        string remind = Get(query, data, "remind");
                        t.RemindUtc = string.IsNullOrEmpty(remind) ? (long?)null : (long?)ParseLocal(remind);
                    }
                    if (Has(query, data, "repeat"))
                    {
                        t.Recurrence = Get(query, data, "repeat") ?? "";
                        t.RecurrenceNextUtc = t.IsRecurring ? Recurrence.NextUtc(t.Recurrence, DateTime.Now) : null;
                    }
                    if (Has(query, data, "pin"))
                    {
                        string pin = Get(query, data, "pin");
                        if (pin == "1" || pin == "true") t.Pinned = true;
                        else if (pin == "0" || pin == "false" || pin == "") t.Pinned = false;
                        else { Respond(ns, 400, "{\"error\":\"bad pin\"}"); return; }
                    }
                }
                catch (Exception) { Respond(ns, 400, "{\"error\":\"bad value\"}"); return; }
                _store.Update(t);
                Respond(ns, 200, "{\"ok\":true,\"rev\":" + _store.Rev + "}");
                return;
            }
            // W6：只读状态（rev/计数/最近事件；不含任何待办原文/token）
            if (path == "/api/state" && method == "GET")
            {
                string sv = Get(query, data, "since");
                long since = 0;
                if (sv != null && !long.TryParse(sv, out since)) { Respond(ns, 400, "{\"error\":\"bad since\"}"); return; }
                Respond(ns, 200, StateJson(_store, since));
                return;
            }
            if (path == "/api/settings" && method == "GET") { Respond(ns, 200, SettingsJson()); return; }
            if (path == "/api/set" && method == "POST")
            {
                string key = Get(query, data, "key");
                string val = Get(query, data, "value");
                if (string.IsNullOrEmpty(key)) { Respond(ns, 400, "{\"error\":\"key required\"}"); return; }
                if (!IsAllowedSettingKey(key)) { Respond(ns, 400, "{\"error\":\"unknown key\"}"); return; }
                _store.SetSetting(key, val ?? "");
                Respond(ns, 200, "{\"ok\":true,\"rev\":" + _store.Rev + "}");
                return;
            }
            Respond(ns, 404, "{\"error\":\"not found\"}");
        }

        private string SettingsJson()
        {
            string[] keys = new string[] { "theme", "growth_on", "growth_all", "growth_days", "remind_l1", "remind_l2", "remind_l3", "flash", "sound", "daily_limit", "http_port" };
            StringBuilder sb = new StringBuilder("{");
            int n = 0;
            foreach (string k in keys)
            {
                if (n++ > 0) sb.Append(",");
                sb.Append("\"" + k + "\":\"" + JsonEscape(_store.GetSetting(k, "")) + "\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string Get(Dictionary<string, string> query, Dictionary<string, object> data, string key)
        {
            string v;
            if (query != null && query.TryGetValue(key, out v)) return v;
            if (data != null && data.ContainsKey(key) && data[key] != null)
            {
                if (data[key] is bool) return ((bool)data[key]) ? "1" : "0";
                return Convert.ToString(data[key]);
            }
            return null;
        }

        private static bool Has(Dictionary<string, string> query, Dictionary<string, object> data, string key)
        {
            if (query != null && query.ContainsKey(key)) return true;
            return data != null && data.ContainsKey(key);
        }

        private static long ParseLocal(string s)
        {
            DateTime dt;
            string[] fmts = new string[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "MM-dd HH:mm" };
            if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return TimeUtil.ToUtc(dt);
            if (DateTime.TryParse(s, out dt)) return TimeUtil.ToUtc(dt);
            throw new Exception("bad time: " + s);
        }

        private static string TodoJson(TodoItem t)
        {
            bool growthAll = false;
            ScoreResult r = Scoring.Evaluate(t, DateTime.Now, false);
            return ItemJson(t, r);
        }

        private static string ItemJson(TodoItem t, ScoreResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"id\":" + t.Id);
            sb.Append(",\"title\":\"" + JsonEscape(t.Title) + "\"");
            sb.Append(",\"importance\":" + t.Importance);
            sb.Append(",\"tier\":\"" + r.Tier + "\"");
            sb.Append(",\"lines\":" + r.Lines);
            sb.Append(",\"score\":" + r.Score.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append(",\"growth\":" + r.Growth.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append(",\"overdue\":" + (r.Overdue ? "true" : "false"));
            sb.Append(",\"pinned\":" + (t.Pinned ? "true" : "false"));
            sb.Append(",\"recurring\":" + (t.IsRecurring ? "true" : "false"));
            sb.Append(",\"due\":" + (t.DueLocal.HasValue ? "\"" + t.DueLocal.Value.ToString("yyyy-MM-dd HH:mm") + "\"" : "null"));
            sb.Append(",\"remind\":" + (t.RemindUtc.HasValue ? "\"" + TimeUtil.FromUtc(t.RemindUtc.Value).ToString("yyyy-MM-dd HH:mm") + "\"" : "null"));
            sb.Append("}");
            return sb.ToString();
        }

        public static string JsonEscape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void Respond(NetworkStream ns, int code, string body)
        {
            byte[] b = Encoding.UTF8.GetBytes(body ?? "");
            string status = code == 200 ? "OK" : (code == 204 ? "No Content" : (code == 400 ? "Bad Request" : (code == 401 ? "Unauthorized" : (code == 403 ? "Forbidden" : (code == 404 ? "Not Found" : (code == 413 ? "Payload Too Large" : "OK"))))));
            string head = "HTTP/1.1 " + code + " " + status + "\r\n" +
                          "Content-Type: application/json; charset=utf-8\r\n" +
                          "Content-Length: " + b.Length + "\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            if (b.Length > 0) ns.Write(b, 0, b.Length);
            ns.Flush();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }
    }
}
