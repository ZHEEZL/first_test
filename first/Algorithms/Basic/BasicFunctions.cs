namespace first
{
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
}
