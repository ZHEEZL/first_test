using System.Collections.Generic;

namespace first
{
    public sealed class Series
    {
        public Algorithm Algo;
        public readonly List<double> N = new List<double>();
        public readonly List<double> T = new List<double>(); // время (с) или количество шагов
        public readonly List<double> TFit = new List<double>(); // теория C·f(n)
        public readonly List<double> StdDev = new List<double>(); // стандартное отклонение замеров на каждом n
        public double C;
        public double MSE;
        public bool MeasuresSteps;
        public int CachedPoints;

        // 3D данные для матрицы T(M, N):
        public double[,] MatrixTimes;
        public int MatrixStepM;
        public int MatrixStepN;
        public int MatrixMaxM;
        public int MatrixMaxN;
    }
}
