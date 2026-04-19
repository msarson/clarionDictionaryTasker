using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Manual "commit the .DCT to git" tool. Detects whether the dictionary
    // lives inside a git working tree (walks upward looking for a .git dir)
    // and shells out to the system `git` executable. Commit message is
    // user-editable but pre-seeded with an auto-generated summary.
    //
    // This is a manual replacement for the originally-planned "git commit
    // hook that fires on save" — hooking the IDE save event reliably across
    // Clarion 12 point releases is not worth the maintenance cost.
    internal class GitCommitDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        Label    lblRepo, lblStatus;
        TextBox  txtMessage, txtOutput;
        Button   btnCommit, btnPush, btnRefresh;
        string   dctPath, repoRoot;

        public GitCommitDialog(object dict) { this.dict = dict; BuildUi(); RefreshStatus(); }

        void BuildUi()
        {
            Text = "Git commit - " + DictModel.GetDictionaryName(dict);
            Width = 1040; Height = 720;
            MinimumSize = new Size(800, 480);
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
                Text = "Git commit   " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            btnRefresh = new Button { Text = "Refresh status", Width = 130, Height = 30, Left = 0,   Top = 6, FlatStyle = FlatStyle.System };
            btnRefresh.Click += delegate { RefreshStatus(); };
            btnCommit  = new Button { Text = "Commit",         Width = 110, Height = 30, Left = 140, Top = 6, FlatStyle = FlatStyle.System, Enabled = false };
            btnCommit.Click += delegate { DoCommit(); };
            btnPush    = new Button { Text = "Commit && push", Width = 140, Height = 30, Left = 260, Top = 6, FlatStyle = FlatStyle.System, Enabled = false };
            btnPush.Click += delegate { DoCommitAndPush(); };
            toolbar.Controls.Add(btnRefresh);
            toolbar.Controls.Add(btnCommit);
            toolbar.Controls.Add(btnPush);

            lblRepo = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Repo: ?"
            };
            lblStatus = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = "Status: ?"
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                BackColor = BgColor, Panel1MinSize = 100, Panel2MinSize = 120
            };

            txtMessage = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true,
                ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
                WordWrap = true, Font = new Font("Consolas", 10F),
                BackColor = Color.White
            };
            split.Panel1.Controls.Add(WrapSection("Commit message", txtMessage));

            txtOutput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5F), BackColor = Color.White
            };
            split.Panel2.Controls.Add(WrapSection("git output", txtOutput));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnClose);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(lblStatus);
            Controls.Add(lblRepo);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;

            Load += delegate { split.SplitterDistance = 150; };
        }

        static Control WrapSection(string title, Control content)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor, Padding = new Padding(6) };
            var lbl = new Label
            {
                Dock = DockStyle.Top, Height = 22,
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = HeaderColor, Text = title
            };
            content.Dock = DockStyle.Fill;
            panel.Controls.Add(content);
            panel.Controls.Add(lbl);
            return panel;
        }

        void RefreshStatus()
        {
            dctPath = DictModel.GetDictionaryFileName(dict) ?? "";
            if (string.IsNullOrEmpty(dctPath) || !File.Exists(dctPath))
            {
                lblRepo.Text = "Dict path not found on disk.";
                lblStatus.Text = "";
                btnCommit.Enabled = btnPush.Enabled = false;
                return;
            }

            repoRoot = FindRepoRoot(Path.GetDirectoryName(dctPath));
            if (repoRoot == null)
            {
                lblRepo.Text = "Repo: not a git working tree (" + dctPath + ").";
                lblStatus.Text = "Initialise a git repo in the dict's folder to use this tool.";
                btnCommit.Enabled = btnPush.Enabled = false;
                return;
            }
            lblRepo.Text = "Repo: " + repoRoot;

            var status = RunGit(repoRoot, "status --short -- \"" + dctPath + "\"");
            if (!status.Ok)
            {
                lblStatus.Text = "git failed: " + status.StdErrFirstLine;
                btnCommit.Enabled = btnPush.Enabled = false;
                return;
            }
            var line = (status.StdOut ?? "").Trim();
            if (string.IsNullOrEmpty(line))
            {
                lblStatus.Text = "Status: nothing to commit for the dictionary file.";
                btnCommit.Enabled = btnPush.Enabled = false;
            }
            else
            {
                lblStatus.Text = "Status: " + line;
                btnCommit.Enabled = btnPush.Enabled = true;
            }

            if (string.IsNullOrEmpty(txtMessage.Text))
                txtMessage.Text = SuggestMessage();
        }

        string SuggestMessage()
        {
            var name = DictModel.GetDictionaryName(dict);
            var sb = new StringBuilder();
            sb.AppendLine("dict: update " + name);
            sb.AppendLine();
            sb.AppendLine("- saved on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("- tables: " + DictModel.GetTables(dict).Count);
            sb.AppendLine("- file: " + Path.GetFileName(dctPath));
            return sb.ToString();
        }

        void DoCommit()     { RunCommit(false); }
        void DoCommitAndPush() { RunCommit(true); }

        void RunCommit(bool alsoPush)
        {
            if (string.IsNullOrEmpty(repoRoot)) return;
            var msg = (txtMessage.Text ?? "").Trim();
            if (string.IsNullOrEmpty(msg))
            { MessageBox.Show(this, "Enter a commit message.", "Git commit", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            txtOutput.Text = "";
            Append("$ git add \"" + dctPath + "\"");
            var add = RunGit(repoRoot, "add \"" + dctPath + "\"");
            Append(add.Combined);
            if (!add.Ok) return;

            // Write the message to a temp file so we don't have to escape quotes.
            var tmp = Path.Combine(Path.GetTempPath(), "tasker-commitmsg-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(tmp, msg, new UTF8Encoding(false));
            Append("$ git commit -F <tempfile>");
            var commit = RunGit(repoRoot, "commit -F \"" + tmp + "\"");
            Append(commit.Combined);
            try { File.Delete(tmp); } catch { }
            if (!commit.Ok) return;

            if (alsoPush)
            {
                Append("$ git push");
                var push = RunGit(repoRoot, "push");
                Append(push.Combined);
            }
            RefreshStatus();
        }

        void Append(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (txtOutput.Text.Length > 0) txtOutput.AppendText("\r\n");
            txtOutput.AppendText(text);
        }

        sealed class GitResult
        {
            public bool Ok;
            public int ExitCode;
            public string StdOut, StdErr;
            public string Combined
            {
                get
                {
                    var s = (StdOut ?? "").TrimEnd();
                    var e = (StdErr ?? "").TrimEnd();
                    if (string.IsNullOrEmpty(e)) return s;
                    if (string.IsNullOrEmpty(s)) return e;
                    return s + "\r\n" + e;
                }
            }
            public string StdErrFirstLine
            {
                get
                {
                    if (string.IsNullOrEmpty(StdErr)) return "";
                    var nl = StdErr.IndexOf('\n');
                    return nl > 0 ? StdErr.Substring(0, nl).Trim() : StdErr.Trim();
                }
            }
        }

        static GitResult RunGit(string workingDir, string args)
        {
            var r = new GitResult();
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    r.StdOut = p.StandardOutput.ReadToEnd();
                    r.StdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    r.ExitCode = p.ExitCode;
                    r.Ok = p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                r.StdErr = "Failed to run git: " + ex.Message + " (is git on PATH?)";
                r.Ok = false;
            }
            return r;
        }

        static string FindRepoRoot(string startDir)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                try { if (Directory.Exists(Path.Combine(dir, ".git"))) return dir; } catch { }
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) return null;
                dir = parent;
            }
            return null;
        }
    }
}
