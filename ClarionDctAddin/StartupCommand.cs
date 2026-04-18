using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Swaps the custom icon onto the Dictionary Tasker toolbar button at
    // startup. The button control SharpDevelop creates is a ToolBarCommand
    // (subclass of ToolStripItem) that paints its icon from a Codon — setting
    // the stock Image property alone does not stick. Strategy:
    //
    //  1. Poll the workbench toolbars after startup, find the item by tooltip.
    //  2. Set the public Image + every Image-typed public/non-public field on
    //     the item and its base classes.
    //  3. Drill into any field named "codon"/"Codon" and do the same inside it.
    //  4. Attach a Paint handler to the owning ToolStrip that blits our icon
    //     over the item's bounds on every repaint — bulletproof fallback.
    //
    // Diagnostic log is appended to %TEMP%\clarion-dct-addin-startup.log.
    public class StartupCommand : AbstractCommand
    {
        const string ButtonTooltip = "Open Dictionary Tasker";
        const string MatchHint     = "Dictionary Tasker";
        const string LogFileName   = "clarion-dct-addin-startup.log";

        static readonly string LogPath = Path.Combine(Path.GetTempPath(), LogFileName);
        static readonly HashSet<ToolStrip> paintHookedStrips = new HashSet<ToolStrip>();

        public override void Run()
        {
            Log("StartupCommand.Run fired");
            try { EmbeddedAssets.RegisterToolbarIcon(); } catch { }
            try { StartPolling(); } catch (Exception ex) { Log("StartPolling threw: " + ex); }
        }

        static void StartPolling()
        {
            var img = EmbeddedAssets.Load24Toolbar();
            if (img == null) { Log("No embedded toolbar bitmap."); return; }

            var timer = new Timer { Interval = 400 };
            int attempts = 0;
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try { done = TryPatch(img, attempts); }
                catch (Exception ex) { Log("TryPatch threw: " + ex); }
                if (done || attempts >= 25)
                {
                    Log("Polling stopped. patched=" + done + " attempts=" + attempts);
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        static bool TryPatch(Image img, int attempt)
        {
            bool found = false;
            foreach (Form form in Application.OpenForms)
                if (WalkAndPatch(form, img, attempt)) found = true;
            return found;
        }

        static bool WalkAndPatch(Control control, Image img, int attempt)
        {
            bool patched = false;
            var ts = control as ToolStrip;
            if (ts != null)
            {
                foreach (ToolStripItem item in ts.Items)
                {
                    if (IsOurButton(item))
                    {
                        ApplyIcon(ts, item, img);
                        Log("  patched item tooltip='" + item.ToolTipText + "' type=" + item.GetType().Name);
                        patched = true;
                    }
                }
            }
            foreach (Control child in control.Controls)
                if (WalkAndPatch(child, img, attempt)) patched = true;
            return patched;
        }

        static void ApplyIcon(ToolStrip owner, ToolStripItem item, Image img)
        {
            // 1. Stock property.
            try { item.Image = img; } catch { }

            // 2. Every Image-typed field on the item's class chain.
            SetImageFields(item, item, img, "item");

            // 3. Image-typed members of any "codon"-named field.
            var codon = FindFieldValue(item, "codon") ?? FindFieldValue(item, "Codon");
            if (codon != null) SetImageFields(codon, codon, img, "codon");

            // 4. Paint-over fallback: subscribe once per owning ToolStrip and
            //    draw our icon over the item's rectangle on every repaint.
            //    ICO files have transparent regions — without erasing what
            //    the control painted first, the stock icon shows through
            //    those transparent pixels and looks like two icons on top
            //    of each other. Fill the slot with the toolbar's background
            //    colour before drawing ours.
            if (owner != null && paintHookedStrips.Add(owner))
            {
                owner.Paint += delegate(object sender, PaintEventArgs e)
                {
                    try
                    {
                        foreach (ToolStripItem ti in owner.Items)
                        {
                            if (!IsOurButton(ti)) continue;
                            if (!ti.Visible) continue;
                            var r = ti.Bounds;

                            // Wipe the stock icon (and any hover state so
                            // the strip repaints cleanly).
                            using (var bg = new SolidBrush(owner.BackColor))
                                e.Graphics.FillRectangle(bg, r);

                            // Draw our bitmap at native size, centered.
                            int x = r.X + (r.Width  - img.Width)  / 2;
                            int y = r.Y + (r.Height - img.Height) / 2;
                            e.Graphics.DrawImage(img, x, y, img.Width, img.Height);
                        }
                    }
                    catch { }
                };
                owner.Invalidate();
                Log("  paint hook installed on " + owner.GetType().Name);
            }

            try { item.Invalidate(); } catch { }
            if (owner != null) { try { owner.Invalidate(); } catch { } }
        }

        static void SetImageFields(object target, object instance, Image img, string tag)
        {
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Image).IsAssignableFrom(f.FieldType)) continue;
                    try { f.SetValue(instance, img); Log("  set " + tag + "." + t.Name + "." + f.Name); }
                    catch (Exception ex) { Log("  set failed " + tag + "." + f.Name + ": " + ex.GetType().Name); }
                }
                t = t.BaseType;
            }
        }

        static object FindFieldValue(object target, string fieldName)
        {
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                var f = t.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null) { try { return f.GetValue(target); } catch { } }
                t = t.BaseType;
            }
            return null;
        }

        static bool IsOurButton(ToolStripItem item)
        {
            if (item == null) return false;
            if (string.Equals(item.ToolTipText, ButtonTooltip, StringComparison.Ordinal)) return true;
            var tip = item.ToolTipText ?? "";
            if (tip.IndexOf(MatchHint, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var name = item.Name ?? "";
            if (name.IndexOf("DictTasker", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var tag = item.Tag == null ? "" : item.Tag.ToString();
            if (tag.IndexOf("LauncherCommand", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
