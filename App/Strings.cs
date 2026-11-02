// Strings.cs — 极简中英字符串表（界面层）。语言存 settings.lang（zh/en）。
using System;
using System.Collections.Generic;

namespace Decanter.App
{
    public static class Strings
    {
        public static string Lang = "zh";
        private static readonly Dictionary<string, string[]> Map = Build();

        private static Dictionary<string, string[]> Build()
        {
            Dictionary<string, string[]> m = new Dictionary<string, string[]>();
            Add(m, "app.title", "醒酒器 Decanter", "Decanter");
            Add(m, "float.header", "醒酒器", "Decanter");
            Add(m, "float.empty", "没有待办。写一条，它会替你盯着。", "No todos. Write one and it will keep watch.");
            Add(m, "float.next", "下一条", "Next");
        Add(m, "float.title", "待办", "Todos");
            Add(m, "float.more", "还有 {0} 条", "{0} more");
            Add(m, "float.new", "＋ 新建待办", "+ New todo");
            Add(m, "float.manage", "管理", "Manage");
            Add(m, "main.title", "醒酒器 · 主界面", "Decanter · Main");
            Add(m, "main.add", "添加", "Add");
            Add(m, "main.done", "完成", "Done");
            Add(m, "main.restore", "恢复", "Restore");
            Add(m, "main.pin", "置顶", "Pin");
            Add(m, "main.delete", "删除", "Delete");
            Add(m, "main.more", "⋯ 更多", "More");
            Add(m, "main.more_filter", "更多 ▾", "More");
            Add(m, "main.more_detail", "细节（说明）…", "Details (note)…");
            Add(m, "main.load", "读取选中", "Load selected");
            Add(m, "main.apply", "应用到选中", "Apply to selected");
            Add(m, "main.settings", "设置", "Settings");
            Add(m, "main.showfloat", "浮窗", "Floating");
            Add(m, "main.search", "搜索", "Search");
            Add(m, "main.search_hint", "标题或说明", "Title or note");
            Add(m, "main.filter.all", "全部", "All");
            Add(m, "main.filter.today", "今天", "Today");
            Add(m, "main.filter.week", "本周", "This week");
            Add(m, "main.filter.overdue", "过期", "Overdue");
            Add(m, "main.filter.done", "已完成", "Completed");
            Add(m, "float.subtitle", "悬浮待办本 · 简约 · 小清新", "Floating todo · minimal");
            Add(m, "theme.short", "主题", "Theme");
            Add(m, "main.input_hint", "写下一件要做的事，Enter 添加", "Write the next thing to do, Enter to add");
            Add(m, "main.selected", "已选：{0}", "Selected: {0}");
            Add(m, "main.empty.active", "还没有未完成的待办。\n在页顶写下要做的事，按 Enter 或点「添加」。", "No open todos yet.\nWrite one at the top of the page, then press Enter or click Add.");
            Add(m, "main.empty.done", "还没有已完成的待办。\n完成一项后，它会出现在这里；点左侧圆圈还能恢复。", "Nothing completed yet.\nFinished items show here; click the left circle to restore.");
            Add(m, "main.empty.overdue", "没有过期待办。\n过了截止时间仍未完成的事项会显示在这里。", "No overdue todos.\nItems past their due time appear here.");
            Add(m, "main.empty.search", "没有找到「{0}」相关的待办。\n换个词试试，或清空搜索查看全部。", "No todos match \"{0}\".\nTry another word, or clear the search.");
            Add(m, "edit.banner", "正在编辑：{0}", "Editing: {0}");
            Add(m, "edit.dirty", "有未保存的修改", "Unsaved changes");
            Add(m, "edit.cancel", "取消", "Cancel");
            Add(m, "edit.new", "新建", "New");
            Add(m, "edit.leave_prompt", "有未保存的修改。\r\n「是」=保存后继续　「否」=放弃修改　「取消」=留在当前编辑。", "Unsaved changes.\r\nYes = save and continue, No = discard, Cancel = stay here.");
            Add(m, "main.save_edit", "保存", "Save");
            Add(m, "main.edit", "编辑", "Edit");
            Add(m, "main.edit_row", "编辑这一条", "Edit this todo");
            Add(m, "main.due_menu", "截止时间", "Due time");
            Add(m, "common.ok", "确定", "OK");
            Add(m, "due.none", "无截止", "No due");
            Add(m, "due.label.today", "今天", "Today");
            Add(m, "due.label.tomorrow", "明天", "Tomorrow");
            Add(m, "due.label.week", "本周", "This week");
            Add(m, "due.menu.today", "今天 23:59", "Today 23:59");
            Add(m, "due.menu.tomorrow", "明天 23:59", "Tomorrow 23:59");
            Add(m, "due.menu.sunday", "本周日 23:59", "Sunday 23:59");
            Add(m, "due.menu.custom", "自定义…", "Custom…");
            Add(m, "field.due", "截止", "Due");
            Add(m, "field.remind", "提醒", "Remind");
            Add(m, "field.format", "格式 2026-10-25 20:00（留空=无）", "Format 2026-10-25 20:00 (empty = none)");
            Add(m, "field.importance", "重要", "Importance");
            Add(m, "field.repeat", "循环", "Repeat");
            Add(m, "field.pinned", "置顶", "Pinned");
            Add(m, "field.note", "说明（可选）", "Note (optional)");
            Add(m, "field.note_hint", "细节写这里（只在主界面显示）", "Details go here (main window only)");
            Add(m, "theme.to_night", "夜间", "Night");
            Add(m, "theme.to_day", "日间", "Day");
            Add(m, "main.firstrun", "这几条是给你练手的，随便改、随便删，点一遍就会了。", "These are samples: edit or delete them freely.");
            Add(m, "imp.low", "低", "Low");
            Add(m, "imp.mid", "中", "Mid");
            Add(m, "imp.high", "高", "High");
            Add(m, "rep.none", "不循环", "None");
            Add(m, "rep.daily", "每天", "Daily");
            Add(m, "rep.weekly", "每周", "Weekly");
            Add(m, "rep.monthly", "每月", "Monthly");
            Add(m, "set.title", "设置", "Settings");
            Add(m, "set.theme", "主题", "Theme");
            Add(m, "set.lang", "语言", "Language");
            Add(m, "set.font", "字号", "Text size");
            Add(m, "set.font.s", "小", "Small");
            Add(m, "set.font.m", "中", "Medium");
            Add(m, "set.font.l", "大", "Large");
            Add(m, "set.theme.auto", "跟随系统", "Follow system");
            Add(m, "set.theme.day", "日间", "Day");
            Add(m, "set.theme.night", "暗夜", "Night");
            Add(m, "set.growth", "成长", "Growth");
            Add(m, "set.growth_on", "启用成长", "Enable growth");
            Add(m, "set.growth_all", "所有待办参与", "All todos");
            Add(m, "set.growth_days", "涨满一档天数", "Days to full growth");
            Add(m, "set.remind", "提醒", "Reminders");
            Add(m, "set.remind_l1", "L1 温和（气泡+声音）", "L1 gentle (balloon + sound)");
            Add(m, "set.remind_l2", "L2 明显（+悬浮窗）", "L2 notable (+ floating)");
            Add(m, "set.remind_l3", "L3 强（+任务栏闪烁）", "L3 strong (+ taskbar flash)");
            Add(m, "set.sound", "提示音", "Sound");
            Add(m, "set.flash", "任务栏闪烁", "Taskbar flash");
            Add(m, "set.level_hint", "提示：关闭某一档后，落入该档的提醒会被跳过（不会提醒）。", "Note: reminders in a disabled level are skipped (no notification).");
            Add(m, "set.daily_limit", "每日提醒上限", "Daily reminder limit");
            Add(m, "set.http_port", "本地 HTTP 端口", "Local HTTP port");
            Add(m, "set.api_token", "API 令牌（只读）", "API token (read-only)");
            Add(m, "set.copy", "复制", "Copy");
            Add(m, "set.open_data", "打开数据目录", "Open data folder");
            Add(m, "set.save", "保存", "Save");
            Add(m, "set.close", "关闭", "Close");
            Add(m, "set.restart_hint", "端口改动需重启程序生效", "Port change needs restart");
            Add(m, "tray.show", "显示悬浮窗", "Show floating");
            Add(m, "tray.main", "主界面", "Main window");
            Add(m, "tray.settings", "设置", "Settings");
            Add(m, "tray.exit", "退出", "Exit");
            Add(m, "tray.tip", "醒酒器 Decanter", "Decanter");
            Add(m, "menu.done_next", "完成下一条", "Complete next");
            Add(m, "menu.manage", "主界面", "Main window");
            Add(m, "menu.refresh", "刷新", "Refresh");
            Add(m, "menu.hide", "隐藏悬浮窗", "Hide floating");
            Add(m, "menu.exit", "退出", "Exit");
            Add(m, "balloon.started", "已启动（连点托盘图标两下打开主界面）", "Started (click the tray icon twice to open main)");
            Add(m, "balloon.reminder", "待办提醒", "Todo reminder");
            Add(m, "balloon.important", "【重要】待办提醒", "[Important] Todo reminder");
            Add(m, "balloon.notable", "【提醒】待办提醒", "[Reminder] Todo reminder");
            Add(m, "overdue.tag", "已过期", "overdue");
            Add(m, "msg.confirm_delete", "删除「{0}」？删除后不能恢复。", "Delete \"{0}\"? This cannot be undone.");
            Add(m, "main.export", "导出…", "Export…");
            Add(m, "main.import", "导入…", "Import…");
            Add(m, "main.about", "关于", "About");
            Add(m, "main.exported", "已导出到：{0}", "Exported to: {0}");
            Add(m, "main.imported", "已导入 {0} 条，跳过重复 {1} 条。", "Imported {0}, skipped {1} duplicates.");
            Add(m, "main.export_failed", "导出失败：", "Export failed: ");
            Add(m, "main.import_failed", "导入失败：", "Import failed: ");
            Add(m, "about.license", "许可：本程序代码 MIT；SQLite 公有领域；.NET Framework 随 Windows 提供。", "License: MIT for this code; SQLite public domain; .NET Framework provided by Windows.");
            Add(m, "about.unsigned", "本程序未使用代码签名，SmartScreen 可能提示未知发布者（如实告知）。", "Not code-signed; SmartScreen may warn (as-is).");
            Add(m, "about.data", "数据目录（明文 SQLite，不加密）：", "Data folder (plaintext SQLite, not encrypted): ");
            Add(m, "main.export_warn", "导出文件包含待办原文（可能敏感），请妥善保管。继续导出？", "The export contains todo text (possibly sensitive). Continue?");
            Add(m, "export.hint", "注意：导出文件包含待办原文（可能敏感），请妥善保管。", "Note: export contains todo text (possibly sensitive).");
            Add(m, "about.disclaimer", "免责：本程序 100% 由 AI 生成，未经专业评审，使用后果自负；提醒不得用于医疗/法律/财务/人身安全等关键用途。", "Disclaimer: generated 100% by AI, not professionally reviewed; use at your own risk.");
            Add(m, "main.hint4", "双击编辑 · 点左侧圆圈完成/恢复", "Double-click to edit · click the left circle to complete/restore");
            Add(m, "main.today_count", "今天 {0} 件", "{0} due today");
            Add(m, "field.importance_full", "重要度", "Importance");
            Add(m, "remind.none", "不提醒", "No reminder");
            Add(m, "remind.none_chip", "无", "None");
            Add(m, "remind.1h", "1 小时后", "In 1 hour");
            Add(m, "remind.today", "今天 21:00", "Today 21:00");
            Add(m, "remind.tomorrow", "明天 09:00", "Tomorrow 09:00");
            Add(m, "main.export", "导出", "Export");
            Add(m, "main.import", "导入", "Import");
            Add(m, "main.showfloat", "悬浮窗", "Floating");
            Add(m, "msg.already_running", "醒酒器已在运行（请看屏幕角落或系统托盘）。", "Decanter is already running (check screen corner or tray).");

            // ---- v5 视觉重做新增（P1）：空状态动作、成长/完成标记、快速主题 ----
            Add(m, "empty.hint.generic", "写下来，剩下的交给它盯着。", "Write it down; let it keep watch.");
            Add(m, "empty.action", "写第一条", "Write the first one");
            Add(m, "empty.action.clear", "清空搜索", "Clear search");
            Add(m, "empty.action.all", "看未完成", "See open todos");
            Add(m, "growth.label", "成长中", "ageing");
            Add(m, "done.at", "完成于", "Done");
            Add(m, "theme.quick", "切换日间/暗夜", "Toggle day/night");
            Add(m, "main.settings_hint", "设置", "Settings");
            Add(m, "main.about_hint", "关于", "About");
            Add(m, "main.compose_hint", "写下一件要做的事，Enter 添加", "Write the next thing to do, Enter to add");
            Add(m, "main.due_chip", "截止", "Due");

            // ---- v6 左索引轨 + 右账本页：视图分组、段头、状态栏、溢出菜单 ----
            Add(m, "rail.views", "视图", "Views");
            Add(m, "main.view", "{0} · {1} 条", "{0} · {1}");
            Add(m, "main.search_result", "搜索「{0}」· {1} 条", "Search \"{0}\" · {1}");
            Add(m, "main.section.pinned", "置顶", "Pinned");
            Add(m, "main.section.active", "进行中", "Open");
            Add(m, "main.overflow", "更多", "More");
            Add(m, "main.hint.select", "已选：{0}", "Selected: {0}");
            Add(m, "main.status", "{0} 条 · 今天 {1} 件", "{0} items · {1} due today");
            Add(m, "main.edit_heading", "正在编辑", "Editing");
            Add(m, "main.note_toggle", "细节（说明）…", "Details (note)…");

            // ---- 首启一屏「三步就会用」（只出现一次；关掉后 welcome_done=1） ----
            Add(m, "welcome.title", "三步就会用", "Three steps");
            Add(m, "welcome.sub", "第一次打开，只需要知道这三件事。", "First launch: three things to know.");
            Add(m, "welcome.step1.title", "写一条", "Write one down");
            Add(m, "welcome.step1.desc", "在页顶输入框写下要做的事，按 Enter 或点「添加」。", "Type it into the box at the top, then press Enter or click Add.");
            Add(m, "welcome.step2.title", "小窗提醒", "Corner reminder");
            Add(m, "welcome.step2.desc", "屏幕角落的小窗显示「下一条」，单击展开，双击进主界面。", "The corner window shows the next item: click to expand, double-click to open the main window.");
            Add(m, "welcome.step3.title", "整理", "Tidy up");
            Add(m, "welcome.step3.desc", "左索引切换视图，点左侧圆圈完成，双击这一条编辑。", "Switch views in the left rail, click the left circle to complete, double-click an item to edit.");
            Add(m, "welcome.start", "开始用", "Start");
            Add(m, "welcome.footer", "之后不再显示", "Won't show again");
            return m;
        }

        private static void Add(Dictionary<string, string[]> m, string k, string zh, string en)
        {
            m[k] = new string[] { zh, en };
        }

        public static string L(string key)
        {
            string[] v;
            if (Map.TryGetValue(key, out v))
                return Lang == "en" ? v[1] : v[0];
            return key;
        }
    }
}
