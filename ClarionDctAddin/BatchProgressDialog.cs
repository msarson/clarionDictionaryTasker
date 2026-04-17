using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    internal class BatchProgressDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color SubColor    = Color.FromArgb(100, 115, 135);

        readonly int total;
        Label lblCurrent;
        Label lblCount;
        ProgressBar bar;
        Button btnCancel;

        public bool CancelRequested { get; private set; }

        public BatchProgressDialog(int total)
        {
            this.total = Math.Max(1, total);
            BuildUi();
        }

        void BuildUi()
        {
            Text = "Batch copy in progress";
            Width = 640;
            Height = 210;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ShowIcon = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = HeaderColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 11F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Text = "Applying batch copy..."
            };

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8), BackColor = BgColor };
            bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 22, Minimum = 0, Maximum = total, Value = 0, Style = ProgressBarStyle.Continuous };
            lblCount = new Label { Dock = DockStyle.Bottom, Height = 22, Font = new Font("Segoe UI", 9F), ForeColor = SubColor, TextAlign = ContentAlignment.MiddleLeft, Text = "0 / " + total };
            lblCurrent = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F), ForeColor = Color.FromArgb(30, 40, 55), TextAlign = ContentAlignment.MiddleLeft, Text = "Starting..." };
            body.Controls.Add(lblCurrent);
            body.Controls.Add(lblCount);
            body.Controls.Add(bar);

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = PanelColor, Padding = new Padding(12, 8, 12, 8) };
            btnCancel = new Button { Text = "Cancel", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCancel.Click += delegate
            {
                CancelRequested = true;
                btnCancel.Enabled = false;
                lblCurrent.Text = "Cancelling after current field...";
            };
            btnPanel.Controls.Add(btnCancel);

            Controls.Add(body);
            Controls.Add(btnPanel);
            Controls.Add(header);
        }

        public void Report(int done, string currentItem)
        {
            if (IsDisposed) return;
            bar.Value = Math.Min(Math.Max(0, done), bar.Maximum);
            lblCount.Text = done + " / " + total;
            if (!string.IsNullOrEmpty(currentItem))
                lblCurrent.Text = currentItem;
            // Pump the message loop so both this dialog and the Clarion IDE stay
            // responsive during the synchronous reflection work.
            Application.DoEvents();
        }
    }
}
