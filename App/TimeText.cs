// TimeText.cs — 日期/时间文案唯一真源（主界面、列表、浮窗共用一套，避免同一件事两种说法）
using System;
using Decanter.Core;

namespace Decanter.App
{
    public static class TimeText
    {
        private static readonly string[] Week = new string[] { "日", "一", "二", "三", "四", "五", "六" };

        // 右列：今天 23:59 / 明天 09:00 / 周日 23:59 / 11-01 18:00
        public static string Due(DateTime due, DateTime now)
        {
            if (due.Date == now.Date) return Strings.L("due.label.today") + " " + due.ToString("HH:mm");
            if (due.Date == now.Date.AddDays(1)) return Strings.L("due.label.tomorrow") + " " + due.ToString("HH:mm");
            if (due <= Store.WeekEnd(now)) return "周" + Week[(int)due.DayOfWeek] + " " + due.ToString("HH:mm");
            return due.ToString("MM-dd HH:mm");
        }

        // 提醒：MM-dd HH:mm；今天/明天也用短说法，避免与"截止"重复
        public static string Remind(DateTime local, DateTime now)
        {
            if (local.Date == now.Date) return Strings.L("due.label.today") + " " + local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(1)) return Strings.L("due.label.tomorrow") + " " + local.ToString("HH:mm");
            return local.ToString("MM-dd HH:mm");
        }

        public static string DoneAt(DateTime local)
        {
            return local.ToString("MM-dd HH:mm");
        }

        public static string Weekday(DateTime d)
        {
            return "周" + Week[(int)d.DayOfWeek];
        }
    }
}
