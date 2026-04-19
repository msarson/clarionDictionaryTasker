using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Find fields with the same label + data type + size appearing on many
    // tables — candidates for extraction into a reusable field group.
    internal class DuplicateFieldsDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        ListView lv;
        Label lblSummary;

        public DuplicateFieldsDialog(object dict)
        {
            this.dict = dict;
            BuildUi();
            Populate();
        }

        void BuildUi()
        {
            Text = "Duplicate fields - " + DictModel.GetDictionaryName(dict);
            Width = 1000; Height = 640;
            MinimumSize = new Size(740, 380);
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
                Text = "Duplicate fields   (same label + type + size on multiple tables)"
            };

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 30,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 115, 135),
                Padding = new Padding(18, 8, 0, 0),
                Text = "Scanning..."
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true,
                HideSelection = false, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None
            };
            lv.Columns.Add("Label",   140);
            lv.Columns.Add("Type",     90);
            lv.Columns.Add("Size",     60, HorizontalAlignment.Right);
            lv.Columns.Add("Count",    60, HorizontalAlignment.Right);
            lv.Columns.Add("Tables",  560);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        sealed class Group
        {
            public string Label, Type, Size;
            public List<string> Tables = new List<string>();
        }

        void Populate()
        {
            var tables = DictModel.GetTables(dict);
            var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields == null) continue;
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var label = DictModel.AsString(DictModel.GetProp(f, "Label"))    ?? "";
                    if (string.IsNullOrEmpty(label)) continue;
                    var type  = DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "";
                    var size  = DictModel.AsString(DictModel.GetProp(f, "FieldSize"))?? "0";
                    var key   = label + "|" + type + "|" + size;
                    Group g;
                    if (!groups.TryGetValue(key, out g))
                    {
                        g = new Group { Label = label, Type = type, Size = size };
                        groups[key] = g;
                    }
                    g.Tables.Add(tName);
                }
            }

            var dupes = groups.Values
                              .Where(g => g.Tables.Count >= 2)
                              .OrderByDescending(g => g.Tables.Count)
                              .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
                              .ToList();

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var g in dupes)
            {
                var tablesDisplay = string.Join(", ",
                    g.Tables.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
                lv.Items.Add(new ListViewItem(new[] {
                    g.Label, g.Type, g.Size, g.Tables.Count.ToString(), tablesDisplay
                }));
            }
            lv.EndUpdate();

            lblSummary.Text = string.Format("{0} duplicate field group{1} across {2} tables.",
                dupes.Count, dupes.Count == 1 ? "" : "s", tables.Count);
        }
    }
}
