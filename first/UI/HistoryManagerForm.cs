using System;
using System.Drawing;
using System.Windows.Forms;

namespace first
{
    public sealed class HistoryManagerForm : Form
    {
        private readonly DataGridView _grid;
        private readonly TextBox _txtNote;
        private readonly Button _btnSaveNote;
        private readonly Button _btnSetBaseline;
        private readonly Button _btnDelete;
        private readonly Button _btnClearAll;
        private readonly Button _btnClose;

        public event Action OnDataChanged;

        public HistoryManagerForm()
        {
            Text = "Управление историей замеров (SQLite)";
            Width = 860;
            Height = 490;
            MinimumSize = new Size(680, 360);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(24, 25, 30);
            ForeColor = Color.FromArgb(230, 237, 243);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(12, 9, 12, 0),
                BackColor = Color.FromArgb(31, 34, 41)
            };
            var lblInfo = new Label
            {
                Text = "Список сохранённых серий замеров. Назначайте эталон (Baseline), добавляйте заметки или удаляйте запуски.",
                AutoSize = true,
                ForeColor = Color.FromArgb(156, 163, 175)
            };
            pnlTop.Controls.Add(lblInfo);
            Controls.Add(pnlTop);

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                Padding = new Padding(12, 8, 12, 10),
                BackColor = Color.FromArgb(31, 34, 41)
            };

            var pnlNote = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var lblNote = new Label { Text = "Заметка для выбранного:", AutoSize = true, Margin = new Padding(0, 6, 6, 0), ForeColor = Color.FromArgb(209, 213, 219) };
            _txtNote = new TextBox
            {
                Width = 280,
                Margin = new Padding(0, 3, 8, 0),
                BackColor = Color.FromArgb(18, 20, 25),
                ForeColor = Color.FromArgb(243, 244, 246),
                BorderStyle = BorderStyle.FixedSingle
            };
            _btnSaveNote = new Button
            {
                Text = "💾 Сохранить заметку",
                AutoSize = true,
                Margin = new Padding(0, 2, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White
            };
            _btnSaveNote.FlatAppearance.BorderSize = 0;
            _btnSaveNote.Click += (s, e) => SaveSelectedNote();
            pnlNote.Controls.AddRange(new Control[] { lblNote, _txtNote, _btnSaveNote });
            pnlBottom.Controls.Add(pnlNote);

            var pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            Button CreateFlatButton(string text, Color textColor, Color bgColor)
            {
                var btn = new Button
                {
                    Text = text,
                    AutoSize = true,
                    ForeColor = textColor,
                    BackColor = bgColor,
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(0, 4, 10, 0)
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(55, 65, 81);
                return btn;
            }

            _btnSetBaseline = CreateFlatButton("⭐ Сделать эталоном", Color.FromArgb(251, 191, 36), Color.FromArgb(44, 49, 62));
            _btnDelete = CreateFlatButton("🗑️ Удалить запуск", Color.FromArgb(248, 113, 113), Color.FromArgb(44, 49, 62));
            _btnClearAll = CreateFlatButton("⚠️ Очистить всю базу", Color.FromArgb(239, 68, 68), Color.FromArgb(44, 49, 62));
            _btnClose = CreateFlatButton("Закрыть", Color.FromArgb(209, 213, 219), Color.FromArgb(44, 49, 62));
            _btnClose.DialogResult = DialogResult.OK;

            _btnSetBaseline.Click += (s, e) => SetSelectedBaseline();
            _btnDelete.Click += (s, e) => DeleteSelectedExperiment();
            _btnClearAll.Click += (s, e) => ClearAll();

            pnlButtons.Controls.AddRange(new Control[] { _btnSetBaseline, _btnDelete, _btnClearAll, _btnClose });
            pnlBottom.Controls.Add(pnlButtons);
            Controls.Add(pnlBottom);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                BackgroundColor = Color.FromArgb(24, 26, 32),
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(45, 50, 63)
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(18, 20, 25);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(209, 213, 219);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _grid.ColumnHeadersHeight = 32;
            _grid.RowTemplate.Height = 28;

            _grid.DefaultCellStyle.BackColor = Color.FromArgb(31, 34, 41);
            _grid.DefaultCellStyle.ForeColor = Color.FromArgb(230, 237, 243);
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 58, 77);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(26, 29, 36);

            _grid.Columns.Add("ExpId", "ID");
            _grid.Columns["ExpId"].Visible = false;
            _grid.Columns.Add("Date", "Дата и время");
            _grid.Columns["Date"].Width = 140;
            _grid.Columns.Add("Note", "Заметка / Название");
            _grid.Columns["Note"].Width = 180;
            _grid.Columns.Add("IsBase", "Эталон");
            _grid.Columns["IsBase"].Width = 65;
            _grid.Columns.Add("Algos", "Алгоритмы");
            _grid.Columns.Add("Count", "Замеров");
            _grid.Columns["Count"].Width = 70;

            _grid.SelectionChanged += (s, e) =>
            {
                if (_grid.CurrentRow != null)
                {
                    _txtNote.Text = Convert.ToString(_grid.CurrentRow.Cells["Note"].Value);
                }
            };

            Controls.Add(_grid);
            _grid.BringToFront();

            ReloadList();
        }

        private void ReloadList()
        {
            _grid.Rows.Clear();
            var list = BenchmarkDb.GetHistoryExperiments();
            foreach (var item in list)
            {
                string algosStr = string.Join(", ", item.Algorithms);
                _grid.Rows.Add(
                    item.ExperimentId,
                    item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    item.Note ?? "",
                    item.IsBaseline ? "⭐ Да" : "—",
                    algosStr,
                    item.TotalMeasurements
                );
            }
        }

        private string GetSelectedExpId()
        {
            if (_grid.CurrentRow == null) return null;
            return Convert.ToString(_grid.CurrentRow.Cells["ExpId"].Value);
        }

        private void SaveSelectedNote()
        {
            string id = GetSelectedExpId();
            if (string.IsNullOrEmpty(id)) return;
            BenchmarkDb.UpdateExperimentNote(id, _txtNote.Text.Trim());
            ReloadList();
            OnDataChanged?.Invoke();
        }

        private void SetSelectedBaseline()
        {
            string id = GetSelectedExpId();
            if (string.IsNullOrEmpty(id)) return;
            BenchmarkDb.SetBaseline(id, true);
            ReloadList();
            OnDataChanged?.Invoke();
        }

        private void DeleteSelectedExperiment()
        {
            string id = GetSelectedExpId();
            if (string.IsNullOrEmpty(id)) return;
            var ans = MessageBox.Show(this, "Вы уверены, что хотите удалить выбранный запуск из истории?", "Подтверждение удаления", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans == DialogResult.Yes)
            {
                BenchmarkDb.DeleteExperiment(id);
                ReloadList();
                OnDataChanged?.Invoke();
            }
        }

        private void ClearAll()
        {
            var ans = MessageBox.Show(this, "Вы действительно хотите полностью очистить ВСЮ историю замеров в benchmark.db?", "Очистка всей базы", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ans == DialogResult.Yes)
            {
                BenchmarkDb.ClearAllHistory();
                ReloadList();
                OnDataChanged?.Invoke();
            }
        }
    }
}
