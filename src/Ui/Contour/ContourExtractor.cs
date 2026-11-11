using System;
using System.Collections.Generic;
using System.Windows;

namespace MirrorpondHUD.Ui.Contour
{
    /// <summary>一对交点构成的等值线片段（面板坐标系，单位：设备无关像素）。</summary>
    public struct ContourSegment
    {
        public double X0;
        public double Y0;
        public double X1;
        public double Y1;
    }

    /// <summary>串联后的折线。Closed 表示首尾同点、可以闭合描边。</summary>
    public sealed class ContourPath
    {
        public Point[] Points;
        public bool Closed;

        /// <summary>这条线代表的高度（归一化）。</summary>
        public double Level;

        /// <summary>属于第几级（从 0 开始）；只用于调试，不再区分计曲线。</summary>
        public int LevelIndex;

        /// <summary>折线总长（像素）。</summary>
        public double Length;

        /// <summary>闭环的有向面积；开放折线为 0。用于最小闭合环过滤。</summary>
        public double SignedArea;

        /// <summary>量长度与面积。改点列之后必须重算。</summary>
        public void Measure()
        {
            Length = 0.0;
            SignedArea = 0.0;
            if (Points == null || Points.Length < 2)
            {
                return;
            }

            for (int i = 0; i < Points.Length - 1; i++)
            {
                double dx = Points[i + 1].X - Points[i].X;
                double dy = Points[i + 1].Y - Points[i].Y;
                Length += Math.Sqrt(dx * dx + dy * dy);
            }

            if (!Closed)
            {
                return;
            }

            // 闭环的收尾段（末点回到首点）也要计入长度与面积。
            double cx = Points[Points.Length - 1].X - Points[0].X;
            double cy = Points[Points.Length - 1].Y - Points[0].Y;
            Length += Math.Sqrt(cx * cx + cy * cy);

            double area = 0.0;
            for (int i = 0; i < Points.Length; i++)
            {
                Point p = Points[i];
                Point q = Points[(i + 1) % Points.Length];
                area += p.X * q.Y - q.X * p.Y;
            }
            SignedArea = area * 0.5;
        }
    }

    /// <summary>
    /// Marching squares 等值线提取 + 折线串联 + Chaikin 平滑。
    ///
    /// 为什么不用现成库：仓库零第三方依赖；这里的实现不到 300 行，
    /// 还能顺手拿到"每条线属于哪一级、有多少顶点"这类统计，
    /// 面板底部的自证信息就是靠它。
    /// </summary>
    public static class ContourExtractor
    {
        /// <summary>端点焊接精度：1/1000 像素。相邻格子的交点本应完全相等，留一点余量防浮点抖动。</summary>
        private const double WeldScale = 1000.0;

