namespace first
{
    /// <summary>
    /// Предотвращает оптимизацию мертвого кода (Dead Code Elimination) в JIT-компиляторе.
    /// </summary>
    public static class Sink
    {
        public static double Value; // «копилка» результатов
        public static void Add(double x) => Value += x; // не даёт JIT выбросить «пустую» работу
    }
}
