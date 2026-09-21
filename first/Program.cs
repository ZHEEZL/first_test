using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace first
{
    // ================= 1. ПАРАМЕТРЫ ИЗМЕРЕНИЙ (стандартные параметры классов) =================

    /// Схема выбора n: «куски» (n = 1, 1+2, 1+2+3, ...) либо равномерная сетка n += Step
    public enum NSampling
    {
        TriangularChunks,
        Linear
    }

    /// Стандартные параметры измерений для класса алгоритмов (подбираются опытным путём).
    public sealed class MeasParams
    {
        public NSampling Sampling = NSampling.Linear;
        public int StartN = 50;
        public int MaxN = 2000; // максимальное n
        public int Step = 50; // шаг сетки по умолчанию
        public int Runs = 5; // независимых прогонов на точку (по ТЗ = 5)
        public int Batch = 1; // вызовов за один замер (для сверхбыстрых функций)
        public bool AutoBatch = false; // калибровать Batch до длительности MinMeasureSec
        public double MinMeasureSec = 0.02;
        public int SmoothWindow = 1; // скользящая медиана по оси n (1 — выкл, 3/5 — вкл)

        public MeasParams Clone() => (MeasParams)MemberwiseClone();
    }

    /// Класс сложности: теоретическая модель f(n) + стандартные параметры измерений.
    public sealed class ComplexityClass
    {
        public readonly string Name;
        public readonly Func<double, double> Model; // f(n) без константы
        public readonly MeasParams Defaults;

        public ComplexityClass(string name, Func<double, double> model, MeasParams def)
        {
            Name = name;
            Model = model;
            Defaults = def;
        }

        public override string ToString() => Name;

        // Дефолты подобраны опытным путём:
        public static readonly ComplexityClass O1 = new ComplexityClass("O(1)",
            _ => 1.0, new MeasParams { MaxN = 1000, Step = 50, Runs = 5, Batch = 20000 });

        public static readonly ComplexityClass Ologn = new ComplexityClass("O(log n)",
            n => Math.Log(Math.Max(2, n), 2), new MeasParams { MaxN = 1000, Step = 50, Runs = 5, Batch = 5000 });

        public static readonly ComplexityClass On = new ComplexityClass("O(n)",
            n => n, new MeasParams { MaxN = 2000, Step = 50, Runs = 5, Batch = 10000 });

        public static readonly ComplexityClass Onlogn = new ComplexityClass("O(n·log n)",
            n => n * Math.Log(Math.Max(2, n), 2), new MeasParams { MaxN = 2000, Step = 50, Runs = 5, SmoothWindow = 3 });

        public static readonly ComplexityClass On2 = new ComplexityClass("O(n^2)",
            n => (double)n * n, new MeasParams { MaxN = 2000, Step = 50, Runs = 5, SmoothWindow = 3 });

        public static readonly ComplexityClass On3 = new ComplexityClass("O(n^3)",
            n => (double)n * n * n,
            new MeasParams { MaxN = 200, Step = 10, Runs = 3, SmoothWindow = 3 }); // 200^3 = 8M операций

        public static readonly ComplexityClass On1_5 = new ComplexityClass("O(n^1.5)",
            n => Math.Pow(n, 1.5), new MeasParams { MaxN = 2000, Step = 50, Runs = 5, SmoothWindow = 3 });

        public static readonly ComplexityClass Ofactorial = new ComplexityClass("O(n·n!)",
            n => n * Fact((int)Math.Min(n, 20)),
            new MeasParams { MaxN = 10, Step = 1, Runs = 15, SmoothWindow = 1 });

        private static double Fact(int k) { double r = 1; for (int i = 2; i <= k; i++) r *= i; return r; }
    }

    // ================= 2. ОБЩИЕ ДАННЫЕ: ОДИН БОЛЬШОЙ ВЕКТОР =================

    public static class Sink
    {
        public static double Value; // «копилка» результатов
        public static void Add(double x) => Value += x; // не даёт JIT выбросить «пустую» работу
    }

    /// Вектор генерируется ОДИН раз (фиксированный seed) и общий для всех алгоритмов.
    public sealed class ExperimentContext
    {
        public int MaxN { get; private set; }
        public double[] V { get; private set; } // полный вектор v (неизменный)
        public double[] Work { get; private set; } // буфер под текущий «кусок»
        private readonly int _seed;
        private readonly Random _rnd;

        public ExperimentContext(int maxN, int seed)
        {
            MaxN = Math.Max(100, maxN);
            _seed = seed;
            _rnd = new Random(seed);
            V = new double[MaxN];
            for (int i = 0; i < MaxN; i++) V[i] = _rnd.NextDouble(); // неотрицательные [0,1)
            Work = new double[MaxN];
        }

        public void EnsureCapacity(int requiredN)
        {
            if (requiredN <= MaxN) return;
            int newN = Math.Max(requiredN, MaxN * 2);
            var newV = new double[newN];
            Array.Copy(V, newV, MaxN);
            for (int i = MaxN; i < newN; i++) newV[i] = _rnd.NextDouble();
            V = newV;
            Work = new double[newN];
            MaxN = newN;
        }

        /// Очередной «кусок»: сначала 1-й, потом 1+2, потом 1+2+3, ... — префикс длины n.
        /// Копирование в буфер НЕ замеряется.
        public void FillPrefix(int n)
        {
            EnsureCapacity(n);
            Array.Copy(V, Work, n);
        }
    }

    // ================= 3. БАЗОВЫЙ КЛАСС АЛГОРИТМА =================

    /// Входные параметры: n + общий контекст. Стандартные: P (от класса) и константы алгоритма.
    public abstract class Algorithm
    {
        public readonly string Name;
        public readonly ComplexityClass Cls;
        public readonly MeasParams P; // копия стандартных параметров класса

        public virtual bool MeasuresSteps => false;
        public long LastStepCount { get; protected set; }

        protected Algorithm(string name, ComplexityClass cls)
        {
            Name = name;
            Cls = cls;
            P = cls.Defaults.Clone();
        }

        /// Подготовка (свежая копия «куска») — время НЕ замеряется.
        public virtual void Prepare(int n, ExperimentContext ctx)
        {
            ctx.EnsureCapacity(n);
        }

        /// Замеряемая работа с первыми n элементами.
        public abstract void Work(int n, ExperimentContext ctx);

        public sealed override string ToString() => $"{Name}  [{Cls.Name}]";
    }

    // ================= 4. АЛГОРИТМЫ =================

    // ---------- I. Функции от вектора ----------
    public sealed class ConstantFunction : Algorithm
    {
        public ConstantFunction() : base("f(v) = 1 (константа)", ComplexityClass.O1)
        {
        }

        public override void Work(int n, ExperimentContext ctx) => Sink.Add(1.0);
    }

    public sealed class SumFunction : Algorithm
    {
        public SumFunction() : base("Сумма элементов", ComplexityClass.On)
        {
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var v = ctx.V;
            double s = 0;
            for (int i = 0; i < n; i++) s += v[i];
            Sink.Add(s);
        }
    }

    public sealed class ProductFunction : Algorithm
    {
        public ProductFunction() : base("Произведение элементов", ComplexityClass.On)
        {
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var v = ctx.V;
            double p = 1.0;
            for (int i = 0; i < n; i++) p *= v[i];
            Sink.Add(p); // возможен underflow до 0 — на время не влияет
        }
    }

    /// Наивное вычисление P(x): каждый член со своим возведением в степень -> O(n^2)
    public sealed class PolynomialNaive : Algorithm
    {
        private readonly double _x;

        public PolynomialNaive(double x = 1.5) : base("Полином (наивно)", ComplexityClass.On2)
        {
            _x = x;
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var v = ctx.V;
            double sum = 0;
            for (int k = 0; k < n; k++)
            {
                double pw = 1.0;
                for (int e = 0; e < k; e++) pw *= _x;
                sum += v[k] * pw;
            }

            Sink.Add(sum);
        }
    }

    /// Схема Горнера: P(x) = v1 + x·(v2 + x·(v3 + ...)) -> O(n)
    public sealed class PolynomialHorner : Algorithm
    {
        private readonly double _x;

        public PolynomialHorner(double x = 1.5) : base("Полином (Горнер)", ComplexityClass.On)
        {
            _x = x;
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var v = ctx.V;
            double acc = 0;
            for (int k = n - 1; k >= 0; k--) acc = acc * _x + v[k];
            Sink.Add(acc);
        }
    }

    // ---------- Сортировки (общая подготовка: свежая копия префикса) ----------
    public abstract class SortBase : Algorithm
    {
        protected SortBase(string name, ComplexityClass cls) : base(name, cls)
        {
        }

        public override void Prepare(int n, ExperimentContext ctx) => ctx.FillPrefix(n);
    }

    public sealed class BubbleSortAlg : SortBase
    {
        public BubbleSortAlg() : base("Сортировка пузырьком", ComplexityClass.On2)
        {
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var a = ctx.Work;
            for (int end = n - 1; end > 0; end--)
            {
                bool swapped = false;
                for (int j = 0; j < end; j++)
                    if (a[j] > a[j + 1])
                    {
                        double t = a[j];
                        a[j] = a[j + 1];
                        a[j + 1] = t;
                        swapped = true;
                    }

                if (!swapped) break;
            }

            Sink.Add(a[n >> 1]);
        }
    }

    public sealed class QuickSortAlg : SortBase
    {
        private static readonly Random Piv = new Random(1234567);

        public QuickSortAlg() : base("Быстрая сортировка (Quick sort)", ComplexityClass.Onlogn)
        {
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            Sort(ctx.Work, 0, n - 1);
            Sink.Add(ctx.Work[n >> 1]);
        }

        private void Sort(double[] a, int lo, int hi)
        {
            while (lo < hi)
            {
                if (hi - lo < 16)
                {
                    Insertion(a, lo, hi);
                    return;
                }

                int p = Partition(a, lo, hi);
                if (p - lo < hi - p)
                {
                    Sort(a, lo, p - 1);
                    lo = p + 1;
                }
                else
                {
                    Sort(a, p + 1, hi);
                    hi = p - 1;
                }
            }
        }

        private int Partition(double[] a, int lo, int hi)
        {
            int m = lo + Piv.Next(hi - lo + 1);
            double t = a[m];
            a[m] = a[hi];
            a[hi] = t;
            double pivot = a[hi];
            int i = lo - 1;
            for (int j = lo; j < hi; j++)
                if (a[j] <= pivot)
                {
                    i++;
                    t = a[i];
                    a[i] = a[j];
                    a[j] = t;
                }

            t = a[i + 1];
            a[i + 1] = a[hi];
            a[hi] = t;
            return i + 1;
        }

        private static void Insertion(double[] a, int lo, int hi)
        {
            for (int i = lo + 1; i <= hi; i++)
            {
                double v = a[i];
                int j = i - 1;
                while (j >= lo && a[j] > v)
                {
                    a[j + 1] = a[j];
                    j--;
                }

                a[j + 1] = v;
            }
        }
    }

    /// Гибридный Timsort: естественные прогоны + бинарная вставка до minrun + слияния по инвариантам стека
    public sealed class TimsortAlg : SortBase
    {
        private readonly List<(int start, int len)> _stack = new List<(int, int)>(64);
        private double[] _tmp = Array.Empty<double>();

        public TimsortAlg() : base("Гибридный Timsort", ComplexityClass.Onlogn)
        {
        }

        public override void Prepare(int n, ExperimentContext ctx)
        {
            ctx.FillPrefix(n);
            if (_tmp.Length < ctx.MaxN) _tmp = new double[ctx.MaxN];
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            Sort(ctx.Work, n);
            Sink.Add(ctx.Work[n >> 1]);
        }

        private void Sort(double[] a, int n)
        {
            _stack.Clear();
            if (n < 2) return;
            int minrun = MinRunLength(n);
            int i = 0;
            while (i < n)
            {
                int end = i + 1;
                if (end < n && a[end] < a[end - 1]) // убывающий прогон — развернём
                {
                    while (end < n && a[end] < a[end - 1]) end++;
                    Array.Reverse(a, i, end - i);
                }
                else // возрастающий
                {
                    while (end < n && a[end] >= a[end - 1]) end++;
                }

                if (end - i < minrun) // добиваем бинарной вставкой
                {
                    int target = Math.Min(n, i + minrun);
                    BinaryInsert(a, i, end, target);
                    end = target;
                }

                _stack.Add((i, end - i));
                i = end;
                while (Collapse(a))
                {
                } // инварианты стека прогонов
            }

            while (_stack.Count > 1) Merge(_stack.Count - 2, a);
        }

        private bool Collapse(double[] a)
        {
            int c = _stack.Count;
            if (c >= 3)
            {
                int z = _stack[c - 3].len, y = _stack[c - 2].len, x = _stack[c - 1].len;
                if (z <= y + x)
                {
                    Merge(z < x ? c - 3 : c - 2, a);
                    return true;
                }
            }

            if (c >= 2 && _stack[c - 2].len <= _stack[c - 1].len)
            {
                Merge(c - 2, a);
                return true;
            }

            return false;
        }

        private void Merge(int idx, double[] a)
        {
            int s1 = _stack[idx].start, l1 = _stack[idx].len;
            int s2 = _stack[idx + 1].start, l2 = _stack[idx + 1].len;
            Array.Copy(a, s1, _tmp, 0, l1);
            int i = 0, j = s2, k = s1, e1 = l1, e2 = s2 + l2;
            while (i < e1 && j < e2) a[k++] = _tmp[i] <= a[j] ? _tmp[i++] : a[j++];
            while (i < e1) a[k++] = _tmp[i++];
            _stack[idx] = (s1, l1 + l2);
            _stack.RemoveAt(idx + 1);
        }

        private static int MinRunLength(int n)
        {
            int r = 0;
            while (n >= 64)
            {
                r |= n & 1;
                n >>= 1;
            }

            return n + r;
        }

        private static void BinaryInsert(double[] a, int lo, int mid, int end)
        {
            for (int p = mid; p < end; p++)
            {
                double v = a[p];
                int left = lo, right = p;
                while (left < right)
                {
                    int m = (left + right) >> 1;
                    if (a[m] <= v) left = m + 1;
                    else right = m;
                }

                Array.Copy(a, left, a, left + 1, p - left);
                a[left] = v;
            }
        }
    }

    /// Сортировка Шелла с последовательностью шагов Кнута: h = 1, 4, 13, 40, 121, 364... -> O(n^1.5)
    public sealed class ShellSortAlg : SortBase
    {
        public ShellSortAlg() : base("Сортировка Шелла", ComplexityClass.On1_5)
        {
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var a = ctx.Work;
            int h = 1;
            while (h < n / 3) h = 3 * h + 1;

            while (h >= 1)
            {
                for (int i = h; i < n; i++)
                {
                    double v = a[i];
                    int j = i;
                    while (j >= h && a[j - h] > v)
                    {
                        a[j] = a[j - h];
                        j -= h;
                    }

                    a[j] = v;
                }

                h /= 3;
            }

            Sink.Add(a[n >> 1]);
        }
    }

    // ---------- Возведение в степень (n — показатель степени) ----------
    public abstract class PowBase : Algorithm
    {
        protected readonly double X;

        protected PowBase(string name, ComplexityClass cls, double x) : base(name, cls)
        {
            X = x;
        }

        public override bool MeasuresSteps => true;

        public override void Work(int n, ExperimentContext ctx)
        {
            long steps = 0;
            double res = Pow(X, n, ref steps);
            LastStepCount = steps;
            Sink.Add(res);
        }

        protected abstract double Pow(double x, int n, ref long steps);
    }

    // ---------- Возведение в степень (4 алгоритма из методички) ----------

    /// Рис. 1: простой алгоритм: f = x·x·...·x (n умножений) -> O(n)
    public sealed class PowNaive : PowBase
    {
        public PowNaive() : this(1.0000001)
        {
        }

        public PowNaive(double x) : base("Степень (простой, рис. 1)", ComplexityClass.On, x)
        {
        }

        protected override double Pow(double x, int n, ref long steps)
        {
            double f = 1.0;
            for (int k = 0; k < n; k++)
            {
                f *= x;
                steps++;
            }
            return f;
        }
    }

    /// Рис. 2: рекурсивный RecPow по формуле (3) -> O(log n)
    public sealed class PowRecursive : PowBase
    {
        public PowRecursive() : this(1.0000001)
        {
        }

        public PowRecursive(double x) : base("Степень (рекурсивный RecPow, рис. 2)", ComplexityClass.Ologn, x)
        {
        }

        protected override double Pow(double x, int n, ref long steps)
        {
            if (n == 0) return 1.0;
            double f = Pow(x, n >> 1, ref steps); // f = RecPow(x, n div 2)
            f *= f; // f = f * f
            steps++;
            if ((n & 1) == 1)
            {
                f *= x; // нечётное: f = f * x
                steps++;
            }
            return f;
        }
    }

    /// Рис. 3: быстрый алгоритм QuickPow (старшие биты вперёд) -> O(log n)
    public sealed class PowQuick : PowBase
    {
        public PowQuick() : this(1.0000001)
        {
        }

        public PowQuick(double x) : base("Степень (быстрый QuickPow, рис. 3)", ComplexityClass.Ologn, x)
        {
        }

        protected override double Pow(double x, int n, ref long steps)
        {
            double c = x;
            int k = n;
            double f = (k & 1) == 1 ? c : 1.0; // k mod 2 = 1 -> f = c, иначе f = 1
            while (k != 0)
            {
                k >>= 1; // k = k div 2
                c *= c; // c = c * c
                steps++;
                if ((k & 1) == 1)
                {
                    f *= c; // k mod 2 = 1 -> f = f * c
                    steps++;
                }
            }

            return f;
        }
    }

    /// Рис. 4: классический быстрый алгоритм QuickPow1 (младшие биты вперёд) -> O(log n)
    public sealed class PowQuick1 : PowBase
    {
        public PowQuick1() : this(1.0000001)
        {
        }

        public PowQuick1(double x) : base("Степень (классический быстрый QuickPow1, рис. 4)", ComplexityClass.Ologn, x)
        {
        }

        protected override double Pow(double x, int n, ref long steps)
        {
            double c = x, f = 1.0;
            int k = n;
            while (k != 0)
            {
                if ((k & 1) == 0)
                {
                    c *= c;
                    steps++;
                    k >>= 1;
                } // чётное: c = c*c, k = k div 2
                else
                {
                    f *= c;
                    steps++;
                    k--;
                } // нечётное: f = f*c, k = k - 1
            }

            return f;
        }
    }

    // ---------- II. Обычное матричное умножение O(n^3) ----------
    public sealed class MatrixMultiply : Algorithm
    {
        private int _maxM;
        private double[] _a, _b; // «большие» матрицы (данные — один раз)
        private double[] _pa, _pb, _pc; // префикс n×n и результат
        private readonly Random _rnd = new Random(777);

        public MatrixMultiply(int maxM = 200) : base("Матричное умножение A×B", ComplexityClass.On3)
        {
            EnsureMatrixCapacity(maxM);
        }

        private void EnsureMatrixCapacity(int m)
        {
            if (m <= _maxM && _a != null) return;
            _maxM = Math.Max(m, _maxM);
            int len = _maxM * _maxM;
            _a = new double[len];
            _b = new double[len];
            _pa = new double[len];
            _pb = new double[len];
            _pc = new double[len];
            for (int i = 0; i < len; i++)
            {
                _a[i] = _rnd.NextDouble();
                _b[i] = _rnd.NextDouble();
            }
        }

        public override void Prepare(int n, ExperimentContext ctx)
        {
            base.Prepare(n, ctx);
            EnsureMatrixCapacity(n);
            for (int i = 0; i < n; i++) // левый верхний угол n×n — «кусок» больших матриц
            {
                Array.Copy(_a, i * _maxM, _pa, i * n, n);
                Array.Copy(_b, i * _maxM, _pb, i * n, n);
            }

            Array.Clear(_pc, 0, n * n);
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            var a = _pa;
            var b = _pb;
            var c = _pc;
            for (int i = 0; i < n; i++)
            {
                int io = i * n;
                for (int k = 0; k < n; k++)
                {
                    double aik = a[io + k];
                    int ko = k * n;
                    for (int j = 0; j < n; j++) c[io + j] += aik * b[ko + j];
                }
            }

            Sink.Add(c[0] + c[n * n - 1]);
        }
    }

    // ================= 5. ИЗМЕРЕНИЯ + ИСКЛЮЧЕНИЕ ВЫБРОСОВ =================

    public sealed class Series
    {
        public Algorithm Algo;
        public readonly List<double> N = new List<double>();
        public readonly List<double> T = new List<double>(); // время (с) или количество шагов
        public readonly List<double> TFit = new List<double>(); // теория C·f(n)
        public double C;
        public double MSE;
        public bool MeasuresSteps;
        public int CachedPoints;
    }

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
            var ns = BuildNList(p);
            string metricUnit = algo.MeasuresSteps ? "шаги (операции)" : "время (с)";
            string msg1 = $"   точек: {ns.Count}, метрика: {metricUnit}, Runs: {p.Runs}, Step: {p.Step}, MaxN: {p.MaxN}";
            Console.WriteLine(msg1);
            log?.Invoke(msg1);

            if (!algo.MeasuresSteps)
            {
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
                if (algo.MeasuresSteps)
                {
                    val = steps[0]; // для элементарных операций шаг строго детерминирован
                }
                else
                {
                    val = RobustMean(times, out dropped);
                    droppedTotal += dropped;
                }

                if (fromCache) s.CachedPoints++;
                s.N.Add(n);
                s.T.Add(val);

                string cacheTag = fromCache ? " [кэш SQLite]" : "";
                string msgPt = algo.MeasuresSteps
                    ? $"   n={n,6}   шагов = {val,8:0}{cacheTag}"
                    : $"   n={n,6}   t = {val,12:0.000E+00} с   (отброшено: {dropped}){cacheTag}";
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

        public static void GlobalWarmup(ExperimentContext ctx, Action<string> log = null)
        {
            string msgStart = ">>> Глобальный прогрев (JIT, GC, память, Turbo Boost CPU)...";
            Console.WriteLine(msgStart);
            log?.Invoke(msgStart);

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

            long targetTicks = Stopwatch.Frequency / 10; // ~100 мс спиннинга
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

        /// «Куски»: n = 1, 1+2, 1+2+3, ... (треугольные числа) либо сетка с шагом Step
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

        static void Warmup(Algorithm a, ExperimentContext ctx, int nMax)
        {
            for (int i = 0; i < 3; i++)
            {
                a.Prepare(nMax, ctx);
                a.Work(nMax, ctx);
            }
        }

        static void CleanHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        static int CalibrateBatch(Algorithm a, ExperimentContext ctx, int n, double minSec)
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

        /// Отбраковка выбросов внутри повторов: модифицированная z-оценка (медиана/MAD), порог 3.5
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

        static double Median(double[] xs)
        {
            var s = (double[])xs.Clone();
            Array.Sort(s);
            return s.Length % 2 == 1 ? s[s.Length / 2] : 0.5 * (s[s.Length / 2 - 1] + s[s.Length / 2]);
        }

        static List<double> MedianSmooth(List<double> t, int w)
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

        static double FitConstant(List<double> ns, List<double> ts, Func<double, double> f)
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
            Console.WriteLine($"Холодный старт: C = {s1.C:0.####E+00}, n=1: t = {s1.T[0]:0.000E+00} с");
            Console.WriteLine($"Горячий старт:  C = {s2.C:0.####E+00}, n=1: t = {s2.T[0]:0.000E+00} с");
            Console.WriteLine($"Разница C: {Math.Abs(s1.C - s2.C) / s2.C * 100:F2}%");
            Console.WriteLine($"Разница n=1: {Math.Abs(s1.T[0] - s2.T[0]) / s2.T[0] * 100:F2}%");
            Console.WriteLine("==========================================================");
        }
    }

    // ================= 6. ГРАФИКИ (строит сама программа) =================


    public static class Plotter
    {
        public static List<KeyValuePair<string, ScottPlot.Plot>> Build(List<Series> results)
        {
            var plots = new List<KeyValuePair<string, ScottPlot.Plot>>();
            foreach (var s in results)
            {
                var plt = new ScottPlot.Plot(1000, 620);
                string yLabel = s.MeasuresSteps ? "Количество элементарных операций (шагов)" : "Среднее время, с";
                plt.Title($"{s.Algo.Name}  —  класс {s.Algo.Cls.Name} (MSE = {s.MSE:0.####E+00})");
                plt.XLabel("Размерность входа n");
                plt.YLabel(yLabel);

                // В ScottPlot 4 сигнатура: AddScatter(xs, ys, color, lineWidth, markerSize, markerShape, lineStyle, label, ...)
                // поэтому label передаём именованным аргументом.
                plt.AddScatter(s.N.ToArray(), s.T.ToArray(),
                    System.Drawing.Color.SteelBlue, 1.2f, 4, ScottPlot.MarkerShape.filledCircle,
                    label: "Экспериментальные результаты");

                plt.AddScatter(s.N.ToArray(), s.TFit.ToArray(),
                    System.Drawing.Color.DarkOrange, 2.2f, 0, ScottPlot.MarkerShape.none,
                    label: $"Аппроксимация: {s.C:0.####E+00} · {s.Algo.Cls.Name}");

                plt.Legend();
                plots.Add(new KeyValuePair<string, ScottPlot.Plot>(s.Algo.Name, plt));
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

                using (var bmp = kv.Value.Render(1000, 620)) // ScottPlot 4: Render() -> System.Drawing.Bitmap
                {
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }

    public sealed class PlotForm : Form
    {
        public PlotForm(IEnumerable<KeyValuePair<string, ScottPlot.Plot>> plots)
        {
            Text = "Графики алгоритмов";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var kv in plots)
            {
                var page = new TabPage(kv.Key);
                var fp = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
                fp.Reset(kv.Value);
                fp.Refresh();
                page.Controls.Add(fp);
                tabs.TabPages.Add(page);
            }

            Controls.Add(tabs);
        }
    }

    public sealed class AlgoConfigItem
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; }
        public string ClassName { get; set; }
        public int DefaultMaxN { get; set; }
        public int MaxN { get; set; }
        public int DefaultStep { get; set; }
        public int Step { get; set; }
        public int DefaultRuns { get; set; }
        public int Runs { get; set; }
        public Func<Algorithm> Factory { get; set; }
    }

    public sealed class MainForm : Form
    {
        private readonly List<AlgoConfigItem> _configs;
        private DataGridView _gridAlgos;
        private CheckBox _chkUseCache;
        private CheckBox _chkForceRecalc;
        private CheckBox _chkWarmup;
        private NumericUpDown _numSeed;
        private Button _btnRun;
        private Button _btnCancel;
        private ProgressBar _progressBar;
        private Label _lblStatus;

        private TabControl _tabsMain;
        private DataGridView _gridSummary;
        private TextBox _txtLog;
        private Label _lblTime;
        private Button _btnExportCsv;
        private Button _btnExportPng;

        private TabPage _tabHistory;
        private CheckedListBox _lstHistory;
        private ComboBox _cmbHistoryAlgo;
        private Button _btnRefreshHistory;
        private Button _btnPlotHistory;
        private ScottPlot.FormsPlot _plotHistory;

        private CancellationTokenSource _cts;
        private List<Series> _lastResults = new List<Series>();
        private List<KeyValuePair<string, ScottPlot.Plot>> _lastPlots = new List<KeyValuePair<string, ScottPlot.Plot>>();

        public MainForm()
        {
            _configs = CreateDefaultConfigs();
            InitializeComponent();
        }

        public static List<AlgoConfigItem> CreateDefaultConfigs() => new List<AlgoConfigItem>
        {
            new AlgoConfigItem { Name = "f(v) = 1 (константа)", ClassName = "O(1)", DefaultMaxN = 1000, MaxN = 1000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ConstantFunction() },
            new AlgoConfigItem { Name = "Сумма элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new SumFunction() },
            new AlgoConfigItem { Name = "Произведение элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ProductFunction() },
            new AlgoConfigItem { Name = "Полином (наивно)", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialNaive() },
            new AlgoConfigItem { Name = "Полином (Горнер)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialHorner() },
            new AlgoConfigItem { Name = "Сортировка пузырьком", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new BubbleSortAlg() },
            new AlgoConfigItem { Name = "Быстрая сортировка (Quick sort)", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new QuickSortAlg() },
            new AlgoConfigItem { Name = "Гибридный Timsort", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new TimsortAlg() },
            new AlgoConfigItem { Name = "Сортировка Шелла", ClassName = "O(n^1.5)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ShellSortAlg() },
            new AlgoConfigItem { Name = "Степень (простой, рис. 1)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowNaive() },
            new AlgoConfigItem { Name = "Степень (рекурсивный RecPow, рис. 2)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowRecursive() },
            new AlgoConfigItem { Name = "Степень (быстрый QuickPow, рис. 3)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick() },
            new AlgoConfigItem { Name = "Степень (классический быстрый QuickPow1, рис. 4)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick1() },
            new AlgoConfigItem { Name = "Матричное умножение A×B", ClassName = "O(n^3)", DefaultMaxN = 200, MaxN = 200, DefaultStep = 10, Step = 10, DefaultRuns = 3, Runs = 3, Factory = () => new MatrixMultiply(maxM: 200) },
        };

        private void InitializeComponent()
        {
            Text = "Лабораторная работа №1 — Эмпирический анализ временной сложности алгоритмов";
            Width = 1400;
            Height = 850;
            MinimumSize = new Size(1020, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 500,
                FixedPanel = FixedPanel.Panel1
            };
            Controls.Add(split);

            // ================= LEFT PANEL =================
            var pnlLeft = split.Panel1;

            var pnlTopButtons = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
            var btnSelectAll = new Button { Text = "Выбрать все", AutoSize = true, Location = new Point(4, 6) };
            var btnDeselectAll = new Button { Text = "Снять все", AutoSize = true, Location = new Point(btnSelectAll.Right + 6, 6) };
            var btnResetDefaults = new Button { Text = "Сбросить параметры", AutoSize = true, Location = new Point(btnDeselectAll.Right + 6, 6) };
            btnSelectAll.Click += (s, e) => { foreach (DataGridViewRow r in _gridAlgos.Rows) r.Cells[0].Value = true; };
            btnDeselectAll.Click += (s, e) => { foreach (DataGridViewRow r in _gridAlgos.Rows) r.Cells[0].Value = false; };
            btnResetDefaults.Click += (s, e) =>
            {
                for (int i = 0; i < _configs.Count; i++)
                {
                    _gridAlgos.Rows[i].Cells[3].Value = _configs[i].DefaultMaxN;
                    _gridAlgos.Rows[i].Cells[4].Value = _configs[i].DefaultStep;
                    _gridAlgos.Rows[i].Cells[5].Value = _configs[i].DefaultRuns;
                }
            };
            pnlTopButtons.Controls.AddRange(new Control[] { btnSelectAll, btnDeselectAll, btnResetDefaults });
            pnlLeft.Controls.Add(pnlTopButtons);

            var pnlBottomLeft = new Panel { Dock = DockStyle.Bottom, Height = 225, Padding = new Padding(8) };
            _chkUseCache = new CheckBox
            {
                Text = "Использовать кэш SQLite (benchmark.db)",
                Checked = true,
                AutoSize = true,
                Location = new Point(8, 8)
            };
            _chkForceRecalc = new CheckBox
            {
                Text = "Принудительный перерасчет (без кэша)",
                Checked = false,
                AutoSize = true,
                Location = new Point(8, 30)
            };
            _chkWarmup = new CheckBox
            {
                Text = "Глобальный прогрев (JIT + CPU Turbo)",
                Checked = true,
                AutoSize = true,
                Location = new Point(8, 52)
            };

            var lblSeed = new Label { Text = "Seed:", AutoSize = true, Location = new Point(8, 78) };
            _numSeed = new NumericUpDown
            {
                Minimum = 1,
                Maximum = int.MaxValue,
                Value = 20240915,
                Width = 110,
                Location = new Point(lblSeed.Right + 8, 74)
            };

            _btnRun = new Button
            {
                Text = "▶  Запустить замеры",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 38),
                Location = new Point(8, 106)
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += async (s, e) => await StartExperimentAsync();

            _btnCancel = new Button
            {
                Text = "■  Остановить",
                Font = new Font("Segoe UI", 9.5f),
                Enabled = false,
                Size = new Size(110, 38),
                Location = new Point(_btnRun.Right + 8, 106)
            };
            _btnCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                _btnCancel.Enabled = false;
                _lblStatus.Text = "Остановка эксперимента...";
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            _lblStatus = new Label
            {
                Text = "Готов к запуску",
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlBottomLeft.Controls.AddRange(new Control[] {
                _chkUseCache, _chkForceRecalc, _chkWarmup,
                lblSeed, _numSeed, _btnRun, _btnCancel,
                _lblStatus, _progressBar
            });
            pnlLeft.Controls.Add(pnlBottomLeft);

            _gridAlgos = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };
            _gridAlgos.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Вкл", Width = 35 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Алгоритм", Width = 180, ReadOnly = true });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Класс", Width = 65, ReadOnly = true });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Max N", Width = 65 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шаг", Width = 55 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Runs", Width = 55 });

            foreach (var cfg in _configs)
            {
                _gridAlgos.Rows.Add(cfg.Enabled, cfg.Name, cfg.ClassName, cfg.MaxN, cfg.Step, cfg.Runs);
            }
            pnlLeft.Controls.Add(_gridAlgos);
            _gridAlgos.BringToFront();

            // ================= RIGHT PANEL =================
            var pnlRight = split.Panel2;

            var pnlTopRight = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
            _btnExportCsv = new Button { Text = "Экспорт в CSV...", AutoSize = true, Location = new Point(6, 6) };
            _btnExportPng = new Button { Text = "Сохранить графики (PNG)...", AutoSize = true, Location = new Point(_btnExportCsv.Right + 8, 6) };
            _lblTime = new Label { Text = "", AutoSize = true, Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 8, 8, 0) };

            _btnExportCsv.Click += (s, e) => ExportCsvDialog();
            _btnExportPng.Click += (s, e) => ExportPngDialog();

            pnlTopRight.Controls.AddRange(new Control[] { _btnExportCsv, _btnExportPng, _lblTime });
            pnlRight.Controls.Add(pnlTopRight);

            _tabsMain = new TabControl { Dock = DockStyle.Fill };

            var tabSummary = new TabPage("Сводка результатов");
            _gridSummary = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _gridSummary.Columns.Add("Algo", "Алгоритм");
            _gridSummary.Columns.Add("Class", "Класс");
            _gridSummary.Columns.Add("MaxN", "Max N");
            _gridSummary.Columns.Add("Step", "Шаг");
            _gridSummary.Columns.Add("Runs", "Повторов");
            _gridSummary.Columns.Add("Pts", "Точек");
            _gridSummary.Columns.Add("Metric", "Метрика");
            _gridSummary.Columns.Add("MSE", "MSE");
            _gridSummary.Columns.Add("C", "Константа C");
            _gridSummary.Columns.Add("TFact", "Факт (MaxN)");
            _gridSummary.Columns.Add("TTheory", "Теория (MaxN)");
            _gridSummary.Columns.Add("Cache", "Из кэша");
            tabSummary.Controls.Add(_gridSummary);
            _tabsMain.TabPages.Add(tabSummary);

            var tabLog = new TabPage("Журнал выполнения");
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9f),
                BackColor = Color.White
            };
            tabLog.Controls.Add(_txtLog);
            _tabsMain.TabPages.Add(tabLog);

            // Tab 3: History Comparison
            _tabHistory = new TabPage("Сравнение из истории");
            var splitHistory = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 350,
                FixedPanel = FixedPanel.Panel1
            };

            var pnlHistLeft = splitHistory.Panel1;
            var pnlHistTop = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
            _btnRefreshHistory = new Button { Text = "↻ Обновить список", AutoSize = true, Location = new Point(6, 6) };
            _btnRefreshHistory.Click += (s, e) => RefreshHistoryList();
            pnlHistTop.Controls.Add(_btnRefreshHistory);
            pnlHistLeft.Controls.Add(pnlHistTop);

            var pnlHistBottom = new Panel { Dock = DockStyle.Bottom, Height = 100, Padding = new Padding(6) };
            var lblHistAlgo = new Label { Text = "Алгоритм для сравнения:", Dock = DockStyle.Top, Height = 22 };
            _cmbHistoryAlgo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            _btnPlotHistory = new Button
            {
                Text = "Построить график сравнения",
                Dock = DockStyle.Bottom,
                Height = 36,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnPlotHistory.FlatAppearance.BorderSize = 0;
            _btnPlotHistory.Click += (s, e) => PlotHistoryComparison();

            pnlHistBottom.Controls.Add(_btnPlotHistory);
            pnlHistBottom.Controls.Add(_cmbHistoryAlgo);
            pnlHistBottom.Controls.Add(lblHistAlgo);
            pnlHistLeft.Controls.Add(pnlHistBottom);

            _lstHistory = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true
            };
            _lstHistory.ItemCheck += (s, e) =>
            {
                BeginInvoke(new Action(UpdateHistoryAlgoCombo));
            };
            pnlHistLeft.Controls.Add(_lstHistory);
            _lstHistory.BringToFront();

            _plotHistory = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
            splitHistory.Panel2.Controls.Add(_plotHistory);

            _tabHistory.Controls.Add(splitHistory);
            _tabsMain.TabPages.Add(_tabHistory);

            pnlRight.Controls.Add(_tabsMain);
            _tabsMain.BringToFront();

            RefreshHistoryList();
        }

        private void RefreshHistoryList()
        {
            try
            {
                var history = BenchmarkDb.GetHistoryExperiments();
                _lstHistory.Items.Clear();
                foreach (var h in history)
                {
                    _lstHistory.Items.Add(h, false);
                }
                UpdateHistoryAlgoCombo();
            }
            catch (Exception ex)
            {
                AppendLog($"[Предупреждение] Ошибка загрузки истории: {ex.Message}");
            }
        }

        private void UpdateHistoryAlgoCombo()
        {
            string currSel = _cmbHistoryAlgo.SelectedItem?.ToString();
            var checkedExps = _lstHistory.CheckedItems.Cast<HistoryExperimentInfo>().ToList();
            var algos = new HashSet<string>();
            foreach (var exp in checkedExps)
            {
                foreach (var a in exp.Algorithms) algos.Add(a);
            }

            if (algos.Count == 0)
            {
                foreach (HistoryExperimentInfo exp in _lstHistory.Items)
                {
                    foreach (var a in exp.Algorithms) algos.Add(a);
                }
            }

            _cmbHistoryAlgo.Items.Clear();
            foreach (var a in algos.OrderBy(x => x))
            {
                _cmbHistoryAlgo.Items.Add(a);
            }

            if (currSel != null && _cmbHistoryAlgo.Items.Contains(currSel))
                _cmbHistoryAlgo.SelectedItem = currSel;
            else if (_cmbHistoryAlgo.Items.Count > 0)
                _cmbHistoryAlgo.SelectedIndex = 0;
        }

        private void PlotHistoryComparison()
        {
            var checkedExps = _lstHistory.CheckedItems.Cast<HistoryExperimentInfo>().ToList();
            if (checkedExps.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы один эксперимент из списка (отметьте галочкой).", "Нет выбора", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedAlgo = _cmbHistoryAlgo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedAlgo))
            {
                MessageBox.Show(this, "Выберите алгоритм для сравнения в выпадающем списке.", "Нет алгоритма", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _plotHistory.Plot.Clear();
            bool isSteps = false;
            int curveCount = 0;

            foreach (var exp in checkedExps)
            {
                var rows = BenchmarkDb.GetMeasurementsForExperiment(exp.ExperimentId)
                    .Where(r => r.AlgorithmName == selectedAlgo)
                    .OrderBy(r => r.N)
                    .ToList();

                if (rows.Count == 0) continue;

                var grouped = rows.GroupBy(r => r.N).OrderBy(g => g.Key).ToList();
                var xs = new List<double>();
                var ys = new List<double>();

                bool expHasSteps = rows.Any(r => r.StepCount.HasValue);
                if (expHasSteps) isSteps = true;

                foreach (var g in grouped)
                {
                    xs.Add(g.Key);
                    if (expHasSteps)
                        ys.Add(g.Average(r => r.StepCount ?? 0));
                    else
                        ys.Add(g.Average(r => r.ElapsedSeconds));
                }

                string label = $"{exp.CreatedAt:yyyy-MM-dd HH:mm} ({exp.ExperimentId.Substring(0, Math.Min(6, exp.ExperimentId.Length))})";
                _plotHistory.Plot.AddScatter(xs.ToArray(), ys.ToArray(), label: label);
                curveCount++;
            }

            if (curveCount == 0)
            {
                MessageBox.Show(this, $"В выбранных экспериментах нет данных для алгоритма \"{selectedAlgo}\".", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _plotHistory.Plot.Title($"Сравнение запусков: {selectedAlgo}");
            _plotHistory.Plot.XLabel("Размерность входа n");
            _plotHistory.Plot.YLabel(isSteps ? "Количество элементарных операций (шагов)" : "Среднее время, с");
            _plotHistory.Plot.Legend();
            _plotHistory.Refresh();
        }

        private async Task StartExperimentAsync()
        {
            _gridAlgos.EndEdit();
            var selected = new List<(AlgoConfigItem cfg, int maxN, int step, int runs)>();
            for (int i = 0; i < _gridAlgos.Rows.Count; i++)
            {
                var row = _gridAlgos.Rows[i];
                bool isEn = Convert.ToBoolean(row.Cells[0].Value);
                if (!isEn) continue;

                if (!int.TryParse(Convert.ToString(row.Cells[3].Value), out int maxN) || maxN <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Max N для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(Convert.ToString(row.Cells[4].Value), out int step) || step <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Шага для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(Convert.ToString(row.Cells[5].Value), out int runs) || runs <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Повторов для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                selected.Add((_configs[i], maxN, step, runs));
            }

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы один алгоритм для проведения замеров.", "Нет выбранных алгоритмов", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnRun.Enabled = false;
            _btnCancel.Enabled = true;
            _progressBar.Value = 0;
            _txtLog.Clear();
            _gridSummary.Rows.Clear();
            while (_tabsMain.TabPages.Count > 3) _tabsMain.TabPages.RemoveAt(3);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            bool doWarmup = _chkWarmup.Checked;
            bool useCache = _chkUseCache.Checked;
            bool forceRecalc = _chkForceRecalc.Checked;
            int seed = (int)_numSeed.Value;

            var results = new List<Series>();
            var swTotal = Stopwatch.StartNew();
            string experimentId = Guid.NewGuid().ToString("N");

            try
            {
                await Task.Run(() =>
                {
                    int maxNVec = Math.Max(2000, selected.Max(x => x.maxN));
                    var ctx = new ExperimentContext(maxNVec, seed);

                    if (doWarmup)
                    {
                        SetStatus("Выполняется глобальный прогрев JIT и CPU...");
                        Bench.GlobalWarmup(ctx, AppendLog);
                    }

                    for (int i = 0; i < selected.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = selected[i];
                        var algo = item.cfg.Factory();
                        algo.P.MaxN = item.maxN;
                        algo.P.Step = item.step;
                        algo.P.StartN = Math.Min(item.step, item.maxN);
                        algo.P.Runs = item.runs;

                        SetStatus($"[{i + 1}/{selected.Count}] Выполняется: {algo.Name} (MaxN = {item.maxN}, Step = {item.step}, Runs = {item.runs})...");
                        AppendLog($"\n=== {algo.Name}  [{algo.Cls.Name}] (MaxN = {item.maxN}, Step = {item.step}, Runs = {item.runs}) ===");

                        var s = Bench.Run(algo, ctx, experimentId, useCache, forceRecalc, AppendLog, (pt, totalPts) =>
                        {
                            int pct = (int)(((i + (double)pt / totalPts) / selected.Count) * 100);
                            SetProgress(pct);
                        }, token);

                        results.Add(s);
                    }
                }, token);

                swTotal.Stop();
                _lastResults = results;
                _lblTime.Text = $"Общее время: {swTotal.Elapsed.TotalSeconds:0.0} с";
                _lblStatus.Text = $"Эксперимент успешно завершён за {swTotal.Elapsed.TotalSeconds:0.0} с";
                _progressBar.Value = 100;

                foreach (var s in results)
                {
                    int last = s.N.Count - 1;
                    string metricStr = s.MeasuresSteps ? "Шаги" : "Время (с)";
                    string factStr = s.MeasuresSteps
                        ? s.T[last].ToString("0", CultureInfo.InvariantCulture)
                        : s.T[last].ToString("0.000E+00", CultureInfo.InvariantCulture);
                    string theoryStr = s.MeasuresSteps
                        ? s.TFit[last].ToString("0.0", CultureInfo.InvariantCulture)
                        : s.TFit[last].ToString("0.000E+00", CultureInfo.InvariantCulture);

                    _gridSummary.Rows.Add(
                        s.Algo.Name,
                        s.Algo.Cls.Name,
                        s.Algo.P.MaxN,
                        s.Algo.P.Step,
                        s.Algo.P.Runs,
                        s.N.Count,
                        metricStr,
                        s.MSE.ToString("0.####E+00", CultureInfo.InvariantCulture),
                        s.C.ToString("0.####E+00", CultureInfo.InvariantCulture),
                        factStr,
                        theoryStr,
                        $"{s.CachedPoints} / {s.N.Count}"
                    );
                }

                _lastPlots = Plotter.Build(results);
                foreach (var kv in _lastPlots)
                {
                    var page = new TabPage(kv.Key);
                    var fp = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
                    fp.Reset(kv.Value);
                    fp.Refresh();
                    page.Controls.Add(fp);
                    _tabsMain.TabPages.Add(page);
                }

                try
                {
                    Program.ExportCsv(results, "results.csv");
                    Plotter.SaveAll(_lastPlots, "charts");
                    AppendLog("\n[Автосохранение] Результаты сохранены в results.csv, charts/ и benchmark.db");
                }
                catch (Exception ex)
                {
                    AppendLog($"\n[Предупреждение] Ошибка автосохранения: {ex.Message}");
                }

                RefreshHistoryList();
                _tabsMain.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                swTotal.Stop();
                _lblStatus.Text = "Эксперимент остановлен пользователем";
                AppendLog("\n[Отмена] Эксперимент был прерван пользователем.");
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                _lblStatus.Text = "Ошибка выполнения";
                AppendLog($"\n[Ошибка] {ex}");
                MessageBox.Show(this, "Произошла ошибка при выполнении замеров:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnRun.Enabled = true;
                _btnCancel.Enabled = false;
            }
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { Invoke(new Action<string>(SetStatus), text); return; }
            _lblStatus.Text = text;
        }

        private void SetProgress(int percent)
        {
            if (InvokeRequired) { Invoke(new Action<int>(SetProgress), percent); return; }
            _progressBar.Value = Math.Max(0, Math.Min(100, percent));
        }

        private void AppendLog(string msg)
        {
            if (InvokeRequired) { Invoke(new Action<string>(AppendLog), msg); return; }
            _txtLog.AppendText(msg + Environment.NewLine);
        }

        private void ExportCsvDialog()
        {
            if (_lastResults == null || _lastResults.Count == 0)
            {
                MessageBox.Show(this, "Нет доступных результатов для экспорта. Сначала запустите замеры.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var sfd = new SaveFileDialog { Filter = "CSV файлы (*.csv)|*.csv", FileName = "results.csv" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    Program.ExportCsv(_lastResults, sfd.FileName);
                    MessageBox.Show(this, "Данные успешно сохранены в " + sfd.FileName, "Экспорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void ExportPngDialog()
        {
            if (_lastPlots == null || _lastPlots.Count == 0)
            {
                MessageBox.Show(this, "Нет построенных графиков для сохранения. Сначала запустите замеры.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var fbd = new FolderBrowserDialog { Description = "Выберите папку для сохранения графиков" })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    Plotter.SaveAll(_lastPlots, fbd.SelectedPath);
                    MessageBox.Show(this, "Графики успешно сохранены в " + fbd.SelectedPath, "Экспорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }

    // ================= 7. MAIN =================

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch
            {
            }

            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            catch
            {
            }

            if (args.Length > 0 && args[0] == "--check-init")
            {
                const int maxVectorN = 2000;
                var ctx = new ExperimentContext(maxVectorN, seed: 20240915);
                Bench.CheckInitImpact(ctx);
                return;
            }

            if (args.Length > 0 && args[0] == "--no-window")
            {
                RunConsoleMode();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void RunConsoleMode()
        {
            var configs = MainForm.CreateDefaultConfigs();
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
                results.Add(Bench.Run(a, ctx, expId, useCache: true, forceRecalc: false));
            }

            swAll.Stop();
            Console.WriteLine($"\nОбщее время эксперимента: {swAll.Elapsed.TotalSeconds:0.0} с");
            Console.WriteLine($"[sink] {Sink.Value:0.000E+00}");

            Console.WriteLine("\n===== Сводка (эмпирика vs теория) =====");
            foreach (var s in results)
            {
                int last = s.N.Count - 1;
                string unit = s.MeasuresSteps ? "шагов" : "с";
                Console.WriteLine(
                    $"{s.Algo.Name,-34} {s.Algo.Cls.Name,-12} C={s.C,12:0.####E+00}   MSE={s.MSE,12:0.####E+00}   " +
                    $"val(n={s.N[last]:0}) = {s.T[last]:0.000E+00} {unit}   теория = {s.TFit[last]:0.000E+00} {unit}");
            }

            ExportCsv(results, "results.csv");
            var plots = Plotter.Build(results);
            Plotter.SaveAll(plots, "charts");
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