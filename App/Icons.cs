// Icons.cs — v6 线性图标（1.5px 描边、16×16 逻辑格、全直角语言）
// 依据：审美决策记录「图标 1.5px 线性一套」、需求 #27（图标优先）、理论 U5（图标旁必须有文字标签）。
// 只画结构线，不用位图、不用彩色 emoji；颜色由调用方给（黄铜只给"要你注意的那一下"）。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Decanter.App
{
    public static class Icons
    {
        public static void Draw(Graphics g, string name, Rectangle r, Color c, float dpi)
        {
            if (string.IsNullOrEmpty(name) || r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1f, 1.5f * (dpi <= 0 ? 1f : dpi));
            using (Pen p = new Pen(c, w))
            using (SolidBrush b = new SolidBrush(c))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "all":
                        Line(g, p, r, 3, 4, 13, 4); Line(g, p, r, 3, 8, 13, 8); Line(g, p, r, 3, 12, 9, 12);
                        break;
                    case "today":
                        Rect(g, p, r, 3, 4, 10, 10); Line(g, p, r, 6, 2, 6, 5); Line(g, p, r, 10, 2, 10, 5);
                        Fill(g, b, r, 12.5f, 8.5f, 0.9f);
                        Fill(g, b, r, 12.5f, 11f, 0.9f);
                        break;
                    case "week":
                        Rect(g, p, r, 3, 4, 10, 10); Line(g, p, r, 6, 2, 6, 5); Line(g, p, r, 10, 2, 10, 5);
                        Line(g, p, r, 3, 7, 13, 7); Line(g, p, r, 8, 7, 8, 14);
                        break;
                    case "overdue":
                        Poly(g, p, r, new float[] { 8, 2.5f, 14, 13.5f, 2, 13.5f });
                        Line(g, p, r, 8, 6, 8, 9.5f); Fill(g, b, r, 8, 11.6f, 0.8f);
                        break;
                    case "done":
                        Ellipse(g, p, r, 2.5f, 2.5f, 11, 11); Check(g, p, r, 5, 8.4f, 7.2f, 10.6f, 11.2f, 6.2f);
                        break;
                    case "search":
                        Ellipse(g, p, r, 2.5f, 2.5f, 8, 8); Line(g, p, r, 9.8f, 9.8f, 14, 14);
                        break;
                    case "settings":
                        Line(g, p, r, 3, 4.5f, 13, 4.5f); Line(g, p, r, 3, 8, 13, 8); Line(g, p, r, 3, 11.5f, 13, 11.5f);
                        Fill(g, b, r, 6, 4.5f, 1.5f); Fill(g, b, r, 10.5f, 8, 1.5f); Fill(g, b, r, 7, 11.5f, 1.5f);
                        break;
                    case "theme":
                        Ellipse(g, p, r, 2.5f, 2.5f, 11, 11); Line(g, p, r, 8, 2.8f, 8, 13.2f);
                        using (SolidBrush hb = new SolidBrush(c))
                            g.FillPie(hb, X(r, 2.5f), Y(r, 2.5f), W(r, 11), H(r, 11), -90, 180);
                        break;
                    case "more":
                        Fill(g, b, r, 4, 8, 1.3f); Fill(g, b, r, 8, 8, 1.3f); Fill(g, b, r, 12, 8, 1.3f);
                        break;
                    case "pin":
                        Line(g, p, r, 5, 10, 11, 10); Line(g, p, r, 8, 2.5f, 8, 10); Line(g, p, r, 8, 10, 8, 14);
                        Poly(g, p, r, new float[] { 6, 2.5f, 10, 2.5f, 11.5f, 10, 4.5f, 10 });
                        break;
                    case "repeat":
                        g.DrawArc(p, X(r, 3), Y(r, 3), W(r, 9), H(r, 8), 200, 140);
                        Line(g, p, r, 9.6f, 3.1f, 12.2f, 3.4f); Line(g, p, r, 12.2f, 3.4f, 11.8f, 6);
                        g.DrawArc(p, X(r, 4), Y(r, 5), W(r, 9), H(r, 8), 20, 140);
                        Line(g, p, r, 6.4f, 12.9f, 3.8f, 12.6f); Line(g, p, r, 3.8f, 12.6f, 4.2f, 10);
                        break;
                    case "bell":
                        g.DrawArc(p, X(r, 4), Y(r, 3), W(r, 8), H(r, 9), 180, 180);
                        Line(g, p, r, 4, 7.5f, 4, 11); Line(g, p, r, 12, 7.5f, 12, 11);
                        Line(g, p, r, 3, 11, 13, 11); Line(g, p, r, 8, 11, 8, 13);
                        break;
                    case "plus":
                        Line(g, p, r, 8, 3, 8, 13); Line(g, p, r, 3, 8, 13, 8);
                        break;
                    case "close":
                        Line(g, p, r, 4, 4, 12, 12); Line(g, p, r, 12, 4, 4, 12);
                        break;
                    case "chevron":
                        Line(g, p, r, 6, 3.5f, 11, 8); Line(g, p, r, 11, 8, 6, 12.5f);
                        break;
                    case "check":
                        Check(g, p, r, 3.5f, 8.2f, 6.8f, 11.4f, 12.5f, 4.8f);
                        break;
                    case "edit":
                        Line(g, p, r, 3.5f, 12.5f, 11.5f, 4.5f); Line(g, p, r, 10.2f, 3.2f, 12.8f, 5.8f);
                        Line(g, p, r, 3.5f, 12.5f, 6, 12);
                        break;
                    case "trash":
                        Line(g, p, r, 3.5f, 4.5f, 12.5f, 4.5f); Line(g, p, r, 6, 2.5f, 10, 2.5f);
                        Poly(g, p, r, new float[] { 5, 4.5f, 11, 4.5f, 10, 13.5f, 6, 13.5f });
                        Line(g, p, r, 7, 7, 7, 11); Line(g, p, r, 9, 7, 9, 11);
                        break;
                    case "clock":
                        Ellipse(g, p, r, 2.5f, 2.5f, 11, 11); Line(g, p, r, 8, 5, 8, 8.5f); Line(g, p, r, 8, 8.5f, 10.5f, 10);
                        break;
                    case "decanter":
                        Poly(g, p, r, new float[] { 6.2f, 2, 9.8f, 2, 9.8f, 5, 12.5f, 8.5f, 12.5f, 13.5f, 3.5f, 13.5f, 3.5f, 8.5f, 6.2f, 5 });
                        Line(g, p, r, 4.2f, 10.5f, 11.8f, 10.5f);
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        private static float X(Rectangle r, float v) { return r.Left + v * r.Width / 16f; }
        private static float Y(Rectangle r, float v) { return r.Top + v * r.Height / 16f; }
        private static float W(Rectangle r, float v) { return v * r.Width / 16f; }
        private static float H(Rectangle r, float v) { return v * r.Height / 16f; }

        private static void Line(Graphics g, Pen p, Rectangle r, float x1, float y1, float x2, float y2)
        {
            g.DrawLine(p, X(r, x1), Y(r, y1), X(r, x2), Y(r, y2));
        }

        private static void Rect(Graphics g, Pen p, Rectangle r, float x, float y, float w, float h)
        {
            g.DrawRectangle(p, X(r, x), Y(r, y), W(r, w), H(r, h));
        }

        private static void Ellipse(Graphics g, Pen p, Rectangle r, float x, float y, float w, float h)
        {
            g.DrawEllipse(p, X(r, x), Y(r, y), W(r, w), H(r, h));
        }

        private static void Fill(Graphics g, SolidBrush b, Rectangle r, float x, float y, float rad)
        {
            float d = W(r, rad * 2);
            g.FillEllipse(b, X(r, x) - d / 2, Y(r, y) - d / 2, d, d);
        }

        private static void Poly(Graphics g, Pen p, Rectangle r, float[] pts)
        {
            PointF[] q = new PointF[pts.Length / 2];
            for (int i = 0; i < q.Length; i++) q[i] = new PointF(X(r, pts[i * 2]), Y(r, pts[i * 2 + 1]));
            g.DrawPolygon(p, q);
        }

        private static void Check(Graphics g, Pen p, Rectangle r, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            g.DrawLine(p, X(r, x1), Y(r, y1), X(r, x2), Y(r, y2));
            g.DrawLine(p, X(r, x2), Y(r, y2), X(r, x3), Y(r, y3));
        }
    }
}
