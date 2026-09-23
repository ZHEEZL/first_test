using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ScottPlot;

namespace first
{
    public static class Plotter
    {
        public static List<KeyValuePair<string, Plot>> Build(
            List<Series> results,
            Dictionary<string, Series> baselineDict = null,
            bool showHistory = true,
            bool showErrorBars = true,
            bool darkTheme = true)
        {
            var plots = new List<KeyValuePair<string, Plot>>();
            foreach (var s in results)
            {
                if (s.MatrixTimes != null || s.Algo is MatrixMultiply)
                {
                    var plt3d = new Plot();
                    ApplyDarkTheme(plt3d, darkTheme);

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
                    plots.Add(new KeyValuePair<string, Plot>(s.Algo.Name, plt3d));
                    continue;
                }

                var plt = new Plot();
                ApplyDarkTheme(plt, darkTheme);

                string yLabel;
                double[] ysFact;
                double[] ysFit;
                string fitLabel;
                double unitScale = 1.0;
                string unitName = "";

                double[] xs = s.N.Select(n => (double)n).ToArray();

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
                        var err = plt.Add.ErrorBar(xs, ysFact, yErrors);
                        err.Color = darkTheme ? Color.FromHex("#60A5FA").WithAlpha(0.6f) : Color.FromHex("#2980B9").WithAlpha(0.6f);
                    }
                }

                // 2. Текущий запуск (Scatter)
                var scFact = plt.Add.Scatter(xs, ysFact);
                scFact.Color = darkTheme ? Color.FromHex("#60A5FA") : Color.FromHex("#2980B9");
                scFact.LineWidth = 2.0f;
                scFact.MarkerSize = 5;
                scFact.LegendText = "Текущий запуск";

                // 3. Теоретическая кривая
                var scFit = plt.Add.Scatter(xs, ysFit);
                scFit.Color = darkTheme ? Color.FromHex("#FB923C") : Color.FromHex("#E67E22");
                scFit.LineWidth = 2.0f;
                scFit.MarkerSize = 0;
                scFit.LegendText = fitLabel;

                // 4. Сравнение с историей (Базовый запуск / Ghost curve)
                Series bSeries = null;
                bool hasBase = showHistory &&
                               baselineDict != null &&
                               baselineDict.TryGetValue(s.Algo.Name, out bSeries) &&
                               bSeries != null &&
                               bSeries.N.Count > 0;

                if (hasBase)
                {
                    double[] bXs = bSeries.N.Select(n => (double)n).ToArray();
                    double[] ysBase = s.MeasuresSteps
                        ? bSeries.T.ToArray()
                        : bSeries.T.Select(t => t * unitScale).ToArray();

                    var scBase = plt.Add.Scatter(bXs, ysBase);
                    scBase.Color = darkTheme ? Color.FromHex("#C084FC") : Color.FromHex("#8E44AD");
                    scBase.LineWidth = 1.8f;
                    scBase.MarkerSize = 5;
                    scBase.LinePattern = LinePattern.Dashed;
                    scBase.LegendText = "Базовый запуск (Эталон)";

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
                            badgeBorder = Color.FromHex("#9CA3AF");
                        }
                        else
                        {
                            badgeText = $"⚪ Шаги: {curFmt} (базовый: {baseFmt})\nРазница: {diffPct:+0.0;-0.0;0.0}% (при n = {s.N.Last():0})";
                            badgeBorder = Color.FromHex("#FB923C");
                        }
                    }
                    else if (diffPct <= -3.0)
                    {
                        badgeText = $"🟢 Быстрее на {Math.Abs(diffPct):F1}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromHex("#34D399");
                    }
                    else if (diffPct >= 3.0)
                    {
                        badgeText = $"🔴 Замедление на +{diffPct:F1}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromHex("#F87171");
                    }
                    else
                    {
                        badgeText = $"⚪ В пределах нормы: {diffPct:+0.0;-0.0;0.0}% (при n = {s.N.Last():0})\nТекущий: {curFmt}  |  Базовый: {baseFmt}";
                        badgeBorder = Color.FromHex("#9CA3AF");
                    }

                    var anno = plt.Add.Annotation(badgeText, Alignment.UpperRight);
                    anno.LabelBackgroundColor = darkTheme ? Color.FromHex("#262A35") : Color.FromHex("#FFFFFF");
                    anno.LabelBorderColor = badgeBorder;
                    anno.LabelFontColor = darkTheme ? Color.FromHex("#F3F4F6") : Color.FromHex("#212529");
                    anno.LabelFontSize = 11;
                    anno.LabelBold = true;
                }
                else
                {
                    string annoText = $"Класс: {s.Algo.Cls.Name}\nMSE = {s.MSE:0.####E+00}";
                    var anno = plt.Add.Annotation(annoText, Alignment.UpperRight);
                    anno.LabelBackgroundColor = darkTheme ? Color.FromHex("#262A35") : Color.FromHex("#FFFFFF");
                    anno.LabelBorderColor = darkTheme ? Color.FromHex("#4B5563") : Color.FromHex("#BDC3C7");
                    anno.LabelFontColor = darkTheme ? Color.FromHex("#D1D5DB") : Color.FromHex("#34495E");
                    anno.LabelFontSize = 11;
                }

                plt.ShowLegend(Alignment.UpperLeft);
                if (darkTheme)
                {
                    plt.Legend.BackgroundColor = Color.FromHex("#262A35");
                    plt.Legend.OutlineColor = Color.FromHex("#4B5563");
                    plt.Legend.FontColor = Color.FromHex("#F3F4F6");
                }

                plots.Add(new KeyValuePair<string, Plot>(s.Algo.Name, plt));
            }

            return plots;
        }

        private static void ApplyDarkTheme(Plot plt, bool darkTheme)
        {
            if (!darkTheme) return;

            plt.FigureBackground.Color = Color.FromHex("#1F2229");
            plt.DataBackground.Color = Color.FromHex("#181A20");
            plt.Axes.Color(Color.FromHex("#E2E8F0"));
            plt.Grid.MajorLineColor = Color.FromHex("#2D323F");
        }

        public static void SaveAll(IEnumerable<KeyValuePair<string, Plot>> plots, string dir)
        {
            Directory.CreateDirectory(dir);
            int i = 0;
            foreach (var kv in plots)
            {
                i++;
                string safe = string.Join("_", kv.Key.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(dir, $"{i:00}_{safe}.png");
                kv.Value.SavePng(path, 1000, 620);
            }
        }
    }
}
