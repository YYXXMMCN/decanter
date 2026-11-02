// Layout.cs — v6 版式唯一真源（逻辑 px，8 栅格）
// 目的：把窗口/索引轨/书写区/列表/动作栏的每一个尺寸收在一处，绘制、命中与自检都引用它，
//       不再出现"自检写死 64/108/44、界面改了自检不知道"的断言漂移（acceptance-notes 缺陷 1/2）。
// 结构：左索引轨（品牌/视图/搜索/设置）+ 右账本页（页眉/书写/横线列表/状态栏）。
using System;

namespace Decanter.App
{
    public static class Layout
    {
        // ---------- 窗口 ----------
        public const int DefaultW = 1120;
        public const int DefaultH = 720;
        public const int MinW = 880;
        public const int MinH = 560;

        // ---------- 整体 ----------
        public const int RailW = 208;          // 左索引轨
        public const int RailPadX = 20;
        public const int PagePadX = 40;        // 页面左右页边
        public const int MinPageContentW = 640;  // task-128: usable page content width
        public const int MinListH = 320;         // task-128: usable list height (>=2-3 rows)
        public const int MinWindowW = 960;       // 208 + 80 + 640 = 928 -> rounded 960
        public const int MinWindowH = 620;
        public const int PageMaxW = 760;       // 版心封顶（理论 M2：版心≤60–70 汉字）
        public const int PageTopStripH = 44;   // 页眉（视图名 + 计数 + 窗控）
        public const int ComposeH = 96;        // 书写区（输入 32 + 值令牌 28 + 上下留白）
        public const int ComposeHNote = 128;   // 展开「细节」时
        public const int StatusH = 28;         // 空闲状态栏
        public const int StatusSelH = 44;      // 选中动作栏
        public const int PageGap = 8;          // 页眉/书写/列表之间

        // ---------- 索引轨 ----------
        public const int RailBrandLogo = 28;
        public const int RailBrandY = 16;
        public const int RailSectionY = 76;
        public const int RailNavTop = 96;
        public const int RailNavH = 34;
        public const int RailNavGap = 2;
        public const int RailSearchH = 32;
        public const int RailFootH = 30;
        public const int RailFootGap = 12;

        // ---------- 书写区 ----------
        public const int ComposeRowY = 16;
        public const int ComposeRowH = 32;
        public const int TokenRowY = 56;
        public const int TokenRowH = 28;
        public const int NoteRowY = 88;
        public const int AddW = 88;
        public const int MoreW = 32;
        public const int EditBtnW = 64;

        // ---------- 列表（横线本） ----------
        public const int ListPadY = 8;         // 列表上下内边距
        public const int SectionH = 28;        // 段头高
        public const int SectionGap = 16;      // 段间（组外）间距
        public const int SectionAfter = 8;     // 段头到第一条
        public const int RowPadY = 8;          // 行内上下
        public const int TitleLineH = 24;
        public const int MetaLineH = 18;
        public const int RailBarW = 3;         // 重要度色段
        public const int BindX = 8;            // 装订线 x
        public const int BarX = 20;            // 色段 x
        public const int CircleX = 32;         // 完成圈 x
        public const int CircleVisual = 24;
        public const int CircleHit = 32;
        public const int TextX = 68;           // 标题/元信息起点
        public const int RightColW = 112;      // 右列（截止）宽
        public const int RightEdge = 8;        // 右页边（列表内）
        public const int ScrollW = 6;
        public const int TextGap = 12;
        public const int RuleInset = 8;        // 横线左端

        // ---------- 字体字号（与 Theme 令牌一致） ----------
        public const float FPageTitle = 16f;   // 页面/视图名（bold）
        public const float FBrand = 16f;       // 字标（bold）
        public const float FTitle = 16f;       // 列表标题
        public const float FBody = 15f;        // 正文/输入
        public const float FNav = 13f;         // 导航/按钮
        public const float FAux = 13f;         // 辅助
        public const float FMeta = 12f;        // 元信息/日期
        public const float FSection = 12f;     // 段头

        // ---------- 字号档位（与 Theme.F* 保持一致，供自检引用） ----------
        public static int TitlePx(float fontScale) { return AtLeast(FTitle * fontScale, 15); }
        public static int BodyPx(float fontScale) { return AtLeast(FBody * fontScale, 15); }
        public static int MetaPx(float fontScale) { return AtLeast(FMeta * fontScale, 12); }
        public static int AuxPx(float fontScale) { return AtLeast(FAux * fontScale, 12); }

        private static int AtLeast(float v, float floor)
        {
            int i = (int)Math.Round(v);
            int f = (int)Math.Round(floor);
            return i < f ? f : i;
        }
    }
}
