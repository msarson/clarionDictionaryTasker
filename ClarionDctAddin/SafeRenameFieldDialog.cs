using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Rename one field's label and auto-update downstream references.
    //
    // Safe parts (handled automatically):
    //   - Keys and relations hold references to the field *object*, so their
    //     components keep pointing at the renamed field — no action needed.
    //
    // Risky parts (user review):
    //   - Trigger bodies are TEXT. A preview lists every trigger mentioning
    //     the old label, per occurrence; apply step does a word-boundary
    //     regex replace on `OLDLABEL` and `PREFIX:OLDLABEL`.
    internal class SafeRenameFieldDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);
        static readonly Color OkColor     = Color.FromArgb(40, 120, 40);
        static readonly Color WarnColor   = Color.FromArgb(170, 95, 10);

        readonly object dict;
        ComboBox cbTable, cbField;
        TextBox  txtNewLabel;
        CheckBox chkPatchTriggers;
        ListView lv;
        Label    lblSummary;
        Button   btnApply;
        List<object> tables;

        public SafeRenameFieldDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Safe rename field - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 720;
            MinimumSize = new Size(860, 480);
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
                Text = "Safe rename field   " + DictModel.GetDictionaryName(dict)
            };

            var row1 = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BgColor, Padding = new Padding(16, 10, 16, 0) };
            var lblT = new Label { Text = "Table:", Left = 4,   Top = 8, Width = 46, Font = new Font("Segoe UI", 9F) };
            cbTable = new ComboBox { Left = 52,  Top = 4, Width = 320, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            var lblF = new Label { Text = "Field:", Left = 388, Top = 8, Width = 42, Font = new Font("Segoe UI", 9F) };
            cbField = new ComboBox { Left = 434, Top = 4, Width = 320, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9F) };
            row1.Controls.Add(lblT); row1.Controls.Add(cbTable); row1.Controls.Add(lblF); row1.Controls.Add(cbField);

            var row2 = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BgColor, Padding = new Padding(16, 6, 16, 0) };
            var lblN = new Label { Text = "New label:", Left = 4, Top = 8, Width = 70, Font = new Font("Segoe UI", 9F) };
            txtNewLabel = new TextBox { Left = 76, Top = 4, Width = 320, Font = new Font("Segoe UI", 10F) };
            chkPatchTriggers = new CheckBox
            {
                Text = "Also substitute in trigger bodies (word-boundary, PREFIX:label-aware)",
                Left = 412, Top = 6, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F)
            };
            row2.Controls.Add(lblN); row2.Controls.Add(txtNewLabel); row2.Controls.Add(chkPatchTriggers);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = ""
            };

            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };
            lv.Columns.Add("Kind",   100);
            lv.Columns.Add("Table",  160);
            lv.Columns.Add("Item",   200);
            lv.Columns.Add("Handling", 120);
            lv.Columns.Add("Detail", 520);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            btnApply = new Button { Text = "Preview && apply...", Width = 170, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnApply.Click += delegate { DoApply(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnApply);

            Controls.Add(lv);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(row2);
            Controls.Add(row1);
            Controls.Add(header);
            CancelButton = btnClose;

            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
                cbTable.Items.Add(DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?");

            cbTable.SelectedIndexChanged += delegate { PopulateFields(); Rescan(); };
            cbField.SelectedIndexChanged += delegate { Rescan(); };
            txtNewLabel.TextChanged      += delegate { Rescan(); };
            if (cbTable.Items.Count > 0) cbTable.SelectedIndex = 0;
        }

        void PopulateFields()
        {
            cbField.Items.Clear();
            if (cbTable.SelectedIndex < 0) return;
            var t = tables[cbTable.SelectedIndex];
            var list = new List<string>();
            foreach (var f in FieldMutator.EnumerateFields(t))
            {
                var lbl = DictModel.AsString(DictModel.GetProp(f, "Label"));
                if (!string.IsNullOrEmpty(lbl)) list.Add(lbl);
            }
            foreach (var l in list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) cbField.Items.Add(l);
            if (cbField.Items.Count > 0) cbField.SelectedIndex = 0;
        }

        void Rescan()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            if (cbTable.SelectedIndex < 0 || cbField.SelectedIndex < 0)
            {
                lv.EndUpdate();
                lblSummary.Text = "Pick a table, a field, and type a new label.";
                return;
            }
            var homeTable = tables[cbTable.SelectedIndex];
            var homeName  = DictModel.AsString(DictModel.GetProp(homeTable, "Name")) ?? "";
            var prefix    = DictModel.AsString(DictModel.GetProp(homeTable, "Prefix")) ?? "";
            var oldLabel  = cbField.SelectedItem as string ?? "";
            var newLabel  = (txtNewLabel.Text ?? "").Trim();

            // Home field itself
            Add("Field",  homeName, oldLabel, "rename",
                "Label: \"" + oldLabel + "\" -> \"" + (string.IsNullOrEmpty(newLabel) ? "(not set)" : newLabel) + "\"", OkColor);

            // Keys that reference the field — these auto-update via object reference
            int keyRefs = 0;
            foreach (var k in DictModel.GetProp(homeTable, "Keys") as IEnumerable ?? new object[0])
            {
                if (k == null) continue;
                if (KeyReferencesField(k, oldLabel))
                {
                    Add("Key", homeName, DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "?", "auto-update",
                        "Key references field object directly — updates follow the rename.", OkColor);
                    keyRefs++;
                }
            }

            int relRefs = 0;
            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                var rels  = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels == null) continue;
                foreach (var r in rels)
                {
                    if (r == null) continue;
                    if (RelationReferencesField(r, oldLabel))
                    {
                        Add("Relation", tName, DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "?", "auto-update",
                            "Relation components hold field references — updates follow.", OkColor);
                        relRefs++;
                    }
                }
            }

            int trigRefs = 0;
            var oldWord    = Regex.Escape(oldLabel);
            var oldPrefix  = Regex.Escape(prefix + ":" + oldLabel);
            var combinedPattern = @"\b(" + oldPrefix + "|" + oldWord + @")\b";
            var combinedRe = new Regex(combinedPattern, RegexOptions.IgnoreCase);
            foreach (var t in tables)
            {
                var tName = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                foreach (var tr in FieldMutator.EnumerateTriggers(t))
                {
                    var body = FieldMutator.GetTriggerBody(tr);
                    if (string.IsNullOrEmpty(body)) continue;
                    var matches = combinedRe.Matches(body);
                    if (matches.Count == 0) continue;
                    Add("Trigger", tName, DictModel.AsString(DictModel.GetProp(tr, "Name")) ?? "?",
                        chkPatchTriggers.Checked ? "text-replace" : "MANUAL",
                        matches.Count + " occurrence(s): " + ClipAround(body, matches),
                        chkPatchTriggers.Checked ? OkColor : WarnColor);
                    trigRefs += matches.Count;
                }
            }
            lv.EndUpdate();
            lblSummary.Text = string.Format("Field + {0} key ref(s) + {1} relation ref(s) + {2} trigger occurrence(s).",
                keyRefs, relRefs, trigRefs);
        }

        void Add(string kind, string table, string item, string handling, string detail, Color color)
        {
            var it = new ListViewItem(new[] { kind, table, item, handling, detail });
            it.ForeColor = color;
            lv.Items.Add(it);
        }

        void DoApply()
        {
            if (cbTable.SelectedIndex < 0 || cbField.SelectedIndex < 0)
            { MessageBox.Show(this, "Pick a table and field first.", "Safe rename", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var homeTable = tables[cbTable.SelectedIndex];
            var prefix    = DictModel.AsString(DictModel.GetProp(homeTable, "Prefix")) ?? "";
            var oldLabel  = cbField.SelectedItem as string ?? "";
            var newLabel  = (txtNewLabel.Text ?? "").Trim();
            if (string.IsNullOrEmpty(newLabel) || newLabel == oldLabel)
            { MessageBox.Show(this, "Enter a new label different from the current one.", "Safe rename", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            // Find the field object by label.
            object targetField = null;
            foreach (var f in FieldMutator.EnumerateFields(homeTable))
            {
                if (string.Equals(DictModel.AsString(DictModel.GetProp(f, "Label")), oldLabel, StringComparison.OrdinalIgnoreCase))
                { targetField = f; break; }
            }
            if (targetField == null)
            { MessageBox.Show(this, "Field not found.", "Safe rename", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            var confirm = MessageBox.Show(this,
                "Rename " + oldLabel + " -> " + newLabel + " ?\r\n\r\n"
                + (chkPatchTriggers.Checked
                    ? "Trigger bodies WILL be patched (word-boundary replace of OLDLABEL and PREFIX:OLDLABEL)."
                    : "Trigger bodies will NOT be touched. You'll need to update them manually.")
                + "\r\n\r\nA .tasker-bak-<timestamp> backup is written first.",
                "Apply safe rename", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var r = new FieldMutator.Result();
            FieldMutator.Backup(DictModel.GetDictionaryFileName(dict), r);
            if (r.BackupFailed)
            { MessageBox.Show(this, "Backup failed — aborting.\r\n" + string.Join("\r\n", r.Messages.ToArray()),
                "Safe rename", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // 1. Field label
            if (FieldMutator.SetStringProp(targetField, "Label", newLabel, r, "field"))
                r.Changed++;
            else r.Failed++;

            // 2. Trigger body substitution (optional)
            int triggersChanged = 0;
            if (chkPatchTriggers.Checked)
            {
                var oldWord   = Regex.Escape(oldLabel);
                var oldPrefix = Regex.Escape(prefix + ":" + oldLabel);
                var pref = prefix + ":";
                var pattern = @"\b(?:(?<pre>" + oldPrefix + ")|(?<plain>" + oldWord + @"))\b";
                var re = new Regex(pattern, RegexOptions.IgnoreCase);
                foreach (var t in tables)
                {
                    foreach (var tr in FieldMutator.EnumerateTriggers(t))
                    {
                        var body = FieldMutator.GetTriggerBody(tr);
                        if (string.IsNullOrEmpty(body)) continue;
                        if (!re.IsMatch(body)) continue;
                        var newBody = re.Replace(body,
                            m => m.Groups["pre"].Success ? pref + newLabel : newLabel);
                        var targetProp = FirstWritableTriggerBodyProp(tr);
                        if (targetProp == null) { r.Failed++; r.Messages.Add("trigger " + (DictModel.AsString(DictModel.GetProp(tr, "Name")) ?? "?") + ": no writable body"); continue; }
                        if (FieldMutator.SetStringProp(tr, targetProp, newBody, r,
                            "trigger " + (DictModel.AsString(DictModel.GetProp(tr, "Name")) ?? "?")))
                        { r.Changed++; triggersChanged++; }
                        else r.Failed++;
                    }
                }
            }

            FieldMutator.ForceMarkDirty(dict, DictModel.GetActiveDictionaryView(), r);

            var summary = "Changed: " + r.Changed
                + "\r\nTriggers updated: " + triggersChanged
                + "\r\nFailed: " + r.Failed
                + (string.IsNullOrEmpty(r.BackupPath) ? "" : "\r\nBackup: " + r.BackupPath)
                + "\r\n\r\nThe dictionary is now DIRTY. Press Ctrl+S in Clarion to save.";
            MessageBox.Show(this, summary, "Safe rename",
                MessageBoxButtons.OK, r.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            Rescan();
        }

        static string FirstWritableTriggerBodyProp(object trigger)
        {
            string[] names = { "Body", "Code", "Source", "TriggerCode" };
            foreach (var n in names)
            {
                var p = trigger.GetType().GetProperty(n,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string)) return n;
            }
            // If none are writable but one exists read-only, we still return its name and
            // SetStringProp will fall back to the backing field.
            foreach (var n in names)
            {
                var p = trigger.GetType().GetProperty(n,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(string)) return n;
            }
            return null;
        }

        static bool KeyReferencesField(object key, string fieldLabel)
        {
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) return false;
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n) && string.Equals(n, fieldLabel, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool RelationReferencesField(object relation, string fieldLabel)
        {
            string[] compProps = { "Components", "Pairs", "Links", "Fields", "KeyFields", "RelationPairs" };
            foreach (var p in compProps)
            {
                var en = DictModel.GetProp(relation, p) as IEnumerable;
                if (en == null || en is string) continue;
                foreach (var c in en)
                {
                    if (c == null) continue;
                    string[] fieldProps = { "ParentField", "ChildField", "FromField", "ToField", "Field", "DDField", "PrimaryField", "ForeignField" };
                    foreach (var fp in fieldProps)
                    {
                        var fld = DictModel.GetProp(c, fp);
                        if (fld == null) continue;
                        var n = DictModel.AsString(DictModel.GetProp(fld, "Label"));
                        if (!string.IsNullOrEmpty(n) && string.Equals(n, fieldLabel, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            return false;
        }

        static string ClipAround(string body, MatchCollection matches)
        {
            if (matches.Count == 0 || string.IsNullOrEmpty(body)) return "";
            var m = matches[0];
            int s = Math.Max(0, m.Index - 30);
            int e = Math.Min(body.Length, m.Index + m.Length + 30);
            var snippet = body.Substring(s, e - s).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            return "..." + snippet + "...";
        }
    }
}
