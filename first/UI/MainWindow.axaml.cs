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
using Avalonia.Interactivity;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;

namespace first
{
    public sealed class AlgoSummaryRow
    {
        public string AlgoName { get; set; }
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

            BtnExportCsv.Click += OnExportCsvClick;
            BtnExportPng.Click += OnExportPngClick;
            BtnManageHistory.Click += OnManageHistoryClick;

            CmbBaselineRun.SelectionChanged += (s, e) => OnBaselineChanged();
            ChkShowHistory.IsCheckedChanged += (s, e) => OnHistoryToggleChanged();
            ChkShowErrorBars.IsCheckedChanged += (s, e) => OnHistoryToggleChanged();

            TabsMain.SelectionChanged += (s, e) => OnTabChanged();
            PnlHistoryCompare.IsVisible = false;
        }

        private void OnTabChanged()
        {
            var tab = TabsMain.SelectedItem as TabItem;
            if (tab == null)
            {
                PnlHistoryCompare.IsVisible = false;
                return;
            }

            string header = tab.Header?.ToString() ?? "";
            if (!header.StartsWith("📈 "))
            {
                PnlHistoryCompare.IsVisible = false;
                return;
            }

            PnlHistoryCompare.IsVisible = true;
            string algoName = (tab.Tag as string) ?? header.Substring(header.IndexOf(' ') + 1).Trim();
            UpdateBaselineComboForAlgo(algoName);
            EnsureTabPlotUpdated(tab, algoName);
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

            string header = tab.Header?.ToString() ?? "";
            if (!header.StartsWith("📈 ")) return;

            string algoName = (tab.Tag as string) ?? header.Substring(header.IndexOf(' ') + 1).Trim();
            ApplyBaselineSelectionForAlgo(algoName);

            var series = _lastResults.FirstOrDefault(s => s.Algo.Name == algoName);
            if (series != null)
            {
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
                        avaPlot.Refresh();
                    }

                    int pIdx = _lastPlots.FindIndex(kv => kv.Key == algoName);
                    if (pIdx >= 0)
                        _lastPlots[pIdx] = single[0];
                    else
                        _lastPlots.Add(single[0]);
                }

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

                        var vp3d = new Matrix3DViewport();
                        vp3d.SetData(mSeries.MatrixTimes, mSeries.MatrixStepM, mSeries.MatrixStepN, "мс");

                        var btnShaded = new Button { Content = "Полигоны (Shaded)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnShaded.Click += (s, e) => { vp3d.Mode = ViewportMode.ShadedWireframe; vp3d.InvalidateVisual(); };

                        var btnWire = new Button { Content = "Каркас (Wireframe)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnWire.Click += (s, e) => { vp3d.Mode = ViewportMode.WireframeOnly; vp3d.InvalidateVisual(); };

                        var btnPoints = new Button { Content = "Точки (PointCloud)", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnPoints.Click += (s, e) => { vp3d.Mode = ViewportMode.PointCloud; vp3d.InvalidateVisual(); };

                        var btnHeatmap = new Button { Content = "2D Heatmap", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnHeatmap.Click += (s, e) => { vp3d.Mode = ViewportMode.Heatmap2D; vp3d.InvalidateVisual(); };

                        var btnResetCam = new Button { Content = "↺ Сброс камеры", FontSize = 11, Padding = new Thickness(8, 4) };
                        btnResetCam.Click += (s, e) => vp3d.ResetCamera();

                        toolBar3d.Children.Add(btnShaded);
                        toolBar3d.Children.Add(btnWire);
                        toolBar3d.Children.Add(btnPoints);
                        toolBar3d.Children.Add(btnHeatmap);
                        toolBar3d.Children.Add(btnResetCam);

                        Grid.SetRow(toolBar3d, 0);
                        Grid.SetRow(vp3d, 1);
                        panel3d.Children.Add(toolBar3d);
                        panel3d.Children.Add(vp3d);

                        var tabItem3D = new TabItem
                        {
                            Header = "🧊 Матрица 3D",
                            Tag = kv.Key,
                            Content = panel3d,
                            FontSize = 13
                        };

                        _dynamicTabs.Add(tabItem3D);
                        TabsMain.Items.Add(tabItem3D);
                        continue;
                    }
                }

                var avaPlot = new AvaPlot();
                avaPlot.Reset(kv.Value);
                avaPlot.Refresh();

                var tabItem = new TabItem
                {
                    Header = $"📈 {kv.Key}",
                    Tag = kv.Key,
                    Content = avaPlot,
                    FontSize = 13
                };

                _dynamicTabs.Add(tabItem);
                TabsMain.Items.Add(tabItem);
            }

            TabsMain.SelectedIndex = 0;
        }

        private void ClearDynamicTabs()
        {
            foreach (var tab in _dynamicTabs)
            {
                TabsMain.Items.Remove(tab);
            }
            _dynamicTabs.Clear();
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
                    string header = tab.Header?.ToString() ?? "";
                    if (header.StartsWith("📈 "))
                    {
                        string algoName = (tab.Tag as string) ?? header.Substring(header.IndexOf(' ') + 1).Trim();
                        UpdateBaselineComboForAlgo(algoName);
                        OnBaselineChanged();
                    }
                }
            };
            dlg.ShowDialog(this);
        }
    }
}