        /// <summary>
        /// 对单个高度提取线段，追加到 sink，返回新增条数。
        ///
        /// 单元格四角命名：a=左上 b=右上 c=右下 d=左下（屏幕坐标，y 向下）。
        /// 按"四角是否高于目标高度"组成 4 位 case，再按 case 决定轮廓穿过哪两条边。
        /// case 5 / 10 是马鞍形歧义：两条等值线在格内交叉，必须用中心值判连通，
        /// 否则山谷会被画成山脊（这是 marching squares 最经典的一处错）。
        /// </summary>
        public static int Extract(ScalarGrid grid, double level, List<ContourSegment> sink)
        {
            if (grid == null)
            {
                throw new ArgumentNullException("grid");
            }
            if (sink == null)
            {
                throw new ArgumentNullException("sink");
            }

            int before = sink.Count;
            double cw = grid.CellW;
            double ch = grid.CellH;
            int vcols = grid.VertexCols;
            int rows = grid.Rows;
            int cols = grid.Cols;
            double[] values = grid.Values;

            for (int j = 0; j < rows; j++)
            {
                int row0 = j * vcols;
                int row1 = (j + 1) * vcols;
                double y0 = j * ch;
                double y1 = (j + 1) * ch;

                for (int i = 0; i < cols; i++)
                {
                    double a = values[row0 + i];
                    double b = values[row0 + i + 1];
                    double c = values[row1 + i + 1];
                    double d = values[row1 + i];

                    int code = 0;
                    if (a >= level)
                    {
                        code |= 1;
                    }
                    if (b >= level)
                    {
                        code |= 2;
                    }
                    if (c >= level)
                    {
                        code |= 4;
                    }
                    if (d >= level)
                    {
                        code |= 8;
                    }
                    if (code == 0 || code == 15)
                    {
                        continue;
                    }

                    double x0 = i * cw;
                    double x1 = (i + 1) * cw;

                    // 四条边上的交点。用不到的那几条也会算（最多 4 次除法），
                    // 换来的是没有分支的直线代码；交叉格子只占少数，这点浪费可以接受。
                    double topX = x0 + cw * Ratio(level, a, b);
                    double rightY = y0 + ch * Ratio(level, b, c);
                    double bottomX = x0 + cw * Ratio(level, d, c);
                    double leftY = y0 + ch * Ratio(level, a, d);

                    switch (code)
                    {
                        case 1:
                            Add(sink, x0, leftY, topX, y0);
                            break;
                        case 2:
                            Add(sink, topX, y0, x1, rightY);
                            break;
                        case 3:
                            Add(sink, x0, leftY, x1, rightY);
                            break;
                        case 4:
                            Add(sink, x1, rightY, bottomX, y1);
                            break;
                        case 6:
                            Add(sink, topX, y0, bottomX, y1);
                            break;
                        case 7:
                            Add(sink, x0, leftY, bottomX, y1);
                            break;
                        case 8:
                            Add(sink, bottomX, y1, x0, leftY);
                            break;
                        case 9:
                            Add(sink, topX, y0, bottomX, y1);
                            break;
                        case 11:
                            Add(sink, x1, rightY, bottomX, y1);
                            break;
                        case 12:
                            Add(sink, x1, rightY, x0, leftY);
                            break;
                        case 13:
                            Add(sink, topX, y0, x1, rightY);
                            break;
                        case 14:
                            Add(sink, x0, leftY, topX, y0);
                            break;
                        case 5:
                            {
                                // a、c 高于目标：马鞍。中心高于目标说明高地连成一片，
                                // 轮廓线各自绕开低角 b、d；否则低地相连，轮廓绕开 a、c。
                                double center = (a + b + c + d) * 0.25;
                                if (center >= level)
                                {
                                    Add(sink, topX, y0, x1, rightY);
                                    Add(sink, x0, leftY, bottomX, y1);
                                }
                                else
                                {
                                    Add(sink, x0, leftY, topX, y0);
                                    Add(sink, x1, rightY, bottomX, y1);
                                }
                                break;
                            }
                        case 10:
                            {
                                // b、d 高于目标，同上的镜像情形。
                                double center = (a + b + c + d) * 0.25;
                                if (center >= level)
                                {
                                    Add(sink, x0, leftY, topX, y0);
                                    Add(sink, x1, rightY, bottomX, y1);
                                }
                                else
                                {
                                    Add(sink, topX, y0, x1, rightY);
                                    Add(sink, x0, leftY, bottomX, y1);
                                }
                                break;
                            }
                    }
                }
            }

            return sink.Count - before;
        }

