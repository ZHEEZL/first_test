using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScottPlot;
using ScottPlot.Avalonia;

namespace first
{
    public sealed class AlgoSummaryRow
    {
        public string AlgoName { get; set; }
        public string Icon => (AlgoName != null && AlgoName.Contains("Матричное")) ? "🧊" : "📈";
        public string ClassName { get; set; }
        public int MaxN { get; set; }
        public int Step { get; set; }
        public int Points { get; set; }
        public string Metric { get; set; }
        public string ConstantC { get; set; }
        public string MSE { get; set; }
        public string FactMaxN { get; set; }
        public string BaseMaxN { get; set; }
        public string DiffPct { get; set; }
        public string TheoryMaxN { get; set; }
        public string Source { get; set; }
    }

    public sealed class BaselineComboItem
    {
        public string ExperimentId { get; set; }
        public string Title { get; set; }
        public override string ToString() => Title;
    }

    public partial class MainWindow : Window
    {
        private List<AlgoConfigItem> _configs;
        private List<Series> _lastResults = new List<Series>();
        private List<KeyValuePair<string, Plot>> _lastPlots = new List<KeyValuePair<string, Plot>>();
        private Dictionary<string, Series> _currentBaselineSeries = new Dictionary<string, Series>();
        private Dictionary<string, string> _algoBaselineExpId = new Dictionary<string, string>();
        private string _selectedBaselineExpId;
        private CancellationTokenSource _cts;
        private bool _isUpdatingCombo = false;

        private ObservableCollection<AlgoSummaryRow> _summaryRows = new ObservableCollection<AlgoSummaryRow>();
        private List<TabItem> _dynamicTabs = new List<TabItem>();
        private Dictionary<string, TabItem> _allDynamicTabs = new Dictionary<string, TabItem>();
        private bool _isSidebarCollapsed = false;
        private double _lastSidebarWidth = 380.0;

        public MainWindow()
        {
            InitializeComponent();

            _configs = AlgoConfigItem.CreateDefaultConfigs();
            GridConfigs.ItemsSource = _configs;
            GridSummary.ItemsSource = _summaryRows;

            BtnSelectAll.Click += (s, e) => { foreach (var c in _configs) c.Enabled = true; GridConfigs.ItemsSource = null; GridConfigs.ItemsSource = _configs; };
            BtnDeselectAll.Click += (s, e) => { foreach (var c in _configs) c.Enabled = false; GridConfigs.ItemsSource = null; GridConfigs.ItemsSource = _configs; };
            BtnResetLimits.Click += (s, e) =>
            {
                foreach (var c in _configs)
                {
                    c.MaxN = c.DefaultMaxN;
                    c.Step = c.DefaultStep;
                    c.Runs = c.DefaultRuns;
                }
                GridConfigs.ItemsSource = null;
                GridConfigs.ItemsSource = _configs;
            };

            BtnRun.Click += async (s, e) => await StartExperimentAsync();
            BtnCancel.Click += (s, e) => _cts?.Cancel();

            BtnToggleSidebar.Click += (s, e) => ToggleSidebar();
            BtnTabListMenu.Click += (s, e) => ShowTabListMenu();

            BtnExportCsv.Click += OnExportCsvClick;
            BtnExportPng.Click += OnExportPngClick;
            BtnManageHistory.Click += OnManageHistoryClick;

            CmbBaselineRun.SelectionChanged += (s, e) => OnBaselineChanged();
            ChkShowHistory.IsCheckedChanged += (s, e) => OnHistoryToggleChanged();
            ChkShowErrorBars.IsCheckedChanged += (s, e) => OnHistoryToggleChanged();

            TabsMain.SelectionChanged += (s, e) => OnTabChanged();
            PnlHistoryCompare.IsVisible = false;
            UpdateTabListMenuButton();
        }

        private void OnTabChanged()
        {
            var tab = TabsMain.SelectedItem as TabItem;
            if (tab == null)
            {
                PnlHistoryCompare.IsVisible = false;
                return;
            }

            string algoName = tab.Tag as string;
            if (string.IsNullOrEmpty(algoName) || algoName == "summary" || algoName == "log" || algoName.Contains("Матричное"))
            {
                PnlHistoryCompare.IsVisible = false;
                return;
            }

            PnlHistoryCompare.IsVisible = true;
            UpdateBaselineComboForAlgo(algoName);
        }

        private void EnsureTabPlotUpdated(TabItem tab, string algoName)
        {
            if (_lastResults == null || _lastResults.Count == 0) return;
            var series = _lastResults.FirstOrDefault(s => s.Algo.Name == algoName);
            if (series == null) return;

            var single = Plotter.Build(
                new List<Series> { series },
                _currentBaselineSeries,
                ChkShowHistory.IsChecked ?? true,
                ChkShowErrorBars.IsChecked ?? true
            );

            if (single.Count > 0)
            {
                var avaPlot = tab.Content as AvaPlot;
                if (avaPlot != null)
                {
                    avaPlot.Reset(single[0].Value);
                    string uName = GetSeriesUnitName(series);
                    double uScale = GetSeriesUnitScale(series);
                    UpdatePlotInteractivity(avaPlot, series, uName, uScale);
                    avaPlot.Refresh();
                }

                int pIdx = _lastPlots.FindIndex(kv => kv.Key == algoName);
                if (pIdx >= 0)
                    _lastPlots[pIdx] = single[0];
                else
                    _lastPlots.Add(single[0]);
            }
        }

