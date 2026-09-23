namespace first
{
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

    // ---------- Возведение в степень (алгоритмы из методички и спецификации) ----------

    /// <summary>
    /// Рис. 1: простой алгоритм: f = x·x·...·x (n умножений) -> O(n)
    /// </summary>
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

    /// <summary>
    /// П. 11: линейная рекурсия по определению x^n = x · x^(n-1), x^0 = 1 -> O(n) шагов
    /// </summary>
    public sealed class PowRecursiveLinear : PowBase
    {
        public PowRecursiveLinear() : this(1.0000001)
        {
        }

        public PowRecursiveLinear(double x) : base("Степень (рекурсивный x·xⁿ⁻¹, п. 11)", ComplexityClass.On, x)
        {
        }

        protected override double Pow(double x, int n, ref long steps)
        {
            if (n <= 0) return 1.0;
            steps++;
            return x * Pow(x, n - 1, ref steps);
        }
    }

    /// <summary>
    /// Рис. 2: рекурсивный бинарный RecPow с делением n div 2 -> O(log n)
    /// </summary>
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

    /// <summary>
    /// Рис. 3: быстрый алгоритм QuickPow (старшие биты вперёд) -> O(log n)
    /// </summary>
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

    /// <summary>
    /// Рис. 4: классический быстрый алгоритм QuickPow1 (младшие биты вперёд) -> O(log n)
    /// </summary>
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
}
