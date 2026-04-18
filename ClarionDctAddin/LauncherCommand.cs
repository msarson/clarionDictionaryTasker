using System.Windows.Forms;
using ICSharpCode.Core;

namespace ClarionDctAddin
{
    public class LauncherCommand : AbstractMenuCommand
    {
        public override void Run()
        {
            object dict;
            string err;
            if (!DictModel.TryGetOpenDictionary(out dict, out err))
            {
                MessageBox.Show(err, "Dictionary Tasker",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new LauncherDialog(dict))
                dlg.ShowDialog();
        }
    }
}
