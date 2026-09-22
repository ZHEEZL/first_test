using System;

namespace first
{
    // ---------- II. Матричное умножение (прямоугольные матрицы N×M×P) ----------
    public sealed class MatrixMultiply : Algorithm
    {
        private int _maxDim;
        private double[] _a, _b; // «большие» матрицы
        private double[] _pa, _pb, _pc; // префиксы и результат
        private readonly Random _rnd = new Random(777);

        public MatrixMultiply(int maxDim = 200) : base("Матричное умножение A×B", ComplexityClass.Om2n)
        {
            EnsureCapacity(maxDim, maxDim, maxDim);
        }

        public void EnsureCapacity(int n, int m, int p)
        {
            int max = Math.Max(n, Math.Max(m, p));
            if (max <= _maxDim && _a != null) return;
            _maxDim = Math.Max(max, _maxDim);
            int len = _maxDim * _maxDim;
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
            PrepareMN(n, n);
        }

        public void PrepareMN(int m, int n)
        {
            EnsureCapacity(m, n, m);
            for (int i = 0; i < m; i++)
                Array.Copy(_a, i * _maxDim, _pa, i * n, n);
            for (int i = 0; i < n; i++)
                Array.Copy(_b, i * _maxDim, _pb, i * m, m);
            Array.Clear(_pc, 0, m * m);
        }

        public void PrepareRectangular(int n, int m, int p)
        {
            EnsureCapacity(n, m, p);
            for (int i = 0; i < n; i++)
                Array.Copy(_a, i * _maxDim, _pa, i * m, m);
            for (int i = 0; i < m; i++)
                Array.Copy(_b, i * _maxDim, _pb, i * p, p);
            Array.Clear(_pc, 0, n * p);
        }

        public override void Work(int n, ExperimentContext ctx)
        {
            MultiplyMN(n, n);
        }

        public void MultiplyMN(int m, int n)
        {
            var a = _pa;
            var b = _pb;
            var c = _pc;
            for (int i = 0; i < m; i++)
            {
                int ioA = i * n;
                int ioC = i * m;
                for (int k = 0; k < n; k++)
                {
                    double aik = a[ioA + k];
                    int koB = k * m;
                    for (int j = 0; j < m; j++)
                    {
                        c[ioC + j] += aik * b[koB + j];
                    }
                }
            }

            Sink.Add(c[0] + c[m * m - 1]);
        }

        public void MultiplyRectangular(int n, int m, int p)
        {
            var a = _pa;
            var b = _pb;
            var c = _pc;
            for (int i = 0; i < n; i++)
            {
                int ioA = i * m;
                int ioC = i * p;
                for (int k = 0; k < m; k++)
                {
                    double aik = a[ioA + k];
                    int koB = k * p;
                    for (int j = 0; j < p; j++)
                    {
                        c[ioC + j] += aik * b[koB + j];
                    }
                }
            }

            Sink.Add(c[0] + c[n * p - 1]);
        }
    }
}
