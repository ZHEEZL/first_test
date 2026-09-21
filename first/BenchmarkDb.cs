using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace first
{
    public sealed class HistoryExperimentInfo
    {
        public string ExperimentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Algorithms { get; set; } = new List<string>();
        public int TotalMeasurements { get; set; }

        public override string ToString()
        {
            string algos = string.Join(", ", Algorithms.Take(3));
            if (Algorithms.Count > 3) algos += $" +{Algorithms.Count - 3}";
            return $"{CreatedAt:yyyy-MM-dd HH:mm:ss} | {algos} ({TotalMeasurements} замеров)";
        }
    }

    public sealed class DbMeasurementRow
    {
        public string AlgorithmName { get; set; }
        public string ComplexityClass { get; set; }
        public int N { get; set; }
        public int RunIndex { get; set; }
        public double ElapsedSeconds { get; set; }
        public long? StepCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public static class BenchmarkDb
    {
        private static readonly string DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "benchmark.db");
        private static readonly string ConnectionString = $"Data Source={DbPath};";
        private static bool _initialized = false;
        private static readonly object LockObj = new object();

        public static void Initialize()
        {
            lock (LockObj)
            {
                if (_initialized) return;

                using (var conn = new SqliteConnection(ConnectionString))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS measurements (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            experiment_id TEXT NOT NULL,
                            algorithm_name TEXT NOT NULL,
                            complexity_class TEXT NOT NULL,
                            n INTEGER NOT NULL,
                            run_index INTEGER NOT NULL,
                            elapsed_seconds REAL NOT NULL,
                            step_count INTEGER,
                            created_at TEXT NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS idx_algo_n ON measurements(algorithm_name, n);
                        CREATE INDEX IF NOT EXISTS idx_exp_id ON measurements(experiment_id);";
                    cmd.ExecuteNonQuery();
                }

                _initialized = true;
            }
        }

        public static bool TryGetCachedRuns(string algoName, int n, int runsCount, out double[] times, out long[] steps)
        {
            Initialize();
            times = null;
            steps = null;

            lock (LockObj)
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT elapsed_seconds, step_count
                    FROM measurements
                    WHERE algorithm_name = @algo AND n = @n
                    ORDER BY id DESC
                    LIMIT @limit;";
                cmd.Parameters.AddWithValue("@algo", algoName);
                cmd.Parameters.AddWithValue("@n", n);
                cmd.Parameters.AddWithValue("@limit", runsCount);

                var listTimes = new List<double>();
                var listSteps = new List<long>();
                bool hasSteps = false;

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    listTimes.Add(reader.GetDouble(0));
                    if (!reader.IsDBNull(1))
                    {
                        listSteps.Add(reader.GetInt64(1));
                        hasSteps = true;
                    }
                }

                if (listTimes.Count >= runsCount)
                {
                    times = listTimes.ToArray();
                    if (hasSteps) steps = listSteps.ToArray();
                    return true;
                }

                return false;
            }
        }

        public static void SaveBatch(string experimentId, string algoName, string clsName, int n, double[] times, long[] stepCounts, DateTime createdAt)
        {
            Initialize();
            lock (LockObj)
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                for (int i = 0; i < times.Length; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO measurements (experiment_id, algorithm_name, complexity_class, n, run_index, elapsed_seconds, step_count, created_at)
                        VALUES (@expId, @algo, @cls, @n, @runIdx, @sec, @steps, @created);";
                    cmd.Parameters.AddWithValue("@expId", experimentId);
                    cmd.Parameters.AddWithValue("@algo", algoName);
                    cmd.Parameters.AddWithValue("@cls", clsName);
                    cmd.Parameters.AddWithValue("@n", n);
                    cmd.Parameters.AddWithValue("@runIdx", i + 1);
                    cmd.Parameters.AddWithValue("@sec", times[i]);
                    if (stepCounts != null && i < stepCounts.Length)
                        cmd.Parameters.AddWithValue("@steps", stepCounts[i]);
                    else
                        cmd.Parameters.AddWithValue("@steps", DBNull.Value);

                    cmd.Parameters.AddWithValue("@created", createdAt.ToString("o", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        public static List<HistoryExperimentInfo> GetHistoryExperiments()
        {
            Initialize();
            var list = new List<HistoryExperimentInfo>();

            lock (LockObj)
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT experiment_id, MIN(created_at) as first_created, COUNT(*) as cnt, GROUP_CONCAT(DISTINCT algorithm_name) as algos
                    FROM measurements
                    GROUP BY experiment_id
                    ORDER BY first_created DESC
                    LIMIT 100;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string expId = reader.GetString(0);
                    DateTime dt = DateTime.TryParse(reader.GetString(1), null, DateTimeStyles.RoundtripKind, out var parsedDt) ? parsedDt : DateTime.Now;
                    int cnt = reader.GetInt32(2);
                    string rawAlgos = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var algos = rawAlgos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

                    list.Add(new HistoryExperimentInfo
                    {
                        ExperimentId = expId,
                        CreatedAt = dt,
                        TotalMeasurements = cnt,
                        Algorithms = algos
                    });
                }
            }

            return list;
        }

        public static List<DbMeasurementRow> GetMeasurementsForExperiment(string experimentId)
        {
            Initialize();
            var list = new List<DbMeasurementRow>();

            lock (LockObj)
            {
                using var conn = new SqliteConnection(ConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT algorithm_name, complexity_class, n, run_index, elapsed_seconds, step_count, created_at
                    FROM measurements
                    WHERE experiment_id = @expId
                    ORDER BY algorithm_name, n, run_index;";
                cmd.Parameters.AddWithValue("@expId", experimentId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new DbMeasurementRow
                    {
                        AlgorithmName = reader.GetString(0),
                        ComplexityClass = reader.GetString(1),
                        N = reader.GetInt32(2),
                        RunIndex = reader.GetInt32(3),
                        ElapsedSeconds = reader.GetDouble(4),
                        StepCount = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
                        CreatedAt = DateTime.TryParse(reader.GetString(6), null, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.Now
                    });
                }
            }

            return list;
        }
    }
}
