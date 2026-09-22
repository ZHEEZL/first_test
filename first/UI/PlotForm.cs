using System.Collections.Generic;
using System.Windows.Forms;

namespace first
{
    /// <summary>
    /// Окно с вкладками для отображения набора 2D графиков ScottPlot.
    /// </summary>
    public sealed class PlotForm : Form
    {
        public PlotForm(IEnumerable<KeyValuePair<string, ScottPlot.Plot>> plots)
        {
            Text = "Графики алгоритмов";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var kv in plots)
            {
                var page = new TabPage(kv.Key);
                var fp = new ScottPlot.FormsPlot { Dock = DockStyle.Fill };
                fp.Reset(kv.Value);
                fp.Refresh();
                page.Controls.Add(fp);
                tabs.TabPages.Add(page);
            }

            Controls.Add(tabs);
        }
    }
}
