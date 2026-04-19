using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Preview of the "standard audit pack" — a preset of audit fields
    // (GUID + CreatedOn/By + ModifiedOn/By + DeletedOn) plus a unique key
    // that a shop typically wants on every user-facing table.
    //
    // This version is preview-only: it generates a Markdown recipe of what
    // would be added to each selected table so it can be reviewed / diffed
    // / pasted into a PR description. Applying the pack to the live dict
    // is left for a future iteration (to avoid untested mutations).
    internal class StandardAuditPackDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        CheckedListBox lstTables;
        CheckedListBox lstPack;
        TextBox        txtPreview;
        Label          lblSummary;
        List<object>   tables;

        sealed class AuditField
        {
            public string Label, DataType, Size, Picture, Description;
        }
        static readonly AuditField[] PresetPack = new[]
        {
            new AuditField { Label = "Guid",         DataType = "STRING",  Size = "36", Picture = "@s36", Description = "Opaque external identifier (UUID)." },
            new AuditField { Label = "CreatedOn",    DataType = "DATE",    Size = "",   Picture = "@d6",  Description = "Row creation timestamp (date part)." },
            new AuditField { Label = "CreatedBy",    DataType = "STRING",  Size = "50", Picture = "@s50", Description = "User who created the row." },
            new AuditField { Label = "ModifiedOn",   DataType = "DATE",    Size = "",   Picture = "@d6",  Description = "Last modification timestamp." },
            new AuditField { Label = "ModifiedBy",   DataType = "STRING",  Size = "50", Picture = "@s50", Description = "User who last modified the row." },
            new AuditField { Label = "DeletedOn",    DataType = "DATE",    Size = "",   Picture = "@d6",  Description = "Soft-delete timestamp (null = live)." },
        };

        public StandardAuditPackDialog(object dict) { this.dict = dict; BuildUi(); Regenerate(); }

        void BuildUi()
        {
            Text = "Standard audit pack - " + DictModel.GetDictionaryName(dict);
            Width = 1180; Height = 740;
            MinimumSize = new Size(900, 500);
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
                Text = "Standard audit pack   (preview-only — Markdown recipe)"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            var btnAll  = new Button { Text = "Select all tables",   Width = 160, Height = 30, Left = 0,   Top = 6, FlatStyle = FlatStyle.System };
            btnAll.Click += delegate { SetAllTables(true); };
            var btnNone = new Button { Text = "Clear",               Width = 120, Height = 30, Left = 170, Top = 6, FlatStyle = FlatStyle.System };
            btnNone.Click += delegate { SetAllTables(false); };
            toolbar.Controls.Add(btnAll);
            toolbar.Controls.Add(btnNone);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = ""
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                BackColor = BgColor, Panel1MinSize = 240, Panel2MinSize = 320
            };

            // Left panel: tables + pack
            var leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BgColor, Panel1MinSize = 120, Panel2MinSize = 100
            };

            lstTables = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true,
                Font = new Font("Segoe UI", 9F), BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            leftSplit.Panel1.Controls.Add(WrapSection("Target tables", lstTables));

            lstPack = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true,
                Font = new Font("Segoe UI", 9F), BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            foreach (var af in PresetPack)
                lstPack.Items.Add(af.Label + "   (" + af.DataType + (string.IsNullOrEmpty(af.Size) ? "" : " " + af.Size) + ")", true);
            leftSplit.Panel2.Controls.Add(WrapSection("Audit pack fields", lstPack));

            split.Panel1.Controls.Add(leftSplit);

            txtPreview = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White
            };
            split.Panel2.Controls.Add(WrapSection("Markdown recipe", txtPreview));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            var btnSave  = new Button { Text = "Save recipe as...", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnSave.Click += delegate { SaveAs(); };
            var btnCopy  = new Button { Text = "Copy to clipboard", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCopy.Click += delegate { try { Clipboard.SetText(txtPreview.Text); } catch { } };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCopy);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;

            Load += delegate { split.SplitterDistance = 360; leftSplit.SplitterDistance = (int)(leftSplit.Height * 0.55); };

            // Populate tables
            tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var t in tables)
                lstTables.Items.Add(DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?", false);

            lstTables.ItemCheck += delegate { BeginInvoke((Action)Regenerate); };
            lstPack.ItemCheck   += delegate { BeginInvoke((Action)Regenerate); };
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

        void SetAllTables(bool on)
        {
            for (int i = 0; i < lstTables.Items.Count; i++) lstTables.SetItemChecked(i, on);
        }

        void Regenerate()
        {
            var selectedTables = new List<string>();
            for (int i = 0; i < lstTables.Items.Count; i++)
                if (lstTables.GetItemChecked(i)) selectedTables.Add(lstTables.Items[i].ToString());
            var selectedPack = new List<AuditField>();
            for (int i = 0; i < lstPack.Items.Count; i++)
                if (lstPack.GetItemChecked(i)) selectedPack.Add(PresetPack[i]);

            var sb = new StringBuilder();
            sb.AppendLine("# Standard audit pack — recipe");
            sb.AppendLine();
            sb.AppendLine("- **Source dictionary:** `" + DictModel.GetDictionaryName(dict) + "`");
            sb.AppendLine("- **Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("- **Tables selected:** " + selectedTables.Count);
            sb.AppendLine("- **Fields in pack:** " + selectedPack.Count);
            sb.AppendLine();
            sb.AppendLine("## Fields to add");
            sb.AppendLine();
            sb.AppendLine("| Label | Type | Size | Picture | Description |");
            sb.AppendLine("|-------|------|------|---------|-------------|");
            foreach (var af in selectedPack)
                sb.AppendLine("| `" + af.Label + "` | " + af.DataType + " | " + af.Size + " | `" + af.Picture + "` | " + af.Description + " |");
            sb.AppendLine();

            sb.AppendLine("## Target tables");
            sb.AppendLine();
            if (selectedTables.Count == 0)
                sb.AppendLine("_No tables selected._");
            else
                foreach (var t in selectedTables) sb.AppendLine("- `" + t + "`");
            sb.AppendLine();

            sb.AppendLine("## Application notes");
            sb.AppendLine();
            sb.AppendLine("For each target table, insert the fields above **at the end of the field list** and set their");
            sb.AppendLine("`ExternalName` to match the database column naming scheme (e.g. snake_case, `created_on`). Add a");
            sb.AppendLine("unique key on `Guid` if the table is user-facing.");
            sb.AppendLine();
            sb.AppendLine("This tool is preview-only in the current version — use **Batch copy fields** to apply the pack");
            sb.AppendLine("once a template table with these fields has been prepared.");

            txtPreview.Text = sb.ToString();
            txtPreview.SelectionStart = 0;
            txtPreview.SelectionLength = 0;
            lblSummary.Text = selectedTables.Count + " table(s) × " + selectedPack.Count + " field(s) = "
                + (selectedTables.Count * selectedPack.Count) + " insertion(s) planned.";
        }

        void SaveAs()
        {
            var suggested = "audit-pack-recipe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md";
            using (var dlg = new SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtPreview.Text);
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Audit pack recipe",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Audit pack recipe",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
