// Theme.cs — 醒酒器 v5 视觉令牌（P1 视觉重做）
// 设计依据：零号用户问卷坐标卡 v2（深色暖调 4/5 暖、繁简 4/5 简、锐利、纸+金属、全直角）
//           + 前端理论第二版（对比度 4.5:1 / 非文字 3:1 / 暗色不是反色 / 语义令牌四层）
//           + 四条禁令（无渐变无纹理无阴影、无装饰动效、颜色不单独承担信息）
// 材质：纸是场（底色/卡片），金属是线（1px 结构线与装订线），黄铜只给「要你注意的那一下」。
// 全部直角（RWindow/RControl/RItem = 0）；语义圆形（完成圈）保留。
using System;
using System.Drawing;
using Microsoft.Win32;

namespace Decanter.App
{
    public class Theme
    {
        // ---------- 色阶层（Theme 层：日/夜两套值，同一组语义名） ----------
        public Color Paper;        // 场：窗口底色
        public Color Card;         // 面：列表/输入/浮层底（比 Paper 亮 3–5%，暗色为暖黑棕）
        public Color CardHi;       // 抬高面：悬停/选中/浮层抬头
        public Color Line;         // 1px 分隔线（横线本的横线）
        public Color LineStrong;   // 强调分隔（区段线 / 表头下）
        public Color Metal;        // 金属结构线（装订线 / 浮窗边框 / 焦点描边底）
        public Color Text;         // 墨：正文/标题
        public Color SubText;      // 次要：元信息
        public Color Aux;          // 辅助：占位/说明（仍 ≥4.5:1）
        public Color Accent;       // 黄铜：主操作、焦点、当前项、完成态
        public Color OnAccent;     // 黄铜实底上的字色（夜：近墨；日：近纸）
        public Color OnSolid;      // 兼容旧字段
        public Color Dark;         // 危险/删除/已过期
        public Color Warn;         // 警示/临期
        public Color Ok;           // 成功/恢复
        public Color Hover;        // 行悬停底
        public Color Selected;     // 行选中底
        public Color Binding;      // 装订线（旧字段，保留兼容）

        // 旧字段：悬浮窗/设置页历史引用，v5 仍给值（不再作为主语义）
        public Color SoftLow;
        public Color SoftMid;
        public Color SoftHigh;

        public bool IsDark;

        // ---------- 间距层（base＝8，理论 §1.2 阶梯） ----------
        public const int S4 = 4;
        public const int S8 = 8;
        public const int S12 = 12;
        public const int S16 = 16;
        public const int S24 = 24;
        public const int S32 = 32;
        public const int S48 = 48;

        // ---------- 形状层：全直角（问卷 Q23/77/86/91/114） ----------
        public const int RWindow = 0;
        public const int RControl = 0;
        public const int RItem = 0;

        // ---------- 字号层（逻辑 px；理论 §1.1 四档） ----------
        public const float FPageTitle = 24f;   // 保留给旧引用（v6 页面名用 16 bold）
        public const float FSettingTitle = 18f; // 设置页抬头
        public const float FBrand = 16f;       // 索引轨字标
        public const float FTitle = 16f;       // 列表标题（统一，不读业务字段）
        public const float FBody = 15f;        // 正文/输入（下限 15）
        public const float FCaption = 12f;     // 元信息
        public const float FAux = 13f;         // 辅助/按钮
        public const float FButton = 13f;
        public const float FNav = 13f;         // 索引项/段头

        // ---------- 尺寸层 ----------
        public const int HBar = 64;            // 顶栏
        public const int HBarB = 44;           // 筛选条
        public const int HControl = 32;      // 标准控件高（按钮/选择/勾选）
        public const int HIcon = 28;         // 兼容旧字段
        public const int HIconBtn = 32;      // 图标按钮标准尺寸（宽=高）
        public const int HWinBtn = 26;       // 自绘窗控高（顶条内）
        public const int HAction = 64;       // 动作栏文字按钮标准宽
        public const int Hglyph = 14;        // 单字符图标字号（◐/◷/↻/⋯/×/□）
        public const int RailW = 3;            // 重要度色条
        public const int RuleW = 1;            // 横线/装订线

        // ---------- 字体族（理论 M10：中文一套、西文一套；字重 ≤2） ----------
        public const string FontUI = "Microsoft YaHei UI";
        public const string FontMono = "Consolas";


        // ---------- 样张 B：石墨金属（同一结构，只换材质/冷暖；给"更冷更硬"的方向做对照） ----------
        public static Theme Graphite()
        {
            Theme t = new Theme();
            t.IsDark = true;
            t.Paper = H("#101013");
            t.Card = H("#17171B");
            t.CardHi = H("#1E1E23");
            t.Line = H("#2A2A31");
            t.LineStrong = H("#383841");
            t.Metal = H("#8C8C96");
            t.Text = H("#E8E8EC");
            t.SubText = H("#B0B0BA");
            t.Aux = H("#8F8F9A");
            t.Accent = H("#C9A96A");
            t.OnAccent = H("#101013");
            t.OnSolid = H("#101013");
            t.Dark = H("#E07070");
            t.Warn = H("#E0B060");
            t.Ok = H("#86AE9A");
            t.Hover = H("#1A1A1F");
            t.Selected = H("#22222A");
            t.Binding = H("#8C8C96");
            t.SoftLow = H("#6A7A6E");
            t.SoftMid = H("#9E8A5A");
            t.SoftHigh = H("#D8BE84");
            return t;
        }

