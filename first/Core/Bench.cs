using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace first
{
    public static class Bench
    {
        public static Series Run(
            Algorithm algo,
            ExperimentContext ctx,
            string experimentId = null,
            bool useCache = true,
            bool forceRecalc = false,
            Action<string> log = null,
            Action<int, int> onPoint = null,
            CancellationToken ct = default)
        {
            experimentId = experimentId ?? Guid.NewGuid().ToString("N");
            var p = algo.P;

            if (algo is MatrixMultiply mm)
            {
                int maxM = p.MaxN;
                int maxN = p.MaxN;
                int stepM = p.Step;
                int stepN = p.Step;
                int runsCount = Math.Max(1, p.Runs);

                int rowsM = Math.Max(1, maxM / stepM);
                int colsN = Math.Max(1, maxN / stepN);
                var rawTimes = new double[rowsM, colsN];
                int totalPts = rowsM * colsN;
                int done = 0;

                string msg1m = $"   точек 3D (M×N): {rowsM}×{colsN} = {totalPts}, метрика: время (с), Runs: {runsCount}, Step: {stepM}, MaxM: {maxM}, MaxN: {maxN}";
                Console.WriteLine(msg1m);
                log?.Invoke(msg1m);

                mm.PrepareMN(Math.Min(50, maxM), Math.Min(50, maxN));
                mm.MultiplyMN(Math.Min(50, maxM), Math.Min(50, maxN));

                var s3d = new Series
                {
                    Algo = algo,
                    MeasuresSteps = false,
                    MatrixTimes = rawTimes,
                    MatrixStepM = stepM,
                    MatrixStepN = stepN,
                    MatrixMaxM = maxM,
                    MatrixMaxN = maxN
                };

                for (int r = 0; r < rowsM; r++)
                {
                    int m = (r + 1) * stepM;
                    for (int c = 0; c < colsN; c++)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = (c + 1) * stepN;

                        // Локальный прогрев матричного буфера под размер (m, n)
                        mm.PrepareMN(m, n);
                        mm.MultiplyMN(m, n);
                        CleanHeap();

                        double sumSec = 0;
                        for (int run = 0; run < runsCount; run++)
                        {
                            mm.PrepareMN(m, n);
                            var sw = Stopwatch.StartNew();
                            mm.MultiplyMN(m, n);
                            sw.Stop();
                            sumSec += sw.Elapsed.TotalSeconds;
                        }
                        double avgSec = sumSec / runsCount;
                        rawTimes[r, c] = avgSec;
                        done++;
                        onPoint?.Invoke(done, totalPts);
                    }
                    string lineMsg = $"   M = {m,4} (N={stepN}..{maxN}): t(M, N_max) = {FormatTime(rawTimes[r, colsN - 1])}";
                    Console.WriteLine(lineMsg);
                    log?.Invoke(lineMsg);
                }

                for (int r = 0; r < rowsM; r++)
                {
                    int m = (r + 1) * stepM;
                    int c = Math.Min(r, colsN - 1);
                    double val = rawTimes[r, c];
                    s3d.N.Add(m);
                    s3d.T.Add(val);
                    if (experimentId != null)
                    {
                        BenchmarkDb.SaveBatch(experimentId, algo.Name, algo.Cls.Name, m, new[] { val }, new long[] { 0 }, DateTime.UtcNow);
                    }
                }

                s3d.C = FitConstant(s3d.N, s3d.T, algo.Cls.Model);
                for (int i = 0; i < s3d.N.Count; i++) s3d.TFit.Add(s3d.C * algo.Cls.Model(s3d.N[i]));
                double sumSq3d = 0;
                for (int i = 0; i < s3d.N.Count; i++)
                {
                    double d = s3d.T[i] - s3d.TFit[i];
                    sumSq3d += d * d;
                }
                s3d.MSE = s3d.N.Count > 0 ? sumSq3d / s3d.N.Count : 0;
                return s3d;
            }

            var ns = BuildNList(p);
            string metricUnit = algo.MeasuresSteps ? "шаги (операции)" : "время (с)";
            string msg1 = $"   точек: {ns.Count}, метрика: {metricUnit}, Runs: {p.Runs}, Step: {p.Step}, MaxN: {p.MaxN}";
            Console.WriteLine(msg1);
            log?.Invoke(msg1);

            if (!algo.MeasuresSteps)
            {
                try
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Highest;
                }
                catch
                {
                }
                Warmup(algo, ctx, p.MaxN); // прогрев JIT на максимальном n
            }

            var s = new Series { Algo = algo, MeasuresSteps = algo.MeasuresSteps };
            int droppedTotal = 0;

            for (int ptIdx = 0; ptIdx < ns.Count; ptIdx++)
            {
                ct.ThrowIfCancellationRequested();
                int n = ns[ptIdx];
                double[] times = null;
                long[] steps = null;
                bool fromCache = false;

                if (useCache && !forceRecalc)
                {
                    if (BenchmarkDb.TryGetCachedRuns(algo.Name, n, p.Runs, out var cTimes, out var cSteps))
                    {
                        if (algo.MeasuresSteps)
                        {
                            if (cSteps != null && cSteps.Length >= p.Runs)
                            {
                                steps = cSteps;
                                fromCache = true;
                            }
                        }
                        else
                        {
                            times = cTimes;
                            fromCache = true;
                        }
                    }
                }

                if (!fromCache)
                {
                    int runsCount = Math.Max(3, p.Runs);
                    int batch = (algo.MeasuresSteps || !p.AutoBatch) ? p.Batch : CalibrateBatch(algo, ctx, n, p.MinMeasureSec);
                    var elapsedSecs = new double[runsCount];
                    var stepCounts = new long[runsCount];

                    // Локальный холостой прогрев L1/L2 кэша и структур данных для среза размера n
                    if (!algo.MeasuresSteps)
                    {
                        algo.Prepare(n, ctx);
                        algo.Work(n, ctx);
                        CleanHeap();
                    }

                    for (int r = 0; r < runsCount; r++)
                    {
                        algo.Prepare(n, ctx); // свежий «кусок» — вне замера
                        if (!algo.MeasuresSteps) CleanHeap(); // убираем мусор перед замером времени
                        var sw = Stopwatch.StartNew();
                        for (int rep = 0; rep < batch; rep++) algo.Work(n, ctx);
                        sw.Stop();
                        elapsedSecs[r] = sw.Elapsed.TotalSeconds / batch;
                        stepCounts[r] = algo.LastStepCount;
                    }

                    BenchmarkDb.SaveBatch(experimentId, algo.Name, algo.Cls.Name, n, elapsedSecs, stepCounts, DateTime.UtcNow);
                    times = elapsedSecs;
                    steps = stepCounts;
                }

                int dropped = 0;
                double val;
                double stdDev = 0;
                if (algo.MeasuresSteps)
                {
                    val = steps[0]; // для элементарных операций шаг строго детерминирован
                }
                else
                {
                    val = RobustMean(times, out dropped);
                    droppedTotal += dropped;
                    if (times != null && times.Length > 1)
                    {
                        double sumSqDiff = 0;
                        for (int r = 0; r < times.Length; r++)
                        {
                            double diff = times[r] - val;
                            sumSqDiff += diff * diff;
                        }
                        stdDev = Math.Sqrt(sumSqDiff / (times.Length - 1));
                    }
                }

                if (fromCache) s.CachedPoints++;
                s.N.Add(n);
                s.T.Add(val);
                s.StdDev.Add(stdDev);

                string cacheTag = fromCache ? " [кэш SQLite]" : "";
                string msgPt = algo.MeasuresSteps
                    ? $"   n={n,6}   шагов = {val,8:0}{cacheTag}"
                    : $"   n={n,6}   t = {FormatTime(val),14}   (отброшено: {dropped}){cacheTag}";
                Console.WriteLine(msgPt);
                log?.Invoke(msgPt);
                onPoint?.Invoke(ptIdx + 1, ns.Count);
            }

            if (!algo.MeasuresSteps && p.SmoothWindow > 1) // скользящая медиана: убирает одиночные «иглы»
            {
                var sm = MedianSmooth(s.T, p.SmoothWindow);
                s.T.Clear();
                s.T.AddRange(sm);
            }

            s.C = FitConstant(s.N, s.T, algo.Cls.Model); // t(n) ≈ C·f(n), C по МНК
            for (int i = 0; i < s.N.Count; i++) s.TFit.Add(s.C * algo.Cls.Model(s.N[i]));

            double sumSq = 0;
            for (int i = 0; i < s.N.Count; i++)
            {
                double d = s.T[i] - s.TFit[i];
                sumSq += d * d;
            }
            s.MSE = s.N.Count > 0 ? sumSq / s.N.Count : 0;

            string msgEnd = $"   Итого: точек {s.N.Count} ({s.CachedPoints} из кэша), отброшено замеров {droppedTotal}, C = {s.C:0.####E+00}, MSE = {s.MSE:0.####E+00}";
            Console.WriteLine(msgEnd);
            log?.Invoke(msgEnd);
            return s;
        }

        public static string FormatTime(double sec)
        {
            if (double.IsNaN(sec) || double.IsInfinity(sec)) return sec.ToString(CultureInfo.InvariantCulture);
            if (sec < 0) return "-" + FormatTime(-sec);
            if (sec == 0) return "0 с";
            if (sec >= 1.0) return $"{sec.ToString("0.000", CultureInfo.InvariantCulture)} с";
            if (sec >= 1e-3) return $"{(sec * 1e3).ToString("0.000", CultureInfo.InvariantCulture)} мс";
            if (sec >= 1e-6) return $"{(sec * 1e6).ToString("0.000", CultureInfo.InvariantCulture)} мкс";
            return $"{(sec * 1e9).ToString("0.000", CultureInfo.InvariantCulture)} нс";
        }

        public static void GlobalWarmup(ExperimentContext ctx, Action<string> log = null)
        {
            string msgStart = ">>> Глобальный прогрев (JIT, память, сброс C-states CPU, фиксация Affinity и Priority)...";
            Console.WriteLine(msgStart);
            log?.Invoke(msgStart);

            try
            {
                Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)1;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch
            {
            }

            var dummy = new double[15];
            for (int i = 0; i < dummy.Length; i++) dummy[i] = 1.0;
            RobustMean(dummy, out _);
            MedianSmooth(new List<double> { 1, 2, 3, 4, 5 }, 3);
            FitConstant(new List<double> { 1, 2, 3 }, new List<double> { 1, 2, 3 }, x => x);
            CleanHeap();

            ctx.EnsureCapacity(2000);
            double sum = 0;
            for (int i = 0; i < Math.Min(2000, ctx.V.Length); i++) sum += ctx.V[i];
            Sink.Add(sum);

            long targetTicks = Stopwatch.Frequency / 10; // ~100 мс спиннинга для вывода ядра в активное C0-состояние
            long start = Stopwatch.GetTimestamp();
            double val = 1.0;
            while (Stopwatch.GetTimestamp() - start < targetTicks)
            {
                val = Math.Sin(val + 0.1);
            }
            Sink.Add(val);

            string msgDone = ">>> Прогрев завершён, измерительный контур стабилизирован.\n";
            Console.WriteLine(msgDone);
            log?.Invoke(msgDone);
        }

        /// <summary>
        /// «Куски»: n = 1, 1+2, 1+2+3, ... (треугольные числа) либо сетка с шагом Step
        /// </summary>
        public static List<int> BuildNList(MeasParams p)
        {
            var res = new List<int>();
            if (p.Sampling == NSampling.Linear)
            {
                int step = Math.Max(1, p.Step);
                int start = Math.Max(1, Math.Min(p.StartN, p.MaxN));
                for (int n = start; n <= p.MaxN; n += step) res.Add(n);
                if (res.Count == 0 || res[res.Count - 1] != p.MaxN)
                {
                    res.Add(p.MaxN);
                }
            }
            else
            {
                int n = 0, k = 0;
                while (true)
                {
                    k++;
                    n += k;
                    if (n > p.MaxN) break;
                    res.Add(n);
                }
            }

            return res;
        }

        private static void Warmup(Algorithm a, ExperimentContext ctx, int nMax)
        {
            for (int i = 0; i < 3; i++)
            {
                a.Prepare(nMax, ctx);
                a.Work(nMax, ctx);
            }
        }

        private static void CleanHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static int CalibrateBatch(Algorithm a, ExperimentContext ctx, int n, double minSec)
        {
            int b = 1;
            a.Prepare(n, ctx);
            var sw = Stopwatch.StartNew();
            a.Work(n, ctx);
            sw.Stop();
            while (sw.Elapsed.TotalSeconds < minSec && b < (1 << 22))
            {
                b = Math.Min(b * 2, 1 << 22);
                a.Prepare(n, ctx);
                sw.Restart();
                for (int i = 0; i < b; i++) a.Work(n, ctx);
                sw.Stop();
            }

            return Math.Max(1, b / 2);
        }

        /// <summary>
        /// Отбраковка выбросов внутри повторов: модифицированная z-оценка (медиана/MAD), порог 3.5
        /// </summary>
        public static double RobustMean(double[] xs, out int dropped)
        {
            dropped = 0;
            double med = Median(xs);
            var devs = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++) devs[i] = Math.Abs(xs[i] - med);
            double mad = Median(devs);

            List<double> kept;
            if (mad > 0)
            {
                double thr = 3.5 * mad / 0.6745;
                kept = xs.Where(x => Math.Abs(x - med) <= thr).ToList();
                dropped = xs.Length - kept.Count;
            }
            else if (xs.Length >= 5) // fallback: усечённое среднее (без min и max)
            {
                var srt = (double[])xs.Clone();
                Array.Sort(srt);
                kept = srt.Skip(1).Take(srt.Length - 2).ToList();
            }
            else kept = xs.ToList();

            return kept.Count > 0 ? kept.Average() : med;
        }

        private static double Median(double[] xs)
        {
            var s = (double[])xs.Clone();
            Array.Sort(s);
            return s.Length % 2 == 1 ? s[s.Length / 2] : 0.5 * (s[s.Length / 2 - 1] + s[s.Length / 2]);
        }

        private static List<double> MedianSmooth(List<double> t, int w)
        {
            if (w < 3) return new List<double>(t);
            if (w % 2 == 0) w++;
            int h = w / 2;
            var res = new List<double>(t.Count);
            for (int i = 0; i < t.Count; i++)
            {
                int a = Math.Max(0, i - h), b = Math.Min(t.Count - 1, i + h);
                var win = new List<double>();
                for (int j = a; j <= b; j++) win.Add(t[j]);
                win.Sort();
                res.Add(win[win.Count / 2]);
            }

            return res;
        }

        private static double FitConstant(List<double> ns, List<double> ts, Func<double, double> f)
        {
            double sff = 0, sft = 0;
            for (int i = 0; i < ns.Count; i++)
            {
                double fi = f(ns[i]);
                sff += fi * fi;
                sft += fi * ts[i];
            }

            return sff > 0 ? sft / sff : 0;
        }

        public static void CheckInitImpact(ExperimentContext ctx)
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine(" ЭКСПЕРИМЕНТ 1: Влияние JIT на первый запуск Work(1, ctx)");
            Console.WriteLine("==========================================================");

            var sumAlg = new SumFunction();
            var sw = Stopwatch.StartNew();
            sumAlg.Work(1, ctx);
            sw.Stop();
            double t1 = sw.Elapsed.TotalSeconds;

            sw.Restart();
            sumAlg.Work(1, ctx);
            sw.Stop();
            double t2 = sw.Elapsed.TotalSeconds;

            sw.Restart();
            sumAlg.Work(1, ctx);
            sw.Stop();
            double t3 = sw.Elapsed.TotalSeconds;

            Console.WriteLine($"1-й вызов (JIT + Work): {t1 * 1e6:F2} мкс");
            Console.WriteLine($"2-й вызов (прогретый):  {t2 * 1e6:F2} мкс");
            Console.WriteLine($"3-й вызов (прогретый):  {t3 * 1e6:F2} мкс");
            Console.WriteLine($"Разница 1-го и 2-го вызовов: в x{t1 / Math.Max(1e-12, t2):F1} раз!");

            Console.WriteLine("\n==========================================================");
            Console.WriteLine(" ЭКСПЕРИМЕНТ 2: Влияние Warmup в Bench.Run на холодном старте");
            Console.WriteLine("==========================================================");

            var c1 = new ConstantFunction();
            Console.WriteLine("--- Первый запуск Bench.Run (сразу после старта процесса) ---");
            var s1 = Bench.Run(c1, ctx);

            var c2 = new ConstantFunction();
            Console.WriteLine("\n--- Повторный запуск Bench.Run (уже все подсистемы прогреты) ---");
            var s2 = Bench.Run(c2, ctx);

            Console.WriteLine("\nСравнение результатов:");
            Console.WriteLine($"Холодный старт: C = {s1.C:0.####E+00}, n=1: t = {Bench.FormatTime(s1.T[0])}");
            Console.WriteLine($"Горячий старт:  C = {s2.C:0.####E+00}, n=1: t = {Bench.FormatTime(s2.T[0])}");
            Console.WriteLine($"Разница C: {Math.Abs(s1.C - s2.C) / s2.C * 100:F2}%");
            Console.WriteLine($"Разница n=1: {Math.Abs(s1.T[0] - s2.T[0]) / s2.T[0] * 100:F2}%");
            Console.WriteLine("==========================================================");
        }
    }
}
