using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Tables that appear in zero relations — neither parent nor child. Looks
    // at DDDataDictionary.RelationsPool plus every DDFile.Relations list so
    // tables referenced only as a parent elsewhere still count as live.
    internal class DeadTablesDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        ListView lv;
        Label lblSummary;

        public DeadTablesDialog(object dict)
        {
            this.dict = dict;
            BuildUi();
            Populate();
        }

        void BuildUi()
        {
            Text = "Dead tables - " + DictModel.GetDictionaryName(dict);
            Width = 920; Height = 540;
            MinimumSize = new Size(680, 360);
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
                Text = "Dead tables   (no relations, neither parent nor child)"
            };

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 32,
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
            lv.Columns.Add("Name", 220);
            lv.Columns.Add("Fields", 70, HorizontalAlignment.Right);
            lv.Columns.Add("Keys", 60, HorizontalAlignment.Right);
            lv.Columns.Add("Driver", 100);
            lv.Columns.Add("Description", 380);

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

        void Populate()
        {
            var referenced = new HashSet<object>();

            // Dict-level relations pool catches everything.
            var pool = DictModel.GetProp(dict, "RelationsPool") as IEnumerable;
            if (pool != null)
                foreach (var rel in pool)
                    AddRelationEnds(rel, referenced);

            // Walk every table's own Relations list too — some builds use only
            // per-table storage.
            foreach (var t in DictModel.GetTables(dict))
            {
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels != null)
                    foreach (var rel in rels)
                        AddRelationEnds(rel, referenced);
            }

            var tables = DictModel.GetTables(dict);
            var dead = tables.Where(t => !referenced.Contains(t))
                             .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                             .ToList();

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var t in dead)
            {
                var name   = DictModel.AsString(DictModel.GetProp(t, "Name"))         ?? "?";
                var desc   = DictModel.AsString(DictModel.GetProp(t, "Description"))  ?? "";
                var driver = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
                var fCount = DictModel.CountEnumerable(t, "Fields");
                var kCount = DictModel.CountEnumerable(t, "Keys");
                lv.Items.Add(new ListViewItem(new[] { name, fCount.ToString(), kCount.ToString(), driver, desc }));
            }
            lv.EndUpdate();

            lblSummary.Text = string.Format("{0} dead table{1} out of {2} total.",
                dead.Count, dead.Count == 1 ? "" : "s", tables.Count);
        }

        static void AddRelationEnds(object rel, HashSet<object> set)
        {
            if (rel == null) return;
            string[] parentCandidates = { "ParentFile", "PrimaryFile", "Parent", "FromFile", "From", "LookupFile", "MasterFile" };
            string[] childCandidates  = { "ChildFile",  "RelatedFile", "Child",  "ToFile",   "To",   "File",       "DetailFile", "ForeignFile" };
            foreach (var n in parentCandidates) { var v = DictModel.GetProp(rel, n); if (v != null) { set.Add(v); break; } }
            foreach (var n in childCandidates)  { var v = DictModel.GetProp(rel, n); if (v != null) { set.Add(v); break; } }
        }
    }
}
