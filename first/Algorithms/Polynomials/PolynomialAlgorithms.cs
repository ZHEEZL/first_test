namespace first
{
    /// <summary>
    /// Наивное вычисление P(x): каждый член со своим возведением в степень -> O(n^2)
    /// </summary>
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

    /// <summary>
    /// Схема Горнера: P(x) = v1 + x·(v2 + x·(v3 + ...)) -> O(n)
    /// </summary>
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
}
