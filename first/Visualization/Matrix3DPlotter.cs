using System;
using System.Drawing;

namespace first
{
    public static class Matrix3DPlotter
    {
        public static (double x, double y) Project3D(double u, double v, double w, double yawDeg, double pitchDeg)
        {
            double yawRad = yawDeg * Math.PI / 180.0;
            double pitchRad = pitchDeg * Math.PI / 180.0;

            double x0 = u - 0.5;
            double y0 = v - 0.5;
            double z0 = w - 0.5;

            double x1 = x0 * Math.Cos(yawRad) - y0 * Math.Sin(yawRad);
            double y1 = x0 * Math.Sin(yawRad) + y0 * Math.Cos(yawRad);
            double z1 = z0;

            double x2 = x1;
            double y2 = y1 * Math.Sin(pitchRad) + z1 * Math.Cos(pitchRad);

            return (x2 * 1.8, y2 * 1.5);
        }

        public static void RenderHeatmap(ScottPlot.Plot plt, double[,] times, int stepM, int stepN, string unitName = "мс")
        {
            plt.Clear();
            int rowsM = times.GetLength(0);
            int colsN = times.GetLength(1);

            double xMin = stepM;
            double xMax = rowsM * stepM;
            double yMin = stepN;
            double yMax = colsN * stepN;

            var hm = plt.AddHeatmap(times, ScottPlot.Drawing.Colormap.Turbo, lockScales: false);
            hm.FlipVertically = true;
            hm.XMin = xMin;
            hm.XMax = xMax;
            hm.YMin = yMin;
            hm.YMax = yMax;
            var cb = plt.AddColorbar(hm, 0);
            cb.Label = $"Время T ({unitName})";

            plt.Title("Тепловая карта (Heatmap): время T от M и N (T × M × N)");
            plt.XLabel("Размерность M (строки A / столбцы B)");
            plt.YLabel("Размерность N (столбцы A / строки B)");
            plt.SetAxisLimits(xMin - stepM * 0.5, xMax + stepM * 0.5, yMin - stepN * 0.5, yMax + stepN * 0.5);
            plt.XAxis.Ticks(true);
            plt.YAxis.Ticks(true);
            plt.XAxis.Grid(true);
            plt.YAxis.Grid(true);
        }

