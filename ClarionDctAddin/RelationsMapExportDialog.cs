using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClarionDctAddin
{
    // Export the dictionary's relations graph as a standalone SVG file.
    // Self-contained layout (grid sorted by degree descending, relations drawn
    // as straight lines) — doesn't depend on the interactive diagram panel,
    // so the output is reproducible and doesn't need WinForms to render.
    internal class RelationsMapExportDialog : Form
    {
        static readonly Color BgColor     = Color.FromArgb(245, 247, 250);
        static readonly Color PanelColor  = Color.FromArgb(225, 230, 235);
        static readonly Color HeaderColor = Color.FromArgb(45,  90, 135);
        static readonly Color MutedColor  = Color.FromArgb(100, 115, 135);

        readonly object dict;
        CheckBox chkHideIsolated, chkSortByDegree;
        NumericUpDown numColumns;
        TextBox  txtPreview;
        Label    lblSummary;

        public RelationsMapExportDialog(object dict) { this.dict = dict; BuildUi(); Regenerate(); }

        void BuildUi()
        {
            Text = "Export relations map - " + DictModel.GetDictionaryName(dict);
            Width = 1100; Height = 720;
            MinimumSize = new Size(840, 460);
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
                Text = "Export relations map   " + DictModel.GetDictionaryName(dict)
            };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = BgColor, Padding = new Padding(16, 10, 16, 6) };
            chkHideIsolated = new CheckBox { Text = "Hide isolated tables", Left = 0,   Top = 8, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            chkSortByDegree = new CheckBox { Text = "Sort by degree",       Left = 180, Top = 8, AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
            var lblCols = new Label { Text = "Columns:", Left = 340, Top = 10, AutoSize = true, Font = new Font("Segoe UI", 9F) };
            numColumns = new NumericUpDown { Left = 398, Top = 6, Width = 60, Minimum = 2, Maximum = 20, Value = 6, Font = new Font("Segoe UI", 9F) };
            toolbar.Controls.Add(chkHideIsolated);
            toolbar.Controls.Add(chkSortByDegree);
            toolbar.Controls.Add(lblCols);
            toolbar.Controls.Add(numColumns);

            lblSummary = new Label
            {
                Dock = DockStyle.Top, Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = MutedColor,
                Padding = new Padding(18, 2, 0, 0),
                Text = ""
            };

            txtPreview = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9F),
                BackColor = Color.White
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = PanelColor, Padding = new Padding(16, 10, 16, 10) };
            var btnClose = new Button { Text = "Close", Width = 120, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnClose.Click += delegate { Close(); };
            var btnSave  = new Button { Text = "Save SVG...", Width = 140, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnSave.Click += delegate { SaveSvg(); };
            var btnCopy  = new Button { Text = "Copy SVG to clipboard", Width = 180, Height = 32, Dock = DockStyle.Right, FlatStyle = FlatStyle.System };
            btnCopy.Click += delegate { try { Clipboard.SetText(txtPreview.Text); } catch { } };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCopy);

            Controls.Add(txtPreview);
            Controls.Add(bottom);
            Controls.Add(lblSummary);
            Controls.Add(toolbar);
            Controls.Add(header);
            CancelButton = btnClose;

            chkHideIsolated.CheckedChanged += delegate { Regenerate(); };
            chkSortByDegree.CheckedChanged += delegate { Regenerate(); };
            numColumns.ValueChanged        += delegate { Regenerate(); };
        }

        sealed class Node
        {
            public string Name;
            public int Degree;
            public int Col, Row;
            public int X, Y;
        }

        sealed class Edge
        {
            public string From, To, Name;
        }

        void Regenerate()
        {
            Cursor = Cursors.WaitCursor;
            try { txtPreview.Text = BuildSvg(); txtPreview.SelectionStart = 0; txtPreview.SelectionLength = 0; }
            catch (Exception ex) { txtPreview.Text = "<!-- Error: " + ex.Message + " -->"; }
            finally { Cursor = Cursors.Default; }
        }

        string BuildSvg()
        {
            var tables = DictModel.GetTables(dict);
            var nodeByName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            var edges = new List<Edge>();

            foreach (var t in tables)
            {
                var n = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "";
                if (string.IsNullOrEmpty(n)) continue;
                if (!nodeByName.ContainsKey(n))
                    nodeByName[n] = new Node { Name = n };
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels == null) continue;
                foreach (var r in rels)
                {
                    if (r == null) continue;
                    string related = "";
                    string[] child = { "ChildFile", "RelatedFile", "Child", "ToFile", "To", "File", "DetailFile", "ForeignFile" };
                    foreach (var p in child)
                    {
                        var v = DictModel.GetProp(r, p);
                        if (v != null) { related = DictModel.AsString(DictModel.GetProp(v, "Name")) ?? ""; break; }
                    }
                    if (string.IsNullOrEmpty(related)) continue;
                    if (!nodeByName.ContainsKey(related)) nodeByName[related] = new Node { Name = related };
                    var rName = DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "";
                    edges.Add(new Edge { From = n, To = related, Name = rName });
                    nodeByName[n].Degree++;
                    nodeByName[related].Degree++;
                }
            }

            IEnumerable<Node> visible = nodeByName.Values;
            if (chkHideIsolated.Checked) visible = visible.Where(v => v.Degree > 0);
            var list = chkSortByDegree.Checked
                ? visible.OrderByDescending(v => v.Degree).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : visible.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();

            int cols = (int)numColumns.Value;
            const int cellW = 180, cellH = 80, margin = 40, hGap = 40, vGap = 40;
            int rows = list.Count == 0 ? 0 : (int)Math.Ceiling(list.Count / (double)cols);
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                n.Col = i % cols;
                n.Row = i / cols;
                n.X = margin + n.Col * (cellW + hGap);
                n.Y = margin + n.Row * (cellH + vGap);
            }
            int width  = margin * 2 + cols * cellW + Math.Max(0, (cols - 1)) * hGap;
            int height = margin * 2 + Math.Max(1, rows) * cellH + Math.Max(0, rows - 1) * vGap;

            var nodeLookup = list.ToDictionary(n => n.Name, n => n, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"" + width + "\" height=\"" + height + "\" viewBox=\"0 0 " + width + " " + height + "\">");
            sb.AppendLine("  <defs>");
            sb.AppendLine("    <marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"10\" refY=\"5\" markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">");
            sb.AppendLine("      <path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#2d5a87\"/>");
            sb.AppendLine("    </marker>");
            sb.AppendLine("  </defs>");
            sb.AppendLine("  <rect x=\"0\" y=\"0\" width=\"" + width + "\" height=\"" + height + "\" fill=\"#f5f7fa\"/>");
            sb.AppendLine("  <text x=\"" + margin + "\" y=\"22\" font-family=\"Segoe UI, Arial\" font-size=\"14\" fill=\"#2d5a87\" font-weight=\"600\">"
                + XmlEscape(DictModel.GetDictionaryName(dict) ?? "dictionary")
                + " — relations map — " + DateTime.Now.ToString("yyyy-MM-dd") + "</text>");

            // edges first (behind nodes)
            foreach (var e in edges)
            {
                Node a, b;
                if (!nodeLookup.TryGetValue(e.From, out a)) continue;
                if (!nodeLookup.TryGetValue(e.To,   out b)) continue;
                int x1 = a.X + cellW / 2, y1 = a.Y + cellH / 2;
                int x2 = b.X + cellW / 2, y2 = b.Y + cellH / 2;
                sb.AppendLine("  <line x1=\"" + x1 + "\" y1=\"" + y1 + "\" x2=\"" + x2 + "\" y2=\"" + y2
                    + "\" stroke=\"#2d5a87\" stroke-opacity=\"0.35\" stroke-width=\"1.5\" marker-end=\"url(#arrow)\"/>");
            }

            // nodes on top
            foreach (var n in list)
            {
                sb.AppendLine("  <g>");
                sb.AppendLine("    <rect x=\"" + n.X + "\" y=\"" + n.Y + "\" width=\"" + cellW + "\" height=\"" + cellH
                    + "\" rx=\"6\" ry=\"6\" fill=\"white\" stroke=\"#2d5a87\" stroke-width=\"1.5\"/>");
                sb.AppendLine("    <text x=\"" + (n.X + cellW / 2) + "\" y=\"" + (n.Y + cellH / 2 + 4)
                    + "\" text-anchor=\"middle\" font-family=\"Segoe UI, Arial\" font-size=\"12\" fill=\"#1b2a3a\">"
                    + XmlEscape(n.Name) + "</text>");
                sb.AppendLine("    <text x=\"" + (n.X + cellW / 2) + "\" y=\"" + (n.Y + cellH / 2 + 22)
                    + "\" text-anchor=\"middle\" font-family=\"Segoe UI, Arial\" font-size=\"10\" fill=\"#66788a\">"
                    + "degree " + n.Degree + "</text>");
                sb.AppendLine("  </g>");
            }

            sb.AppendLine("</svg>");

            lblSummary.Text = list.Count + " nodes, " + edges.Count + " relations, " + width + "×" + height + " px.";
            return sb.ToString();
        }

        void SaveSvg()
        {
            var suggested = (DictModel.GetDictionaryName(dict) ?? "dict") + "-relations-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".svg";
            using (var dlg = new SaveFileDialog
            {
                Filter = "SVG files (*.svg)|*.svg|All files (*.*)|*.*",
                FileName = suggested
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, txtPreview.Text, new UTF8Encoding(false));
                    MessageBox.Show(this, "Saved: " + dlg.FileName, "Relations map",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Save failed: " + ex.Message, "Relations map",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static string XmlEscape(string s)
        {
            return (s ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