        // ---------- 夜间：暖黑墨 + 黄铜
        public static Theme Night()
        {
            Theme t = new Theme();
            t.IsDark = true;
            t.Paper = H("#15110D");      // 暖黑（不是纯黑，Q19/Q76）
            t.Card = H("#1D1813");       // 纸面，比场亮 ~4%
            t.CardHi = H("#251E17");
            t.Line = H("#2E2820");       // 1px 横线（对底 1.29:1，纯结构线，不承担识别）
            t.LineStrong = H("#3D362B");
            t.Metal = H("#7A705F");      // 装订线/边框（对底 3.86:1；对抬高面仍 ≥3）
            t.Text = H("#EFE6D4");       // 墨（14.2:1）
            t.SubText = H("#B3A58C");    // 7.3:1
            t.Aux = H("#9C8E78");        // 5.5:1（选中底上 4.84:1，仍 ≥4.5）
            t.Accent = H("#D89A4E");     // 黄铜（7.7:1）
            t.OnAccent = H("#15110D");   // 黄铜上的墨（7.7:1，非白字，避免刺眼）
            t.OnSolid = H("#15110D");
            t.Dark = H("#E4795F");       // 6.0:1
            t.Warn = H("#E5B45E");       // 9.2:1
            t.Ok = H("#8FAE86");         // 7.2:1
            t.Hover = H("#211B15");
            t.Selected = H("#2A231B");
            t.Binding = H("#7A705F");
            t.SoftLow = H("#5F6B54");
            t.SoftMid = H("#A08449");
            t.SoftHigh = H("#E2A85C");
            return t;
        }

        // ---------- 日间：暖纸 + 深黄铜（同结构另一套值，不是反色；理论 U7） ----------
        public static Theme Day()
        {
            Theme t = new Theme();
            t.IsDark = false;
            t.Paper = H("#F2EAD8");      // 暖纸（Q83/Q110）
            t.Card = H("#F9F3E4");
            t.CardHi = H("#EFE6D2");
            t.Line = H("#D8CBB1");       // 对底 1.34:1
            t.LineStrong = H("#C4B49A");
            t.Metal = H("#8A7963");      // 3.51:1
            t.Text = H("#241E17");       // 14.9:1
            t.SubText = H("#564C3F");    // 7.6:1
            t.Aux = H("#665B4A");        // 6.0:1（选中底上 4.88:1）
            t.Accent = H("#8A5A2B");     // 5.3:1
            t.OnAccent = H("#FFFDF6");   // 5.8:1
            t.OnSolid = H("#FFFDF6");
            t.Dark = H("#A9301F");       // 6.1:1
            t.Warn = H("#7A5414");       // 6.1:1
            t.Ok = H("#3E6A45");         // 5.7:1
            t.Hover = H("#EDE3CF");
            t.Selected = H("#E7DCC4");
            t.Binding = H("#8A7963");
            t.SoftLow = H("#85956F");
            t.SoftMid = H("#8A6A2B");
            t.SoftHigh = H("#5E3510");
            return t;
        }

        private static Color H(string hex) { return ColorTranslator.FromHtml(hex); }

