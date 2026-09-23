using System;
using System.Collections.Generic;

namespace first
{
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

    /// <summary>
    /// Гибридный Timsort: естественные прогоны + бинарная вставка до minrun + слияния по инвариантам стека
    /// </summary>
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

    /// <summary>
    /// Древовидная сортировка через бинарное дерево поиска (Binary Search Tree, BST):
    /// • В среднем (на случайных данных): O(n·log n)
    /// • В худшем случае (на отсортированных данных): O(n^2) из-за вырождения дерева в связный список («бамбук»).
    /// </summary>
    public class TreeSortAlg : SortBase
    {
        protected struct BstNode
        {
            public double Value;
            public int Left;
            public int Right;
        }

        protected readonly bool _worstCase;
        protected BstNode[] _nodes = Array.Empty<BstNode>();
        protected int[] _stack = Array.Empty<int>();

        public TreeSortAlg(string name = "Древовидная сортировка (Tree sort через BST)", ComplexityClass cls = null, bool worstCase = false)
            : base(name, cls ?? ComplexityClass.Onlogn)
        {
            _worstCase = worstCase;
        }

        public override void Prepare(int n, ExperimentContext ctx)
        {
            base.Prepare(n, ctx);

            if (_worstCase)
            {
                // Для демонстрации худшего случая входной массив предварительно сортируется
                // вне замера времени (в фазе Prepare).
                // При этом каждый последующий элемент больше предыдущего, дерево вырождается
                // в линейный список глубины n, вызывая O(n^2) сравнений.
                Array.Sort(ctx.Work, 0, n);
            }

            if (_nodes.Length < n)
            {
                _nodes = new BstNode[n];
                _stack = new int[n];
            }
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            if (n <= 1) return;

            var work = ctx.Work;
            var nodes = _nodes;

            // Корень дерева BST — первый элемент
            nodes[0].Value = work[0];
            nodes[0].Left = -1;
            nodes[0].Right = -1;

            // Вставка n-1 элементов в бинарное дерево поиска
            for (int i = 1; i < n; i++)
            {
                double val = work[i];
                nodes[i].Value = val;
                nodes[i].Left = -1;
                nodes[i].Right = -1;

                int curr = 0;
                while (true)
                {
                    if (val < nodes[curr].Value)
                    {
                        int left = nodes[curr].Left;
                        if (left == -1)
                        {
                            nodes[curr].Left = i;
                            break;
                        }
                        curr = left;
                    }
                    else
                    {
                        int right = nodes[curr].Right;
                        if (right == -1)
                        {
                            nodes[curr].Right = i;
                            break;
                        }
                        curr = right;
                    }
                }
            }

            // Итеративный симметричный обход (In-order: Left -> Node -> Right).
            // Извлекает элементы в отсортированном порядке без рекурсии и риска переполнения стека.
            int top = -1;
            int currNode = 0;
            int outIdx = 0;
            var stack = _stack;

            while (currNode != -1 || top >= 0)
            {
                while (currNode != -1)
                {
                    stack[++top] = currNode;
                    currNode = nodes[currNode].Left;
                }

                currNode = stack[top--];
                work[outIdx++] = nodes[currNode].Value;
                currNode = nodes[currNode].Right;
            }

            Sink.Add(work[0] + work[n >> 1] + work[n - 1]);
        }
    }

    /// <summary>
    /// Древовидная сортировка — худший случай (деградация BST на отсортированном входе): O(n^2).
    /// </summary>
    public sealed class TreeSortWorstCaseAlg : TreeSortAlg
    {
        public TreeSortWorstCaseAlg()
            : base("Древовидная сортировка (худший случай BST)", ComplexityClass.On2, worstCase: true)
        {
        }
    }

    /// <summary>
    /// Поразрядная сортировка (Radix Sort, LSD): O(n) линейная некомпаративная сортировка
    /// Поддерживает вещественные числа (IEEE-754 64-bit double) через монотонное битовое отображение.
    /// </summary>
    public sealed class RadixSortAlg : SortBase
    {
        private ulong[] _keys = Array.Empty<ulong>();
        private ulong[] _auxKeys = Array.Empty<ulong>();
        private double[] _auxVal = Array.Empty<double>();
        private readonly int[] _count = new int[256];
        private readonly int[] _pref = new int[256];

        public RadixSortAlg() : base("Поразрядная сортировка (Radix sort)", ComplexityClass.On)
        {
        }

        public override void Prepare(int n, ExperimentContext ctx)
        {
            base.Prepare(n, ctx);
            if (_keys.Length < n)
            {
                _keys = new ulong[n];
                _auxKeys = new ulong[n];
                _auxVal = new double[n];
            }
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            if (n <= 1) return;

            var srcKeys = _keys;
            var dstKeys = _auxKeys;
            var srcVal = ctx.Work;
            var dstVal = _auxVal;

            // 1. Преобразуем double в монотонные ulong ключи с сохранением порядка IEEE-754
            for (int i = 0; i < n; i++)
            {
                srcKeys[i] = FloatFlip(srcVal[i]);
            }

            // 2. LSD Radix sort: 8 проходов по 8 бит (256 корзин)
            for (int pass = 0; pass < 8; pass++)
            {
                int shift = pass * 8;
                Array.Clear(_count, 0, 256);

                // Подсчет частот
                for (int i = 0; i < n; i++)
                {
                    int b = (int)((srcKeys[i] >> shift) & 0xFF);
                    _count[b]++;
                }

                // Префиксные суммы для позиций корзин
                _pref[0] = 0;
                for (int b = 1; b < 256; b++)
                {
                    _pref[b] = _pref[b - 1] + _count[b - 1];
                }

                // Распределение по корзинам
                for (int i = 0; i < n; i++)
                {
                    int b = (int)((srcKeys[i] >> shift) & 0xFF);
                    int pos = _pref[b]++;
                    dstKeys[pos] = srcKeys[i];
                    dstVal[pos] = srcVal[i];
                }

                // Смена местами массивов источников и приемников
                var tmpK = srcKeys; srcKeys = dstKeys; dstKeys = tmpK;
                var tmpV = srcVal; srcVal = dstVal; dstVal = tmpV;
            }

            // После 8 проходов srcVal совпадает с ctx.Work
            Sink.Add(ctx.Work[n >> 1]);
        }

        /// <summary>
        /// Отображение 64-битного IEEE-754 double в монотонный ulong ключ
        /// </summary>
        private static ulong FloatFlip(double d)
        {
            ulong f = (ulong)BitConverter.DoubleToInt64Bits(d);
            ulong mask = ((long)f >> 63) == 0 ? 0x8000000000000000UL : 0xFFFFFFFFFFFFFFFFUL;
            return f ^ mask;
        }
    }
}
