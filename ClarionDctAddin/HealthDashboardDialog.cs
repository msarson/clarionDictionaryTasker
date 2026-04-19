using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Read-only summary of the open dictionary — totals, largest tables,
    // driver mix bar chart, relation-density histogram.
    internal class HealthDashboardDialog : Form
    {
        static readonly Color BgColor      = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor   = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor  = Color.FromArgb(45,  90, 135);
        static readonly Color AccentColor  = Color.FromArgb(45,  90, 135);
        static readonly Color AccentLight  = Color.FromArgb(200, 220, 240);
        static readonly Color MutedColor   = Color.FromArgb(100, 115, 135);

        readonly object dict;
        Label lblStats;

        public HealthDashboardDialog(object dict)
        {
            this.dict = dict;
            BuildUi();
        }

        void BuildUi()
        {
            Text = "Health dashboard - " + DictModel.GetDictionaryName(dict);
            Width = 1080; Height = 760;
            MinimumSize = new Size(860, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            ShowIcon = false; ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Show;

            var header = new Label
            {
                Dock = DockStyle.Top, Height = 48,
                BackColor = HeaderColor, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Health dashboard   " + DictModel.GetDictionaryName(dict)
            };

            var tables = DictModel.GetTables(dict);
            int totalTables = tables.Count;
            int totalFields = tables.Sum(t => DictModel.CountEnumerable(t, "Fields"));
            int totalKeys   = tables.Sum(t => DictModel.CountEnumerable(t, "Keys"));
            int totalRel    = CountDictionaryRelations(dict);
            double avgFieldsPerTable = totalTables == 0 ? 0 : (double)totalFields / totalTables;

            lblStats = new Label
            {
                Dock = DockStyle.Top, Height = 74,
                BackColor = BgColor,
                Font = new Font("Segoe UI", 10F),
                ForeColor = MutedColor,
                Padding = new Padding(20, 14, 20, 10),
                Text = string.Format(
                    "Tables: {0}      Fields: {1}      Keys: {2}      Relations: {3}      Avg fields/table: {4:0.0}",
                    totalTables, totalFields, totalKeys, totalRel, avgFieldsPerTable)
            };

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = BgColor,
                Padding = new Padding(14)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            body.Controls.Add(BuildTopTablesPanel(tables), 0, 0);
            body.Controls.Add(BuildDriverMixPanel(tables), 1, 0);
            body.Controls.Add(BuildRelationsHistogramPanel(tables), 0, 1);
            body.SetColumnSpan(body.Controls[body.Controls.Count - 1], 2);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(body);
            Controls.Add(bottom);
            Controls.Add(lblStats);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        Control BuildTopTablesPanel(IList<object> tables)
        {
            var panel = MakeSectionPanel("Largest tables (top 10 by field count)");
            var lv = MakeListView();
            lv.Columns.Add("Table", 220);
            lv.Columns.Add("Fields", 70, HorizontalAlignment.Right);
            lv.Columns.Add("Keys",   60, HorizontalAlignment.Right);
            lv.Columns.Add("Driver", 90);

            var top = tables.Select(t => new
            {
                Name    = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?",
                Fields  = DictModel.CountEnumerable(t, "Fields"),
                Keys    = DictModel.CountEnumerable(t, "Keys"),
                Driver  = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? ""
            })
            .OrderByDescending(x => x.Fields)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
            foreach (var r in top)
                lv.Items.Add(new ListViewItem(new[] { r.Name, r.Fields.ToString(), r.Keys.ToString(), r.Driver }));
            panel.Controls.Add(lv);
            return panel;
        }

        Control BuildDriverMixPanel(IList<object> tables)
        {
            var panel = MakeSectionPanel("Driver distribution");
            var groups = tables.GroupBy(t => DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "(none)")
                               .Select(g => new { Driver = g.Key, Count = g.Count() })
                               .OrderByDescending(g => g.Count)
                               .ToList();
            int total = groups.Sum(g => g.Count);
            int maxCount = groups.Count > 0 ? groups.Max(g => g.Count) : 1;

            var chart = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            chart.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (groups.Count == 0) return;
                int rowH = 28;
                int y = 12;
                int leftLabel = 12, leftBar = 140;
                int availBar = chart.ClientSize.Width - leftBar - 120;
                using (var labelFont = new Font("Segoe UI", 9F))
                using (var labelBrush = new SolidBrush(Color.FromArgb(30, 40, 55)))
                using (var numBrush = new SolidBrush(MutedColor))
                using (var barFill = new SolidBrush(AccentColor))
                using (var barTrack = new SolidBrush(AccentLight))
                {
                    foreach (var grp in groups)
                    {
                        if (y + rowH > chart.ClientSize.Height) break;
                        g.DrawString(grp.Driver, labelFont, labelBrush, leftLabel, y + 4);
                        g.FillRectangle(barTrack, leftBar, y + 6, availBar, 14);
                        int w = Math.Max(2, (int)(availBar * (grp.Count / (double)maxCount)));
                        g.FillRectangle(barFill, leftBar, y + 6, w, 14);
                        var countText = grp.Count.ToString() + string.Format("   ({0:0.0}%)", 100.0 * grp.Count / Math.Max(1, total));
                        g.DrawString(countText, labelFont, numBrush, leftBar + availBar + 8, y + 4);
                        y += rowH;
                    }
                }
            };
            panel.Controls.Add(chart);
            return panel;
        }

        Control BuildRelationsHistogramPanel(IList<object> tables)
        {
            var panel = MakeSectionPanel("Relations per table");
            // Buckets: 0, 1, 2, 3, 4, 5, 6-9, 10+
            int[] buckets = new int[8];
            string[] labels = { "0", "1", "2", "3", "4", "5", "6-9", "10+" };
            foreach (var t in tables)
            {
                int c = DictModel.CountEnumerable(t, "Relations");
                int idx;
                if (c <= 5) idx = c;
                else if (c <= 9) idx = 6;
                else idx = 7;
                buckets[idx]++;
            }
            int maxCount = buckets.Max();

            var chart = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            chart.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int w = chart.ClientSize.Width, h = chart.ClientSize.Height;
                int pad = 30;
                int chartW = w - pad * 2;
                int chartH = h - pad - 30;
                int barCount = buckets.Length;
                int barGap = 18;
                int barW = Math.Max(10, (chartW - (barCount + 1) * barGap) / barCount);

                using (var barFill  = new SolidBrush(AccentColor))
                using (var labelFont = new Font("Segoe UI", 9F))
                using (var labelBrush = new SolidBrush(Color.FromArgb(30, 40, 55)))
                using (var numBrush  = new SolidBrush(MutedColor))
                {
                    for (int i = 0; i < barCount; i++)
                    {
                        int x = pad + barGap + i * (barW + barGap);
                        int bh = maxCount == 0 ? 0 : (int)(chartH * (buckets[i] / (double)maxCount));
                        int y = pad + (chartH - bh);
                        g.FillRectangle(barFill, x, y, barW, bh);
                        var count = buckets[i].ToString();
                        var labelSize = g.MeasureString(count, labelFont);
                        g.DrawString(count, labelFont, numBrush,
                            x + (barW - labelSize.Width) / 2, y - 16);
                        var axisLabel = labels[i];
                        var axisSize = g.MeasureString(axisLabel, labelFont);
                        g.DrawString(axisLabel, labelFont, labelBrush,
                            x + (barW - axisSize.Width) / 2, pad + chartH + 4);
                    }
                }
            };
            panel.Controls.Add(chart);
            return panel;
        }

        Panel MakeSectionPanel(string title)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor,
                Text = title
            };
            p.Controls.Add(lbl);
            return p;
        }

        static ListView MakeListView()
        {
            return new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        static int CountDictionaryRelations(object dict)
        {
            var seen = new HashSet<object>();
            var pool = DictModel.GetProp(dict, "RelationsPool") as IEnumerable;
            if (pool != null) foreach (var r in pool) if (r != null) seen.Add(r);
            foreach (var t in DictModel.GetTables(dict))
            {
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels != null) foreach (var r in rels) if (r != null) seen.Add(r);
            }
            return seen.Count;
        }
    }
}
