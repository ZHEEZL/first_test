using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using Avalonia;

namespace first
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length > 0 && args[0] == "--check-init")
            {
                const int maxVectorN = 2000;
                var ctx = new ExperimentContext(maxVectorN, seed: 20240915);
                Bench.CheckInitImpact(ctx);
                return;
            }

            if (args.Length > 0 && args[0] == "--test-history")
            {
                var hist = BenchmarkDb.GetHistoryExperiments();
                Console.WriteLine($"Найдено экспериментов в БД: {hist.Count}");
                foreach (var h in hist)
                {
                    Console.WriteLine($"Exp: {h.ExperimentId} | {h.CreatedAt:yyyy-MM-dd HH:mm:ss} | {h.TotalMeasurements} замеров | {string.Join(", ", h.Algorithms)}");
                }
                return;
            }

            if (args.Length > 0 && args[0] == "--no-window")
            {
                bool force = args.Contains("--force");
                RunConsoleMode(force);
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        private static void RunConsoleMode(bool forceRecalc = false)
        {
            try
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                {
                    Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)1;
                }
                if (OperatingSystem.IsWindows())
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                }
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                GCSettings.LatencyMode = GCLatencyMode.Batch;
                Console.WriteLine("[Изоляция процесса] CPU: Core 0 | Приоритет: High | GC: Batch (неконкурентный)");
            }
            catch
            {
            }

            var configs = AlgoConfigItem.CreateDefaultConfigs();
            const int maxVectorN = 2000;
            var ctx = new ExperimentContext(maxVectorN, seed: 20240915);
            Bench.GlobalWarmup(ctx);

            var results = new List<Series>();
            var swAll = Stopwatch.StartNew();
            string expId = Guid.NewGuid().ToString("N");

            foreach (var cfg in configs)
            {
                var a = cfg.Factory();
                a.P.MaxN = cfg.DefaultMaxN;
                a.P.Step = cfg.DefaultStep;
                a.P.StartN = cfg.DefaultStep;
                a.P.Runs = cfg.DefaultRuns;
                Console.WriteLine($"\n=== {a} ===");
                results.Add(Bench.Run(a, ctx, expId, useCache: !forceRecalc, forceRecalc: forceRecalc));
            }

            swAll.Stop();
            Console.WriteLine($"\nОбщее время эксперимента: {swAll.Elapsed.TotalSeconds:0.0} с");
            Console.WriteLine($"[sink] {Sink.Value:0.000E+00}");

            Console.WriteLine("\n===== Сводка (эмпирика vs теория) =====");
            foreach (var s in results)
            {
                int last = s.N.Count - 1;
                string factStr = s.MeasuresSteps ? $"{s.T[last]:0} шагов" : Bench.FormatTime(s.T[last]);
                string theoryStr = s.MeasuresSteps ? $"{s.TFit[last]:0.0} шагов" : Bench.FormatTime(s.TFit[last]);
                Console.WriteLine(
                    $"{s.Algo.Name,-34} {s.Algo.Cls.Name,-12} C={s.C,12:0.####E+00}   MSE={s.MSE,12:0.####E+00}   " +
                    $"val(n={s.N[last]:0}) = {factStr,-14}   теория = {theoryStr}");
            }

            ExportCsv(results, "results.csv");
            var history = BenchmarkDb.GetHistoryExperiments();
            Dictionary<string, Series> baseDict = null;
            var fullExp = history.FirstOrDefault(h => h.Algorithms.Count > 1);
            if (fullExp != null)
            {
                baseDict = BenchmarkDb.GetBaselineSeries(fullExp.ExperimentId);
            }
            var plots = Plotter.Build(results, baseDict, showHistory: true, showErrorBars: true);
            Plotter.SaveAll(plots, "charts");

            // 3D & Heatmap матричного умножения T x M x N
            Console.WriteLine("\n>>> Построение 3D и Heatmap графиков матричного умножения T × M × N...");
            int mStep = 10, mMaxM = 100, mMaxN = 100;
            int mRows = mMaxM / mStep, mCols = mMaxN / mStep;
            var mTimes = new double[mRows, mCols];
            var mAlgo = new MatrixMultiply(100);
            for (int r = 0; r < mRows; r++)
            {
                int m = (r + 1) * mStep;
                for (int c = 0; c < mCols; c++)
                {
                    int n = (c + 1) * mStep;
                    double sumSec = 0;
                    for (int run = 0; run < 3; run++)
                    {
                        mAlgo.PrepareMN(m, n);
                        var sw = Stopwatch.StartNew();
                        mAlgo.MultiplyMN(m, n);
                        sw.Stop();
                        sumSec += sw.Elapsed.TotalSeconds;
                    }
                    mTimes[r, c] = (sumSec / 3.0) * 1e3; // миллисекунды
                }
            }

            Matrix3DViewport.RenderOffline(mTimes, mStep, mStep, "мс", Path.Combine("charts", "17_Матричное_умножение_3D.png"), 1200, 750, ViewportMode.ShadedWireframe);
            Matrix3DViewport.RenderOffline(mTimes, mStep, mStep, "мс", Path.Combine("charts", "17_Матричное_умножение_Heatmap.png"), 1200, 750, ViewportMode.Heatmap2D);
            Console.WriteLine("3D график T × M × N сохранен: charts/17_Матричное_умножение_3D.png");
            Console.WriteLine("Heatmap сохранен: charts/17_Матричное_умножение_Heatmap.png");

            Console.WriteLine("\nCSV: results.csv;  PNG: charts/;  SQLite: benchmark.db");
        }

        public static void ExportCsv(List<Series> results, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("algorithm;class;metric;mse;C;n;value_fact;value_theory;is_steps");
            foreach (var s in results)
            {
                string metric = s.MeasuresSteps ? "steps" : "seconds";
                for (int i = 0; i < s.N.Count; i++)
                    sb.Append(s.Algo.Name).Append(';').Append(s.Algo.Cls.Name).Append(';')
                        .Append(metric).Append(';')
                        .Append(s.MSE.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                        .Append(s.C.ToString("R", CultureInfo.InvariantCulture)).Append(';')
                        .Append(s.N[i].ToString("0", CultureInfo.InvariantCulture)).Append(';')
                        .Append(s.T[i].ToString("R", CultureInfo.InvariantCulture)).Append(';')
                        .Append(s.TFit[i].ToString("R", CultureInfo.InvariantCulture)).Append(';')
                        .AppendLine(s.MeasuresSteps ? "1" : "0");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}