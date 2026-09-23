using System;

namespace first
{
    /// <summary>
    /// Элемент конфигурации алгоритма для отображения в таблице настройки параметров.
    /// </summary>
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

        public static System.Collections.Generic.List<AlgoConfigItem> CreateDefaultConfigs() => new System.Collections.Generic.List<AlgoConfigItem>
        {
            new AlgoConfigItem { Name = "f(v) = 1 (константа)", ClassName = "O(1)", DefaultMaxN = 1000, MaxN = 1000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ConstantFunction() },
            new AlgoConfigItem { Name = "Сумма элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new SumFunction() },
            new AlgoConfigItem { Name = "Произведение элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ProductFunction() },
            new AlgoConfigItem { Name = "Полином (наивно)", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialNaive() },
            new AlgoConfigItem { Name = "Полином (Горнер)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialHorner() },
            new AlgoConfigItem { Name = "Сортировка пузырьком", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new BubbleSortAlg() },
            new AlgoConfigItem { Name = "Быстрая сортировка (Quick sort)", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new QuickSortAlg() },
            new AlgoConfigItem { Name = "Гибридный Timsort", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new TimsortAlg() },
            new AlgoConfigItem { Name = "Древовидная сортировка (Tree sort через BST)", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new TreeSortAlg() },
            new AlgoConfigItem { Name = "Древовидная сортировка (худший случай BST)", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new TreeSortWorstCaseAlg() },
            new AlgoConfigItem { Name = "Поразрядная сортировка (Radix sort)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new RadixSortAlg() },
            new AlgoConfigItem { Name = "Степень (простой, рис. 1)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowNaive() },
            new AlgoConfigItem { Name = "Степень (рекурсивный x·xⁿ⁻¹, п. 11)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowRecursiveLinear() },
            new AlgoConfigItem { Name = "Степень (рекурсивный RecPow, рис. 2)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowRecursive() },
            new AlgoConfigItem { Name = "Степень (быстрый QuickPow, рис. 3)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick() },
            new AlgoConfigItem { Name = "Степень (классический быстрый QuickPow1, рис. 4)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick1() },
            new AlgoConfigItem { Name = "Матричное умножение A×B", ClassName = "O(M²·N)", DefaultMaxN = 100, MaxN = 100, DefaultStep = 10, Step = 10, DefaultRuns = 3, Runs = 3, Factory = () => new MatrixMultiply(maxDim: 200) },
        };
    }
}