        // ---------- 跟随系统（需求 #26；问卷 Q116 深色第一）：默认值由系统决定，用户可覆盖 ----------
        public static bool SystemPrefersDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                if (v is int) return ((int)v) == 0;
            }
            catch { }
            return true;   // 取不到时按零号用户偏好（深色）
        }

        // 设置值：system / day / night（旧库只有 day/night，仍兼容）
        // ---------- v1 暖纸（历史主题表：纸白场 + 淡卡面 + 墨字 + 松绿主操作） ----------
        public static Theme WarmPaper()
        {
            Theme t = new Theme();
            t.IsDark = false;
            t.Paper = ColorTranslator.FromHtml("#FAF8F3");
            t.Card = ColorTranslator.FromHtml("#FFFFFF");
            t.CardHi = ColorTranslator.FromHtml("#F5F2EA");
            t.Hover = ColorTranslator.FromHtml("#F5F2EA");
            t.Selected = ColorTranslator.FromHtml("#EFEBE1");
            t.Line = ColorTranslator.FromHtml("#ECE7DD");
            t.LineStrong = ColorTranslator.FromHtml("#DED7C9");
            t.Metal = ColorTranslator.FromHtml("#DED7C9");
            t.Text = ColorTranslator.FromHtml("#1F2421");
            t.SubText = ColorTranslator.FromHtml("#5A615C");
            t.Aux = ColorTranslator.FromHtml("#646B65");
            t.Accent = ColorTranslator.FromHtml("#4F7256");
            t.OnAccent = ColorTranslator.FromHtml("#FFFFFF");
            t.OnSolid = ColorTranslator.FromHtml("#1F2421");
            t.Dark = ColorTranslator.FromHtml("#A33A2E");
            t.Warn = ColorTranslator.FromHtml("#9A6B1F");
            t.Ok = ColorTranslator.FromHtml("#4F7256");
            t.Binding = ColorTranslator.FromHtml("#DED7C9");
            t.SoftLow = ColorTranslator.FromHtml("#EFEBE1");
            t.SoftMid = ColorTranslator.FromHtml("#DED7C9");
            t.SoftHigh = ColorTranslator.FromHtml("#A9B7AD");
            return t;
        }
        public static Theme FromSetting(string v)
        {
            if (v == "v1" || v == "paper" || v == "day") return (v == "day") ? Day() : WarmPaper();
            if (v == "v6" || v == "night") return Night();
            if (v == "v7") return Day();
            if (v == "v8" || v == "graphite") return Graphite();
            return SystemPrefersDark() ? Night() : Day();
        }

        // ---------- 主题切换过渡：逐帧插值（只做颜色过渡 150ms；无位移动画；理论 U6） ----------
        public static Theme Blend(Theme a, Theme b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            Theme r = new Theme();
            r.IsDark = t < 0.5 ? a.IsDark : b.IsDark;
            r.Paper = Lerp(a.Paper, b.Paper, t);
            r.Card = Lerp(a.Card, b.Card, t);
            r.CardHi = Lerp(a.CardHi, b.CardHi, t);
            r.Line = Lerp(a.Line, b.Line, t);
            r.LineStrong = Lerp(a.LineStrong, b.LineStrong, t);
            r.Metal = Lerp(a.Metal, b.Metal, t);
            r.Text = Lerp(a.Text, b.Text, t);
            r.SubText = Lerp(a.SubText, b.SubText, t);
            r.Aux = Lerp(a.Aux, b.Aux, t);
            r.Accent = Lerp(a.Accent, b.Accent, t);
            r.OnAccent = Lerp(a.OnAccent, b.OnAccent, t);
            r.OnSolid = Lerp(a.OnSolid, b.OnSolid, t);
            r.Dark = Lerp(a.Dark, b.Dark, t);
            r.Warn = Lerp(a.Warn, b.Warn, t);
            r.Ok = Lerp(a.Ok, b.Ok, t);
            r.Hover = Lerp(a.Hover, b.Hover, t);
            r.Selected = Lerp(a.Selected, b.Selected, t);
            r.Binding = Lerp(a.Binding, b.Binding, t);
            r.SoftLow = Lerp(a.SoftLow, b.SoftLow, t);
            r.SoftMid = Lerp(a.SoftMid, b.SoftMid, t);
            r.SoftHigh = Lerp(a.SoftHigh, b.SoftHigh, t);
            return r;
        }

        // ---------- 醒目度色段：5 个离散档，不再连续渐变；行数＋符号仍同时编码（需求 #30） ----------
        public static int ScoreBand(double score)
        {
            double s = score;
            if (s < 0) s = 0;
            if (s > 1) s = 1;
            int b = (int)Math.Floor(s * 5.0);
            if (b > 4) b = 4;
            if (b < 0) b = 0;
            return b;
        }

        public static Color ScoreColor(double score, bool dark)
        {
            int b = ScoreBand(score);
            if (dark)
            {
                switch (b)
                {
                    case 0: return H("#7FA083");
                    case 1: return H("#A7B96B");
                    case 2: return H("#D9A84E");
                    case 3: return H("#E07A4F");
                    default: return H("#E0605A");
                }
            }
            switch (b)
            {
                case 0: return H("#6F8F72");
                case 1: return H("#8FA05C");
                case 2: return H("#C08A3E");
                case 3: return H("#B35C36");
                default: return H("#8F2F2F");
            }
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Mix(Color a, Color b, double t) { return Lerp(a, b, t); }

        // 半透明（浮层/滚动条/装饰线；不用于正文）
        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c.R, c.G, c.B); }
    }

    // ---------- Win32 深色标题栏 / 应用模式（让系统窗口边框与自绘内容同一套气质） ----------
    public static class Win32Theme
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int mode);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subApp, string subId);

        public static void DarkenScrollbars(System.Windows.Forms.Control c)
        {
            try { if (c != null && c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        // mode: 0 默认 / 1 AllowDark / 2 ForceDark / 3 ForceLight / 4 Max
        public static void SetAppMode(bool dark)
        {
            try { SetPreferredAppMode(dark ? 2 : 3); } catch { }
            try { FlushMenuThemes(); } catch { }
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int));   // Win10 1903+
                DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));   // Win10 1809
            }
            catch { }
        }
    }
}