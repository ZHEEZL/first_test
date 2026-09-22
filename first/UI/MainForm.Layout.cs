using System;
using System.Drawing;
using System.Windows.Forms;

namespace first
{
    public sealed partial class MainForm
    {
        private DataGridView _gridAlgos;
        private CheckBox _chkUseCache;
        private CheckBox _chkForceRecalc;
        private CheckBox _chkWarmup;
        private NumericUpDown _numSeed;
        private Button _btnRun;
        private Button _btnCancel;
        private ProgressBar _progressBar;
        private Label _lblStatus;

        private TabControl _tabsMain;
        private DataGridView _gridSummary;
        private TextBox _txtLog;
        private Label _lblTime;
        private Button _btnExportCsv;
        private Button _btnExportPng;

        // Панель сравнения с историей
        private FlowLayoutPanel _pnlHistoryCompare;
        private ComboBox _cmbBaselineRun;
        private CheckBox _chkShowHistory;
        private CheckBox _chkShowErrorBars;
        private Button _btnManageHistory;

        private TabPage _tabMatrix3D;
        private ComboBox _cmbMatrixPlotMode;
        private Button _btnResetCamera;
        private Button _btnSaveMatrix3D;
        private Matrix3DViewport _viewport3D;

        private void InitializeComponent()
        {
            Text = "Лабораторная работа №1 — Эмпирический анализ временной сложности алгоритмов";
            Width = 1440;
            Height = 870;
            MinimumSize = new Size(1060, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(24, 25, 30);
            ForeColor = Color.FromArgb(230, 237, 243);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 505,
                SplitterWidth = 2,
                BackColor = Color.FromArgb(45, 50, 63),
                FixedPanel = FixedPanel.Panel1
            };
            Controls.Add(split);

            Button CreateStyledButton(string text, Color textColor, Color bgColor, Color borderColor)
            {
                var btn = new Button
                {
                    Text = text,
                    AutoSize = true,
                    ForeColor = textColor,
                    BackColor = bgColor,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                    Margin = new Padding(0, 0, 6, 0),
                    Padding = new Padding(4, 2, 4, 2)
                };
                btn.FlatAppearance.BorderColor = borderColor;
                btn.FlatAppearance.BorderSize = 1;
                return btn;
            }

            // ================= LEFT PANEL =================
            var pnlLeft = split.Panel1;
            pnlLeft.BackColor = Color.FromArgb(31, 34, 41);

            var pnlTopLeftHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(8, 6, 8, 4),
                BackColor = Color.FromArgb(24, 26, 32)
            };

            var lblAlgoTitle = new Label
            {
                Text = "⚡ Алгоритмы и параметры",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(243, 244, 246),
                Dock = DockStyle.Top,
                Height = 24
            };
            pnlTopLeftHeader.Controls.Add(lblAlgoTitle);

