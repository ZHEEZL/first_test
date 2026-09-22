using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace first
{
    public sealed partial class MainForm : Form
    {
        private readonly List<AlgoConfigItem> _configs;
        private CancellationTokenSource _cts;
        private List<Series> _lastResults = new List<Series>();
        private List<KeyValuePair<string, ScottPlot.Plot>> _lastPlots = new List<KeyValuePair<string, ScottPlot.Plot>>();
        private double[,] _lastMatrix3DData;
        private int _lastMatrixStepM, _lastMatrixStepN;

        private Dictionary<string, Series> _currentBaselineSeries = new Dictionary<string, Series>();
        private string _selectedBaselineExpId;
        private bool _isUpdatingCombo = false;
        private readonly Dictionary<string, string> _algoBaselineExpId = new Dictionary<string, string>();

        private sealed class BaselineComboItem
        {
            public string ExperimentId { get; set; }
            public string Title { get; set; }
            public override string ToString() => Title;
        }

        public MainForm()
        {
            _configs = CreateDefaultConfigs();
            InitializeComponent();
        }

        public static List<AlgoConfigItem> CreateDefaultConfigs() => new List<AlgoConfigItem>
        {
            new AlgoConfigItem { Name = "f(v) = 1 (константа)", ClassName = "O(1)", DefaultMaxN = 1000, MaxN = 1000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ConstantFunction() },
            new AlgoConfigItem { Name = "Сумма элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new SumFunction() },
            new AlgoConfigItem { Name = "Произведение элементов", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new ProductFunction() },
            new AlgoConfigItem { Name = "Полином (наивно)", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialNaive() },
            new AlgoConfigItem { Name = "Полином (Горнер)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PolynomialHorner() },
            new AlgoConfigItem { Name = "Сортировка пузырьком", ClassName = "O(n^2)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new BubbleSortAlg() },
            new AlgoConfigItem { Name = "Быстрая сортировка (Quick sort)", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new QuickSortAlg() },
            new AlgoConfigItem { Name = "Гибридный Timsort", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new TimsortAlg() },
            new AlgoConfigItem { Name = "Сортировка по Z-кривой Мортона", ClassName = "O(n·log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new MortonZCurveSortAlg() },
            new AlgoConfigItem { Name = "Поразрядная сортировка (Radix sort)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new RadixSortAlg() },
            new AlgoConfigItem { Name = "Степень (простой, рис. 1)", ClassName = "O(n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowNaive() },
            new AlgoConfigItem { Name = "Степень (рекурсивный RecPow, рис. 2)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowRecursive() },
            new AlgoConfigItem { Name = "Степень (быстрый QuickPow, рис. 3)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick() },
            new AlgoConfigItem { Name = "Степень (классический быстрый QuickPow1, рис. 4)", ClassName = "O(log n)", DefaultMaxN = 2000, MaxN = 2000, DefaultStep = 50, Step = 50, DefaultRuns = 5, Runs = 5, Factory = () => new PowQuick1() },
            new AlgoConfigItem { Name = "Матричное умножение A×B", ClassName = "O(M²·N)", DefaultMaxN = 100, MaxN = 100, DefaultStep = 10, Step = 10, DefaultRuns = 3, Runs = 3, Factory = () => new MatrixMultiply(maxDim: 200) },
        };

        private void SelectAlgoTab(string algoName)
        {
            if (string.IsNullOrEmpty(algoName)) return;
            if (algoName.Contains("Матричное"))
            {
                if (_tabsMain.TabPages.Contains(_tabMatrix3D))
                {
                    _tabsMain.SelectedTab = _tabMatrix3D;
                }
                return;
            }
            foreach (TabPage page in _tabsMain.TabPages)
            {
                if (page.Text.IndexOf(algoName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _tabsMain.SelectedTab = page;
                    return;
                }
            }
        }

        private void ClearDynamicTabs()
        {
            if (_tabsMain.TabPages.Contains(_tabMatrix3D))
            {
                _tabsMain.TabPages.Remove(_tabMatrix3D);
            }
            var toRemove = _tabsMain.TabPages.Cast<TabPage>()
                .Where(p => p.Text.StartsWith("📈 "))
                .ToList();
            foreach (var p in toRemove)
            {
                _tabsMain.TabPages.Remove(p);
            }
        }

        private void OnTabChanged()
        {
            var tab = _tabsMain.SelectedTab;
            if (tab == null || !tab.Text.StartsWith("📈 "))
            {
                // Не открыт график алгоритма (Сводка, Журнал или Матрица 3D)
                _pnlHistoryCompare.Visible = false;
                return;
            }

            if (tab.Text.Contains("Сравнение"))
            {
                // На сводных сравнительных графиках скрываем панель сравнения с историей
                _pnlHistoryCompare.Visible = false;
                return;
            }

            // Открыт график конкретного алгоритма — показываем панель сравнения
            _pnlHistoryCompare.Visible = true;
            string algoName = (tab.Tag as string) ?? tab.Text.Substring(tab.Text.IndexOf(' ') + 1).Trim();
            UpdateBaselineComboForAlgo(algoName);

            // Гарантируем, что выбранный график всегда отображает актуальные настройки погрешностей и истории
            EnsureTabPlotUpdated(tab, algoName);
        }

        private void EnsureTabPlotUpdated(TabPage tab, string algoName)
        {
            if (_lastResults == null || _lastResults.Count == 0) return;
            var series = _lastResults.FirstOrDefault(s => s.Algo.Name == algoName);
            if (series == null) return;

            var plotKv = _lastPlots.FirstOrDefault(kv => kv.Key == algoName);
            var fp = tab.Controls.OfType<ScottPlot.FormsPlot>().FirstOrDefault();
            if (fp != null)
            {
                if (plotKv.Value != null)
                {
                    fp.Reset(plotKv.Value);
                }
                else
                {
                    var single = Plotter.Build(new List<Series> { series }, _currentBaselineSeries, _chkShowHistory.Checked, _chkShowErrorBars.Checked);
                    if (single.Count > 0) fp.Reset(single[0].Value);
                }
                fp.Refresh();
            }
        }

        private void UpdateBaselineComboForAlgo(string algoName)
        {
            if (string.IsNullOrEmpty(algoName)) return;

            var allHistory = BenchmarkDb.GetHistoryExperiments();
            // Сравниваем только с теми запусками, где реально замерялся данный открытый алгоритм!
            var relevantHistory = allHistory
                .Where(h => h.Algorithms.Any(a => string.Equals(a, algoName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _isUpdatingCombo = true;
            _cmbBaselineRun.BeginUpdate();
            try
            {
                _cmbBaselineRun.Items.Clear();
                _cmbBaselineRun.Items.Add(new BaselineComboItem { ExperimentId = null, Title = "[ Не сравнивать ]" });

                if (relevantHistory.Count == 0)
                {
                    _cmbBaselineRun.SelectedIndex = 0;
                    _cmbBaselineRun.Enabled = false;
                    return;
                }

                _cmbBaselineRun.Enabled = true;
                BaselineComboItem toSelect = null;
                string preferredExpId = _algoBaselineExpId.TryGetValue(algoName, out var savedId) ? savedId : _selectedBaselineExpId;

                for (int i = 0; i < relevantHistory.Count; i++)
                {
                    var h = relevantHistory[i];
                    string label;
                    if (!string.IsNullOrWhiteSpace(h.Note))
                        label = (h.IsBaseline ? "⭐ " : "") + $"{h.Note} ({h.CreatedAt:dd.MM HH:mm})";
                    else if (h.IsBaseline)
                        label = $"⭐ [Эталон] {h.CreatedAt:dd.MM HH:mm}";
                    else if (i == 0)
                        label = $"Предыдущий ({h.CreatedAt:dd.MM HH:mm})";
                    else
                        label = $"Запуск {h.CreatedAt:dd.MM HH:mm}";

                    var item = new BaselineComboItem { ExperimentId = h.ExperimentId, Title = label };
                    _cmbBaselineRun.Items.Add(item);

                    if (preferredExpId != null && h.ExperimentId == preferredExpId)
                        toSelect = item;
                    else if (toSelect == null && preferredExpId == null && h.IsBaseline)
                        toSelect = item;
                }

                if (toSelect != null)
                {
                    _cmbBaselineRun.SelectedItem = toSelect;
                }
                else if (preferredExpId == null && _cmbBaselineRun.Items.Count > 1)
                {
                    _cmbBaselineRun.SelectedIndex = 1; // по умолчанию эталон или предыдущий
                }
                else
                {
                    _cmbBaselineRun.SelectedIndex = 0;
                }
            }
            finally
            {
                _cmbBaselineRun.EndUpdate();
                _isUpdatingCombo = false;
            }
        }

        private void OnBaselineChanged()
        {
            if (_isUpdatingCombo) return;

            var tab = _tabsMain.SelectedTab;
            if (tab == null || !tab.Text.StartsWith("📈 ")) return;

            string algoName = (tab.Tag as string) ?? tab.Text.Substring(tab.Text.IndexOf(' ') + 1).Trim();
            string expId = (_cmbBaselineRun.SelectedItem as BaselineComboItem)?.ExperimentId;

            _algoBaselineExpId[algoName] = expId;
            if (!string.IsNullOrEmpty(expId))
                _selectedBaselineExpId = expId;

            if (!string.IsNullOrEmpty(expId))
            {
                var bDict = BenchmarkDb.GetBaselineSeries(expId);
                if (bDict.TryGetValue(algoName, out var bSeries))
                    _currentBaselineSeries[algoName] = bSeries;
                else
                    _currentBaselineSeries.Remove(algoName);
            }
            else
            {
                _currentBaselineSeries.Remove(algoName);
            }

            UpdateSinglePlotAndSummary(algoName, tab);
        }

        private void OnHistoryToggleChanged()
        {
            if (_lastResults == null || _lastResults.Count == 0) return;

            // Перестраиваем все графики с учетом актуального состояния чекбоксов погрешностей и истории
            _lastPlots = Plotter.Build(
                _lastResults,
                _currentBaselineSeries,
                _chkShowHistory.Checked,
                _chkShowErrorBars.Checked
            );

            // Мгновенно обновляем FormsPlot на КАЖДОЙ созданной вкладке
            foreach (TabPage page in _tabsMain.TabPages)
            {
                string name = (page.Tag as string) ?? (page.Text.StartsWith("📈 ") ? page.Text.Substring(page.Text.IndexOf(' ') + 1).Trim() : null);
                if (string.IsNullOrEmpty(name)) continue;

                var plotKv = _lastPlots.FirstOrDefault(kv => kv.Key == name);
                if (plotKv.Value != null)
                {
                    var fp = page.Controls.OfType<ScottPlot.FormsPlot>().FirstOrDefault();
                    if (fp != null)
                    {
                        fp.Reset(plotKv.Value);
                        fp.Refresh();
                    }
                }
            }

            // Обновляем сводную таблицу
            foreach (var s in _lastResults)
            {
                UpdateSummaryRowForAlgo(s);
            }
        }

        private void UpdateSinglePlotAndSummary(string algoName, TabPage tab)
        {
            var series = _lastResults.FirstOrDefault(s => s.Algo.Name == algoName);
            if (series == null) return;

            var singlePlots = Plotter.Build(
                new List<Series> { series },
                _currentBaselineSeries,
                _chkShowHistory.Checked,
                _chkShowErrorBars.Checked
            );

            if (singlePlots.Count > 0)
            {
                var newPlot = singlePlots[0].Value;
                var fp = tab.Controls.OfType<ScottPlot.FormsPlot>().FirstOrDefault();
                if (fp != null)
                {
                    fp.Reset(newPlot);
                    fp.Refresh();
                }

                int pIdx = _lastPlots.FindIndex(kv => kv.Key == algoName);
                if (pIdx >= 0)
                    _lastPlots[pIdx] = singlePlots[0];
                else
                    _lastPlots.Add(singlePlots[0]);
            }

            UpdateSummaryRowForAlgo(series);
        }

        private void UpdateSummaryRowForAlgo(Series s)
        {
            if (s == null || s.N.Count == 0) return;
            int last = s.N.Count - 1;

            string baseStr = "—";
            string diffStr = "—";

            if (_chkShowHistory.Checked && _currentBaselineSeries != null &&
                _currentBaselineSeries.TryGetValue(s.Algo.Name, out var bSeries) && bSeries.T.Count > 0)
            {
                double bLast = bSeries.T.Last();
                baseStr = s.MeasuresSteps ? $"{bLast:0}" : Bench.FormatTime(bLast);

                if (bLast > 1e-15)
                {
                    double diffPct = ((s.T[last] - bLast) / bLast) * 100.0;
                    if (s.MeasuresSteps)
                        diffStr = (Math.Abs(diffPct) < 1e-6) ? "⚪ 0.0% (совпадает)" : $"⚪ {diffPct:+0.0;-0.0;0.0}%";
                    else if (diffPct <= -3.0)
                        diffStr = $"🟢 ▼ {Math.Abs(diffPct):F1}%";
                    else if (diffPct >= 3.0)
                        diffStr = $"🔴 ▲ +{diffPct:F1}%";
                    else
                        diffStr = $"⚪ {diffPct:+0.0;-0.0;0.0}%";
                }
            }

            foreach (DataGridViewRow row in _gridSummary.Rows)
            {
                if (Convert.ToString(row.Cells[0].Value) == s.Algo.Name)
                {
                    row.Cells[10].Value = baseStr;
                    row.Cells[11].Value = diffStr;
                    break;
                }
            }
        }

        private void OpenHistoryManager()
        {
            using var dlg = new HistoryManagerForm();
            dlg.OnDataChanged += () =>
            {
                var tab = _tabsMain.SelectedTab;
                if (tab != null && tab.Text.StartsWith("📈 "))
                {
                    string algoName = tab.Text.Substring(2).Trim();
                    UpdateBaselineComboForAlgo(algoName);
                    OnBaselineChanged();
                }
            };
            dlg.ShowDialog(this);
            var currentTab = _tabsMain.SelectedTab;
            if (currentTab != null && currentTab.Text.StartsWith("📈 "))
            {
                string algoName = currentTab.Text.Substring(2).Trim();
                UpdateBaselineComboForAlgo(algoName);
                OnBaselineChanged();
            }
        }

        private void RebuildPlotsAndSummary()
        {
            if (_lastResults == null || _lastResults.Count == 0) return;

            // 1. Обновление сводной таблицы
            _gridSummary.Rows.Clear();
            foreach (var s in _lastResults)
            {
                if (s.N.Count == 0) continue;
                int last = s.N.Count - 1;
                string metricStr = s.MeasuresSteps ? "Шаги" : "Время";
                string factStr = s.MeasuresSteps
                    ? s.T[last].ToString("0", CultureInfo.InvariantCulture)
                    : Bench.FormatTime(s.T[last]);
                string theoryStr = s.MeasuresSteps
                    ? s.TFit[last].ToString("0.0", CultureInfo.InvariantCulture)
                    : Bench.FormatTime(s.TFit[last]);

                string baseStr = "—";
                string diffStr = "—";

                if (_chkShowHistory.Checked && _currentBaselineSeries != null &&
                    _currentBaselineSeries.TryGetValue(s.Algo.Name, out var bSeries) && bSeries.T.Count > 0)
                {
                    double bLast = bSeries.T.Last();
                    baseStr = s.MeasuresSteps ? $"{bLast:0}" : Bench.FormatTime(bLast);

                    if (bLast > 1e-15)
                    {
                        double diffPct = ((s.T[last] - bLast) / bLast) * 100.0;
                        if (s.MeasuresSteps)
                            diffStr = (Math.Abs(diffPct) < 1e-6) ? "⚪ 0.0% (совпадает)" : $"⚪ {diffPct:+0.0;-0.0;0.0}%";
                        else if (diffPct <= -3.0)
                            diffStr = $"🟢 ▼ {Math.Abs(diffPct):F1}%";
                        else if (diffPct >= 3.0)
                            diffStr = $"🔴 ▲ +{diffPct:F1}%";
                        else
                            diffStr = $"⚪ {diffPct:+0.0;-0.0;0.0}%";
                    }
                }

                _gridSummary.Rows.Add(
                    s.Algo.Name,
                    s.Algo.Cls.Name,
                    s.Algo.P.MaxN,
                    s.Algo.P.Step,
                    s.Algo.P.Runs,
                    s.N.Count,
                    metricStr,
                    s.MSE.ToString("0.####E+00", CultureInfo.InvariantCulture),
                    s.C.ToString("0.####E+00", CultureInfo.InvariantCulture),
                    factStr,
                    baseStr,
                    diffStr,
                    theoryStr,
                    $"{s.CachedPoints} / {s.N.Count}"
                );
            }

            // 2. Обновление графиков ScottPlot
            _lastPlots = Plotter.Build(
                _lastResults,
                _currentBaselineSeries,
                _chkShowHistory.Checked,
                _chkShowErrorBars.Checked
            );

            // 3. Пересоздание динамических вкладок
            ClearDynamicTabs();

            // Вкладка матрицы 3D добавляется только при наличии данных расчёта!
            if (_lastMatrix3DData != null)
            {
                _tabsMain.TabPages.Add(_tabMatrix3D);
            }

            foreach (var kv in _lastPlots)
            {
                if (kv.Key.Contains("Матричное"))
                {
                    continue;
                }
                var page = new TabPage($"📈 {kv.Key}");
                page.Tag = kv.Key;
                page.BackColor = Color.FromArgb(24, 25, 30);
                var fp = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
                fp.Reset(kv.Value);
                fp.Refresh();
                page.Controls.Add(fp);
                _tabsMain.TabPages.Add(page);
            }
        }

        private void UpdateMatrix3DPlot()
        {
            if (_lastMatrix3DData == null) return;
            int rowsM = _lastMatrix3DData.GetLength(0);
            int colsN = _lastMatrix3DData.GetLength(1);

            double maxSec = 0;
            for (int r = 0; r < rowsM; r++)
                for (int c = 0; c < colsN; c++)
                    if (_lastMatrix3DData[r, c] > maxSec) maxSec = _lastMatrix3DData[r, c];

            double scale = 1e3;
            string unit = "мс";
            if (maxSec < 1e-3) { scale = 1e6; unit = "мкс"; }
            else if (maxSec >= 1.0) { scale = 1.0; unit = "с"; }

            double[,] scaled = new double[rowsM, colsN];
            for (int r = 0; r < rowsM; r++)
                for (int c = 0; c < colsN; c++)
                    scaled[r, c] = _lastMatrix3DData[r, c] * scale;

            if (_viewport3D != null)
            {
                _viewport3D.Mode = _cmbMatrixPlotMode.SelectedIndex switch
                {
                    1 => ViewportMode.WireframeOnly,
                    2 => ViewportMode.PointCloud,
                    3 => ViewportMode.Heatmap2D,
                    _ => ViewportMode.ShadedWireframe
                };
                _viewport3D.SetData(scaled, _lastMatrixStepM, _lastMatrixStepN, unit);
            }
        }

        private void SaveMatrix3DDialog()
        {
            if (_viewport3D == null || _lastMatrix3DData == null)
            {
                MessageBox.Show(this, "Сначала выполните расчёт 3D-матрицы.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", FileName = "14_Матричное_умножение_3D.png" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    using (var bmp = _viewport3D.RenderToBitmap(1200, 750))
                    {
                        bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    MessageBox.Show(this, "График сохранён в " + sfd.FileName, "Экспорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private async Task StartExperimentAsync()
        {
            _gridAlgos.EndEdit();
            var selected = new List<(AlgoConfigItem cfg, int maxN, int step, int runs)>();
            for (int i = 0; i < _gridAlgos.Rows.Count; i++)
            {
                var row = _gridAlgos.Rows[i];
                bool isEn = Convert.ToBoolean(row.Cells[0].Value);
                if (!isEn) continue;

                if (!int.TryParse(Convert.ToString(row.Cells[3].Value), out int maxN) || maxN <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Max N для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(Convert.ToString(row.Cells[4].Value), out int step) || step <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Шага для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!int.TryParse(Convert.ToString(row.Cells[5].Value), out int runs) || runs <= 0)
                {
                    MessageBox.Show(this, $"Некорректное значение Повторов для строки {i + 1} ({_configs[i].Name}). Число должно быть целым положительным.",
                        "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                selected.Add((_configs[i], maxN, step, runs));
            }

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Пожалуйста, выберите хотя бы один алгоритм для проведения замеров.", "Нет выбранных алгоритмов", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string previousLatestExpId = BenchmarkDb.GetHistoryExperiments().FirstOrDefault()?.ExperimentId;

            _btnRun.Enabled = false;
            _btnRun.BackColor = Color.FromArgb(30, 58, 138);
            _btnCancel.Enabled = true;
            _btnCancel.BackColor = Color.FromArgb(220, 38, 38);
            _btnCancel.ForeColor = Color.White;
            _progressBar.Value = 0;
            _txtLog.Clear();
            _gridSummary.Rows.Clear();
            _lastMatrix3DData = null;
            _btnSaveMatrix3D.Enabled = false;
            ClearDynamicTabs();
            OnTabChanged();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            bool doWarmup = _chkWarmup.Checked;
            bool useCache = _chkUseCache.Checked;
            bool forceRecalc = _chkForceRecalc.Checked;
            int seed = (int)_numSeed.Value;

            var results = new List<Series>();
            var swTotal = Stopwatch.StartNew();
            string experimentId = Guid.NewGuid().ToString("N");

            try
            {
                await Task.Run(() =>
                {
                    int maxNVec = Math.Max(2000, selected.Max(x => x.maxN));
                    var ctx = new ExperimentContext(maxNVec, seed);

                    if (doWarmup)
                    {
                        SetStatus("Выполняется глобальный прогрев JIT и CPU...");
                        Bench.GlobalWarmup(ctx, AppendLog);
                    }

                    for (int i = 0; i < selected.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = selected[i];
                        var algo = item.cfg.Factory();
                        algo.P.MaxN = item.maxN;
                        algo.P.Step = item.step;
                        algo.P.StartN = Math.Min(item.step, item.maxN);
                        algo.P.Runs = item.runs;

                        SetStatus($"[{i + 1}/{selected.Count}] Выполняется: {algo.Name} (MaxN = {item.maxN}, Step = {item.step}, Runs = {item.runs})...");
                        AppendLog($"\n=== {algo.Name}  [{algo.Cls.Name}] (MaxN = {item.maxN}, Step = {item.step}, Runs = {item.runs}) ===");

                        var s = Bench.Run(algo, ctx, experimentId, useCache, forceRecalc, AppendLog, (pt, totalPts) =>
                        {
                            int pct = (int)(((i + (double)pt / totalPts) / selected.Count) * 100);
                            SetProgress(pct);
                        }, token);

                        results.Add(s);
                    }
                }, token);

                swTotal.Stop();
                _lastResults = results;
                _lblTime.Text = $"Общее время: {swTotal.Elapsed.TotalSeconds:0.0} с";
                _lblStatus.Text = $"Эксперимент успешно завершён за {swTotal.Elapsed.TotalSeconds:0.0} с";
                _progressBar.Value = 100;

                bool hasMatrix = false;
                foreach (var s in results)
                {
                    if (s.MatrixTimes != null || s.Algo is MatrixMultiply)
                    {
                        hasMatrix = true;
                        _lastMatrix3DData = s.MatrixTimes;
                        _lastMatrixStepM = s.MatrixStepM;
                        _lastMatrixStepN = s.MatrixStepN;
                        UpdateMatrix3DPlot();
                        _btnSaveMatrix3D.Enabled = true;
                    }
                }

                // Настраиваем базовые замеры по умолчанию для запущенных алгоритмов
                _currentBaselineSeries.Clear();
                var allHistory = BenchmarkDb.GetHistoryExperiments();
                foreach (var s in results)
                {
                    string targetExpId = null;
                    var baseExp = allHistory.FirstOrDefault(h => h.IsBaseline && h.Algorithms.Any(a => string.Equals(a, s.Algo.Name, StringComparison.OrdinalIgnoreCase)));
                    if (baseExp != null)
                        targetExpId = baseExp.ExperimentId;
                    else if (previousLatestExpId != null)
                    {
                        var prevExp = allHistory.FirstOrDefault(h => h.ExperimentId == previousLatestExpId && h.Algorithms.Any(a => string.Equals(a, s.Algo.Name, StringComparison.OrdinalIgnoreCase)));
                        if (prevExp != null)
                            targetExpId = prevExp.ExperimentId;
                    }

                    if (targetExpId != null)
                    {
                        _algoBaselineExpId[s.Algo.Name] = targetExpId;
                        var bDict = BenchmarkDb.GetBaselineSeries(targetExpId);
                        if (bDict.TryGetValue(s.Algo.Name, out var bSeries))
                            _currentBaselineSeries[s.Algo.Name] = bSeries;
                    }
                }

                RebuildPlotsAndSummary();

                try
                {
                    Program.ExportCsv(results, "results.csv");
                    Plotter.SaveAll(_lastPlots, "charts");
                    AppendLog("\n[Автосохранение] Результаты сохранены в results.csv, charts/ и benchmark.db");
                }
                catch (Exception ex)
                {
                    AppendLog($"\n[Предупреждение] Ошибка автосохранения: {ex.Message}");
                }

                if (selected.Count == 1 && hasMatrix)
                {
                    _tabsMain.SelectedTab = _tabMatrix3D;
                }
                else
                {
                    _tabsMain.SelectedIndex = 0;
                }
                OnTabChanged();
            }
            catch (OperationCanceledException)
            {
                swTotal.Stop();
                _lblStatus.Text = "Эксперимент остановлен пользователем";
                AppendLog("\n[Отмена] Эксперимент был прерван пользователем.");
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                _lblStatus.Text = "Ошибка выполнения";
                AppendLog($"\n[Ошибка] {ex}");
                MessageBox.Show(this, "Произошла ошибка при выполнении замеров:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnRun.Enabled = true;
                _btnRun.BackColor = Color.FromArgb(37, 99, 235);
                _btnCancel.Enabled = false;
                _btnCancel.BackColor = Color.FromArgb(55, 65, 81);
                _btnCancel.ForeColor = Color.FromArgb(156, 163, 175);
            }
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { Invoke(new Action<string>(SetStatus), text); return; }
            string dot = "● ";
            _lblStatus.Text = dot + text;
            if (text.Contains("Ошибка") || text.Contains("остановлен"))
                _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
            else if (text.Contains("Выполняется") || text.Contains("прогрев"))
                _lblStatus.ForeColor = Color.FromArgb(96, 165, 250);
            else
                _lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
        }

        private void SetProgress(int percent)
        {
            if (InvokeRequired) { Invoke(new Action<int>(SetProgress), percent); return; }
            _progressBar.Value = Math.Max(0, Math.Min(100, percent));
        }

        private void AppendLog(string msg)
        {
            if (InvokeRequired) { Invoke(new Action<string>(AppendLog), msg); return; }
            _txtLog.AppendText(msg + Environment.NewLine);
        }

        private void ExportCsvDialog()
        {
            if (_lastResults == null || _lastResults.Count == 0)
            {
                MessageBox.Show(this, "Нет доступных результатов для экспорта. Сначала запустите замеры.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var sfd = new SaveFileDialog { Filter = "CSV файлы (*.csv)|*.csv", FileName = "results.csv" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    Program.ExportCsv(_lastResults, sfd.FileName);
                    MessageBox.Show(this, "Данные успешно сохранены в " + sfd.FileName, "Экспорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void ExportPngDialog()
        {
            if (_lastPlots == null || _lastPlots.Count == 0)
            {
                MessageBox.Show(this, "Нет построенных графиков для сохранения. Сначала запустите замеры.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var fbd = new FolderBrowserDialog { Description = "Выберите папку для сохранения графиков" })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    Plotter.SaveAll(_lastPlots, fbd.SelectedPath);
                    MessageBox.Show(this, "Графики успешно сохранены в " + fbd.SelectedPath, "Экспорт завершён", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
