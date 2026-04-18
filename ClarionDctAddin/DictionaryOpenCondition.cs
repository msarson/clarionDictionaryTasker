using System;
using System.Collections;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Gui;

namespace ClarionDctAddin
{
    // SharpDevelop condition evaluator: returns true iff at least one view
    // content currently open in the workbench is a Clarion dictionary editor.
    // Registered in ClarionDctAddin.addin and used by the toolbar ToolbarItem
    // so the Dictionary Tasker button is disabled unless a .DCT is open.
    public class DictionaryOpenCondition : IConditionEvaluator
    {
        const string DictViewFullName = "SoftVelocity.DataDictionary.Editor.DataDictionaryViewContent";

        public bool IsValid(object caller, Condition condition)
        {
            try
            {
                var wb = WorkbenchSingleton.Workbench;
                if (wb == null) return false;

                // ViewContentCollection returns something enumerable — cast loosely
                // so we don't depend on the exact generic type from this SD build.
                var vcs = wb.ViewContentCollection as IEnumerable;
                if (vcs == null) return false;

                foreach (var vc in vcs)
                {
                    if (vc == null) continue;
                    if (vc.GetType().FullName == DictViewFullName) return true;
                }
            }
            catch
            {
                // Any failure — fall back to "assume closed" so the button
                // stays disabled rather than throwing into the UI thread.
            }
            return false;
        }
    }
}