            var pnlTopButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnSelectAll = CreateStyledButton("✓ Все", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));
            var btnDeselectAll = CreateStyledButton("✗ Снять", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));
            var btnResetDefaults = CreateStyledButton("↻ Сброс параметров", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));

            btnSelectAll.Click += (s, e) => { foreach (DataGridViewRow r in _gridAlgos.Rows) r.Cells[0].Value = true; };
            btnDeselectAll.Click += (s, e) => { foreach (DataGridViewRow r in _gridAlgos.Rows) r.Cells[0].Value = false; };
            btnResetDefaults.Click += (s, e) =>
            {
                for (int i = 0; i < _configs.Count; i++)
                {
                    _gridAlgos.Rows[i].Cells[3].Value = _configs[i].DefaultMaxN;
                    _gridAlgos.Rows[i].Cells[4].Value = _configs[i].DefaultStep;
                    _gridAlgos.Rows[i].Cells[5].Value = _configs[i].DefaultRuns;
                }
            };
            pnlTopButtons.Controls.AddRange(new Control[] { btnSelectAll, btnDeselectAll, btnResetDefaults });
            pnlTopLeftHeader.Controls.Add(pnlTopButtons);
            pnlLeft.Controls.Add(pnlTopLeftHeader);

            var pnlBottomLeft = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 236,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.FromArgb(38, 42, 53)
            };

            _chkUseCache = new CheckBox
            {
                Text = "Использовать кэш SQLite (benchmark.db)",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, 8)
            };
            _chkForceRecalc = new CheckBox
            {
                Text = "Принудительный перерасчет (без кэша)",
                Checked = false,
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, 30)
            };
            _chkWarmup = new CheckBox
            {
                Text = "Глобальный прогрев (JIT + CPU Turbo)",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(10, 52)
            };

            var lblSeed = new Label
            {
                Text = "Seed:",
                AutoSize = true,
                ForeColor = Color.FromArgb(156, 163, 175),
                Location = new Point(10, 78)
            };
            _numSeed = new NumericUpDown
            {
                Minimum = 1,
                Maximum = int.MaxValue,
                Value = 20240915,
                Width = 110,
                BackColor = Color.FromArgb(24, 26, 32),
                ForeColor = Color.FromArgb(243, 244, 246),
                Location = new Point(lblSeed.Right + 8, 74)
            };

            _btnRun = new Button
            {
                Text = "▶  ЗАПУСТИТЬ ЗАМЕРЫ",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 42),
                Location = new Point(10, 106),
                Cursor = Cursors.Hand
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += async (s, e) => await StartExperimentAsync();

            _btnCancel = new Button
            {
                Text = "■  Остановить",
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(55, 65, 81),
                ForeColor = Color.FromArgb(156, 163, 175),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Size = new Size(120, 42),
                Location = new Point(_btnRun.Right + 10, 106)
            };
            _btnCancel.FlatAppearance.BorderSize = 0;
            _btnCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                _btnCancel.Enabled = false;
                _lblStatus.Text = "Остановка эксперимента...";
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 8,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };

            _lblStatus = new Label
            {
                Text = "● Готов к запуску",
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 24,
                ForeColor = Color.FromArgb(156, 163, 175),
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnlBottomLeft.Controls.AddRange(new Control[] {
                _chkUseCache, _chkForceRecalc, _chkWarmup,
                lblSeed, _numSeed, _btnRun, _btnCancel,
                _lblStatus, _progressBar
            });
            pnlLeft.Controls.Add(pnlBottomLeft);

            _gridAlgos = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(27, 30, 38),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(42, 47, 58)
            };
            _gridAlgos.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(18, 20, 25);
            _gridAlgos.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(209, 213, 219);
            _gridAlgos.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _gridAlgos.ColumnHeadersHeight = 32;
            _gridAlgos.RowTemplate.Height = 28;

            _gridAlgos.DefaultCellStyle.BackColor = Color.FromArgb(31, 34, 41);
            _gridAlgos.DefaultCellStyle.ForeColor = Color.FromArgb(230, 237, 243);
            _gridAlgos.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 58, 77);
            _gridAlgos.DefaultCellStyle.SelectionForeColor = Color.White;
            _gridAlgos.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 28, 35);

            _gridAlgos.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Вкл", Width = 35 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Алгоритм", Width = 180, ReadOnly = true });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Класс", Width = 65, ReadOnly = true });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Max N", Width = 65 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шаг", Width = 55 });
            _gridAlgos.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Runs", Width = 55 });

            foreach (var cfg in _configs)
            {
                _gridAlgos.Rows.Add(cfg.Enabled, cfg.Name, cfg.ClassName, cfg.MaxN, cfg.Step, cfg.Runs);
            }
            pnlLeft.Controls.Add(_gridAlgos);
            _gridAlgos.BringToFront();

            // ================= RIGHT PANEL =================
            var pnlRight = split.Panel2;
            pnlRight.BackColor = Color.FromArgb(24, 25, 30);

            var pnlTopRight = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(8, 8, 8, 6),
                BackColor = Color.FromArgb(31, 34, 41)
            };
            _lblTime = new Label
            {
                Text = "",
                AutoSize = true,
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(96, 165, 250),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Padding = new Padding(0, 6, 8, 0)
            };
            var flowTopRight = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            _btnExportCsv = CreateStyledButton("📊 CSV", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));
            _btnExportPng = CreateStyledButton("🖼 PNG", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));

            _btnExportCsv.Click += (s, e) => ExportCsvDialog();
            _btnExportPng.Click += (s, e) => ExportPngDialog();

            _pnlHistoryCompare = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };

            var lblSep = new Label { Text = "│", AutoSize = true, ForeColor = Color.FromArgb(75, 85, 99), Margin = new Padding(2, 5, 4, 0) };
            var lblBaseline = new Label { Text = "База:", AutoSize = true, ForeColor = Color.FromArgb(156, 163, 175), Margin = new Padding(2, 6, 2, 0) };

            _cmbBaselineRun = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 190,
                Margin = new Padding(0, 2, 6, 0),
                BackColor = Color.FromArgb(18, 20, 25),
                ForeColor = Color.FromArgb(243, 244, 246),
                FlatStyle = FlatStyle.Flat
            };

            _chkShowHistory = new CheckBox
            {
                Text = "История",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(209, 213, 219),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 5, 6, 0)
            };

            _chkShowErrorBars = new CheckBox
            {
                Text = "Погрешности (±σ)",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(209, 213, 219),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 5, 6, 0)
            };

            _btnManageHistory = CreateStyledButton("⚙ База...", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));

            _cmbBaselineRun.SelectedIndexChanged += (s, e) => OnBaselineChanged();
            _chkShowHistory.CheckedChanged += (s, e) => OnHistoryToggleChanged();
            _chkShowErrorBars.CheckedChanged += (s, e) => OnHistoryToggleChanged();
            _btnManageHistory.Click += (s, e) => OpenHistoryManager();

            _pnlHistoryCompare.Controls.Add(lblSep);
            _pnlHistoryCompare.Controls.Add(lblBaseline);
            _pnlHistoryCompare.Controls.Add(_cmbBaselineRun);
            _pnlHistoryCompare.Controls.Add(_chkShowHistory);
            _pnlHistoryCompare.Controls.Add(_chkShowErrorBars);
            _pnlHistoryCompare.Controls.Add(_btnManageHistory);

            flowTopRight.Controls.Add(_btnExportCsv);
            flowTopRight.Controls.Add(_btnExportPng);
            flowTopRight.Controls.Add(_pnlHistoryCompare);

            pnlTopRight.Controls.Add(flowTopRight);
            pnlTopRight.Controls.Add(_lblTime);
            pnlRight.Controls.Add(pnlTopRight);

            // ================= TABS =================
            _tabsMain = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                ItemSize = new Size(135, 36),
                SizeMode = TabSizeMode.Normal
            };

            _tabsMain.DrawItem += (s, e) =>
            {
                var tab = _tabsMain.TabPages[e.Index];
                bool isSelected = (_tabsMain.SelectedIndex == e.Index);
                var bounds = e.Bounds;

                Color bg = isSelected ? Color.FromArgb(38, 42, 53) : Color.FromArgb(24, 26, 32);
                Color fg = isSelected ? Color.FromArgb(243, 244, 246) : Color.FromArgb(156, 163, 175);

                using (var brush = new SolidBrush(bg))
                {
                    e.Graphics.FillRectangle(brush, bounds);
                }

                if (isSelected)
                {
                    using var accentBrush = new SolidBrush(Color.FromArgb(59, 130, 246));
                    e.Graphics.FillRectangle(accentBrush, bounds.X, bounds.Bottom - 3, bounds.Width, 3);
                }

                using var font = new Font(_tabsMain.Font.FontFamily, 9f, isSelected ? FontStyle.Bold : FontStyle.Regular);
                using var textBrush = new SolidBrush(fg);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };
                e.Graphics.DrawString(tab.Text, font, textBrush, bounds, sf);
            };

            var tabSummary = new TabPage("📋 Сводка");
            tabSummary.BackColor = Color.FromArgb(24, 25, 30);

            _gridSummary = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(24, 26, 32),
                BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(42, 47, 58)
            };
            _gridSummary.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(18, 20, 25);
            _gridSummary.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(209, 213, 219);
            _gridSummary.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _gridSummary.ColumnHeadersHeight = 34;
            _gridSummary.RowTemplate.Height = 30;

            _gridSummary.DefaultCellStyle.BackColor = Color.FromArgb(31, 34, 41);
            _gridSummary.DefaultCellStyle.ForeColor = Color.FromArgb(230, 237, 243);
            _gridSummary.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 58, 77);
            _gridSummary.DefaultCellStyle.SelectionForeColor = Color.White;
            _gridSummary.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(26, 29, 36);

            _gridSummary.Columns.Add("Algo", "Алгоритм");
            _gridSummary.Columns.Add("Class", "Класс");
            _gridSummary.Columns.Add("MaxN", "Max N");
            _gridSummary.Columns.Add("Step", "Шаг");
            _gridSummary.Columns.Add("Runs", "Повторов");
            _gridSummary.Columns.Add("Pts", "Точек");
            _gridSummary.Columns.Add("Metric", "Метрика");
            _gridSummary.Columns.Add("MSE", "MSE");
            _gridSummary.Columns.Add("C", "Константа C");
            _gridSummary.Columns.Add("TFact", "Факт (MaxN)");
            _gridSummary.Columns.Add("TBase", "Базовый");
            _gridSummary.Columns.Add("Diff", "Динамика (Δ%)");
            _gridSummary.Columns.Add("TTheory", "Теория (MaxN)");
            _gridSummary.Columns.Add("Cache", "Из кэша");

            _gridSummary.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex == 11 && e.Value != null)
                {
                    string val = e.Value.ToString();
                    if (val.StartsWith("🟢"))
                    {
                        e.CellStyle.ForeColor = Color.FromArgb(52, 211, 153);
                        e.CellStyle.Font = new Font(_gridSummary.Font, FontStyle.Bold);
                    }
                    else if (val.StartsWith("🔴"))
                    {
                        e.CellStyle.ForeColor = Color.FromArgb(248, 113, 113);
                        e.CellStyle.Font = new Font(_gridSummary.Font, FontStyle.Bold);
                    }
                    else if (val.StartsWith("⚪"))
                    {
                        e.CellStyle.ForeColor = Color.FromArgb(156, 163, 175);
                    }
                }
            };
            tabSummary.Controls.Add(_gridSummary);
            _tabsMain.TabPages.Add(tabSummary);

            var tabLog = new TabPage("📝 Журнал");
            tabLog.BackColor = Color.FromArgb(18, 20, 25);
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.FromArgb(18, 20, 25),
                ForeColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.None
            };
            tabLog.Controls.Add(_txtLog);
            _tabsMain.TabPages.Add(tabLog);

            // Tab 2: 3D График (T × M × N) для матричного умножения
            _tabMatrix3D = new TabPage("🧊 Матрица 3D");
            _tabMatrix3D.BackColor = Color.FromArgb(24, 25, 30);
            var pnlMatrixTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 6, 8, 4),
                BackColor = Color.FromArgb(31, 34, 41)
            };

            var lblMode = new Label { Text = "Режим:", AutoSize = true, ForeColor = Color.FromArgb(156, 163, 175), Margin = new Padding(0, 6, 4, 0) };
            _cmbMatrixPlotMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 230,
                Margin = new Padding(0, 3, 12, 0),
                BackColor = Color.FromArgb(18, 20, 25),
                ForeColor = Color.FromArgb(243, 244, 246),
                FlatStyle = FlatStyle.Flat
            };
            _cmbMatrixPlotMode.Items.AddRange(new object[] {
                "3D Полигоны + Сетка (Unity)",
                "3D Каркас (Wireframe)",
                "3D Облако точек (Points)",
                "2D Тепловая карта (Heatmap)"
            });
            _cmbMatrixPlotMode.SelectedIndex = 0;
            _cmbMatrixPlotMode.SelectedIndexChanged += (s, e) => UpdateMatrix3DPlot();

            _btnResetCamera = CreateStyledButton("🎯 Сброс вида (Space)", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));
            _btnResetCamera.Click += (s, e) => _viewport3D?.ResetCamera();

            _btnSaveMatrix3D = CreateStyledButton("💾 Сохранить PNG...", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62), Color.FromArgb(55, 65, 81));
            _btnSaveMatrix3D.Enabled = false;
            _btnSaveMatrix3D.Click += (s, e) => SaveMatrix3DDialog();

            pnlMatrixTop.Controls.AddRange(new Control[] {
                lblMode, _cmbMatrixPlotMode,
                _btnResetCamera, _btnSaveMatrix3D
            });

            _viewport3D = new Matrix3DViewport { Dock = DockStyle.Fill };

            _tabMatrix3D.Controls.Add(_viewport3D);
            _tabMatrix3D.Controls.Add(pnlMatrixTop);
            _viewport3D.BringToFront();
            // Внимание: _tabMatrix3D добавляется в _tabsMain только при наличии рассчитанных данных матрицы!

            pnlRight.Controls.Add(_tabsMain);
            _tabsMain.BringToFront();

            _tabsMain.SelectedIndexChanged += (s, e) => OnTabChanged();

            _gridAlgos.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _configs.Count) return;
                string algoName = _configs[e.RowIndex].Name;
                SelectAlgoTab(algoName);
            };

            _gridSummary.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _gridSummary.Rows.Count) return;
                string algoName = Convert.ToString(_gridSummary.Rows[e.RowIndex].Cells[0].Value);
                SelectAlgoTab(algoName);
            };

            OnTabChanged();
        }
    }
}
