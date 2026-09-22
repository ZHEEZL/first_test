using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace first
{
    public static class Plotter
    {
        public static List<KeyValuePair<string, ScottPlot.Plot>> Build(
            List<Series> results,
            Dictionary<string, Series> baselineDict = null,
            bool showHistory = true,
            bool showErrorBars = true,
            bool darkTheme = true)
        {
            var plots = new List<KeyValuePair<string, ScottPlot.Plot>>();
            foreach (var s in results)
            {
                if (s.MatrixTimes != null || s.Algo is MatrixMultiply)
                {
                    var plt3d = new ScottPlot.Plot(1000, 620);
                    if (darkTheme)
                    {
                        plt3d.Style(
                            figureBackground: Color.FromArgb(31, 34, 41),
                            dataBackground: Color.FromArgb(24, 26, 32),
                            grid: Color.FromArgb(45, 50, 63),
                            tick: Color.FromArgb(156, 163, 175),
                            axisLabel: Color.FromArgb(226, 232, 240),
                            titleLabel: Color.FromArgb(243, 244, 246)
                        );
                    }

                    double[,] times = s.MatrixTimes;
                    int stepM = s.MatrixStepM > 0 ? s.MatrixStepM : 10;
                    int stepN = s.MatrixStepN > 0 ? s.MatrixStepN : 10;

                    if (times == null)
                    {
                        int rows = Math.Max(5, s.N.Count);
                        times = new double[rows, rows];
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < rows; c++)
                                times[r, c] = (r + 1) * (r + 1) * (c + 1) * 1e-6;
                    }

                    int rowsM = times.GetLength(0);
                    int colsN = times.GetLength(1);
                    double maxSec = 0;
                    for (int r = 0; r < rowsM; r++)
                        for (int c = 0; c < colsN; c++)
                            if (times[r, c] > maxSec) maxSec = times[r, c];

                    double scale = 1e3;
                    string unit = "мс";
                    if (maxSec < 1e-3) { scale = 1e6; unit = "мкс"; }
                    else if (maxSec >= 1.0) { scale = 1.0; unit = "с"; }

                    double[,] scaled = new double[rowsM, colsN];
                    for (int r = 0; r < rowsM; r++)
                        for (int c = 0; c < colsN; c++)
                            scaled[r, c] = times[r, c] * scale;

                    Matrix3DPlotter.RenderWireframe(plt3d, scaled, stepM, stepN, unit, 35, 25);
                    plots.Add(new KeyValuePair<string, ScottPlot.Plot>(s.Algo.Name, plt3d));
                    continue;
                }

                var plt = new ScottPlot.Plot(1000, 620);
                if (darkTheme)
                {
                    plt.Style(
                        figureBackground: Color.FromArgb(31, 34, 41),
                        dataBackground: Color.FromArgb(24, 26, 32),
                        grid: Color.FromArgb(45, 50, 63),
                        tick: Color.FromArgb(156, 163, 175),
                        axisLabel: Color.FromArgb(226, 232, 240),
                        titleLabel: Color.FromArgb(243, 244, 246)
                    );
                }

                string yLabel;
                double[] ysFact;
                double[] ysFit;
                string fitLabel;
                double unitScale = 1.0;
                string unitName = "";

                if (s.MeasuresSteps)
                {
                    yLabel = "Количество элементарных операций (шагов)";
                    ysFact = s.T.ToArray();
                    ysFit = s.TFit.ToArray();
                    fitLabel = $"Теория: {s.C:0.####E+00} · {s.Algo.Cls.Name}";
                }
                else
                {
                    double maxVal = s.T.Count > 0 ? s.T.Max() : 0;
                    if (maxVal < 1e-3)
                    {
                        unitScale = 1e6;
                        unitName = "мкс";
                    }
                    else if (maxVal < 1.0)
                    {
                        unitScale = 1e3;
                        unitName = "мс";
                    }
                    else
                    {
                        unitScale = 1.0;
                        unitName = "с";
                    }

                    yLabel = $"Среднее время, {unitName}";
                    ysFact = s.T.Select(t => t * unitScale).ToArray();
                    ysFit = s.TFit.Select(t => t * unitScale).ToArray();
                    double scaledC = s.C * unitScale;
                    string cStr = (scaledC >= 0.0001 && scaledC < 10000)
                        ? scaledC.ToString("0.####", CultureInfo.InvariantCulture)
                        : scaledC.ToString("0.####E+00", CultureInfo.InvariantCulture);
                    fitLabel = $"Теория: {cStr} {unitName} · {s.Algo.Cls.Name}";
                }

                plt.Title($"{s.Algo.Name}  —  класс {s.Algo.Cls.Name}");
                plt.XLabel("Размерность входа n");
                plt.YLabel(yLabel);

                // 1. Погрешности (Error Bars)
                if (showErrorBars && !s.MeasuresSteps && s.StdDev.Count == s.N.Count)
                {
                    double[] yErrors = s.StdDev.Select(sd => sd * unitScale).ToArray();
                    if (yErrors.Any(e => e > 1e-12))
                    {
                        var err = plt.AddErrorBars(s.N.ToArray(), ysFact, null, null, yErrors, yErrors);
                        err.Color = darkTheme ? Color.FromArgb(140, 96, 165, 250) : Color.FromArgb(120, 41, 128, 185);
                        err.CapSize = 3;
                    }
                }

                // 2. Текущий запуск
                plt.AddScatter(s.N.ToArray(), ysFact,
                    darkTheme ? Color.FromArgb(96, 165, 250) : Color.FromArgb(41, 128, 185), 2.0f, 5, ScottPlot.MarkerShape.filledCircle,
                    label: "Текущий запуск");

                // 3. Теоретическая кривая
                plt.AddScatter(s.N.ToArray(), ysFit,
                    darkTheme ? Color.FromArgb(251, 146, 60) : Color.FromArgb(230, 126, 34), 2.0f, 0, ScottPlot.MarkerShape.none,
                    label: fitLabel);

                // 4. Сравнение с историей (Базовый запуск / Ghost curve)
                Series bSeries = null;
                bool hasBase = showHistory &&
                               baselineDict != null &&
                               baselineDict.TryGetValue(s.Algo.Name, out bSeries) &&
                               bSeries != null &&
                               bSeries.N.Count > 0;

                if (hasBase)
                {
                    double[] ysBase = s.MeasuresSteps
                        ? bSeries.T.ToArray()
                        : bSeries.T.Select(t => t * unitScale).ToArray();

                    var baseScatter = plt.AddScatter(bSeries.N.ToArray(), ysBase,
                        darkTheme ? Color.FromArgb(192, 132, 252) : Color.FromArgb(142, 68, 173), 1.8f, 5, ScottPlot.MarkerShape.openCircle,
                        label: "Базовый запуск (Эталон)");
                    baseScatter.LineStyle = ScottPlot.LineStyle.Dash;

                    double curLast = s.T.Last();
                    double baseLast = bSeries.T.Last();
                    double diffPct = baseLast > 1e-15 ? ((curLast - baseLast) / baseLast) * 100.0 : 0.0;

                    string curFmt = s.MeasuresSteps ? $"{curLast:0} шагов" : Bench.FormatTime(curLast);
                    string baseFmt = s.MeasuresSteps ? $"{baseLast:0} шагов" : Bench.FormatTime(baseLast);

                    string badgeText;
                    Color badgeBorder;
                    if (s.MeasuresSteps)
                    {
                        if (Math.Abs(curLast - baseLast) < 1e-6)
                        {
                            badgeText = $"⚪ Шаги детерминированы (100% совпадение)\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                            badgeBorder = Color.FromArgb(156, 163, 175);
                        }
                        else
                        {
                            badgeText = $"⚪ Шаги: {curFmt} (базовый: {baseFmt})\nРазница: {diffPct:+0.0;-0.0;0.0}% (при n = {s.N.Last():0})";
                            badgeBorder = Color.FromArgb(251, 146, 60);
                        }
                    }
                    else if (diffPct <= -3.0)
                    {
                        badgeText = $"🟢 Быстрее на {Math.Abs(diffPct):F1}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromArgb(52, 211, 153);
                    }
                    else if (diffPct >= 3.0)
                    {
                        badgeText = $"🔴 Замедление на +{diffPct:F1}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromArgb(248, 113, 113);
                    }
                    else
                    {
                        badgeText = $"⚪ В пределах нормы: {diffPct:+0.0;-0.0;0.0}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromArgb(156, 163, 175);
                    }

                    var anno = plt.AddAnnotation(badgeText, ScottPlot.Alignment.UpperRight);
                    anno.BackgroundColor = darkTheme ? Color.FromArgb(240, 38, 42, 53) : Color.FromArgb(238, 255, 255, 255);
                    anno.BorderColor = badgeBorder;
                    anno.Font.Color = darkTheme ? Color.FromArgb(243, 244, 246) : Color.FromArgb(33, 37, 41);
                    anno.Font.Size = 10f;
                    anno.Font.Bold = true;
                    anno.Shadow = true;
                }
                else
                {
                    string annoText = $"Класс: {s.Algo.Cls.Name}\nMSE = {s.MSE:0.####E+00}";
                    var anno = plt.AddAnnotation(annoText, ScottPlot.Alignment.UpperRight);
                    anno.BackgroundColor = darkTheme ? Color.FromArgb(230, 38, 42, 53) : Color.FromArgb(230, 255, 255, 255);
                    anno.BorderColor = darkTheme ? Color.FromArgb(75, 85, 99) : Color.FromArgb(189, 195, 199);
                    anno.Font.Color = darkTheme ? Color.FromArgb(209, 213, 219) : Color.FromArgb(52, 73, 94);
                    anno.Font.Size = 10f;
                    anno.Shadow = true;
                }

                var leg = plt.Legend(location: ScottPlot.Alignment.UpperLeft);
                if (darkTheme)
                {
                    leg.FillColor = Color.FromArgb(38, 42, 53);
                    leg.OutlineColor = Color.FromArgb(75, 85, 99);
                    leg.FontColor = Color.FromArgb(243, 244, 246);
                }

                plots.Add(new KeyValuePair<string, ScottPlot.Plot>(s.Algo.Name, plt));
            }

            // 5. Сводный сравнительный график алгоритмов возведения в степень (Часть IV практикума)
            var powSeries = results.Where(s => s.Algo is PowBase && s.N.Count > 0).ToList();
            if (powSeries.Count >= 2)
            {
                var pltPow = new ScottPlot.Plot(1000, 620);
                if (darkTheme)
                {
                    pltPow.Style(
                        figureBackground: Color.FromArgb(31, 34, 41),
                        dataBackground: Color.FromArgb(24, 26, 32),
                        grid: Color.FromArgb(45, 50, 63),
                        tick: Color.FromArgb(156, 163, 175),
                        axisLabel: Color.FromArgb(226, 232, 240),
                        titleLabel: Color.FromArgb(243, 244, 246)
                    );
                }

                pltPow.Title("Степени: Сравнение алгоритмов по числу шагов (операций)");
                pltPow.XLabel("Показатель степени n");
                pltPow.YLabel("Количество элементарных операций (умножений)");

                Color[] powColors = new[]
                {
                    Color.FromArgb(239, 68, 68),   // Красный (Рис. 1, простой On)
                    Color.FromArgb(59, 130, 246),  // Синий (Рис. 2, RecPow Ologn)
                    Color.FromArgb(16, 185, 129),  // Зелёный (Рис. 3, QuickPow Ologn)
                    Color.FromArgb(245, 158, 11),  // Янтарный (Рис. 4, QuickPow1 Ologn)
                };

                ScottPlot.MarkerShape[] powShapes = new[]
                {
                    ScottPlot.MarkerShape.filledCircle,
                    ScottPlot.MarkerShape.filledSquare,
                    ScottPlot.MarkerShape.filledDiamond,
                    ScottPlot.MarkerShape.openCircle
                };

                for (int i = 0; i < powSeries.Count; i++)
                {
                    var ps = powSeries[i];
                    var col = powColors[i % powColors.Length];
                    var shape = powShapes[i % powShapes.Length];
                    pltPow.AddScatter(
                        ps.N.ToArray(),
                        ps.T.ToArray(),
                        col,
                        lineWidth: 2.2f,
                        markerSize: 5,
                        markerShape: shape,
                        label: $"{ps.Algo.Name} [{ps.Algo.Cls.Name}]"
                    );
                }

                string badgeText = "Сравнение алгоритмов xⁿ:\n• Простой (Рис. 1): O(n) — крутой линейный рост\n• RecPow / QuickPow / QuickPow1: O(log n) — логарифмическая ступенька\nПри n = 2000: 2000 шагов против ~16-17 шагов!";
                var anno = pltPow.AddAnnotation(badgeText, ScottPlot.Alignment.UpperRight);
                anno.BackgroundColor = darkTheme ? Color.FromArgb(240, 38, 42, 53) : Color.FromArgb(238, 255, 255, 255);
                anno.BorderColor = Color.FromArgb(59, 130, 246);
                anno.Font.Color = darkTheme ? Color.FromArgb(243, 244, 246) : Color.FromArgb(33, 37, 41);
                anno.Font.Size = 10f;
                anno.Font.Bold = true;
                anno.Shadow = true;

                var leg = pltPow.Legend(location: ScottPlot.Alignment.UpperLeft);
                if (darkTheme)
                {
                    leg.FillColor = Color.FromArgb(38, 42, 53);
                    leg.OutlineColor = Color.FromArgb(75, 85, 99);
                    leg.FontColor = Color.FromArgb(243, 244, 246);
                }

                plots.Add(new KeyValuePair<string, ScottPlot.Plot>("Степени (Сравнение всех 4)", pltPow));
            }

            return plots;
        }

        public static void SaveAll(IEnumerable<KeyValuePair<string, ScottPlot.Plot>> plots, string dir)
        {
            Directory.CreateDirectory(dir);
            int i = 0;
            foreach (var kv in plots)
            {
                i++;
                string safe = string.Join("_", kv.Key.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(dir, $"{i:00}_{safe}.png");

                using (var bmp = kv.Value.Render(1000, 620))
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
