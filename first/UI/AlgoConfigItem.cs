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
    }
}
