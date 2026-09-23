using System;
using ScottPlot;

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

        public static void RenderHeatmap(Plot plt, double[,] times, int stepM, int stepN, string unitName = "мс")
        {
            plt.Clear();
            int rowsM = times.GetLength(0);
            int colsN = times.GetLength(1);

            var hm = plt.Add.Heatmap(times);
            hm.Colormap = new ScottPlot.Colormaps.Turbo();
            var cb = plt.Add.ColorBar(hm);
            cb.Label = $"Время T ({unitName})";

            plt.Title("Тепловая карта (Heatmap): время T от M и N (T × M × N)");
            plt.XLabel("Размерность M (строки A / столбцы B)");
            plt.YLabel("Размерность N (столбцы A / строки B)");
        }

        public static void RenderWireframe(
            Plot plt,
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
            var floorColor = Color.FromHex("#DCE1E6");
            int gridTicks = 5;
            for (int i = 0; i <= gridTicks; i++)
            {
                double u = (double)i / gridTicks;
                var p1 = Project3D(u, 0, 0, yawDeg, pitchDeg);
                var p2 = Project3D(u, 1, 0, yawDeg, pitchDeg);
                AddLine(plt, p1, p2, floorColor, 1.0f);
            }
            for (int j = 0; j <= gridTicks; j++)
            {
                double v = (double)j / gridTicks;
                var p1 = Project3D(0, v, 0, yawDeg, pitchDeg);
                var p2 = Project3D(1, v, 0, yawDeg, pitchDeg);
                AddLine(plt, p1, p2, floorColor, 1.0f);
            }

            // 2. Ограничивающий 3D параллелепипед
            var boxColor = Color.FromHex("#EBEFF2");
            var b001 = Project3D(0, 0, 1, yawDeg, pitchDeg);
            var b101 = Project3D(1, 0, 1, yawDeg, pitchDeg);
            var b111 = Project3D(1, 1, 1, yawDeg, pitchDeg);
            var b011 = Project3D(0, 1, 1, yawDeg, pitchDeg);

            AddLine(plt, b001, b101, boxColor, 1.0f);
            AddLine(plt, b101, b111, boxColor, 1.0f);
            AddLine(plt, b111, b011, boxColor, 1.0f);
            AddLine(plt, b011, b001, boxColor, 1.0f);

            var b100 = Project3D(1, 0, 0, yawDeg, pitchDeg);
            var b010 = Project3D(0, 1, 0, yawDeg, pitchDeg);
            var b110 = Project3D(1, 1, 0, yawDeg, pitchDeg);

            AddLine(plt, b100, b101, boxColor, 1.0f);
            AddLine(plt, b010, b011, boxColor, 1.0f);
            AddLine(plt, b110, b111, boxColor, 1.0f);

            // 3. Координатные оси M, N, T
            var pOrigin = Project3D(0, 0, 0, yawDeg, pitchDeg);
            var pM = Project3D(1.15, 0, 0, yawDeg, pitchDeg);
            var pN = Project3D(0, 1.15, 0, yawDeg, pitchDeg);
            var pT = Project3D(0, 0, 1.15, yawDeg, pitchDeg);

            AddLine(plt, pOrigin, pM, Color.FromHex("#E74C3C"), 2.5f);
            AddLine(plt, pOrigin, pN, Color.FromHex("#2ECC71"), 2.5f);
            AddLine(plt, pOrigin, pT, Color.FromHex("#3498DB"), 2.5f);

            plt.Add.Text($"Ось M (строки, 0..{maxM})", pM.x, pM.y);
            plt.Add.Text($"Ось N (столбцы, 0..{maxN})", pN.x, pN.y);
            plt.Add.Text($"Ось T ({unitName}, 0..{maxT:0.0})", pT.x, pT.y);

            // 4. Каркас поверхности сложности T(M, N)
            var projX = new double[rowsM, colsN];
            var projY = new double[rowsM, colsN];
            for (int r = 0; r < rowsM; r++)
            {
                double u = (double)(r + 1) / rowsM;
                for (int c = 0; c < colsN; c++)
                {
                    double v = (double)(c + 1) / colsN;
                    double w = (times[r, c] - minT) / rangeT;
                    var pt = Project3D(u, v, w, yawDeg, pitchDeg);
                    projX[r, c] = pt.x;
                    projY[r, c] = pt.y;
                }
            }

            // Рёбра вдоль M
            for (int c = 0; c < colsN; c++)
            {
                double[] xs = new double[rowsM];
                double[] ys = new double[rowsM];
                for (int r = 0; r < rowsM; r++)
                {
                    xs[r] = projX[r, c];
                    ys[r] = projY[r, c];
                }
                var sc = plt.Add.Scatter(xs, ys);
                sc.MarkerSize = 0;
                sc.LineWidth = 1.6f;
                sc.Color = Color.FromHex("#2980B9");
            }

            // Рёбра вдоль N
            for (int r = 0; r < rowsM; r++)
            {
                double[] xs = new double[colsN];
                double[] ys = new double[colsN];
                for (int c = 0; c < colsN; c++)
                {
                    xs[c] = projX[r, c];
                    ys[c] = projY[r, c];
                }
                var sc = plt.Add.Scatter(xs, ys);
                sc.MarkerSize = 0;
                sc.LineWidth = 1.6f;
                sc.Color = Color.FromHex("#27AE60");
            }

            // Узловые точки
            int totalPts = rowsM * colsN;
            double[] allXs = new double[totalPts];
            double[] allYs = new double[totalPts];
            int idx = 0;
            for (int r = 0; r < rowsM; r++)
                for (int c = 0; c < colsN; c++)
                {
                    allXs[idx] = projX[r, c];
                    allYs[idx] = projY[r, c];
                    idx++;
                }

            var scPts = plt.Add.Scatter(allXs, allYs);
            scPts.MarkerSize = 5;
            scPts.LineWidth = 0;
            scPts.Color = Color.FromHex("#8E44AD");

            plt.Title($"3D Пространственная сложность матричного умножения T(M,N) ~ O(M²·N)\nMax M={maxM}, Max N={maxN}, T_max={maxT:0.0} {unitName}");
            plt.Axes.Frameless();
            plt.HideGrid();
        }

        private static void AddLine(Plot plt, (double x, double y) p1, (double x, double y) p2, Color color, float width)
        {
            var sc = plt.Add.Scatter(new[] { p1.x, p2.x }, new[] { p1.y, p2.y });
            sc.MarkerSize = 0;
            sc.LineWidth = width;
            sc.Color = color;
        }
    }
}
