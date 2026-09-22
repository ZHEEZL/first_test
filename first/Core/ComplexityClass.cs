using System;

namespace first
{
    /// <summary>
    /// Класс сложности: теоретическая модель f(n) + стандартные параметры измерений.
    /// </summary>
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
            new MeasParams { MaxN = 200, Step = 10, Runs = 3, SmoothWindow = 3 });

        public static readonly ComplexityClass Om2n = new ComplexityClass("O(M²·N)",
            n => (double)n * n * n,
            new MeasParams { MaxN = 100, Step = 10, Runs = 3, SmoothWindow = 3 });

        public static readonly ComplexityClass On1_5 = new ComplexityClass("O(n^1.5)",
            n => Math.Pow(n, 1.5), new MeasParams { MaxN = 2000, Step = 50, Runs = 5, SmoothWindow = 3 });

        public static readonly ComplexityClass Ofactorial = new ComplexityClass("O(n·n!)",
            n => n * Fact((int)Math.Min(n, 20)),
            new MeasParams { MaxN = 10, Step = 1, Runs = 15, SmoothWindow = 1 });

        private static double Fact(int k) { double r = 1; for (int i = 2; i <= k; i++) r *= i; return r; }
    }
}
