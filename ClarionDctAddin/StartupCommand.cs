using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    // Runs at /Workspace/Autostart. Patches our Dictionary Tasker toolbar
    // button's Image with the embedded custom icon after the toolbar is
    // built. Writes a diagnostic log to %TEMP% so we can see what's on the
    // toolbar if the swap doesn't land.
    public class StartupCommand : AbstractCommand
    {
        const string ButtonTooltip = "Open Dictionary Tasker";
        const string MatchHint     = "Dictionary Tasker";
        const string LogFileName   = "clarion-dct-addin-startup.log";

        static readonly string LogPath = Path.Combine(Path.GetTempPath(), LogFileName);

        public override void Run()
        {
            Log("StartupCommand.Run fired");
            try { EmbeddedAssets.RegisterToolbarIcon(); }
            catch (Exception ex) { Log("RegisterToolbarIcon threw: " + ex.GetType().Name); }

            try { StartPollingForToolbarButton(); }
            catch (Exception ex) { Log("StartPolling threw: " + ex.GetType().Name); }
        }

        static void StartPollingForToolbarButton()
        {
            var img = EmbeddedAssets.Load24Toolbar();
            if (img == null) { Log("No embedded toolbar bitmap available."); return; }
            Log("Toolbar bitmap loaded: " + img.Width + "x" + img.Height);

            var timer = new Timer { Interval = 400 };
            int attempts = 0;
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try { done = TryPatch(img, attempts); } catch (Exception ex) { Log("TryPatch threw: " + ex); }
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
            int formCount = Application.OpenForms.Count;
            if (attempt == 1 || attempt == 5)
                Log("attempt " + attempt + " — OpenForms=" + formCount);

            foreach (Form form in Application.OpenForms)
            {
                if (WalkAndPatch(form, img, attempt)) found = true;
            }
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
                    // Dump every ToolStripItem once so we can see what's there.
                    if (attempt == 1 || attempt == 5)
                    {
                        Log(string.Format("  item name='{0}' text='{1}' tooltip='{2}' tag='{3}' type={4}",
                            item.Name, item.Text, item.ToolTipText, item.Tag, item.GetType().Name));
                    }

                    if (IsOurButton(item))
                    {
                        item.Image = img;
                        Log("  PATCHED item tooltip='" + item.ToolTipText + "' name='" + item.Name + "'");
                        patched = true;
                    }
                }
            }
            foreach (Control child in control.Controls)
                if (WalkAndPatch(child, img, attempt)) patched = true;
            return patched;
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
