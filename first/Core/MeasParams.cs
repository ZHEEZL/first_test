using System;

namespace first
{
    /// <summary>
    /// Схема выбора n: «куски» (n = 1, 1+2, 1+2+3, ...) либо равномерная сетка n += Step
    /// </summary>
    public enum NSampling
    {
        TriangularChunks,
        Linear
    }

    /// <summary>
    /// Стандартные параметры измерений для класса алгоритмов (подбираются опытным путём).
    /// </summary>
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

    /// <summary>
    /// Вектор генерируется ОДИН раз (фиксированный seed) и общий для всех алгоритмов.
    /// </summary>
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

        /// <summary>
        /// Очередной «кусок»: сначала 1-й, потом 1+2, потом 1+2+3, ... — префикс длины n.
        /// Копирование в буфер НЕ замеряется.
        /// </summary>
        public void FillPrefix(int n)
        {
            EnsureCapacity(n);
            Array.Copy(V, Work, n);
        }
    }
}