        private void UpdateBaselineComboForAlgo(string algoName)
        {
            if (string.IsNullOrEmpty(algoName)) return;

            var allHistory = BenchmarkDb.GetHistoryExperiments();
            var relevantHistory = allHistory
                .Where(h => h.Algorithms.Any(a => string.Equals(a, algoName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _isUpdatingCombo = true;
            try
            {
                var items = new List<BaselineComboItem>
                {
                    new BaselineComboItem { ExperimentId = null, Title = "[ Не сравнивать ]" }
                };

                if (relevantHistory.Count == 0)
                {
                    CmbBaselineRun.ItemsSource = items;
                    CmbBaselineRun.SelectedIndex = 0;
                    CmbBaselineRun.IsEnabled = false;
                    _algoBaselineExpId[algoName] = null;
                    _currentBaselineSeries.Remove(algoName);
                    return;
                }

                CmbBaselineRun.IsEnabled = ChkShowHistory.IsChecked ?? true;
                BaselineComboItem toSelect = null;

                bool hasExplicit = _algoBaselineExpId.TryGetValue(algoName, out var savedId);
                string preferredExpId = hasExplicit ? savedId : _selectedBaselineExpId;

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
                    items.Add(item);

                    if (preferredExpId != null && h.ExperimentId == preferredExpId)
                        toSelect = item;
                    else if (toSelect == null && !hasExplicit && preferredExpId == null && h.IsBaseline)
                        toSelect = item;
                }

                CmbBaselineRun.ItemsSource = items;

                if (toSelect != null)
                {
                    CmbBaselineRun.SelectedItem = toSelect;
                }
                else if (hasExplicit && savedId == null)
                {
                    CmbBaselineRun.SelectedIndex = 0;
                }
                else if (!hasExplicit && items.Count > 1)
                {
                    CmbBaselineRun.SelectedIndex = 1;
                }
                else
                {
                    CmbBaselineRun.SelectedIndex = 0;
                }
            }
            finally
            {
                _isUpdatingCombo = false;
            }

            ApplyBaselineSelectionForAlgo(algoName);
        }

        private void ApplyBaselineSelectionForAlgo(string algoName)
        {
            string expId = (CmbBaselineRun.SelectedItem as BaselineComboItem)?.ExperimentId;
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
        }

        private void OnBaselineChanged()
        {
            if (_isUpdatingCombo) return;

            var tab = TabsMain.SelectedItem as TabItem;
            if (tab == null) return;

            string algoName = tab.Tag as string;
            if (string.IsNullOrEmpty(algoName) || algoName == "summary" || algoName == "log" || algoName.Contains("Матричное")) return;

            ApplyBaselineSelectionForAlgo(algoName);

            var series = _lastResults.FirstOrDefault(s => s.Algo.Name == algoName);
            if (series != null)
            {
                EnsureTabPlotUpdated(tab, algoName);
                UpdateSummaryRowForAlgo(series);
            }
        }

        private void OnHistoryToggleChanged()
        {
            bool showHist = ChkShowHistory.IsChecked ?? true;
            bool showErr = ChkShowErrorBars.IsChecked ?? true;

            CmbBaselineRun.IsEnabled = showHist && (CmbBaselineRun.ItemCount > 1);

            if (_lastResults == null || _lastResults.Count == 0) return;

            _lastPlots = Plotter.Build(_lastResults, _currentBaselineSeries, showHist, showErr);

            foreach (var tab in _dynamicTabs)
            {
                string name = tab.Tag as string;
                if (string.IsNullOrEmpty(name)) continue;

                var plotKv = _lastPlots.FirstOrDefault(kv => kv.Key == name);
                if (plotKv.Value != null)
                {
                    var avaPlot = tab.Content as AvaPlot;
                    if (avaPlot != null)
                    {
                        avaPlot.Reset(plotKv.Value);
                        var s = _lastResults.FirstOrDefault(r => r.Algo.Name == name);
                        if (s != null)
                        {
                            string uName = GetSeriesUnitName(s);
                            double uScale = GetSeriesUnitScale(s);
                            UpdatePlotInteractivity(avaPlot, s, uName, uScale);
                        }
                        avaPlot.Refresh();
                    }
                }
            }

            foreach (var s in _lastResults)
            {
                UpdateSummaryRowForAlgo(s);
            }
        }

        private void UpdateSummaryRowForAlgo(Series s)
        {
            if (s == null || s.N.Count == 0) return;
            var row = _summaryRows.FirstOrDefault(r => r.AlgoName == s.Algo.Name);
            if (row == null) return;

            int last = s.N.Count - 1;
            string baseStr = "—";
            string diffStr = "—";

            bool showHist = ChkShowHistory.IsChecked ?? true;
            if (showHist && _currentBaselineSeries != null &&
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

            row.BaseMaxN = baseStr;
            row.DiffPct = diffStr;

            int idx = _summaryRows.IndexOf(row);
            if (idx >= 0)
            {
                _summaryRows[idx] = row; // trigger notify
            }
        }

        private async Task StartExperimentAsync()
        {
            var selected = _configs.Where(c => c.Enabled).ToList();
            if (selected.Count == 0)
            {
                SetStatus("Не выбран ни один алгоритм");
                return;
            }

            BtnRun.IsEnabled = false;
            BtnCancel.IsEnabled = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            ProgressBarMain.Value = 0;
            TxtLog.Text = "";
            SetStatus("Инициализация эксперимента...");

            string experimentId = Guid.NewGuid().ToString("N");
            bool useCache = ChkCache.IsChecked ?? true;
            bool forceRecalc = ChkForce.IsChecked ?? false;
            bool warmup = ChkWarmup.IsChecked ?? true;

            int maxVectorN = selected
                .Where(c => !(c.Factory() is MatrixMultiply))
                .Select(c => c.MaxN)
                .DefaultIfEmpty(2000)
                .Max();

            int seed = 20240915;
            int.TryParse(TxtSeed.Text?.Trim(), out seed);

            var swTotal = Stopwatch.StartNew();
            List<Series> results = new List<Series>();

            try
            {
                string previousLatestExpId = BenchmarkDb.GetHistoryExperiments().FirstOrDefault()?.ExperimentId;

                await Task.Run(() =>
                {
                    var ctx = new ExperimentContext(maxVectorN, seed);
                    if (warmup)
                    {
                        Dispatcher.UIThread.Post(() => SetStatus("Прогрев процессора и JIT..."));
                        Bench.GlobalWarmup(ctx);
                    }

                    for (int i = 0; i < selected.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var item = selected[i];
                        var algo = item.Factory();
                        algo.P.MaxN = item.MaxN;
                        algo.P.Step = item.Step;
                        algo.P.Runs = item.Runs;

                        int curIdx = i + 1;
                        Dispatcher.UIThread.Post(() =>
                        {
                            SetStatus($"[{curIdx}/{selected.Count}] Выполняется: {algo.Name}...");
                            AppendLog($"\n=== {algo.Name} [{algo.Cls.Name}] (MaxN = {item.MaxN}, Step = {item.Step}, Runs = {item.Runs}) ===");
                        });

                        var s = Bench.Run(algo, ctx, experimentId, useCache, forceRecalc,
                            log => Dispatcher.UIThread.Post(() => AppendLog(log)),
                            (pt, totalPts) =>
                            {
                                int pct = (int)(((curIdx - 1 + (double)pt / totalPts) / selected.Count) * 100);
                                Dispatcher.UIThread.Post(() => ProgressBarMain.Value = pct);
                            },
                            token);

                        results.Add(s);
                    }
                }, token);

                swTotal.Stop();
                _lastResults = results;
                LblTime.Text = $"Общее время: {swTotal.Elapsed.TotalSeconds:0.0} с";
                SetStatus($"Эксперимент успешно завершён за {swTotal.Elapsed.TotalSeconds:0.0} с");
                ProgressBarMain.Value = 100;

                // Базовые замеры
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

                    _algoBaselineExpId[s.Algo.Name] = targetExpId;
                    if (targetExpId != null)
                    {
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
            }
            catch (OperationCanceledException)
            {
                swTotal.Stop();
                SetStatus("Эксперимент остановлен пользователем");
                AppendLog("\n[Отмена] Эксперимент был прерван пользователем.");
            }
            catch (Exception ex)
            {
                swTotal.Stop();
                SetStatus("Ошибка выполнения: " + ex.Message);
                AppendLog($"\n[Ошибка] {ex}");
            }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnCancel.IsEnabled = false;
            }
        }

        private void RebuildPlotsAndSummary()
        {
            _summaryRows.Clear();
            ClearDynamicTabs();

            bool showHist = ChkShowHistory.IsChecked ?? true;
            bool showErr = ChkShowErrorBars.IsChecked ?? true;

            _lastPlots = Plotter.Build(_lastResults, _currentBaselineSeries, showHist, showErr);

            foreach (var s in _lastResults)
            {
                int last = s.N.Count - 1;
                string factStr = s.MeasuresSteps ? $"{s.T[last]:0} шагов" : Bench.FormatTime(s.T[last]);
                string thStr = s.MeasuresSteps ? $"{s.TFit[last]:0.0} шагов" : Bench.FormatTime(s.TFit[last]);

                string baseStr = "—";
                string diffStr = "—";
                if (showHist && _currentBaselineSeries != null &&
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

                _summaryRows.Add(new AlgoSummaryRow
                {
                    AlgoName = s.Algo.Name,
                    ClassName = s.Algo.Cls.Name,
                    MaxN = (int)s.N.LastOrDefault(),
                    Step = s.N.Count > 1 ? (int)(s.N[1] - s.N[0]) : 0,
                    Points = s.N.Count,
                    Metric = s.MeasuresSteps ? "Шаги" : "Время (с)",
                    ConstantC = s.C.ToString("0.####E+00", CultureInfo.InvariantCulture),
                    MSE = s.MSE.ToString("0.####E+00", CultureInfo.InvariantCulture),
                    FactMaxN = factStr,
                    BaseMaxN = baseStr,
                    DiffPct = diffStr,
                    TheoryMaxN = thStr,
                    Source = s.CachedPoints == s.N.Count ? "100% SQLite" : $"{s.CachedPoints}/{s.N.Count} кэш"
                });
            }

            // Создаем динамические вкладки графиков
            foreach (var kv in _lastPlots)
            {
                if (kv.Key.Contains("Матричное"))
                {
                    var mSeries = _lastResults.FirstOrDefault(s => s.MatrixTimes != null || s.Algo is MatrixMultiply);
                    if (mSeries != null && mSeries.MatrixTimes != null)
                    {
                        var panel3d = new Grid { RowDefinitions = new RowDefinitions("Auto, *") };
                        var toolBar3d = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 6) };

                        int rowsM = mSeries.MatrixTimes.GetLength(0);
                        int colsN = mSeries.MatrixTimes.GetLength(1);
                        double maxSec = 0;
                        for (int r = 0; r < rowsM; r++)
                            for (int c = 0; c < colsN; c++)
                                if (mSeries.MatrixTimes[r, c] > maxSec) maxSec = mSeries.MatrixTimes[r, c];

                        double scale = 1e3;
                        string unit = "мс";
                        if (maxSec < 1e-3) { scale = 1e6; unit = "мкс"; }
                        else if (maxSec >= 1.0) { scale = 1.0; unit = "с"; }

                        double[,] scaledTimes = new double[rowsM, colsN];
                        for (int r = 0; r < rowsM; r++)
                            for (int c = 0; c < colsN; c++)
                                scaledTimes[r, c] = mSeries.MatrixTimes[r, c] * scale;

                        var vp3d = new Matrix3DViewport();
                        vp3d.SetData(scaledTimes, mSeries.MatrixStepM, mSeries.MatrixStepN, unit);

                        var btnShaded = new Button { Content = "Полигоны (Shaded)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnShaded.Click += (s, e) => { vp3d.Mode = ViewportMode.ShadedWireframe; vp3d.InvalidateVisual(); };

                        var btnWire = new Button { Content = "Каркас (Wireframe)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnWire.Click += (s, e) => { vp3d.Mode = ViewportMode.WireframeOnly; vp3d.InvalidateVisual(); };

                        var btnPoints = new Button { Content = "Точки (PointCloud)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnPoints.Click += (s, e) => { vp3d.Mode = ViewportMode.PointCloud; vp3d.InvalidateVisual(); };

                        var btnHeatmap = new Button { Content = "2D Heatmap", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnHeatmap.Click += (s, e) => { vp3d.Mode = ViewportMode.Heatmap2D; vp3d.InvalidateVisual(); };

                        var btnPanMode = new Button { Content = "🖐 Сдвиг (Pan)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnPanMode.Click += (s, e) =>
                        {
                            vp3d.IsPanMode = !vp3d.IsPanMode;
                            btnPanMode.Content = vp3d.IsPanMode ? "🔄 Вращение" : "🖐 Сдвиг (Pan)";
                        };

                        var btnResetCam = new Button { Content = "↺ Сброс камеры", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnResetCam.Click += (s, e) => vp3d.ResetCamera();

                        toolBar3d.Children.Add(btnShaded);
                        toolBar3d.Children.Add(btnWire);
                        toolBar3d.Children.Add(btnPoints);
                        toolBar3d.Children.Add(btnHeatmap);
                        toolBar3d.Children.Add(btnPanMode);
                        toolBar3d.Children.Add(btnResetCam);

                        Grid.SetRow(toolBar3d, 0);
                        Grid.SetRow(vp3d, 1);
                        panel3d.Children.Add(toolBar3d);
                        panel3d.Children.Add(vp3d);

                        var tabItem3D = new TabItem
                        {
                            Tag = kv.Key,
                            Content = panel3d,
                            FontSize = 12
                        };
                        tabItem3D.Header = CreateTabHeader("Матрица 3D", "🧊", tabItem3D, true);

                        _dynamicTabs.Add(tabItem3D);
                        _allDynamicTabs[kv.Key] = tabItem3D;
                        TabsMain.Items.Add(tabItem3D);
                        continue;
                    }
                }

                var avaPlot = new AvaPlot();
                avaPlot.Reset(kv.Value);

                var series = _lastResults.FirstOrDefault(s => s.Algo.Name == kv.Key);
                if (series != null)
                {
                    string uName = GetSeriesUnitName(series);
                    double uScale = GetSeriesUnitScale(series);
                    UpdatePlotInteractivity(avaPlot, series, uName, uScale);
                }
                avaPlot.Refresh();

                var tabItem = new TabItem
                {
                    Tag = kv.Key,
                    Content = avaPlot,
                    FontSize = 12
                };
                tabItem.Header = CreateTabHeader(kv.Key, "📈", tabItem, true);

                _dynamicTabs.Add(tabItem);
                _allDynamicTabs[kv.Key] = tabItem;
                TabsMain.Items.Add(tabItem);
            }

            UpdateTabListMenuButton();
            TabsMain.SelectedIndex = 0;
        }

        private void ClearDynamicTabs()
        {
            foreach (var tab in _dynamicTabs)
            {
                TabsMain.Items.Remove(tab);
            }
            _dynamicTabs.Clear();
            _allDynamicTabs.Clear();
            UpdateTabListMenuButton();
        }

        private Control CreateTabHeader(string title, string icon, TabItem tabItem, bool canClose)
        {
            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var iconTb = new TextBlock
            {
                Text = icon,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            panel.Children.Add(iconTb);

            var textTb = new TextBlock
            {
                Text = title,
                FontSize = 12,
                MaxWidth = 170,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            ToolTip.SetTip(textTb, title);
            panel.Children.Add(textTb);

            if (canClose)
            {
                var btnClose = new Button
                {
                    Content = "×",
                    Padding = new Thickness(0),
                    Width = 16,
                    Height = 16,
                    FontSize = 13,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Background = Brushes.Transparent,
                    Foreground = Brush.Parse("#9CA3AF"),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                ToolTip.SetTip(btnClose, "Закрыть вкладку");
                btnClose.Click += (s, e) =>
                {
                    e.Handled = true;
                    CloseTab(tabItem);
                };
                panel.Children.Add(btnClose);
            }

            return panel;
        }

        private void CloseTab(TabItem tabItem)
        {
            if (tabItem == null) return;
            int index = TabsMain.Items.IndexOf(tabItem);
            if (index >= 0)
            {
                TabsMain.Items.Remove(tabItem);
                if (TabsMain.SelectedIndex < 0 || TabsMain.SelectedItem == null)
                {
                    TabsMain.SelectedIndex = Math.Clamp(index - 1, 0, TabsMain.Items.Count - 1);
                }
            }
            UpdateTabListMenuButton();
        }

        private void UpdateTabListMenuButton()
        {
            int openCount = TabsMain.Items.Count;
            BtnTabListMenu.Content = $"📑 Вкладки ({openCount}) ▼";
        }

        private void ToggleSidebar()
        {
            var col = MainGrid.ColumnDefinitions[0];
            if (!_isSidebarCollapsed)
            {
                _lastSidebarWidth = col.Width.Value > 50 ? col.Width.Value : 380.0;
                col.MinWidth = 0;
                col.Width = new GridLength(0);
                _isSidebarCollapsed = true;
                BtnToggleSidebar.Content = "◧ Развернуть";
                ToolTip.SetTip(BtnToggleSidebar, "Развернуть панель параметров");
            }
            else
            {
                col.MinWidth = 260;
                col.Width = new GridLength(_lastSidebarWidth);
                _isSidebarCollapsed = false;
                BtnToggleSidebar.Content = "◨ Панель";
                ToolTip.SetTip(BtnToggleSidebar, "Свернуть панель параметров");
            }
        }

        private void ShowTabListMenu()
        {
            var menu = new ContextMenu();

            var itemSummary = new MenuItem { Header = "📋 Сводка" };
            itemSummary.Click += (s, e) => { TabsMain.SelectedIndex = 0; };
            menu.Items.Add(itemSummary);

            var itemLog = new MenuItem { Header = "📝 Журнал" };
            itemLog.Click += (s, e) => { TabsMain.SelectedIndex = 1; };
            menu.Items.Add(itemLog);

            if (_allDynamicTabs.Count > 0)
            {
                menu.Items.Add(new Separator());

                foreach (var kv in _allDynamicTabs)
                {
                    string algoName = kv.Key;
                    var tab = kv.Value;
                    bool isOpen = TabsMain.Items.Contains(tab);

                    string prefix = algoName.Contains("Матричное") ? "🧊 " : "📈 ";
                    var item = new MenuItem
                    {
                        Header = isOpen ? $"✔ {prefix}{algoName}" : $"  {prefix}{algoName} (закрыта)",
                        Foreground = isOpen ? Brush.Parse("#60A5FA") : Brush.Parse("#9CA3AF")
                    };

                    item.Click += (s, e) =>
                    {
                        if (!TabsMain.Items.Contains(tab))
                        {
                            TabsMain.Items.Add(tab);
                        }
                        TabsMain.SelectedItem = tab;
                        UpdateTabListMenuButton();
                    };

                    menu.Items.Add(item);
                }

                menu.Items.Add(new Separator());
                var itemRestoreAll = new MenuItem { Header = "↺ Показать все графики" };
                itemRestoreAll.Click += (s, e) =>
                {
                    foreach (var kv in _allDynamicTabs)
                    {
                        if (!TabsMain.Items.Contains(kv.Value))
                        {
                            TabsMain.Items.Add(kv.Value);
                        }
                    }
                    UpdateTabListMenuButton();
                };
                menu.Items.Add(itemRestoreAll);
            }

            menu.Open(BtnTabListMenu);
        }

        private void SetStatus(string text)
        {
            LblStatus.Text = "● " + text;
            if (text.Contains("Ошибка") || text.Contains("остановлен"))
                LblStatus.Foreground = Avalonia.Media.Brushes.IndianRed;
            else if (text.Contains("Выполняется") || text.Contains("Прогрев") || text.Contains("Инициализация"))
                LblStatus.Foreground = Avalonia.Media.Brushes.LightSkyBlue;
            else
                LblStatus.Foreground = Avalonia.Media.Brushes.MediumSeaGreen;
        }

        private void AppendLog(string msg)
        {
            TxtLog.Text += msg + Environment.NewLine;
            TxtLog.CaretIndex = TxtLog.Text.Length;
        }

        private async void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            if (_lastResults == null || _lastResults.Count == 0) return;
            try
            {
                Program.ExportCsv(_lastResults, "results.csv");
                SetStatus("Результаты успешно экспортированы в results.csv");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка экспорта: " + ex.Message);
            }
        }

        private async void OnExportPngClick(object sender, RoutedEventArgs e)
        {
            if (_lastPlots == null || _lastPlots.Count == 0) return;
            try
            {
                Plotter.SaveAll(_lastPlots, "charts");
                SetStatus("Графики сохранены в папку charts/");
            }
            catch (Exception ex)
            {
                SetStatus("Ошибка сохранения графиков: " + ex.Message);
            }
        }

        private void OnManageHistoryClick(object sender, RoutedEventArgs e)
        {
            var dlg = new HistoryManagerWindow();
            dlg.OnDataChanged += () =>
            {
                var tab = TabsMain.SelectedItem as TabItem;
                if (tab != null)
                {
                    string algoName = tab.Tag as string;
                    if (!string.IsNullOrEmpty(algoName) && algoName != "summary" && algoName != "log" && !algoName.Contains("Матричное"))
                    {
                        UpdateBaselineComboForAlgo(algoName);
                        OnBaselineChanged();
                    }
                }
            };
            dlg.ShowDialog(this);
        }

        private static string GetSeriesUnitName(Series s)
        {
            if (s.MeasuresSteps) return "шагов";
            double maxVal = s.T.Count > 0 ? s.T.Max() : 0;
            if (maxVal < 1e-3) return "мкс";
            if (maxVal < 1.0) return "мс";
            return "с";
        }

        private static double GetSeriesUnitScale(Series s)
        {
            if (s.MeasuresSteps) return 1.0;
            double maxVal = s.T.Count > 0 ? s.T.Max() : 0;
            if (maxVal < 1e-3) return 1e6;
            if (maxVal < 1.0) return 1e3;
            return 1.0;
        }

        private sealed class PlotInteractivityContext
        {
            public Series Series;
            public string UnitName;
            public double UnitScale;
            public ScottPlot.Plottables.Marker HighlightMarker;
            public ScottPlot.Plottables.Crosshair Crosshair;
            public ScottPlot.Plottables.Annotation InspectorAnno;
            public int LastHighlightedIndex = -1;
        }

        private void UpdatePlotInteractivity(AvaPlot avaPlot, Series s, string unitName, double unitScale)
        {
            if (avaPlot == null || s == null || s.N.Count == 0) return;

            var plt = avaPlot.Plot;

            var highlightMarker = plt.Add.Marker(0, 0);
            highlightMarker.IsVisible = false;
            highlightMarker.Color = ScottPlot.Color.FromHex("#FBBF24");
            highlightMarker.Size = 10;
            highlightMarker.Shape = MarkerShape.OpenCircle;

            var crosshair = plt.Add.Crosshair(0, 0);
            crosshair.IsVisible = false;
            crosshair.LineColor = ScottPlot.Color.FromHex("#FBBF24").WithAlpha(0.6f);
            crosshair.LineWidth = 1.2f;
            crosshair.LinePattern = LinePattern.Dashed;

            var inspectorAnno = plt.Add.Annotation("", Alignment.UpperCenter);
            inspectorAnno.IsVisible = false;
            inspectorAnno.LabelBackgroundColor = ScottPlot.Color.FromHex("#1E232E").WithAlpha(0.92f);
            inspectorAnno.LabelBorderColor = ScottPlot.Color.FromHex("#FBBF24");
            inspectorAnno.LabelFontColor = ScottPlot.Color.FromHex("#FFFFFF");
            inspectorAnno.LabelFontSize = 12;
            inspectorAnno.LabelBold = true;

            var ctx = avaPlot.Tag as PlotInteractivityContext;
            if (ctx == null)
            {
                ctx = new PlotInteractivityContext();
                avaPlot.Tag = ctx;

                avaPlot.PointerMoved += (sender, e) =>
                {
                    var curCtx = avaPlot.Tag as PlotInteractivityContext;
                    if (curCtx == null || curCtx.Series == null || curCtx.Series.N.Count == 0) return;

                    var props = e.GetCurrentPoint(avaPlot).Properties;
                    if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
                    {
                        // Пользователь выполняет панорамирование (Pan) или масштабирование (Zoom) — не мешаем ScottPlot и не вызываем Refresh!
                        if (curCtx.LastHighlightedIndex != -1)
                        {
                            curCtx.LastHighlightedIndex = -1;
                            if (curCtx.HighlightMarker != null) curCtx.HighlightMarker.IsVisible = false;
                            if (curCtx.Crosshair != null) curCtx.Crosshair.IsVisible = false;
                            if (curCtx.InspectorAnno != null) curCtx.InspectorAnno.IsVisible = false;
                            avaPlot.Refresh();
                        }
                        return;
                    }

                    var curPlt = avaPlot.Plot;
                    var pt = e.GetPosition(avaPlot);
                    Pixel mousePixel = new Pixel((float)pt.X, (float)pt.Y);

                    int closestIndex = -1;
                    double closestDistPixel = double.MaxValue;
                    var ser = curCtx.Series;
                    double scale = curCtx.UnitScale;

                    for (int i = 0; i < ser.N.Count; i++)
                    {
                        double xVal = ser.N[i];
                        double yVal = ser.T[i] * scale;

                        Coordinates ptCoord = new Coordinates(xVal, yVal);
                        Pixel ptPixel = curPlt.GetPixel(ptCoord);

                        double dx = ptPixel.X - mousePixel.X;
                        double dy = ptPixel.Y - mousePixel.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        if (dist < closestDistPixel)
                        {
                            closestDistPixel = dist;
                            closestIndex = i;
                        }
                    }

                    if (closestIndex >= 0 && closestDistPixel <= 40.0)
                    {
                        if (closestIndex != curCtx.LastHighlightedIndex)
                        {
                            curCtx.LastHighlightedIndex = closestIndex;

                            double xVal = ser.N[closestIndex];
                            double yVal = ser.T[closestIndex] * scale;
                            double yThVal = ser.TFit.Count > closestIndex ? ser.TFit[closestIndex] * scale : 0;

                            if (curCtx.HighlightMarker != null)
                            {
                                curCtx.HighlightMarker.Coordinates = new Coordinates(xVal, yVal);
                                curCtx.HighlightMarker.IsVisible = true;
                            }

                            if (curCtx.Crosshair != null)
                            {
                                curCtx.Crosshair.Position = new Coordinates(xVal, yVal);
                                curCtx.Crosshair.IsVisible = true;
                            }

                            string sdStr = "";
                            if (ser.StdDev.Count > closestIndex && ser.StdDev[closestIndex] > 1e-12)
                            {
                                double sdVal = ser.StdDev[closestIndex] * scale;
                                sdStr = $" (±{sdVal:0.##} {curCtx.UnitName})";
                            }

                            string thStr = (yThVal >= 0.001 && yThVal < 10000)
                                ? yThVal.ToString("0.###", CultureInfo.InvariantCulture)
                                : yThVal.ToString("0.###E+00", CultureInfo.InvariantCulture);

                            string factStr = (yVal >= 0.001 && yVal < 10000)
                                ? yVal.ToString("0.###", CultureInfo.InvariantCulture)
                                : yVal.ToString("0.###E+00", CultureInfo.InvariantCulture);

                            string baseInfo = "";
                            if (_currentBaselineSeries != null &&
                                _currentBaselineSeries.TryGetValue(ser.Algo.Name, out var bSeries) &&
                                bSeries != null && bSeries.N.Count > 0)
                            {
                                int bIdx = bSeries.N.IndexOf(ser.N[closestIndex]);
                                if (bIdx >= 0 && bIdx < bSeries.T.Count)
                                {
                                    double bVal = bSeries.T[bIdx] * scale;
                                    double diff = bVal > 1e-15 ? ((yVal - bVal) / bVal) * 100.0 : 0;
                                    string sign = diff >= 0 ? "+" : "";
                                    baseInfo = $"  |  База: {bVal:0.###} {curCtx.UnitName} ({sign}{diff:F1}%)";
                                }
                            }

                            if (curCtx.InspectorAnno != null)
                            {
                                curCtx.InspectorAnno.LabelText = $"● n = {xVal:N0}  |  Факт: {factStr} {curCtx.UnitName}{sdStr}  |  Теория: {thStr} {curCtx.UnitName}{baseInfo}";
                                curCtx.InspectorAnno.IsVisible = true;
                            }

                            avaPlot.Refresh();
                        }
                    }
                    else
                    {
                        if (curCtx.LastHighlightedIndex != -1)
                        {
                            curCtx.LastHighlightedIndex = -1;
                            if (curCtx.HighlightMarker != null) curCtx.HighlightMarker.IsVisible = false;
                            if (curCtx.Crosshair != null) curCtx.Crosshair.IsVisible = false;
                            if (curCtx.InspectorAnno != null) curCtx.InspectorAnno.IsVisible = false;
                            avaPlot.Refresh();
                        }
                    }
                };

                avaPlot.PointerExited += (sender, e) =>
                {
                    var curCtx = avaPlot.Tag as PlotInteractivityContext;
                    if (curCtx != null && curCtx.LastHighlightedIndex != -1)
                    {
                        curCtx.LastHighlightedIndex = -1;
                        if (curCtx.HighlightMarker != null) curCtx.HighlightMarker.IsVisible = false;
                        if (curCtx.Crosshair != null) curCtx.Crosshair.IsVisible = false;
                        if (curCtx.InspectorAnno != null) curCtx.InspectorAnno.IsVisible = false;
                        avaPlot.Refresh();
                    }
                };
            }

            ctx.Series = s;
            ctx.UnitName = unitName;
            ctx.UnitScale = unitScale;
            ctx.HighlightMarker = highlightMarker;
            ctx.Crosshair = crosshair;
            ctx.InspectorAnno = inspectorAnno;
            ctx.LastHighlightedIndex = -1;
        }

        public void OpenOrSwitchToAlgoTab(string algoName)
        {
            if (string.IsNullOrWhiteSpace(algoName)) return;

            TabItem targetTab = null;

            if (_allDynamicTabs.TryGetValue(algoName, out var exactTab))
            {
                targetTab = exactTab;
            }
            else
            {
                var match = _allDynamicTabs.FirstOrDefault(kv =>
                    string.Equals(kv.Key, algoName, StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.Contains(algoName, StringComparison.OrdinalIgnoreCase) ||
                    algoName.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    (kv.Value.Tag as string)?.Contains(algoName, StringComparison.OrdinalIgnoreCase) == true);
                if (match.Value != null)
                {
                    targetTab = match.Value;
                }
            }

            if (targetTab != null)
            {
                if (!TabsMain.Items.Contains(targetTab))
                {
                    TabsMain.Items.Add(targetTab);
                    UpdateTabListMenuButton();
                }

                TabsMain.SelectedItem = targetTab;
                targetTab.BringIntoView();
                SetStatus($"Открыт график: {algoName}");
                return;
            }

            if (_lastResults != null && _lastResults.Count > 0)
            {
                RebuildPlotsAndSummary();
                if (_allDynamicTabs.TryGetValue(algoName, out var reloadedTab))
                {
                    if (!TabsMain.Items.Contains(reloadedTab))
                    {
                        TabsMain.Items.Add(reloadedTab);
                        UpdateTabListMenuButton();
                    }
                    TabsMain.SelectedItem = reloadedTab;
                    reloadedTab.BringIntoView();
                    SetStatus($"Открыт график: {algoName}");
                }
            }
        }

        private void OnSummaryAlgoClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string algoName)
            {
                OpenOrSwitchToAlgoTab(algoName);
            }
            else if ((sender as Control)?.DataContext is AlgoSummaryRow row)
            {
                OpenOrSwitchToAlgoTab(row.AlgoName);
            }
        }

        private void OnGridSummaryDoubleTapped(object sender, TappedEventArgs e)
        {
            var visual = e.Source as Visual;
            if (visual == null) return;
            if (visual.FindAncestorOfType<DataGridColumnHeader>() != null) return;
            if (visual.FindAncestorOfType<ScrollBar>() != null) return;

            var row = (visual.DataContext as AlgoSummaryRow) ?? (GridSummary.SelectedItem as AlgoSummaryRow);
            if (row != null)
            {
                OpenOrSwitchToAlgoTab(row.AlgoName);
            }
        }

        private void OnGridSummaryTapped(object sender, TappedEventArgs e)
        {
            var visual = e.Source as Visual;
            if (visual == null) return;
            if (visual.FindAncestorOfType<DataGridColumnHeader>() != null) return;
            if (visual.FindAncestorOfType<ScrollBar>() != null) return;

            var cell = visual.FindAncestorOfType<DataGridCell>();
            var row = (visual.DataContext as AlgoSummaryRow) ?? (GridSummary.SelectedItem as AlgoSummaryRow);
            if (row == null) return;

            if (GridSummary.CurrentColumn != null &&
                (GridSummary.CurrentColumn.DisplayIndex == 0 || GridSummary.CurrentColumn.Header?.ToString() == "Алгоритм"))
            {
                OpenOrSwitchToAlgoTab(row.AlgoName);
            }
        }

        private void OnGridSummaryKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (GridSummary.SelectedItem is AlgoSummaryRow row)
                {
                    e.Handled = true;
                    OpenOrSwitchToAlgoTab(row.AlgoName);
                }
            }
        }

        private void OnContextMenuOpenPlotClick(object sender, RoutedEventArgs e)
        {
            if (GridSummary.SelectedItem is AlgoSummaryRow row)
            {
                OpenOrSwitchToAlgoTab(row.AlgoName);
            }
        }

        private async void OnContextMenuCopyRowClick(object sender, RoutedEventArgs e)
        {
            if (GridSummary.SelectedItem is AlgoSummaryRow row && Clipboard != null)
            {
                string text = $"{row.AlgoName}\t{row.ClassName}\tMaxN={row.MaxN}\tШаг={row.Step}\tТочек={row.Points}\tМетрика={row.Metric}\tC={row.ConstantC}\tMSE={row.MSE}\tФакт={row.FactMaxN}\tБаза={row.BaseMaxN}\tΔ%={row.DiffPct}\tТеория={row.TheoryMaxN}";
                await Clipboard.SetTextAsync(text);
                SetStatus($"Строка «{row.AlgoName}» скопирована в буфер обмена");
            }
        }
    }
}
