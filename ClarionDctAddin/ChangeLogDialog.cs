using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Compare two *.tasker-snap save-points and emit a human-readable changelog.
    // Reuses DictSnapshot and DictDiff; differs from CompareDictionariesDialog in
    // that neither side is the live dict — both come from disk.
    internal class ChangeLogDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        Label    lblOld, lblNew, lblSummary;
        TextBox  txtOutput;
        Button   btnSave, btnCopy;
        DictSnapshot oldSnap, newSnap;

        public ChangeLogDialog(object dict) { this.dict = dict; BuildUi(); }

        void BuildUi()
        {
            Text = "Change-log generator - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 720;
            MinimumSize = new Size(840, 480);
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
                Text = "Change-log generator   (two snapshots -> Markdown changelog)"
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            var btnOld = new Button { Text = "Load old snapshot...", Width = 180, Height = 30, Left = 0,   Top = 6, FlatStyle = FlatStyle.System };
            btnOld.Click += delegate { LoadSnap(true); };
            var btnNew = new Button { Text = "Load new snapshot...", Width = 180, Height = 30, Left = 190, Top = 6, FlatStyle = FlatStyle.System };
            btnNew.Click += delegate { LoadSnap(false); };
            var btnSaveNow = new Button { Text = "Save current dict as snapshot...", Width = 240, Height = 30, Left = 380, Top = 6, FlatStyle = FlatStyle.System };
            btnSaveNow.Click += delegate { SaveCurrentSnap(); };
            toolbar.Controls.Add(btnOld);
            toolbar.Controls.Add(btnNew);
            toolbar.Controls.Add(btnSaveNow);

            lblOld = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Old snapshot: (none)"
            };
            lblNew = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "New snapshot: (none)"
            };
            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 4, 0, 0),
                Text = ""
            };

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
            btnSave = new Button { Text = "Save as...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnSave.Click += delegate { SaveAs(); };
            btnCopy = new Button { Text = "Copy to clipboard", Width = 160, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System, Enabled = false };
            btnCopy.Click += delegate { CopyToClipboard(); };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCopy);

            Controls.Add(txtOutput);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(lblNew);
            Controls.Add(lblOld);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;
        }

        void LoadSnap(bool isOld)
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Dictionary snapshot (*.tasker-snap)|*.tasker-snap|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var snap = DictSnapshot.Load(dlg.FileName);
                    if (isOld) { oldSnap = snap; lblOld.Text = "Old snapshot: " + Path.GetFileName(dlg.FileName) + "   (" + snap.TakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")"; }
                    else       { newSnap = snap; lblNew.Text = "New snapshot: " + Path.GetFileName(dlg.FileName) + "   (" + snap.TakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")"; }
                    if (oldSnap != null && newSnap != null) Generate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Load failed: " + ex.Message, "Change-log",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void SaveCurrentSnap()
        {
            var snap = DictSnapshot.CaptureFromLive(dict);
            var suggested = (string.IsNullOrEmpty(snap.DictName) ? "dictionary" : snap.DictName)
                          + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tasker-snap";
            using (var dlg = new SaveFileDialog
            {
                Filter = "Dictionary snapshot (*.tasker-snap)|*.tasker-snap|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    snap.Save(dlg.FileName);
                    MessageBox.Show(this, "Snapshot saved:\r\n" + dlg.FileName, "Change-log",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Change-log",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void Generate()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var diff = DictDiff.Compute(oldSnap, newSnap);
                txtOutput.Text = DictDiff.RenderMarkdown(oldSnap, newSnap, diff);
                txtOutput.SelectionStart = 0;
                txtOutput.SelectionLength = 0;
                lblSummary.Text = string.Format("Tables: +{0} added, -{1} removed, ~{2} changed, ={3} unchanged.",
                    diff.AddedTables.Count, diff.RemovedTables.Count, diff.ChangedTables.Count, diff.UnchangedTableCount);
                btnSave.Enabled = true;
                btnCopy.Enabled = true;
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
            var suggested = "changelog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md";
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
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Change-log",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Change-log",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
