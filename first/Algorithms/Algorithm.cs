namespace first
{
    /// <summary>
    /// Базовый класс алгоритма.
    /// Входные параметры: n + общий контекст. Стандартные: P (от класса) и константы алгоритма.
    /// </summary>
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

        /// <summary>
        /// Подготовка (свежая копия «куска») — время НЕ замеряется.
        /// </summary>
        public virtual void Prepare(int n, ExperimentContext ctx)
        {
            ctx.EnsureCapacity(n);
        }

        /// <summary>
        /// Замеряемая работа с первыми n элементами.
        /// </summary>
        public abstract void Work(int n, ExperimentContext ctx);

        public sealed override string ToString() => $"{Name}  [{Cls.Name}]";
    }
}