        public static void RenderWireframe(
            ScottPlot.Plot plt,
            double[,] times,
            int stepM,
            int stepN,
            string unitName = "мс",
            double yawDeg = 35,
            double pitchDeg = 25)
        {
            plt.Clear();
            int rowsM = times.GetLength(0);
            int colsN = times.GetLength(1);

            double minT = double.MaxValue, maxT = double.MinValue;
            for (int r = 0; r < rowsM; r++)
            {
                for (int c = 0; c < colsN; c++)
                {
                    if (times[r, c] < minT) minT = times[r, c];
                    if (times[r, c] > maxT) maxT = times[r, c];
                }
            }
            if (minT > maxT) { minT = 0; maxT = 1; }
            double rangeT = Math.Max(1e-12, maxT - minT);
            int maxM = rowsM * stepM;
            int maxN = colsN * stepN;

            // 1. Координатная сетка пола (T = 0)
            var floorColor = Color.FromArgb(220, 225, 230);
            int gridTicks = 5;
            for (int i = 0; i <= gridTicks; i++)
            {
                double u = (double)i / gridTicks;
                var p1 = Project3D(u, 0, 0, yawDeg, pitchDeg);
                var p2 = Project3D(u, 1, 0, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { p1.x, p2.x }, new[] { p1.y, p2.y }, floorColor, 1.0f);
            }
            for (int j = 0; j <= gridTicks; j++)
            {
                double v = (double)j / gridTicks;
                var p1 = Project3D(0, v, 0, yawDeg, pitchDeg);
                var p2 = Project3D(1, v, 0, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { p1.x, p2.x }, new[] { p1.y, p2.y }, floorColor, 1.0f);
            }

            // 2. Ограничивающий 3D параллелепипед (задние грани и потолок)
            var boxColor = Color.FromArgb(235, 238, 242);
            var b001 = Project3D(0, 0, 1, yawDeg, pitchDeg);
            var b101 = Project3D(1, 0, 1, yawDeg, pitchDeg);
            var b111 = Project3D(1, 1, 1, yawDeg, pitchDeg);
            var b011 = Project3D(0, 1, 1, yawDeg, pitchDeg);
            plt.AddScatterLines(new[] { b001.x, b101.x, b111.x, b011.x, b001.x },
                                new[] { b001.y, b101.y, b111.y, b011.y, b001.y }, boxColor, 1.0f);

            var b100 = Project3D(1, 0, 0, yawDeg, pitchDeg);
            var b010 = Project3D(0, 1, 0, yawDeg, pitchDeg);
            var b110 = Project3D(1, 1, 0, yawDeg, pitchDeg);
            plt.AddScatterLines(new[] { b100.x, b101.x }, new[] { b100.y, b101.y }, boxColor, 1.0f);
            plt.AddScatterLines(new[] { b010.x, b011.x }, new[] { b010.y, b011.y }, boxColor, 1.0f);
            plt.AddScatterLines(new[] { b110.x, b111.x }, new[] { b110.y, b111.y }, boxColor, 1.0f);

            // 3. Основные 3D координатные оси (M, N, T)
            var pOrigin = Project3D(0, 0, 0, yawDeg, pitchDeg);

            // Ось M
            var pMEnd = Project3D(1.08, 0, 0, yawDeg, pitchDeg);
            plt.AddScatterLines(new[] { pOrigin.x, pMEnd.x }, new[] { pOrigin.y, pMEnd.y }, Color.FromArgb(39, 174, 96), 2.2f);
            var pMArr = Project3D(1.14, 0, 0, yawDeg, pitchDeg);
            plt.AddArrow(pMArr.x, pMArr.y, pMEnd.x, pMEnd.y, 2, Color.FromArgb(39, 174, 96));
            var pMLbl = Project3D(1.14, 0, 0, yawDeg, pitchDeg);
            plt.AddText($"Ось M (до {maxM})", pMLbl.x - 0.06, pMLbl.y - 0.06, 10, Color.FromArgb(39, 174, 96));

            // Засечки по оси M
            for (int i = 1; i <= gridTicks; i++)
            {
                double u = (double)i / gridTicks;
                int valM = (int)Math.Round(u * maxM);
                var pt = Project3D(u, 0, 0, yawDeg, pitchDeg);
                var ptOut = Project3D(u, -0.04, 0, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { pt.x, ptOut.x }, new[] { pt.y, ptOut.y }, Color.FromArgb(120, Color.Gray), 1.0f);
                plt.AddText($"{valM}", ptOut.x, ptOut.y - 0.03, 8, Color.FromArgb(100, 100, 100));
            }

            // Ось N
            var pNEnd = Project3D(0, 1.08, 0, yawDeg, pitchDeg);
            plt.AddScatterLines(new[] { pOrigin.x, pNEnd.x }, new[] { pOrigin.y, pNEnd.y }, Color.FromArgb(41, 128, 185), 2.2f);
            var pNArr = Project3D(0, 1.14, 0, yawDeg, pitchDeg);
            plt.AddArrow(pNArr.x, pNArr.y, pNEnd.x, pNEnd.y, 2, Color.FromArgb(41, 128, 185));
            var pNLbl = Project3D(0, 1.15, 0, yawDeg, pitchDeg);
            plt.AddText($"Ось N (до {maxN})", pNLbl.x - 0.16, pNLbl.y + 0.04, 10, Color.FromArgb(41, 128, 185));

            // Засечки по оси N
            for (int j = 1; j <= gridTicks; j++)
            {
                double v = (double)j / gridTicks;
                int valN = (int)Math.Round(v * maxN);
                var pt = Project3D(0, v, 0, yawDeg, pitchDeg);
                var ptOut = Project3D(-0.04, v, 0, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { pt.x, ptOut.x }, new[] { pt.y, ptOut.y }, Color.FromArgb(120, Color.Gray), 1.0f);
                plt.AddText($"{valN}", ptOut.x - 0.04, ptOut.y - 0.02, 8, Color.FromArgb(100, 100, 100));
            }

            // Ось T
            var pTEnd = Project3D(0, 0, 1.08, yawDeg, pitchDeg);
            plt.AddScatterLines(new[] { pOrigin.x, pTEnd.x }, new[] { pOrigin.y, pTEnd.y }, Color.FromArgb(192, 57, 43), 2.5f);
            var pTArr = Project3D(0, 0, 1.15, yawDeg, pitchDeg);
            plt.AddArrow(pTArr.x, pTArr.y, pTEnd.x, pTEnd.y, 2, Color.FromArgb(192, 57, 43));
            var pTLbl = Project3D(0, 0, 1.20, yawDeg, pitchDeg);
            plt.AddText($"Ось T ({unitName})", pTLbl.x - 0.08, pTLbl.y + 0.03, 11, Color.FromArgb(192, 57, 43));

            // Засечки по оси T
            for (int k = 1; k <= 4; k++)
            {
                double w = (double)k / 4.0;
                double valT = minT + w * rangeT;
                var pt = Project3D(0, 0, w, yawDeg, pitchDeg);
                var ptOut = Project3D(-0.04, 0, w, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { pt.x, ptOut.x }, new[] { pt.y, ptOut.y }, Color.FromArgb(192, 57, 43), 1.2f);
                plt.AddText($"{valT:F2}", ptOut.x - 0.08, ptOut.y, 8, Color.FromArgb(192, 57, 43));
            }

            // 4. Поверхность 3D: сетка линий по строкам M и столбцам N
            for (int r = 0; r < rowsM; r++)
            {
                double u = rowsM > 1 ? (double)r / (rowsM - 1) : 0;
                var xs = new double[colsN];
                var ys = new double[colsN];
                for (int c = 0; c < colsN; c++)
                {
                    double v = colsN > 1 ? (double)c / (colsN - 1) : 0;
                    double w = (times[r, c] - minT) / rangeT;
                    var p = Project3D(u, v, w, yawDeg, pitchDeg);
                    xs[c] = p.x;
                    ys[c] = p.y;
                }

                float fraction = (float)((times[r, colsN - 1] - minT) / rangeT);
                var color = InterpolateColor(Color.FromArgb(41, 128, 185), Color.FromArgb(231, 76, 60), fraction);
                plt.AddScatterLines(xs, ys, color, 1.8f);
                plt.AddScatterPoints(xs, ys, color, 4.0f);
            }

            for (int c = 0; c < colsN; c++)
            {
                double v = colsN > 1 ? (double)c / (colsN - 1) : 0;
                var xs = new double[rowsM];
                var ys = new double[rowsM];
                for (int r = 0; r < rowsM; r++)
                {
                    double u = rowsM > 1 ? (double)r / (rowsM - 1) : 0;
                    double w = (times[r, c] - minT) / rangeT;
                    var p = Project3D(u, v, w, yawDeg, pitchDeg);
                    xs[r] = p.x;
                    ys[r] = p.y;
                }
                plt.AddScatterLines(xs, ys, Color.FromArgb(90, Color.SteelBlue), 1.0f);
            }

            // Линии сброса (Drop-lines) от дальнего угла поверхности к полу
            if (rowsM > 0 && colsN > 0)
            {
                double wMax = (times[rowsM - 1, colsN - 1] - minT) / rangeT;
                var pSurfCorner = Project3D(1, 1, wMax, yawDeg, pitchDeg);
                var pFloorCorner = Project3D(1, 1, 0, yawDeg, pitchDeg);
                plt.AddScatterLines(new[] { pSurfCorner.x, pFloorCorner.x },
                                    new[] { pSurfCorner.y, pFloorCorner.y },
                                    Color.FromArgb(140, Color.IndianRed), 1.2f);
            }

            // Заголовок и аннотация
            plt.Title("3D График сложности матричного умножения: T × M × N");
            plt.AddAnnotation($"A(M×N) × B(N×M) → C(M×M) | O(M²·N)\nMax M={maxM}, Max N={maxN}\nMax T={maxT:F2} {unitName}", ScottPlot.Alignment.UpperLeft);

            plt.SetAxisLimits(-1.75, 1.85, -1.35, 1.35);
            plt.XAxis.Ticks(false);
            plt.YAxis.Ticks(false);
            plt.XAxis.Grid(false);
            plt.YAxis.Grid(false);
        }

        private static Color InterpolateColor(Color c1, Color c2, float fraction)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            int r = (int)(c1.R + (c2.R - c1.R) * fraction);
            int g = (int)(c1.G + (c2.G - c1.G) * fraction);
            int b = (int)(c1.B + (c2.B - c1.B) * fraction);
            return Color.FromArgb(r, g, b);
        }
    }
}
