using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class MarkdownDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);

        readonly object dict;
        readonly object singleTable;

        CheckBox chkFields, chkKeys, chkRelations, chkTOC;
        TextBox  txtOutput;
        bool inSetup = true;

        public MarkdownDialog(object dict) : this(dict, null) { }

        public MarkdownDialog(object dict, object singleTable)
        {
            this.dict = dict;
            this.singleTable = singleTable;
            BuildUi();
            inSetup = false;
            Regenerate();
        }

        void BuildUi()
        {
            var singleLabel = singleTable == null ? "" :
                (DictModel.AsString(DictModel.GetProp(singleTable, "Label")) ?? "");
            Text = singleTable == null
                ? "Markdown documentation - " + DictModel.GetDictionaryName(dict)
                : "Markdown documentation - " + singleLabel;
            Width = 1080; Height = 720;
            MinimumSize = new Size(820, 480);
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
                Text = singleTable == null
                    ? "Markdown documentation   " + DictModel.GetDictionaryName(dict)
                    : "Markdown   table: " + singleLabel + "   dict: " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            chkFields    = MakeCheck("Include fields",    0,   10, true);
            chkKeys      = MakeCheck("Include keys",      170, 10, true);
            chkRelations = MakeCheck("Include relations", 320, 10, true);
            chkTOC       = MakeCheck("Include table of contents", 490, 10, singleTable == null);
            chkTOC.Enabled = singleTable == null;
            toolbar.Controls.Add(chkFields);
            toolbar.Controls.Add(chkKeys);
            toolbar.Controls.Add(chkRelations);
            toolbar.Controls.Add(chkTOC);

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            var btnSave  = new Button { Text = "Save as...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnSave.Click += delegate { SaveAs(); };
            var btnCopy  = new Button { Text = "Copy to clipboard", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCopy.Click += delegate { CopyToClipboard(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCopy);

            Controls.Add(txtOutput);
            Controls.Add(bottom);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        CheckBox MakeCheck(string text, int left, int top, bool on)
        {
            var c = new CheckBox
            {
                Text = text, Left = left, Top = top,
                Width = 160, Height = 22, Checked = on,
                AutoSize = true, Font = new Font("Segoe UI", 9F)
            };
            c.CheckedChanged += delegate { if (!inSetup) Regenerate(); };
            return c;
        }

        void Regenerate()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var opt = new MarkdownGenerator.Options
                {
                    IncludeFields    = chkFields.Checked,
                    IncludeKeys      = chkKeys.Checked,
                    IncludeRelations = chkRelations.Checked,
                    IncludeTOC       = chkTOC.Checked
                };
                txtOutput.Text = singleTable != null
                    ? MarkdownGenerator.GenerateForTable(singleTable, opt)
                    : MarkdownGenerator.Generate(dict, opt);
                txtOutput.SelectionStart = 0;
                txtOutput.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                txtOutput.Text = "<!-- Error: " + ex.Message + " -->";
            }
            finally { Cursor = Cursors.Default; }
        }

        void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(txtOutput.Text)) return;
            try { Clipboard.SetText(txtOutput.Text); } catch { }
        }

        void SaveAs()
        {
            var suggested = (singleTable == null
                ? DictModel.GetDictionaryName(dict)
                : DictModel.AsString(DictModel.GetProp(singleTable, "Label")) ?? "table") + ".md";
            using (var dlg = new SaveFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtOutput.Text);
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Markdown export",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Markdown export",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
