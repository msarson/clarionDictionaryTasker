using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Pick two tables from the open dictionary and diff their fields and keys.
    // Useful for CLIENTES vs CLIENTES_ARCHIVO style archive tables.
    internal class CompareTablesDialog : Form
    {
        static readonly Color BgColor      = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor   = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor  = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor   = Color.FromArgb(100, 115, 135);
        static readonly Color SameColor    = Color.FromArgb(60, 120, 60);
        static readonly Color DiffColor    = Color.FromArgb(190, 110, 20);
        static readonly Color OnlyColor    = Color.FromArgb(170, 50, 50);

        readonly object dict;
        ComboBox cbA, cbB;
        ListView lvFields, lvKeys;
        Label    lblSummary;
        List<object> tables;

        public CompareTablesDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Compare tables - " + DictModel.GetDictionaryName(dict);
            Width = 1120; Height = 740;
            MinimumSize = new Size(880, 500);
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
                Text = "Compare tables   " + DictModel.GetDictionaryName(dict)
            };

            var top = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = BgColor, Padding = new Padding(16, 14, 16, 8) };
            var lblA = new Label { Text = "Table A:", Left = 4, Top = 10, Width = 58, Font = new Font("Segoe UI", 9F) };
            cbA = new ComboBox { Left = 66, Top = 6, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            var lblB = new Label { Text = "Table B:", Left = 484, Top = 10, Width = 58, Font = new Font("Segoe UI", 9F) };
            cbB = new ComboBox { Left = 546, Top = 6, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            top.Controls.Add(lblA); top.Controls.Add(cbA); top.Controls.Add(lblB); top.Controls.Add(cbB);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 26,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = "Pick two tables to compare."
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BgColor, Panel1MinSize = 140, Panel2MinSize = 100
            };

            lvFields = MakeListView();
            lvFields.Columns.Add("Field",   200);
            lvFields.Columns.Add("In A",     60, HorizontalAlignment.Center);
            lvFields.Columns.Add("In B",     60, HorizontalAlignment.Center);
            lvFields.Columns.Add("Status",  100);
            lvFields.Columns.Add("A: type / size / picture", 240);
            lvFields.Columns.Add("B: type / size / picture", 240);
            split.Panel1.Controls.Add(WrapSection("Fields", lvFields));

            lvKeys = MakeListView();
            lvKeys.Columns.Add("Key",       200);
            lvKeys.Columns.Add("In A",       60, HorizontalAlignment.Center);
            lvKeys.Columns.Add("In B",       60, HorizontalAlignment.Center);
            lvKeys.Columns.Add("Status",    100);
            lvKeys.Columns.Add("A: components",           240);
            lvKeys.Columns.Add("B: components",           240);
            split.Panel2.Controls.Add(WrapSection("Keys", lvKeys));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(split);
            Controls.Add(lblSummary);
            Controls.Add(bottom);
            Controls.Add(top);
            Controls.Add(header);
            CancelButton = btnClose;

            Load += delegate { split.SplitterDistance = (int)(split.Height * 0.62); };

            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
            {
                var n = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                cbA.Items.Add(n);
                cbB.Items.Add(n);
            }
            if (cbA.Items.Count > 0) cbA.SelectedIndex = 0;
            if (cbB.Items.Count > 1) cbB.SelectedIndex = 1;
            else if (cbB.Items.Count > 0) cbB.SelectedIndex = 0;

            cbA.SelectedIndexChanged += delegate { Recompute(); };
            cbB.SelectedIndexChanged += delegate { Recompute(); };
            Recompute();
        }

        static Control WrapSection(string title, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor,
                Text = title
            };
            content.Dock = DockStyle.Fill;
            panel.Controls.Add(content);
            panel.Controls.Add(lbl);
            return panel;
        }

        static ListView MakeListView()
        {
            return new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        void Recompute()
        {
            if (cbA.SelectedIndex < 0 || cbB.SelectedIndex < 0) return;
            var ta = tables[cbA.SelectedIndex];
            var tb = tables[cbB.SelectedIndex];

            var fa = FieldsByLabel(ta);
            var fb = FieldsByLabel(tb);
            var allFieldKeys = fa.Keys.Concat(fb.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            lvFields.BeginUpdate();
            lvFields.Items.Clear();
            int sameF = 0, diffF = 0, onlyAF = 0, onlyBF = 0;
            foreach (var label in allFieldKeys)
            {
                object a, b;
                fa.TryGetValue(label, out a);
                fb.TryGetValue(label, out b);
                var aSig = a == null ? "" : FieldSig(a);
                var bSig = b == null ? "" : FieldSig(b);
                string status; Color color;
                if (a != null && b == null) { status = "Only in A"; color = OnlyColor; onlyAF++; }
                else if (a == null && b != null) { status = "Only in B"; color = OnlyColor; onlyBF++; }
                else if (string.Equals(aSig, bSig, StringComparison.Ordinal)) { status = "Same"; color = SameColor; sameF++; }
                else { status = "Differs"; color = DiffColor; diffF++; }
                var it = new ListViewItem(new[] {
                    label, a != null ? "yes" : "", b != null ? "yes" : "", status, aSig, bSig
                });
                it.ForeColor = color;
                lvFields.Items.Add(it);
            }
            lvFields.EndUpdate();

            var ka = KeysByName(ta);
            var kb = KeysByName(tb);
            var allKeyNames = ka.Keys.Concat(kb.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            lvKeys.BeginUpdate();
            lvKeys.Items.Clear();
            int sameK = 0, diffK = 0, onlyAK = 0, onlyBK = 0;
            foreach (var name in allKeyNames)
            {
                object a, b;
                ka.TryGetValue(name, out a);
                kb.TryGetValue(name, out b);
                var aSig = a == null ? "" : KeySig(a);
                var bSig = b == null ? "" : KeySig(b);
                string status; Color color;
                if (a != null && b == null) { status = "Only in A"; color = OnlyColor; onlyAK++; }
                else if (a == null && b != null) { status = "Only in B"; color = OnlyColor; onlyBK++; }
                else if (string.Equals(aSig, bSig, StringComparison.Ordinal)) { status = "Same"; color = SameColor; sameK++; }
                else { status = "Differs"; color = DiffColor; diffK++; }
                var it = new ListViewItem(new[] {
                    name, a != null ? "yes" : "", b != null ? "yes" : "", status, aSig, bSig
                });
                it.ForeColor = color;
                lvKeys.Items.Add(it);
            }
            lvKeys.EndUpdate();

            lblSummary.Text = string.Format(
                "Fields: {0} same, {1} differ, {2} only-A, {3} only-B     Keys: {4} same, {5} differ, {6} only-A, {7} only-B",
                sameF, diffF, onlyAF, onlyBF, sameK, diffK, onlyAK, onlyBK);
        }

        static Dictionary<string, object> FieldsByLabel(object table)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var en = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (en == null) return d;
            foreach (var f in en)
            {
                if (f == null) continue;
                var lbl = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                if (string.IsNullOrEmpty(lbl)) continue;
                if (!d.ContainsKey(lbl)) d[lbl] = f;
            }
            return d;
        }

        static Dictionary<string, object> KeysByName(object table)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var en = DictModel.GetProp(table, "Keys") as IEnumerable;
            if (en == null) return d;
            foreach (var k in en)
            {
                if (k == null) continue;
                var n = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                if (string.IsNullOrEmpty(n)) continue;
                if (!d.ContainsKey(n)) d[n] = k;
            }
            return d;
        }

        static string FieldSig(object f)
        {
            var dt   = DictModel.AsString(DictModel.GetProp(f, "DataType"))      ?? "";
            var size = DictModel.AsString(DictModel.GetProp(f, "FieldSize"))     ?? "";
            var pic  = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
            return dt + " / " + size + (string.IsNullOrEmpty(pic) ? "" : " / " + pic);
        }

        static string KeySig(object k)
        {
            var comps = ComponentSummary(k);
            var unique = DictModel.AsString(DictModel.GetProp(k, "AttributeUnique"));
            var prim   = DictModel.AsString(DictModel.GetProp(k, "AttributePrimary"));
            var tags = new List<string>();
            if (string.Equals(unique, "True", StringComparison.OrdinalIgnoreCase)) tags.Add("unique");
            if (string.Equals(prim,   "True", StringComparison.OrdinalIgnoreCase)) tags.Add("primary");
            return comps + (tags.Count > 0 ? "   [" + string.Join(", ", tags.ToArray()) + "]" : "");
        }

        static string ComponentSummary(object key)
        {
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) return "";
            var names = new List<string>();
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            return string.Join(" + ", names.ToArray());
        }
    }
}
