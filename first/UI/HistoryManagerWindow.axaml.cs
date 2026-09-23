using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace first
{
    public sealed class HistoryExpRow
    {
        public string ExperimentId { get; set; }
        public string ShortId => ExperimentId != null && ExperimentId.Length >= 8 ? ExperimentId.Substring(0, 8) : ExperimentId;
        public DateTime CreatedAt { get; set; }
        public string CreatedAtFormatted => CreatedAt.ToString("dd.MM.yyyy HH:mm");
        public int TotalMeasurements { get; set; }
        public bool IsBaseline { get; set; }
        public string BaselineDisplay => IsBaseline ? "⭐ Да" : "Нет";
        public string Note { get; set; }
        public List<string> Algorithms { get; set; } = new List<string>();
        public string AlgorithmsDisplay => string.Join(", ", Algorithms);
    }

    public partial class HistoryManagerWindow : Window
    {
        public event Action OnDataChanged;

        private List<HistoryExpRow> _rows = new List<HistoryExpRow>();

        public HistoryManagerWindow()
        {
            InitializeComponent();
            ReloadData();

            GridHistory.SelectionChanged += OnSelectionChanged;
            BtnSaveNote.Click += OnSaveNoteClick;
            BtnSetBaseline.Click += OnSetBaselineClick;
            BtnDelete.Click += OnDeleteClick;
            BtnClearAll.Click += OnClearAllClick;
            BtnClose.Click += (s, e) => Close();
        }

        private void ReloadData()
        {
            var exps = BenchmarkDb.GetHistoryExperiments();
            _rows = exps.Select(e => new HistoryExpRow
            {
                ExperimentId = e.ExperimentId,
                CreatedAt = e.CreatedAt,
                TotalMeasurements = e.TotalMeasurements,
                IsBaseline = e.IsBaseline,
                Note = e.Note,
                Algorithms = e.Algorithms
            }).ToList();

            GridHistory.ItemsSource = null;
            GridHistory.ItemsSource = _rows;
        }

        private HistoryExpRow SelectedRow => GridHistory.SelectedItem as HistoryExpRow;

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = SelectedRow;
            TxtNote.Text = row?.Note ?? "";
        }

        private void OnSaveNoteClick(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null) return;
            string newNote = TxtNote.Text?.Trim();
            BenchmarkDb.UpdateExperimentNote(row.ExperimentId, newNote);
            row.Note = newNote;
            ReloadData();
            OnDataChanged?.Invoke();
        }

        private void OnSetBaselineClick(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null) return;
            BenchmarkDb.SetBaseline(row.ExperimentId, true);
            ReloadData();
            OnDataChanged?.Invoke();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var row = SelectedRow;
            if (row == null) return;
            BenchmarkDb.DeleteExperiment(row.ExperimentId);
            ReloadData();
            OnDataChanged?.Invoke();
        }

        private void OnClearAllClick(object sender, RoutedEventArgs e)
        {
            BenchmarkDb.ClearAllHistory();
            ReloadData();
            OnDataChanged?.Invoke();
        }
    }
}
