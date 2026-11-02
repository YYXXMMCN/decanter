// Model.cs / Scoring.cs — 领域模型与优先级算法（纯逻辑，无 UI）
using System;
using System.Collections.Generic;

namespace Decanter.Core
{
    public class TodoItem
    {
        public long Id;
        public string Title = "";
        public string Note = "";
        public int Importance = 2;          // 1 低 / 2 中 / 3 高
        public long? DueUtc;                // UTC epoch 秒
        public long? RemindUtc;
        public string Recurrence = "";      // "" none / daily / weekly / monthly / every:N(天)
        public long? RecurrenceNextUtc;
        public bool Pinned;
        public bool Done;
        public long CreatedUtc;
        public long UpdatedUtc;
        public bool Deleted;

        public bool IsRecurring { get { return !string.IsNullOrEmpty(Recurrence) && Recurrence != "none"; } }

        public DateTime? DueLocal
        {
            get { return DueUtc.HasValue ? (DateTime?)TimeUtil.FromUtc(DueUtc.Value) : null; }
            set { DueUtc = value.HasValue ? (long?)TimeUtil.ToUtc(value.Value) : null; }
        }
    }

    public static class TimeUtil
    {
        public static long NowUtc() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        public static DateTime FromUtc(long utcSec) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(utcSec).ToLocalTime(); }
        public static long ToUtc(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
    }

    public class ScoreResult
    {
        public double Score;        // 0..1 醒目度
        public double Importance;   // 0..1
        public double Urgency;      // 0..1
        public double Growth;       // 0..0.12
        public bool Overdue;
        public string Tier;         // 高/中/低
        public int Lines;           // 1/2/3
    }

    public static class Scoring
    {
        public const double GrowthCap = 0.12;
        public const int GrowthDays = 90;      // 默认 90 天涨满 0.12（≈每 30 天 +0.04）
        public const double WeightImportance = 2.0;
        public const double WeightUrgency = 1.0;

        public static string TierName(int importance)
        {
            if (importance >= 3) return "高";
            if (importance <= 1) return "低";
            return "中";
        }

        public static double ImportanceValue(int importance)
        {
            if (importance >= 3) return 1.0;
            if (importance <= 1) return 0.15;
            return 0.5;
        }

        public static void UrgencyValue(TodoItem t, DateTime now, out double urgency, out bool overdue)
        {
            overdue = false;
            if (!t.DueUtc.HasValue) { urgency = 0.0; return; }
            DateTime due = t.DueLocal.Value;
            if (due < now) { urgency = 1.0; overdue = true; return; }
            double days = (due - now).TotalDays;
            if (days <= 1) { urgency = 1.0; return; }
            if (days <= 7) { urgency = 0.55; return; }
            urgency = 0.2;
        }

        public static ScoreResult Evaluate(TodoItem t, DateTime now, bool growthEnabled)
        {
            ScoreResult r = new ScoreResult();
            bool overdue;
            r.Importance = ImportanceValue(t.Importance);
            UrgencyValue(t, now, out r.Urgency, out overdue);
            r.Overdue = overdue;
            r.Tier = TierName(t.Importance);
            r.Lines = t.Importance >= 3 ? 3 : (t.Importance <= 1 ? 1 : 2);

            double baseScore = (WeightImportance * r.Importance + WeightUrgency * r.Urgency) / (WeightImportance + WeightUrgency);
            r.Growth = 0.0;
            if (growthEnabled)
            {
                double ageDays = (now - TimeUtil.FromUtc(t.CreatedUtc)).TotalDays;
                if (ageDays < 0) ageDays = 0;
                double g = (ageDays / GrowthDays) * GrowthCap;
                if (g > GrowthCap) g = GrowthCap;
                r.Growth = g;
            }
            double s = baseScore + r.Growth;
            if (s < 0) s = 0;
            if (s > 1) s = 1;
            r.Score = s;
            return r;
        }

        // 是否参与成长：默认只对"非循环且无提醒"；可设置 growth_all=1 放开
        public static bool GrowthEnabled(TodoItem t, bool growthAll, bool growthOn)
        {
            if (!growthOn) return false;
            if (growthAll) return true;
            if (t.IsRecurring) return false;
            if (t.RemindUtc.HasValue) return false;
            return true;
        }
    }

    public static class Recurrence
    {
        // 返回下一次的时间（UTC 秒），无法解析返回 null
        public static long? NextUtc(string rule, DateTime now)
        {
            if (string.IsNullOrEmpty(rule) || rule == "none") return null;
            DateTime next;
            if (rule == "daily") next = now.Date.AddDays(1);
            else if (rule == "weekly") next = now.Date.AddDays(7);
            else if (rule == "monthly") next = now.Date.AddMonths(1);
            else if (rule.StartsWith("every:"))
            {
                int days;
                if (!int.TryParse(rule.Substring(6), out days) || days <= 0) return null;
                next = now.Date.AddDays(days);
            }
            else return null;
            return TimeUtil.ToUtc(next);
        }
    }
}
