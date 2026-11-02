// Selftest.cs — 自检（v6）：版式常量/索引轨/账本页/DPI/行高/浮窗/主题/种子/编辑/恢复/截止/API
// 门禁要求（acceptance-notes 缺陷 1/2）：
//   1) 有失败项时以非零退出，JSON 里显式列出 failures；
//   2) 断言引用 Layout 常量与关系（不再写死 64/108/44 或 96×42），避免"界面改了自检不知道"。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Decanter.Core;

namespace Decanter.App
{
    public static class Selftest
    {
        private static readonly List<string> Failures = new List<string>();

        private static void Check(string name, bool ok, string detail)
        {
            if (!ok) Failures.Add(name + (detail == null ? "" : ("=" + detail)));
        }

        public static int Run(string outPath)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            Failures.Clear();
            if (Environment.GetEnvironmentVariable("DECANTER_SELFTEST_FORCE_FAIL") == "1")
                Failures.Add("forced-failure-for-gate-test");   // 门禁自测：验证失败时确实非零退出
            try
            {
                Store store = new Store(Paths.DbPath());
                Program.InitSettings(store);
                Strings.Lang = store.GetSetting("lang", "zh");
                try { Program.SeedDemoTodos(store); } catch { }   // task-148: selftest seeds its own demo data (GUI first run no longer does)
        FloatingForm floating = new FloatingForm(store);
                MainForm main = new MainForm(store, floating);
                try
                {
                    // 1) 自适应：4 宽 × 3 高 + 1600×900（页宽/版心/状态栏都必须成立）
                    Dictionary<string, object> sizes = new Dictionary<string, object>();
                    List<object> checks = new List<object>();
                    int[] widths = new int[] { 880, 1000, 1200, 1600 };
                    int[] heights = new int[] { 560, 680, 900 };
                    for (int wi = 0; wi < widths.Length; wi++)
                    {
                        for (int hi = 0; hi < heights.Length; hi++)
                        {
                            string nm = widths[wi] + "x" + heights[hi];
                            main.TestSetClientSize(widths[wi], heights[hi]);
                            main.TestReload();
                            Dictionary<string, string> snap = main.LayoutSnapshot();
                            sizes[nm] = snap;
                            Dictionary<string, object> chk = new Dictionary<string, object>();
                            chk["size"] = nm;
                            chk["railW"] = W(snap["rail"]);
                            chk["topStrip_h"] = H(snap["topStrip"]);
                            chk["compose_h"] = H(snap["compose"]);
                            chk["status_h"] = H(snap["status"]);
                            chk["list_left"] = L(snap["list"]);
                            chk["list_top"] = T(snap["list"]);
                            chk["list_w"] = W(snap["list"]);
                            chk["list_h"] = H(snap["list"]);
                            chk["statusBottom"] = B(snap["status"]);
                            int pageW = widths[wi] - Layout.RailW;
                            int expectContent = Math.Min(Layout.PageMaxW, pageW - Layout.PagePadX * 2);
                            int expectLeft = Layout.RailW + (pageW - expectContent) / 2;

                            bool railOk = W(snap["rail"]) == Layout.RailW;
                            bool topOk = H(snap["topStrip"]) == Layout.PageTopStripH;
                            bool composeOk = H(snap["compose"]) == Layout.ComposeH;
                            bool statusOk = H(snap["status"]) == Layout.StatusH || H(snap["status"]) == Layout.StatusSelH;
                            bool statusBottomOk = B(snap["status"]) == heights[hi];
                            bool listTopOk = T(snap["list"]) == Layout.PageTopStripH + Layout.ComposeH + Layout.PageGap;
                            bool listWidthOk = Math.Abs(W(snap["list"]) - expectContent) <= 1;
                            bool listLeftOk = Math.Abs(L(snap["list"]) - expectLeft) <= 2;
                            bool capOk = W(snap["list"]) <= Layout.PageMaxW;
                            bool noOverlap = B(snap["topStrip"]) <= T(snap["compose"]) && B(snap["compose"]) <= T(snap["list"]) && B(snap["list"]) <= T(snap["status"]);
                            bool searchInRail = L(snap["searchBox"]) + W(snap["searchBox"]) <= Layout.RailW;
                            bool navInRail = L(snap["navAll"]) + W(snap["navAll"]) <= Layout.RailW;
                            bool controlsInWidth = R(snap["btnAdd"]) <= widths[wi] && R(snap["pageCount"]) <= widths[wi] && R(snap["searchBox"]) <= widths[wi];

                            chk["railOk"] = railOk;
                            chk["topOk"] = topOk;
                            chk["composeOk"] = composeOk;
                            chk["statusOk"] = statusOk;
                            chk["statusBottomOk"] = statusBottomOk;
                            chk["listTopOk"] = listTopOk;
                            chk["listWidthOk"] = listWidthOk;
                            chk["listLeftOk"] = listLeftOk;
                            chk["capOk"] = capOk;
                            chk["noOverlap"] = noOverlap;
                            chk["searchInRail"] = searchInRail;
                            chk["navInRail"] = navInRail;
                            chk["controlsInWidth"] = controlsInWidth;

                            Check(nm + ".rail", railOk, W(snap["rail"]).ToString());
                            Check(nm + ".top", topOk, H(snap["topStrip"]).ToString());
                            Check(nm + ".compose", composeOk, H(snap["compose"]).ToString());
                            Check(nm + ".status", statusOk, H(snap["status"]).ToString());
                            Check(nm + ".statusBottom", statusBottomOk, B(snap["status"]).ToString());
                            Check(nm + ".listTop", listTopOk, T(snap["list"]).ToString());
                            Check(nm + ".listWidth", listWidthOk, W(snap["list"]).ToString() + "/" + expectContent);
                            Check(nm + ".listLeft", listLeftOk, L(snap["list"]).ToString() + "/" + expectLeft);
                            Check(nm + ".cap", capOk, W(snap["list"]).ToString());
                            Check(nm + ".overlap", noOverlap, "");
                            Check(nm + ".searchInRail", searchInRail, L(snap["searchBox"]).ToString());
                            Check(nm + ".navInRail", navInRail, L(snap["navAll"]).ToString());
                            Check(nm + ".controlsInWidth", controlsInWidth, R(snap["btnAdd"]).ToString());

                            main.ListView.ScrollToBottomForTest();
                            List<int> rowTops = main.ListView.RowTopsForTest();
                            List<int> rowHs = main.ListView.RowHeightsForTest();
                            int firstRow = main.ListView.FirstRowForTest();
                            int innerH = main.ListView.Height - 2 * TodoListView.CardPad;
                            bool lastVisible = rowTops.Count == 0 || (rowTops[rowTops.Count - 1] + rowHs[rowHs.Count - 1] - (firstRow == 0 ? 0 : rowTops[firstRow])) <= innerH;
                            chk["lastRowVisibleAtBottom"] = lastVisible;
                            chk["firstRowAtBottom"] = firstRow;
                            chk["rows"] = rowTops.Count;
                            Check(nm + ".lastRowVisible", lastVisible, firstRow.ToString());
                            checks.Add(chk);
                        }
                    }
                    root["sizes"] = sizes;
                    root["layoutChecks"] = checks;

                    // 1b) DPI 等比：100% / 150% / 200%
                    List<object> dpiChecks = new List<object>();
                    double[] scales = new double[] { 1.0, 1.5, 2.0 };
                    for (int di = 0; di < scales.Length; di++)
                    {
                        double sc = scales[di];
                        main.TestForceDpi((float)sc);
                        main.TestSetClientSize((int)Math.Round(Layout.DefaultW * sc), (int)Math.Round(Layout.DefaultH * sc));
                        main.TestReload();
                        Dictionary<string, string> snap = main.LayoutSnapshot();
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["scale"] = sc;
                        d["railW"] = W(snap["rail"]);
                        d["topStrip_h"] = H(snap["topStrip"]);
                        d["compose_h"] = H(snap["compose"]);
                        d["status_h"] = H(snap["status"]);
                        d["btnAdd_w"] = W(snap["btnAdd"]);
                        d["btnAdd_h"] = H(snap["btnAdd"]);
                        d["list_left"] = L(snap["list"]);
                        d["list_w"] = W(snap["list"]);
                        d["ok_rail"] = Math.Abs(W(snap["rail"]) - Layout.RailW * sc) <= 1;
                        d["ok_top"] = Math.Abs(H(snap["topStrip"]) - Layout.PageTopStripH * sc) <= 1;
                        d["ok_compose"] = Math.Abs(H(snap["compose"]) - Layout.ComposeH * sc) <= 1;
                        d["ok_status"] = Math.Abs(H(snap["status"]) - Layout.StatusH * sc) <= 1 || Math.Abs(H(snap["status"]) - Layout.StatusSelH * sc) <= 1;
                        d["ok_btnAdd"] = Math.Abs(W(snap["btnAdd"]) - Layout.AddW * sc) <= 1 && Math.Abs(H(snap["btnAdd"]) - Layout.ComposeRowH * sc) <= 1;
                        d["ok_listCap"] = W(snap["list"]) <= Math.Round(Layout.PageMaxW * sc) + 1;
                        d["ok_listLeft"] = L(snap["list"]) >= Math.Round(Layout.RailW * sc);
                        Check("dpi" + sc + ".rail", (bool)d["ok_rail"], d["railW"].ToString());
                        Check("dpi" + sc + ".top", (bool)d["ok_top"], d["topStrip_h"].ToString());
                        Check("dpi" + sc + ".compose", (bool)d["ok_compose"], d["compose_h"].ToString());
                        Check("dpi" + sc + ".status", (bool)d["ok_status"], d["status_h"].ToString());
                        Check("dpi" + sc + ".btnAdd", (bool)d["ok_btnAdd"], W(snap["btnAdd"]) + "x" + H(snap["btnAdd"]));
                        Check("dpi" + sc + ".listCap", (bool)d["ok_listCap"], d["list_w"].ToString());
                        Check("dpi" + sc + ".listLeft", (bool)d["ok_listLeft"], d["list_left"].ToString());
                        dpiChecks.Add(d);
                    }
                    root["dpiChecks"] = dpiChecks;
                    main.TestForceDpi(1f);
                    main.TestSetClientSize(Layout.DefaultW, Layout.DefaultH);
                    main.TestReload();

                    // 2) 行高常量
                    Dictionary<string, object> fonts = new Dictionary<string, object>();
                    fonts["titleLineH"] = TodoListView.TitleLineH;
                    fonts["metaLineH"] = TodoListView.MetaLineH;
                    fonts["rowPadY"] = TodoListView.RowPadY;
                    fonts["cardPad"] = TodoListView.CardPad;
                    fonts["railW"] = TodoListView.RailW;
                    fonts["circleVisual"] = TodoListView.CircleVisual;
                    fonts["circleHit"] = TodoListView.CircleHit;
                    fonts["sectionH"] = Layout.SectionH;
                    fonts["sectionGap"] = Layout.SectionGap;
                    for (int imp = 1; imp <= 3; imp++)
                    {
                        Dictionary<string, object> f = new Dictionary<string, object>();
                        f["maxLines"] = TodoListView.MaxLines(imp);
                        f["compactH"] = FloatingForm.CompactHeightOf(imp);
                        f["compactW"] = FloatingForm.CompactW;
                        fonts["imp" + imp] = f;
                    }
                    root["fonts"] = fonts;

                    // 3) 列表行高（内容驱动：1 行=40 / 2 行=58 / 3 行=76）
                    main.TestSetClientSize(Layout.DefaultW, Layout.DefaultH);
                    main.TestReload();
                    List<TodoItem> act = store.Active();
                    List<object> rowInfo = new List<object>();
                    List<int> hs = main.ListView.RowHeightsForTest();
                    List<int> tops = main.ListView.RowTopsForTest();
                    bool heightFormulaOk = true;
                    for (int i = 0; i < act.Count; i++)
                    {
                        int hh = i < hs.Count ? hs[i] : -1;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = act[i].Id;
                        d["imp"] = act[i].Importance;
                        d["titleLen"] = act[i].Title.Length;
                        d["noteLen"] = act[i].Note == null ? 0 : act[i].Note.Length;
                        d["height"] = hh;
                        d["top"] = i < tops.Count ? tops[i] : -1;
                        d["lines"] = hh < 0 ? -1 : (hh - 2 * TodoListView.RowPadY - TodoListView.TitleLineH) / TodoListView.MetaLineH + 1;
                        rowInfo.Add(d);
                        int lines = d["lines"] is int ? (int)d["lines"] : -1;
                        if (hh != TodoListView.TitleLineH + 2 * TodoListView.RowPadY + (lines - 1) * TodoListView.MetaLineH) heightFormulaOk = false;
                        if (act[i].Importance == 1 && hh != TodoListView.TitleLineH + 2 * TodoListView.RowPadY) heightFormulaOk = false;
                    }
                    root["listRows"] = rowInfo;
                    Dictionary<string, object> rowChecks = new Dictionary<string, object>();
                    rowChecks["heightFormulaOk"] = heightFormulaOk;
                    rowChecks["count"] = act.Count;
                    rowChecks["sectionCount"] = main.ListView.SectionCountForTest();
                    rowChecks["sectionsPresent"] = main.ListView.SectionCountForTest() > 0;
                    root["rowChecks"] = rowChecks;
                    Check("row.heightFormula", heightFormulaOk, "");
                    Check("row.count", act.Count > 0, act.Count.ToString());
                    Check("row.sections", main.ListView.SectionCountForTest() > 0, main.ListView.SectionCountForTest().ToString());

                    // 4) 悬浮窗
                    Dictionary<string, object> fw = new Dictionary<string, object>();
                    List<object> compacts = new List<object>();
                    string longTitle = "这是一条很长很长很长很长很长很长很长很长很长的示例标题用来换行";
                    for (int kind = 0; kind < 2; kind++)
                    {
                        for (int imp = 1; imp <= 3; imp++)
                        {
                            TodoItem one = new TodoItem();
                            one.Title = kind == 0 ? ("短标题" + imp) : longTitle;
                            one.Importance = imp;
                            List<TodoItem> l = new List<TodoItem>();
                            l.Add(one);
                            floating.TestSetItems(l);
                            Rectangle g = floating.GeometryForTest();
                            Dictionary<string, object> c = new Dictionary<string, object>();
                            c["kind"] = kind == 0 ? "short" : "long";
                            c["imp"] = imp;
                            c["titleLen"] = one.Title.Length;
                            c["w"] = g.Width;
                            c["h"] = g.Height;
                            c["lines"] = FloatingForm.AllocateByBudget(l, FloatingForm.MaxCompactItems, FloatingForm.CompactLineBudget)[0];
                            compacts.Add(c);
                        }
                    }
                    fw["compactGeomByTitle"] = compacts;

                    List<TodoItem> midLow = new List<TodoItem>();
                    TodoItem m1 = new TodoItem(); m1.Title = "中级两行标题"; m1.Importance = 2; midLow.Add(m1);
                    TodoItem l1 = new TodoItem(); l1.Title = "低级单行标题"; l1.Importance = 1; midLow.Add(l1);
                    int[] mLines = FloatingForm.AllocateByBudget(midLow, FloatingForm.MaxCompactItems, FloatingForm.CompactLineBudget);
                    fw["midPlusLowLines"] = mLines.Length > 0 ? mLines[0].ToString() : "0";
                    fw["midPlusLowCount"] = mLines.Length;
                    fw["midPlusLowFonts"] = mLines.Length > 0 ? FloatingForm.FontPxOfLines(mLines[0]).ToString() : "0";

                    List<TodoItem> threeLow = new List<TodoItem>();
                    for (int i = 0; i < 3; i++) { TodoItem x = new TodoItem(); x.Title = "低级" + (i + 1); x.Importance = 1; threeLow.Add(x); }
                    int[] ll = FloatingForm.AllocateByBudget(threeLow, FloatingForm.MaxCompactItems, FloatingForm.CompactLineBudget);
                    fw["threeLowLines"] = ll.Length > 0 ? ll[0].ToString() : "0";
                    fw["threeLowCount"] = ll.Length;
                    fw["threeLowFonts"] = ll.Length > 0 ? FloatingForm.FontPxOfLines(ll[0]).ToString() : "0";
                    floating.TestSetItems(threeLow);
                    Rectangle c3 = floating.GeometryForTest();
                    fw["compact3lowSize"] = c3.Width + "x" + c3.Height;
                    fw["compactShownFor3"] = floating.TestShownCount();
                    fw["compactLineBudget"] = FloatingForm.CompactLineBudget;
                    fw["expandedLineBudget"] = FloatingForm.ExpandedLineBudget;

                    List<TodoItem> calib = new List<TodoItem>();
                    for (int i = 0; i < 2; i++) { TodoItem x = new TodoItem(); x.Title = "高" + i; x.Importance = 3; x.Note = "说明"; calib.Add(x); }
                    for (int i = 0; i < 2; i++) { TodoItem x = new TodoItem(); x.Title = "中" + i; x.Importance = 2; calib.Add(x); }
                    for (int i = 0; i < 2; i++) { TodoItem x = new TodoItem(); x.Title = "低" + i; x.Importance = 1; calib.Add(x); }
                    int[] cal = FloatingForm.AllocateByBudget(calib, FloatingForm.MaxExpandedItems, FloatingForm.ExpandedLineBudget);
                    int calSum = 0;
                    List<string> calParts = new List<string>();
                    for (int i = 0; i < cal.Length; i++) { calSum += cal[i]; calParts.Add(cal[i].ToString()); }
                    fw["calibLines"] = string.Join("/", calParts.ToArray());
                    fw["calibSum"] = calSum;
                    fw["calibCount"] = cal.Length;
                    fw["calibFitsExactly"] = (calSum == FloatingForm.ExpandedLineBudget) && (cal.Length == 6);
                    floating.TestSetItems(calib);
                    floating.TestExpand();
                    fw["calibShown"] = floating.TestShownCount();
                    fw["calibRow0"] = RectOf(floating.TestRowRect(0));
                    fw["calibCircle0"] = RectOf(floating.TestCircleRect(0));
                    fw["calibExpandedH"] = FloatingForm.ExpandedH;
                    fw["calibCompactH"] = FloatingForm.CompactH;
                    floating.TestCollapse();

                    floating.TestSetItems(store.Active());
                    floating.Location = new Point(200, 200);
                    Rectangle before = floating.GeometryForTest();
                    floating.TestExpand();
                    Rectangle after = floating.GeometryForTest();
                    fw["expandBefore"] = before.X + "," + before.Y + "," + before.Width + "," + before.Height;
                    fw["expandAfter"] = after.X + "," + after.Y + "," + after.Width + "," + after.Height;
                    fw["expandDx"] = after.X - before.X;
                    fw["expandDy"] = after.Y - before.Y;
                    floating.TestCollapse();
                    Rectangle back = floating.GeometryForTest();
                    fw["collapseDx"] = back.X - before.X;
                    fw["collapseDy"] = back.Y - before.Y;
                    fw["collapseSameSize"] = back.Width == before.Width && back.Height == before.Height;

                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    int lowY = wa.Bottom - 100;
                    floating.Location = new Point(200, lowY);
                    Rectangle fb = floating.GeometryForTest();
                    floating.TestExpand();
                    Rectangle fa = floating.GeometryForTest();
                    fw["flipBefore"] = fb.X + "," + fb.Y + "," + fb.Width + "," + fb.Height;
                    fw["flipAfter"] = fa.X + "," + fa.Y + "," + fa.Width + "," + fa.Height;
                    fw["flipBottomFixed"] = (fa.Y + fa.Height) == (fb.Y + fb.Height);
                    fw["flipDx"] = fa.X - fb.X;
                    fw["flipInsideWorkArea"] = fa.Y >= wa.Top;
                    fw["animMs"] = FloatingForm.AnimMs;
                    root["float"] = fw;
                    Check("float.calib", (bool)fw["calibFitsExactly"], calSum.ToString());
                    Check("float.expandDx", (int)fw["expandDx"] == 0, fw["expandDx"].ToString());
                    Check("float.expandDy", (int)fw["expandDy"] == 0, fw["expandDy"].ToString());   // task-110: top-left locked => Top must not move
                    Check("float.collapse", (bool)fw["collapseSameSize"], "");
                    Check("float.flip", (int)fw["flipDx"] == 0 && (int)fw["expandDy"] == 0, "top-locked: no flip, overflow allowed for now");   // task-110

                    // 4b) 字号三档 + 下限
                    List<object> fontChecks = new List<object>();
                    string[] fsKeys = new string[] { "s", "m", "l" };
                    for (int fi = 0; fi < fsKeys.Length; fi++)
                    {
                        main.TestApplyFontScale(fsKeys[fi]);
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["setting"] = store.GetSetting("font_scale", "?");
                        d["titlePx"] = main.TestFontPx("title");
                        d["bodyPx"] = main.TestFontPx("body");
                        d["metaPx"] = main.TestFontPx("meta");
                        d["bodyFloorOk"] = main.TestFontPx("body") >= 15;
                        d["metaFloorOk"] = main.TestFontPx("meta") >= 12;
                        fontChecks.Add(d);
                        Check("font." + fsKeys[fi] + ".body", (bool)d["bodyFloorOk"], d["bodyPx"].ToString());
                        Check("font." + fsKeys[fi] + ".meta", (bool)d["metaFloorOk"], d["metaPx"].ToString());
                    }
                    main.TestApplyFontScale("m");
                    root["fontScaleChecks"] = fontChecks;

                    // 5) 主题连点
                    List<string> seq = new List<string>();
                    seq.Add(main.TestThemeSetting());
                    for (int i = 0; i < 3; i++) { main.TestCycleThemeSync(); seq.Add(main.TestThemeSetting()); }
                    bool alt = true;
                    for (int i = 1; i < seq.Count; i++) if (seq[i] == seq[i - 1]) alt = false;
                    Dictionary<string, object> th = new Dictionary<string, object>();
                    th["sequence"] = seq;
                    th["strictAlternating"] = alt;
                    root["theme"] = th;
                    Check("theme.alternating", alt, string.Join(",", seq.ToArray()));

                    // 6) 种子
                    Dictionary<string, object> seed = new Dictionary<string, object>();
                    seed["demo_seeded"] = store.GetSetting("demo_seeded", "0");
                    seed["activeCount"] = act.Count;
                    List<object> titles = new List<object>();
                    bool hasPinned = false, hasRecur = false, hasRemind = false;
                    long nowUtc = TimeUtil.NowUtc();
                    int overdue = 0;
                    foreach (TodoItem t in act)
                    {
                        titles.Add(t.Title + " | imp" + t.Importance + (t.Pinned ? " | pin" : "") + (t.IsRecurring ? " | " + t.Recurrence : "") + (t.DueUtc.HasValue ? " | due" : "") + (t.RemindUtc.HasValue ? " | remind" : "") + " | note" + (t.Note == null ? 0 : t.Note.Length));
                        if (t.Pinned) hasPinned = true;
                        if (t.IsRecurring) hasRecur = true;
                        if (t.RemindUtc.HasValue) hasRemind = true;
                        if (t.DueUtc.HasValue && t.DueUtc.Value < nowUtc) overdue++;
                    }
                    seed["items"] = titles;
                    seed["hasPinned"] = hasPinned;
                    seed["hasRecurrence"] = hasRecur;
                    seed["hasRemind"] = hasRemind;
                    seed["overdueCount"] = overdue;
                    root["seed"] = seed;
                    Check("seed.count", act.Count == 8, act.Count.ToString());
                    Check("seed.pinned", hasPinned, "");
                    Check("seed.recurrence", hasRecur, "");
                    Check("seed.remind", hasRemind, "");

                    // 6b) 真实输入路径：添加 / 搜索 / 清空
                    Dictionary<string, object> ux = new Dictionary<string, object>();
                    int preAdd = store.CountActive();
                    main.TestSetFilter(0);
                    main.TestAddTodoFromInput("SMOKE-ADD-1");
                    main.TestReload();
                    ux["activeAfterAdd"] = store.CountActive();
                    ux["addOk"] = store.CountActive() == preAdd + 1;
                    main.TestSearch("SMOKE-ADD-1");
                    ux["searchRows"] = main.TestVisibleRows();
                    ux["searchOk"] = main.TestVisibleRows() == 1;
                    main.TestSearch("");
                    main.TestReload();
                    ux["clearedRows"] = main.TestVisibleRows();
                    ux["clearOk"] = main.TestVisibleRows() >= 8;
                    root["uxChecks"] = ux;
                    Check("ux.add", (bool)ux["addOk"], ux["activeAfterAdd"].ToString());
                    Check("ux.search", (bool)ux["searchOk"], ux["searchRows"].ToString());
                    Check("ux.clearSearch", (bool)ux["clearOk"], ux["clearedRows"].ToString());

                    // 7) 编辑态（页顶编辑条 + 行内标记）
                    Dictionary<string, object> ec = new Dictionary<string, object>();
                    EditorSession es = new EditorSession();
                    TodoItem fake = new TodoItem(); fake.Id = 999; fake.Title = "fake";
                    es.BeginEdit(fake); es.MarkDirty();
                    es.ConfirmLeave = delegate { return LeaveAction.Discard; };
                    ec["canLeaveDiscard"] = es.CanLeave();
                    es.BeginEdit(fake); es.MarkDirty();
                    es.ConfirmLeave = delegate { return LeaveAction.Stay; };
                    ec["canLeaveStay"] = es.CanLeave();
                    es.BeginEdit(fake); es.MarkDirty();
                    es.ConfirmLeave = delegate { return LeaveAction.Save; };
                    ec["canLeaveSave"] = es.CanLeave();
                    es.BeginNew();
                    ec["newNotDirty"] = (!es.IsEditing) && (!es.Dirty);

                    List<TodoItem> act2 = store.Active();
                    TodoItem remindItem = null;
                    foreach (TodoItem x in act2) if (x.RemindUtc.HasValue) { remindItem = x; break; }
                    if (remindItem == null)
                    {
                        TodoItem x = new TodoItem();
                        x.Title = "W3 提醒测试"; x.Importance = 2;
                        x.RemindUtc = TimeUtil.ToUtc(DateTime.Now.AddHours(2));
                        remindItem = store.Get(store.Add(x));
                    }
                    main.TestReload();
                    long rid = remindItem.Id;
                    long remBefore = remindItem.RemindUtc.HasValue ? remindItem.RemindUtc.Value : 0;
                    main.TestBeginEditById(rid);
                    ec["beginEditing"] = main.TestIsEditing();
                    ec["bannerHasEditing"] = main.TestEditBannerText().Contains(Strings.L("edit.banner").Replace("{0}", ""));
                    Dictionary<string, string> editSnap = main.LayoutSnapshot();
                    ec["editAddButtonText"] = main.TestAddButtonText();
                    ec["editBannerRect"] = editSnap["editBanner"];
                    ec["editEditingIndex"] = editSnap["editingIndex"];
                    ec["editComposeRect"] = editSnap["compose"];
                    ec["editStatusRect"] = editSnap["status"];
                    ec["editCancelRect"] = editSnap["btnCancelEdit"];
                    ec["editNewRect"] = editSnap["btnNewEdit"];
                    ec["editFooterActions"] = editSnap["footerActionsVisible"];
                    int editIdx = int.Parse(editSnap["editingIndex"]);
                    Rectangle editRow = main.ListView.RowRectForTest(editIdx);
                    ec["editRowRect"] = RectOf(editRow);
                    ec["editRowVisible"] = editRow.Height > 0 && editRow.Top >= 0 && editRow.Bottom <= main.ListView.Height;
                    main.TestToggleNote();
                    Dictionary<string, string> editSnap2 = main.LayoutSnapshot();
                    ec["editNoteComposeRect"] = editSnap2["compose"];
                    ec["editNoteBoxRect"] = editSnap2["noteBox"];
                    main.TestToggleNote();
                    main.TestSetTitleText("W3 改名");
                    ec["dirtyAfterChange"] = main.TestIsDirty();
                    ec["bannerHasDirty"] = main.TestEditBannerText().Contains(Strings.L("edit.dirty"));
                    main.TestSetImpIndex(0);
                    main.TestSaveEdit();
                    TodoItem afterItem = store.Get(rid);
                    ec["savedTitle"] = afterItem.Title;
                    ec["remindPreserved"] = afterItem.RemindUtc.HasValue && afterItem.RemindUtc.Value == remBefore;
                    ec["impChangedSaved"] = afterItem.Importance == 1;
                    ec["editingEnded"] = !main.TestIsEditing();
                    main.TestBeginEditById(rid);
                    main.TestSetTitleText("W3 取消改名");
                    main.TestCancelEdit();
                    ec["cancelKeptTitle"] = store.Get(rid).Title == afterItem.Title;
                    ec["cancelEnded"] = !main.TestIsEditing();
                    root["editChecks"] = ec;
                    Check("edit.begin", (bool)ec["beginEditing"], "");
                    Check("edit.banner", (bool)ec["bannerHasEditing"], "");
                    Check("edit.button", (string)ec["editAddButtonText"] == Strings.L("main.save_edit"), (string)ec["editAddButtonText"]);
                    Check("edit.rowVisible", (bool)ec["editRowVisible"], (string)ec["editRowRect"]);
                    Check("edit.composeH", H(editSnap["compose"]) == Layout.ComposeH, H(editSnap["compose"]).ToString());
                    Check("edit.noteComposeH", H(editSnap2["compose"]) == Layout.ComposeHNote, H(editSnap2["compose"]).ToString());
                    Check("edit.dirty", (bool)ec["dirtyAfterChange"] && (bool)ec["bannerHasDirty"], "");
                    Check("edit.saved", (string)ec["savedTitle"] == "W3 改名" && (bool)ec["remindPreserved"] && (bool)ec["impChangedSaved"], "");
                    Check("edit.cancel", (bool)ec["cancelKeptTitle"] && (bool)ec["cancelEnded"], "");

                    // 8) 完成/恢复
                    Dictionary<string, object> rc = new Dictionary<string, object>();
                    List<TodoItem> act4 = store.Active();
                    TodoItem victim = null;
                    foreach (TodoItem x in act4) if (!x.IsRecurring) { victim = x; break; }
                    if (victim == null && act4.Count > 0) victim = act4[0];
                    if (victim != null)
                    {
                        long vid = victim.Id;
                        int activePre = store.CountActive();
                        main.TestSetFilter(0); main.TestReload();
                        main.TestSelectById(vid);
                        main.TestToggleDone();
                        rc["uiDoneAfterToggle"] = store.Get(vid).Done;
                        rc["uiActiveAfterDone"] = store.CountActive();
                        main.TestSetFilter(4); main.TestReload();
                        main.TestSelectById(vid);
                        main.TestToggleDone();
                        rc["uiRestoredNotDone"] = !store.Get(vid).Done;
                        rc["uiActiveAfterRestore"] = store.CountActive();
                        rc["uiActiveBack"] = store.CountActive() == activePre;
                        main.TestSetFilter(0); main.TestReload();
                        rc["restoreNotDoneReturnsFalse"] = store.Restore(vid);
                    }
                    rc["restoreUnknownIdReturnsFalse"] = store.Restore(999999);
                    TodoItem recItem = null;
                    foreach (TodoItem x in store.Active()) if (x.IsRecurring) { recItem = x; break; }
                    if (recItem != null)
                    {
                        long recId = recItem.Id;
                        long recNext = recItem.RecurrenceNextUtc.HasValue ? recItem.RecurrenceNextUtc.Value : 0;
                        int activeBeforeRec = store.CountActive();
                        bool spawned;
                        store.Complete(recId, out spawned);
                        rc["recSpawned"] = spawned;
                        rc["recActiveAfterComplete"] = store.CountActive();
                        rc["recDoneBeforeRestore"] = store.Get(recId).Done;
                        rc["recRestored"] = store.Restore(recId);
                        TodoItem rr = store.Get(recId);
                        rc["recDoneAfterRestore"] = rr == null ? true : rr.Done;
                        long recNextAfter = (rr != null && rr.RecurrenceNextUtc.HasValue) ? rr.RecurrenceNextUtc.Value : 0;
                        rc["recNextUnchanged"] = recNextAfter == recNext;
                        rc["recActiveAfterRestore"] = store.CountActive();
                        rc["recDidNotSpawnAgain"] = store.CountActive() == activeBeforeRec + 1;
                    }
                    root["restoreChecks"] = rc;
                    Check("restore.done", (bool)rc["uiDoneAfterToggle"] && (bool)rc["uiRestoredNotDone"] && (bool)rc["uiActiveBack"], "");
                    Check("restore.unknown", !(bool)rc["restoreUnknownIdReturnsFalse"], "");
                    if (recItem != null) Check("restore.recurring", (bool)rc["recSpawned"] && (bool)rc["recNextUnchanged"] && (bool)rc["recDidNotSpawnAgain"], "");

                    // 9) 截止
                    Dictionary<string, object> dc = new Dictionary<string, object>();
                    DateTime fixedNow = new DateTime(2026, 10, 29, 10, 0, 0);
                    dc["presetToday"] = TimeUtil.FromUtc(MainForm.DuePresetUtc(1, fixedNow, null).Value).ToString("yyyy-MM-dd HH:mm");
                    dc["presetTomorrow"] = TimeUtil.FromUtc(MainForm.DuePresetUtc(2, fixedNow, null).Value).ToString("yyyy-MM-dd HH:mm");
                    DateTime sun = TimeUtil.FromUtc(MainForm.DuePresetUtc(3, fixedNow, null).Value);
                    dc["presetSunday"] = sun.ToString("yyyy-MM-dd HH:mm");
                    dc["presetSundayIsSunday"] = sun.DayOfWeek == DayOfWeek.Sunday;
                    DateTime customLocal = new DateTime(2026, 12, 24, 18, 30, 0);
                    dc["presetCustom"] = TimeUtil.FromUtc(MainForm.DuePresetUtc(4, fixedNow, customLocal).Value).ToString("yyyy-MM-dd HH:mm");
                    dc["presetNoneIsNull"] = !MainForm.DuePresetUtc(0, fixedNow, null).HasValue;
                    using (Store dueStore = new Store(Path.Combine(Paths.DataDir(), "due-selftest.db")))
                    {
                        TodoItem d0 = new TodoItem(); d0.Title = "五档测试";
                        long did = dueStore.Add(d0);
                        bool allApplied = true;
                        for (int p = 1; p <= 4; p++)
                        {
                            TodoItem it = dueStore.Get(did);
                            it.DueUtc = (p == 4) ? MainForm.DuePresetUtc(4, fixedNow, customLocal) : MainForm.DuePresetUtc(p, fixedNow, null);
                            dueStore.Update(it);
                            if (!dueStore.Get(did).DueUtc.HasValue) allApplied = false;
                        }
                        dc["fivePresetsApplied"] = allApplied;
                        TodoItem cleared = dueStore.Get(did); cleared.DueUtc = null; dueStore.Update(cleared);
                        dc["clearedIsNull"] = !dueStore.Get(did).DueUtc.HasValue;
                        dueStore.SoftDelete(did);

                        TodoItem pin = new TodoItem(); pin.Title = "pin"; pin.Pinned = true; long pinId = dueStore.Add(pin);
                        TodoItem ov = new TodoItem(); ov.Title = "overdue"; ov.DueUtc = TimeUtil.ToUtc(fixedNow.AddHours(-2)); long ovId = dueStore.Add(ov);
                        TodoItem td = new TodoItem(); td.Title = "today"; td.DueUtc = TimeUtil.ToUtc(fixedNow.Date.AddHours(23).AddMinutes(59)); long tdId = dueStore.Add(td);
                        TodoItem wk = new TodoItem(); wk.Title = "week"; wk.DueUtc = TimeUtil.ToUtc(fixedNow.Date.AddDays(3).AddHours(10)); long wkId = dueStore.Add(wk);
                        TodoItem nq = new TodoItem(); nq.Title = "none"; long nqId = dueStore.Add(nq);
                        List<TodoItem> sorted = dueStore.ActiveAt(fixedNow);
                        List<long> order = new List<long>();
                        foreach (TodoItem x in sorted) order.Add(x.Id);
                        dc["sortOrderIds"] = string.Join(",", new string[] { pinId.ToString(), ovId.ToString(), tdId.ToString(), wkId.ToString(), nqId.ToString() });
                        dc["sortActualIds"] = string.Join(",", order.ConvertAll(delegate(long v) { return v.ToString(); }).ToArray());
                        dc["sortOk"] = order.Count == 5 && order[0] == pinId && order[1] == ovId && order[2] == tdId && order[3] == wkId && order[4] == nqId;

                        TodoListView lv = main.ListView;
                        TodoItem tx = new TodoItem(); tx.Title = "t";
                        tx.DueUtc = TimeUtil.ToUtc(fixedNow.Date.AddHours(23).AddMinutes(59));
                        string sToday = lv.RightTextForTest(tx, fixedNow);
                        tx.DueUtc = TimeUtil.ToUtc(fixedNow.Date.AddDays(1).AddHours(9));
                        string sTomorrow = lv.RightTextForTest(tx, fixedNow);
                        tx.DueUtc = TimeUtil.ToUtc(fixedNow.Date.AddDays(3).AddHours(10));
                        string sWeek = lv.RightTextForTest(tx, fixedNow);
                        tx.DueUtc = TimeUtil.ToUtc(fixedNow.AddHours(-2));
                        string sOverdue = lv.RightTextForTest(tx, fixedNow);
                        tx.DueUtc = TimeUtil.ToUtc(customLocal);
                        string sCustom = lv.RightTextForTest(tx, fixedNow);
                        dc["rowToday"] = sToday.Contains("今天 ");
                        dc["rowTomorrow"] = sTomorrow.Contains("明天 ");
                        dc["rowWeek"] = sWeek.StartsWith("周");
                        dc["rowOverdue"] = sOverdue.Contains("已过期");
                        dc["rowCustom"] = sCustom.Contains("12-24 18:30");
                    }
                    root["dueChecks"] = dc;
                    Check("due.sort", (bool)dc["sortOk"], (string)dc["sortActualIds"]);
                    Check("due.rowText", (bool)dc["rowToday"] && (bool)dc["rowTomorrow"] && (bool)dc["rowWeek"] && (bool)dc["rowOverdue"] && (bool)dc["rowCustom"], "");

                    // 10) API
                    Dictionary<string, object> ac = new Dictionary<string, object>();
                    long rev0 = store.Rev;
                    int fired = 0;
                    Action onChanged = delegate { fired++; };
                    store.Changed += onChanged;
                    TodoItem apiItem = new TodoItem(); apiItem.Title = "SECRET-TITLE-W6";
                    long apiId = store.Add(apiItem);
                    long rev1 = store.Rev;
                    store.SetSetting("font_scale", store.GetSetting("font_scale", "m"));
                    long rev2 = store.Rev;
                    store.Changed -= onChanged;
                    ac["revIncreases"] = rev1 > rev0 && rev2 > rev1;
                    ac["changedEventFired"] = fired > 0;
                    string stateJson = ApiServer.StateJson(store, 0);
                    ac["stateHasRev"] = stateJson.Contains("\"rev\":");
                    ac["stateHasCounts"] = stateJson.Contains("\"counts\":");
                    ac["stateHasLastEvents"] = stateJson.Contains("\"lastEvents\":");
                    ac["stateNoTitle"] = !stateJson.Contains("SECRET-TITLE-W6");
                    ac["stateNoToken"] = !stateJson.Contains("token");
                    string evJoined = string.Join("|", store.RecentEvents().ToArray());
                    ac["eventsAtMost5"] = store.RecentEvents().Count <= 5;
                    ac["eventsNoTitle"] = !evJoined.Contains("SECRET-TITLE-W6");
                    ac["allowSettingTheme"] = ApiServer.IsAllowedSettingKey("theme");
                    ac["allowSettingFont"] = ApiServer.IsAllowedSettingKey("font_scale");
                    ac["denySettingApiToken"] = !ApiServer.IsAllowedSettingKey("api_token");
                    ac["denySettingUnknown"] = !ApiServer.IsAllowedSettingKey("nope");
                    ac["allowEditTitle"] = ApiServer.IsAllowedEditField("title");
                    ac["allowEditDue"] = ApiServer.IsAllowedEditField("due");
                    ac["denyEditBogus"] = !ApiServer.IsAllowedEditField("bogus");
                    store.SoftDelete(apiId);
                    root["apiChecks"] = ac;
                    Check("api.rev", (bool)ac["revIncreases"] && (bool)ac["changedEventFired"], "");
                    Check("api.privacy", (bool)ac["stateNoTitle"] && (bool)ac["stateNoToken"] && (bool)ac["eventsNoTitle"], "");
                    Check("api.allow", (bool)ac["allowSettingTheme"] && (bool)ac["allowSettingFont"] && (bool)ac["allowEditTitle"] && (bool)ac["allowEditDue"] && (bool)ac["denySettingApiToken"] && (bool)ac["denySettingUnknown"] && (bool)ac["denyEditBogus"], "");

                    // 11) 选中改截止后仍保持选中
                    List<TodoItem> actDue = store.Active();
                    if (actDue.Count > 0)
                    {
                        long anyId = actDue[0].Id;
                        main.TestSetFilter(0);
                        main.TestReload();
                        main.TestSelectById(anyId);
                        main.TestApplyDuePreset(1);
                        dc["applyDueKeepsSelected"] = main.ListView.SelectedItem != null && main.ListView.SelectedItem.Id == anyId;
                        dc["applyDueWritten"] = store.Get(anyId).DueUtc.HasValue;
                        Check("due.keepSelected", (bool)dc["applyDueKeepsSelected"], "");
                        Check("due.written", (bool)dc["applyDueWritten"], "");
                    }

                    root["firstrunHint"] = Strings.L("main.firstrun");
                    root["lang"] = Strings.Lang;
                }
                finally
                {
                    try { main.Dispose(); } catch { }
                    try { floating.Dispose(); } catch { }
                    try { store.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Failures.Add("exception=" + ex.GetType().Name + ":" + ex.Message);
                root["error"] = ex.ToString();
            }

            root["allPassed"] = Failures.Count == 0;
            root["failures"] = new List<string>(Failures);
            root["failureCount"] = Failures.Count;

            try
            {
                System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
                js.MaxJsonLength = 8 * 1024 * 1024;
                File.WriteAllText(outPath, js.Serialize(root), new UTF8Encoding(false));
            }
            catch { }

            if (Failures.Count > 0)
            {
                try
                {
                    Console.Error.WriteLine("SELFTEST FAILED: " + Failures.Count + " check(s)");
                    for (int i = 0; i < Failures.Count && i < 40; i++) Console.Error.WriteLine("  - " + Failures[i]);
                }
                catch { }
                return 1;
            }
            return 0;
        }

        private static int Part(string s, int i)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            string[] p = s.Split(',');
            if (i < 0 || i >= p.Length) return 0;
            int v;
            return int.TryParse(p[i], out v) ? v : 0;
        }
        private static int T(string s) { return Part(s, 1); }
        private static string RectOf(Rectangle r) { return r.Left + "," + r.Top + "," + r.Width + "," + r.Height; }
        private static int L(string s) { return Part(s, 0); }
        private static int H(string s) { return Part(s, 3); }
        private static int W(string s) { return Part(s, 2); }
        private static int R(string s) { return Part(s, 0) + Part(s, 2); }
        private static int B(string s) { return Part(s, 1) + Part(s, 3); }
    }
}