        /// <summary>
        /// 把零散线段串成折线。端点键控在 1/1000 像素上，所以相邻格子的交点
        /// 能精确对上——这正是 marching squares 让相邻格子共享边的意义。
        ///
        /// 先走"自由端"（只挂一条线段的端点），再收剩下的闭环：
        /// 反过来的话，闭环会从中间被切开，接缝处多出一个不存在的端点。
        /// </summary>
        public static List<ContourPath> Chain(List<ContourSegment> segments, int smoothIterations)
        {
            List<ContourPath> result = new List<ContourPath>();
            if (segments == null || segments.Count == 0)
            {
                return result;
            }

            Dictionary<long, List<int>> map = new Dictionary<long, List<int>>(segments.Count * 2);
            for (int i = 0; i < segments.Count; i++)
            {
                ContourSegment s = segments[i];
                PushEnd(map, s.X0, s.Y0, i << 1);
                PushEnd(map, s.X1, s.Y1, (i << 1) | 1);
            }

            bool[] used = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }
                ContourSegment s = segments[i];
                bool freeStart = Degree(map, s.X0, s.Y0) == 1;
                bool freeEnd = Degree(map, s.X1, s.Y1) == 1;
                if (!freeStart && !freeEnd)
                {
                    continue;
                }
                Walk(map, segments, used, i, freeStart, false, smoothIterations, result);
            }

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }
                Walk(map, segments, used, i, true, true, smoothIterations, result);
            }

            return result;
        }

        /// <summary>
        /// Chaikin 角切平滑。原始 marching squares 的折线是每格一段的硬折线，
        /// 直接描边会有明显的锯齿台阶；切两轮之后才像手绘的等高线。
        /// 开放折线保留首尾（否则线会缩进），闭环按环形处理。
        /// </summary>
        public static Point[] Chaikin(IList<Point> source, bool closed, int iterations)
        {
            if (source == null || source.Count < 3 || iterations <= 0)
            {
                Point[] copy = new Point[source == null ? 0 : source.Count];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = source[i];
                }
                return copy;
            }

            List<Point> current = new List<Point>(source);

            for (int it = 0; it < iterations; it++)
            {
                int n = current.Count;
                List<Point> next = new List<Point>(n * 2);

                if (closed)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Point p = current[i];
                        Point q = current[(i + 1) % n];
                        next.Add(new Point(p.X * 0.75 + q.X * 0.25, p.Y * 0.75 + q.Y * 0.25));
                        next.Add(new Point(p.X * 0.25 + q.X * 0.75, p.Y * 0.25 + q.Y * 0.75));
                    }
                }
                else
                {
                    next.Add(current[0]);
                    for (int i = 0; i < n - 1; i++)
                    {
                        Point p = current[i];
                        Point q = current[i + 1];
                        next.Add(new Point(p.X * 0.75 + q.X * 0.25, p.Y * 0.75 + q.Y * 0.25));
                        next.Add(new Point(p.X * 0.25 + q.X * 0.75, p.Y * 0.25 + q.Y * 0.75));
                    }
                    next.Add(current[n - 1]);
                }

                current = next;
            }

            return current.ToArray();
        }

        private static void Add(List<ContourSegment> sink, double ax, double ay, double bx, double by)
        {
            // 顶点恰好落在等高线上时会算出两个重合的交点。零长度线段对串联有害：
            // 它会在同一个键上挂两条记录，把一条闭环切成碎片。直接丢掉。
            double dx = bx - ax;
            double dy = by - ay;
            if (dx * dx + dy * dy < 1e-12)
            {
                return;
            }

            ContourSegment segment;
            segment.X0 = ax;
            segment.Y0 = ay;
            segment.X1 = bx;
            segment.Y1 = by;
            sink.Add(segment);
        }

        /// <summary>边 a→b 上的交点参数 t；两边等高时退化取中点（此时这条边本来就不该被交叉）。</summary>
        private static double Ratio(double level, double va, double vb)
        {
            double den = vb - va;
            if (Math.Abs(den) < 1e-12)
            {
                return 0.5;
            }
            return (level - va) / den;
        }

        private static long Key(double x, double y)
        {
            long ix = (long)Math.Round(x * WeldScale);
            long iy = (long)Math.Round(y * WeldScale);
            return (ix << 32) ^ (iy & 0xFFFFFFFFL);
        }

        private static void PushEnd(Dictionary<long, List<int>> map, double x, double y, int code)
        {
            long key = Key(x, y);
            List<int> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(code);
        }

        private static int Degree(Dictionary<long, List<int>> map, double x, double y)
        {
            List<int> list;
            if (!map.TryGetValue(Key(x, y), out list))
            {
                return 0;
            }
            return list.Count;
        }

        private static void Walk(
            Dictionary<long, List<int>> map,
            List<ContourSegment> segments,
            bool[] used,
            int startSeg,
            bool startAtFirstEnd,
            bool tryClose,
            int smoothIterations,
            List<ContourPath> output)
        {
            List<Point> points = new List<Point>(24);

            int segId = startSeg;
            bool atFirstEnd = startAtFirstEnd;

            ContourSegment seg = segments[segId];
            Point p0 = new Point(seg.X0, seg.Y0);
            Point p1 = new Point(seg.X1, seg.Y1);
            Point start = atFirstEnd ? p0 : p1;
            points.Add(start);

            bool closed = false;
            while (true)
            {
                used[segId] = true;
                Point target = atFirstEnd ? p1 : p0;

                // 回到起点即闭环：此时不把起点再塞一次，闭合由几何的 IsClosed 表达。
                if (tryClose && points.Count > 2
                    && Math.Abs(target.X - start.X) < 0.01
                    && Math.Abs(target.Y - start.Y) < 0.01)
                {
                    closed = true;
                    break;
                }

                points.Add(target);

                List<int> candidates;
                if (!map.TryGetValue(Key(target.X, target.Y), out candidates))
                {
                    break;
                }

                int nextId = -1;
                bool nextAtFirstEnd = true;
                for (int k = 0; k < candidates.Count; k++)
                {
                    int code = candidates[k];
                    int candidateId = code >> 1;
                    if (used[candidateId])
                    {
                        continue;
                    }
                    nextId = candidateId;
                    nextAtFirstEnd = (code & 1) == 0;
                    break;
                }

                if (nextId < 0)
                {
                    break;
                }

                segId = nextId;
                atFirstEnd = nextAtFirstEnd;
                seg = segments[segId];
                p0 = new Point(seg.X0, seg.Y0);
                p1 = new Point(seg.X1, seg.Y1);
            }

            if (points.Count < 2)
            {
                return;
            }

            Point[] smoothed = Chaikin(points, closed, smoothIterations);
            if (smoothed.Length < 2)
            {
                return;
            }

            ContourPath path = new ContourPath();
            path.Points = smoothed;
            path.Closed = closed;
            path.Measure();
            output.Add(path);
        }
    }
}
