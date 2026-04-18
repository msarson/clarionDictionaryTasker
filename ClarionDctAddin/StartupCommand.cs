using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Replaces the ToolStrip renderer so that OUR button's image render step
    // always draws our embedded bitmap and never the Codon's stock icon.
    // Subclassing ToolStripProfessionalRenderer keeps every other item's
    // visual styling intact.
    internal class DictTaskerToolbarRenderer : ToolStripProfessionalRenderer
    {
        // Standard "disabled icon" transform — luminance-weighted grayscale
        // with reduced alpha, matches what WinForms does for stock disabled
        // toolbar images.
        static readonly ColorMatrix DisabledMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0.30f, 0.30f, 0.30f, 0,    0 },
            new float[] { 0.59f, 0.59f, 0.59f, 0,    0 },
            new float[] { 0.11f, 0.11f, 0.11f, 0,    0 },
            new float[] { 0,     0,     0,     0.45f, 0 },
            new float[] { 0,     0,     0,     0,    1 }
        });

        readonly Image icon;
        public DictTaskerToolbarRenderer(Image icon) { this.icon = icon; }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (icon != null && StartupCommand.IsOurToolbarItem(e.Item))
            {
                var r = e.ImageRectangle;
                if (r.Width > 0 && r.Height > 0)
                {
                    if (e.Item.Enabled)
                    {
                        e.Graphics.DrawImage(icon, r);
                    }
                    else
                    {
                        using (var attrs = new ImageAttributes())
                        {
                            attrs.SetColorMatrix(DisabledMatrix);
                            e.Graphics.DrawImage(
                                icon, r,
                                0, 0, icon.Width, icon.Height,
                                GraphicsUnit.Pixel, attrs);
                        }
                    }
                }
                return; // skip the base call so the stock icon is never drawn
            }
            base.OnRenderItemImage(e);
        }
    }

    public class StartupCommand : AbstractCommand
    {
        const string ButtonTooltip = "Open Dictionary Tasker";
        const string MatchHint     = "Dictionary Tasker";
        const string LogFileName   = "clarion-dct-addin-startup.log";

        static readonly string LogPath = Path.Combine(Path.GetTempPath(), LogFileName);
        static readonly HashSet<ToolStrip> renderersReplaced = new HashSet<ToolStrip>();

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
                if (WalkAndPatch(form, img)) found = true;
            return found;
        }

        static bool WalkAndPatch(Control control, Image img)
        {
            bool patched = false;
            var ts = control as ToolStrip;
            if (ts != null)
            {
                foreach (ToolStripItem item in ts.Items)
                {
                    if (IsOurToolbarItem(item))
                    {
                        ApplyRenderer(ts, item, img);
                        Log("  patched item tooltip='" + item.ToolTipText + "' type=" + item.GetType().Name);
                        patched = true;
                    }
                }
            }
            foreach (Control child in control.Controls)
                if (WalkAndPatch(child, img)) patched = true;
            return patched;
        }

        static void ApplyRenderer(ToolStrip owner, ToolStripItem item, Image img)
        {
            // Harmless extras — in case some code path uses item.Image directly.
            try { item.Image = img; } catch { }
            SetImageFields(item, img, "item");
            var codon = FindFieldValue(item, "codon") ?? FindFieldValue(item, "Codon");
            if (codon != null) SetImageFields(codon, img, "codon");

            // Primary fix: own the image-render step by swapping the renderer.
            if (owner != null && renderersReplaced.Add(owner))
            {
                try
                {
                    owner.Renderer = new DictTaskerToolbarRenderer(img);
                    Log("  renderer replaced on " + owner.GetType().Name);
                }
                catch (Exception ex) { Log("  renderer replace failed: " + ex.GetType().Name); }
                try { owner.Invalidate(); } catch { }
            }
            try { item.Invalidate(); } catch { }
        }

        static void SetImageFields(object instance, Image img, string tag)
        {
            var t = instance.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Image).IsAssignableFrom(f.FieldType)) continue;
                    try { f.SetValue(instance, img); }
                    catch { /* non-fatal */ }
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

        internal static bool IsOurToolbarItem(ToolStripItem item)
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
