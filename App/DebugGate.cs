// v0.11 App · DebugGate: built-in loopback HTTP listener, off by default (--debug / --debug-control)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public sealed class DebugGate : IDisposable
    {
        public const string MonitorMode = "monitor";
        public const string ControlMode = "control";

        private readonly Control _ui;
        private readonly Store _store;
        private readonly DebugProbe _probe;
        private readonly string _mode;
        private readonly string _token;
        private readonly int _port;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private readonly List<string> _audit = new List<string>();
        private static readonly object AuditLock = new object();

        public DebugGate(Control ui, Store store, int port, string mode)
        {
            _ui = ui;
            _store = store;
            _probe = new DebugProbe(ui, store);
            _port = port;
            _mode = mode == ControlMode ? ControlMode : MonitorMode;
            _token = NewToken();
        }

        public DebugProbe Probe { get { return _probe; } }

        public int Port { get { return _port; } }
        public string Token { get { return _token; } }
        public string Mode { get { return _mode; } }

        public static int FreePort()
        {
            System.Net.Sockets.TcpListener l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
        }

        private static string NewToken()
        {
            byte[] b = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public bool Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Start();
                WriteEndpointFile();
                Audit("start port=" + _port.ToString(CultureInfo.InvariantCulture) + " mode=" + _mode);
                return true;
            }
            catch (Exception ex) { Audit("start-failed " + ex.GetType().Name); return false; }
        }

        private static string LogDirOf()
        {
            return Path.Combine(Paths.Home(), "logs");
        }

        private void WriteEndpointFile()
        {
            try
            {
                string dir = LogDirOf();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "debug-endpoint.json"),
                    "{\"port\":" + _port.ToString(CultureInfo.InvariantCulture) + ",\"token\":\"" + _token + "\",\"mode\":\"" + _mode + "\"}",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; }
                try { Handle(ctx); }
                catch { try { Write(ctx, 500, "{\"ok\":false}"); } catch { } Audit("error"); }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = (ctx.Request.Url.AbsolutePath ?? "/").ToLowerInvariant();
            string method = ctx.Request.HttpMethod.ToUpperInvariant();
            if (!Auth(ctx)) return;

            IPEndPoint remote = ctx.Request.RemoteEndPoint;
            if (remote != null && !IPAddress.IsLoopback(remote.Address))
            {
                Audit("403 remote");
                Write(ctx, 403, "{\"ok\":false,\"error\":\"loopback only\"}");
                return;
            }

            bool write = path.StartsWith("/debug/click") || path.StartsWith("/debug/type") || path.StartsWith("/debug/key")
                || path.StartsWith("/debug/action") || path.StartsWith("/debug/theme") || path.StartsWith("/debug/delete")
                || path.StartsWith("/debug/confirm") || path.StartsWith("/debug/cancel");
            if (write && method != "POST") { Audit("405 " + path); Write(ctx, 405, "{\"ok\":false,\"error\":\"use POST\"}"); return; }
            if (write && _mode != ControlMode) { Audit("403 " + path); Write(ctx, 403, "{\"ok\":false,\"error\":\"monitor mode\"}"); return; }

            if (path == "/debug/state") { Write(ctx, 200, _probe.StateJson()); return; }
            if (path == "/debug/tree") { Write(ctx, 200, _probe.TreeJson()); return; }
            if (path == "/debug/log" || path == "/debug/events") { Write(ctx, 200, AuditJson()); return; }
            if (path == "/debug/screenshot")
            {
                string target = ctx.Request.QueryString["target"];
                if (string.IsNullOrEmpty(target)) target = "main";
                Control shot = _ui;
                if (target == "settings" && _probe.Settings != null && _probe.Settings.Visible) shot = _probe.Settings;
                else if (target == "floating" && _probe.Floating != null && _probe.Floating.Visible) shot = _probe.Floating;
                else if (target == "welcome" && _probe.Welcome != null && _probe.Welcome.Visible) shot = _probe.Welcome;
                else if (target != "main" && !(target == "settings" && _probe.Settings != null) && !(target == "floating" && _probe.Floating != null) && !(target == "welcome" && _probe.Welcome != null))
                {
                    Write(ctx, 400, "{\"ok\":false,\"error\":\"target not supported\"}");
                    return;
                }
                byte[] png = Snapshot.PngOfOnUi(shot);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = png.Length;
                ctx.Response.OutputStream.Write(png, 0, png.Length);
                ctx.Response.OutputStream.Close();
                Audit("screenshot " + png.Length.ToString(CultureInfo.InvariantCulture) + "B");
                return;
            }
            if (path == "/debug/click")
            {
                string id = Field(Body(ctx), "id");
                if (!_probe.IsClickable(id)) { Audit("404 click " + id); Write(ctx, 404, "{\"ok\":false,\"error\":\"unknown id\"}"); return; }
                Ui(delegate { bool ok = _probe.Click(id); Audit("click " + id + " ok=" + ok); });
                Write(ctx, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/debug/type")
            {
                string text = Field(Body(ctx), "text");
                Ui(delegate { _probe.Type(text); Audit("type len=" + (text == null ? 0 : text.Length).ToString(CultureInfo.InvariantCulture)); });
                Write(ctx, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/debug/key")
            {
                string key = Field(Body(ctx), "key");
                Ui(delegate { _probe.Key(key); Audit("key " + key); });
                Write(ctx, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/debug/select")
            {
                string ids = Field(Body(ctx), "id");
                long sid; bool okId = long.TryParse(ids, out sid);
                bool ok = false;
                Ui(delegate { ok = okId && _probe.SelectRow(sid); Audit("select " + ids + " ok=" + ok); });
                Write(ctx, 200, ok ? "{\"ok\":true}" : "{\"ok\":false,\"error\":\"no such row\"}");
                return;
            }
            if (path == "/debug/theme")
            {
                string theme = Field(Body(ctx), "theme");
                if (string.IsNullOrEmpty(theme)) { Ui(delegate { _probe.CycleTheme(); Audit("theme cycle"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                bool ok = theme == "system" || theme == "day" || theme == "night"
                    || theme == "v1" || theme == "v6" || theme == "v7" || theme == "v8";
                if (!ok) { Audit("400 theme " + theme); Write(ctx, 400, "{\"ok\":false,\"error\":\"bad theme\"}"); return; }
                Ui(delegate { _probe.SetTheme(theme); Audit("theme " + theme); });
                Write(ctx, 200, "{\"ok\":true,\"theme\":\"" + theme + "\"}");
                return;
            }
            if (path == "/debug/action")
            {
                string act = Field(Body(ctx), "action");
                if (string.IsNullOrEmpty(act) && !string.IsNullOrEmpty(ctx.Request.QueryString["where"])) act = "snap";
                if (act == "closeSettings") { Ui(delegate { if (_probe.Settings != null) _probe.Settings.Close(); Audit("action closeSettings"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "reloadFloating") { Ui(delegate { if (_probe.Floating != null) _probe.Floating.ReloadTheme(); Audit("action reloadFloating"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "showMain") { Ui(delegate { if (_probe.MainForm != null) { _probe.MainForm.Show(); _probe.MainForm.BringToFront(); } Audit("action showMain"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "float.expand") { Ui(delegate { if (_probe.Floating != null) _probe.Floating.TestExpand(); Audit("float.expand"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "float.collapse") { Ui(delegate { if (_probe.Floating != null) _probe.Floating.TestCollapse(); Audit("float.collapse"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "float.settle") { Ui(delegate { if (_probe.Floating != null) _probe.Floating.SettleForTest(); Audit("float.settle"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "float.diag")
                {
                    string diag = "";
                    Ui(delegate
                    {
                        FloatingForm f = _probe.Floating;
                        if (f == null) { diag = "null"; return; }
                        System.Drawing.Rectangle g = f.GeometryForTest();
                        int items = f.TestItems() == null ? 0 : f.TestItems().Count;
                        int rows = f.TestShownCount();
                        int maxBottom = 0;
                        for (int i = 0; i < rows; i++) { System.Drawing.Rectangle rr = f.TestRowRect(i); if (rr.Bottom > maxBottom) maxBottom = rr.Bottom; }
                        diag = "h=" + g.Height + " expandedH=" + FloatingForm.ExpandedH + " compactH=" + FloatingForm.CompactH
                             + " rows=" + rows + " items=" + items + " maxRowBottom=" + maxBottom
                             + " limit=" + (32 + FloatingForm.ExpandedContentH) + " expanded=" + f.TestExpanded;
                    });
                    Write(ctx, 200, "{\"ok\":true,\"diag\":\"" + diag.Replace("\"", "'") + "\"}");
                    return;
                }
                if (act == "win.visible")
                {
                    string vis = "";
                    Ui(delegate { vis = (_probe.MainForm != null && _probe.MainForm.Visible) ? "1" : "0"; });
                    Write(ctx, 200, "{\"ok\":true,\"visible\":\"" + vis + "\"}");
                    return;
                }
                if (act == "win.stats")
                {
                    string st = "";
                    Ui(delegate { if (_probe.MainForm != null) st = _probe.MainForm.LiveResizeStats(); });
                    Write(ctx, 200, "{\"ok\":true,\"stats\":\"" + st + "\"}");
                    return;
                }
                if (act == "win.resize")
                {
                    string mode = Field(Body(ctx), "mode");
                    string dxs = Field(Body(ctx), "dx");
                    string dys = Field(Body(ctx), "dy");
                    int idxx, idxy; int.TryParse(dxs, out idxx); int.TryParse(dys, out idxy);
                    Ui(delegate
                    {
                        FrameForm ff = _probe.MainForm;
                        if (ff == null) return;
                        if (mode == "begin") { ff.TestResizeBegin(17); Audit("win.resize begin"); }
                        else if (mode == "step") { ff.TestResizeTo(idxx, idxy); Audit("win.resize step " + idxx + "," + idxy); }
                        else if (mode == "end") { ff.TestResizeEnd(); Audit("win.resize end"); }
                    });
                    Write(ctx, 200, "{\"ok\":true}");
                    return;
                }
                if (act == "float.hwnd")
                {
                    string hw = "";
                    Ui(delegate { if (_probe.Floating != null) hw = _probe.Floating.Handle.ToString(); });
                    Write(ctx, 200, "{\"ok\":true,\"hwnd\":\"" + hw + "\"}");
                    return;
                }
                if (act == "float.lanes")
                {
                    string ln = "";
                    Ui(delegate { if (_probe.Floating != null) ln = _probe.Floating.LaneGeometryForTest(); });
                    Write(ctx, 200, "{\"ok\":true,\"lanes\":\"" + ln.Replace("\"", "'") + "\"}");
                    return;
                }
                if (act == "float.items")
                {
                    string sc = Field(Body(ctx), "scenario");
                    string lanes = "";
                    Ui(delegate
                    {
                        List<TodoItem> its = new List<TodoItem>();
                        TodoItem a = new TodoItem(); a.Title = "A"; a.Importance = 3; a.CreatedUtc = 0L; a.UpdatedUtc = 0L;
                        TodoItem b = new TodoItem(); b.Title = "B"; b.Importance = 2; b.CreatedUtc = 0L; b.UpdatedUtc = 0L;
                        TodoItem c2 = new TodoItem(); c2.Title = "C"; c2.Importance = 1; c2.CreatedUtc = 0L; c2.UpdatedUtc = 0L;
                        TodoItem d = new TodoItem(); d.Title = "D"; d.Importance = 1; d.CreatedUtc = 0L; d.UpdatedUtc = 0L;
                        if (sc == "high3low3") { for (int hi = 0; hi < 3; hi++) { TodoItem hh3 = new TodoItem(); hh3.Title = "H" + (hi + 1); hh3.Importance = 3; hh3.CreatedUtc = 0L; hh3.UpdatedUtc = 0L; its.Add(hh3); } for (int lo = 0; lo < 3; lo++) { TodoItem ll3 = new TodoItem(); ll3.Title = "L" + (lo + 1); ll3.Importance = 1; ll3.CreatedUtc = 0L; ll3.UpdatedUtc = 0L; its.Add(ll3); } }
                        if (sc == "default3low") { its.Add(c2); its.Add(d); TodoItem e = new TodoItem(); e.Title = "E"; e.Importance = 1; e.CreatedUtc = 0L; e.UpdatedUtc = 0L; its.Add(e); }
                        else if (sc == "pin1high_def2") { a.Pinned = true; its.Add(a); its.Add(c2); its.Add(d); }
                        else if (sc == "pin2mid_default0") { b.Pinned = true; its.Add(b); TodoItem b2 = new TodoItem(); b2.Title = "B2"; b2.Importance = 2; b2.Pinned = true; b2.CreatedUtc = 0L; b2.UpdatedUtc = 0L; its.Add(b2); }
                        else if (sc == "pinhigh") { a.Pinned = true; a.Importance = 3; its.Add(a); }
                        else if (sc == "high8") { for (int hh8 = 0; hh8 < 8; hh8++) { TodoItem h8 = new TodoItem(); h8.Title = "H" + (hh8 + 1); h8.Importance = 3; h8.CreatedUtc = 0L; h8.UpdatedUtc = 0L; its.Add(h8); } }
                        else if (sc == "high5") { for (int hh5 = 0; hh5 < 5; hh5++) { TodoItem h5 = new TodoItem(); h5.Title = "H" + (hh5 + 1); h5.Importance = 3; h5.CreatedUtc = 0L; h5.UpdatedUtc = 0L; its.Add(h5); } }
                        else { its.Add(a); its.Add(b); its.Add(c2); its.Add(d); }
                        try { _probe.Floating.Show(); _probe.Floating.TestSetItems(its); lanes = _probe.Floating.LaneGeometryForTest(); } catch (Exception ex) { lanes = "ERR " + ex.Message; }
                        Audit("float.items " + sc + " -> " + lanes);
                    });
                    Write(ctx, 200, "{\"ok\":true,\"lanes\":\"" + lanes.Replace("\"", "'") + "\"}");
                    return;
                }                if (act == "draft.new") { Ui(delegate { _probe.DraftNew(); Audit("draft.new"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "draft.edit") { bool ok = false; Ui(delegate { ok = _probe.DraftEdit(); Audit("draft.edit ok=" + ok); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "draft.title") { string tx = Field(Body(ctx), "text"); Ui(delegate { _probe.DraftSetTitle(tx); Audit("draft.title len=" + (tx == null ? 0 : tx.Length)); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "draft.commit") { bool ok2 = false; Ui(delegate { ok2 = _probe.DraftCommit(); Audit("draft.commit ok=" + ok2); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "draft.cancel") { int st = 0; Ui(delegate { st = _probe.DraftCancel(); Audit("draft.cancel stage=" + st); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "draft.outside") { bool ok3 = false; Ui(delegate { ok3 = _probe.DraftClickOutside(); Audit("draft.outside ok=" + ok3); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "snap")
                {
                    string where = Field(Body(ctx), "where");
                    if (string.IsNullOrEmpty(where)) where = ctx.Request.QueryString["where"];
                    Ui(delegate { if (_probe.MainForm != null) _probe.MainForm.TestSnap(where); Audit("action snap " + where); });
                    Write(ctx, 200, "{\"ok\":true}");
                    return;
                }
                if (act == "openThemeMenu") { Ui(delegate { if (_probe.MainForm != null) _probe.MainForm.TestOpenThemeMenu(); Audit("action openThemeMenu"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "openSettings") { Ui(delegate { if (_probe.Settings == null || _probe.Settings.IsDisposed) { _probe.Settings = new SettingsForm(_store, _probe.Floating); _probe.Settings.Owner = _probe.MainForm; } _probe.Settings.Show(); _probe.Settings.BringToFront(); Audit("action openSettings"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "focusCheck") { Ui(delegate { bool ok2 = _probe.FocusFirstCheck(); Audit("action focusCheck ok=" + ok2); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "blurCheck") { Ui(delegate { _probe.BlurChecks(); Audit("action blurCheck"); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "toggleCheck") { Ui(delegate { bool ok3 = _probe.ToggleFirstCheck(); Audit("action toggleCheck ok=" + ok3); }); Write(ctx, 200, "{\"ok\":true}"); return; }
                if (act == "cmdDeleteSelected")
                {
                    Ui(delegate { if (_probe.MainForm != null) _probe.MainForm.CmdDeleteSelected(); Audit("action cmdDeleteSelected"); });
                    Write(ctx, 200, "{\"ok\":true}");
                    return;
                }
                if (act == "resize")
                {
                    string body = Body(ctx);
                    System.Text.RegularExpressions.Match mw = System.Text.RegularExpressions.Regex.Match(body, "\"w\"\\s*:\\s*(\\d+)");
                    System.Text.RegularExpressions.Match mh = System.Text.RegularExpressions.Regex.Match(body, "\"h\"\\s*:\\s*(\\d+)");
                    int ww = mw.Success ? int.Parse(mw.Groups[1].Value) : 0;
                    int hh = mh.Success ? int.Parse(mh.Groups[1].Value) : 0;
                    int.TryParse(ctx.Request.QueryString["w"], out ww);
                    int.TryParse(ctx.Request.QueryString["h"], out hh);
                    Ui(delegate { System.Drawing.Size oldMin = _ui.MinimumSize; _ui.MinimumSize = System.Drawing.Size.Empty; _ui.Size = new System.Drawing.Size(ww, hh); _ui.MinimumSize = oldMin; Audit("resize " + ww + "x" + hh + " -> " + _ui.Width + "x" + _ui.Height); });
                    Write(ctx, 200, "{\"ok\":true}");
                    return;
                }
                Ui(delegate { _probe.Action(act); Audit("action " + act); });
                Write(ctx, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/debug/delete")
            {
                string body = Body(ctx);
                string ids = Field(body, "id");
                long did;
                bool okId = long.TryParse(ids, out did);
                Ui(delegate { bool ok = okId && _probe.BeginDelete(did); Audit("delete->confirming " + ids + " ok=" + ok); });
                Write(ctx, 200, "{\"ok\":true,\"confirming\":true}");
                return;
            }
            if (path == "/debug/confirm")
            {
                Ui(delegate { bool ok = _probe.ConfirmDelete(); Audit("confirm delete ok=" + ok); });
                Write(ctx, 200, "{\"ok\":true,\"deleted\":true}");
                return;
            }
            if (path == "/debug/cancel")
            {
                Ui(delegate { _probe.CancelDelete(); Audit("cancel delete"); });
                Write(ctx, 200, "{\"ok\":true,\"confirming\":false}");
                return;
            }
            Write(ctx, 404, "{\"ok\":false,\"error\":\"no route\"}");
        }

        private bool Auth(HttpListenerContext ctx)
        {
            string given = ctx.Request.Headers["X-Debug-Token"];
            if (given == null || given.Length == 0) given = ctx.Request.QueryString["token"];
            if (given == _token) return true;
            Audit("401");
            Write(ctx, 401, "{\"ok\":false,\"error\":\"bad token\"}");
            return false;
        }

        public static bool DirectUi;   // --debug-selftest only: queue onto the harness pump, never block the HTTP worker
        public static bool QueueUi;

        private void Ui(MethodInvoker action)
        {
            try
            {
                if (DirectUi) { action(); return; }
                if (_ui == null || !_ui.IsHandleCreated) return;
                if (QueueUi) { _ui.BeginInvoke(action); return; }
                if (_ui.InvokeRequired) _ui.Invoke(action);
                else action();
            }
            catch { }
        }

        // NOTE: the request stream can only be read once; cache per context so Field(Body(ctx), ...)
        // may be called several times for one request (w+h, action+text, etc.).
        private static HttpListenerContext _bodyCtx;
        private static string _bodyText;

        private static string Body(HttpListenerContext ctx)
        {
            if (object.ReferenceEquals(ctx, _bodyCtx)) return _bodyText;
            using (StreamReader r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                char[] buf = new char[16384];
                int n = r.Read(buf, 0, buf.Length);
                _bodyCtx = ctx;
                _bodyText = n <= 0 ? "" : new string(buf, 0, n);
                return _bodyText;
            }
        }

        private static string Field(string json, string name)
        {
            if (json == null) return "";
            int at = json.IndexOf("\"" + name + "\"", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return "";
            int colon = json.IndexOf(':', at);
            if (colon < 0) return "";
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i < json.Length && json[i] == '"')
            {
                int end = json.IndexOf('"', i + 1);
                return end < 0 ? "" : json.Substring(i + 1, end - i - 1);
            }
            int stop = i;
            while (stop < json.Length && json[stop] != ',' && json[stop] != '}') stop++;
            return json.Substring(i, stop - i).Trim();
        }

        private static void Write(HttpListenerContext ctx, int status, string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = b.Length;
            ctx.Response.OutputStream.Write(b, 0, b.Length);
            ctx.Response.OutputStream.Close();
        }

        private void Audit(string line)
        {
            string entry = "{\"at\":" + 0L.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + ",\"event\":\"" + line.Replace("\"", "").Replace("\\", "/") + "\"}";
            lock (AuditLock)
            {
                _audit.Add(entry);
                while (_audit.Count > 200) _audit.RemoveAt(0);
                try
                {
                    string dir = LogDirOf();
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "audit.jsonl"), entry + "\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private string AuditJson()
        {
            StringBuilder sb = new StringBuilder("{\"ok\":true,\"entries\":[");
            lock (AuditLock)
            {
                for (int i = 0; i < _audit.Count; i++) { if (i > 0) sb.Append(","); sb.Append(_audit[i]); }
            }
            return sb.Append("]}").ToString();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) { _listener.Stop(); _listener.Close(); } } catch { }
            try
            {
                string f = Path.Combine(LogDirOf(), "debug-endpoint.json");
                if (File.Exists(f)) File.Delete(f);
            }
            catch { }
        }
    }
}
