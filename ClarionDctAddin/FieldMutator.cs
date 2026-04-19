using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ClarionDctAddin
{
    // Shared helpers for in-place field mutations (rename, retype, edit
    // description/heading/prompt). The pattern mirrors FieldCopier's add path
    // but is tailored to property changes rather than inserts:
    //   1. back up the .DCT first
    //   2. set the property via public setter or non-public backing field
    //   3. flip itemHasChanged / stored / Touched on the field
    //   4. ChildListTouched on table + dict
    //   5. mark view dirty so Clarion's Save button activates
    internal static class FieldMutator
    {
        public sealed class Result
        {
            public int Changed;
            public int Failed;
            public List<string> Messages = new List<string>();
            public string BackupPath;
            public bool   BackupFailed;
        }

        public static string Backup(string dctPath, Result r)
        {
            if (string.IsNullOrEmpty(dctPath) || !File.Exists(dctPath)) return null;
            try
            {
                var path = dctPath + ".tasker-bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(dctPath, path, false);
                r.BackupPath = path;
                return path;
            }
            catch (Exception ex)
            {
                r.BackupFailed = true;
                r.Messages.Add("Backup failed: " + ex.GetType().Name + " - " + ex.Message);
                return null;
            }
        }

        public static bool SetStringProp(object target, string propName, string value, Result r, string tag)
        {
            if (target == null) return false;
            var t = target.GetType();
            var p = t.GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            try
            {
                if (p != null && p.CanWrite)
                {
                    p.SetValue(target, value, null);
                }
                else
                {
                    // Fall back to non-public backing field anywhere in the class chain.
                    if (!SetBackingStringField(target, propName, value))
                    {
                        r.Messages.Add(tag + ": no writable path for " + propName);
                        return false;
                    }
                }
                TouchField(target);
                return true;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                r.Messages.Add(tag + ": set " + propName + " failed - " + inner.GetType().Name + " " + inner.Message);
                return false;
            }
        }

        static bool SetBackingStringField(object target, string propName, string value)
        {
            // Typical SoftVelocity convention: PublicProperty <-> privateField (camelCase)
            var candidates = new[]
            {
                char.ToLowerInvariant(propName[0]) + (propName.Length > 1 ? propName.Substring(1) : ""),
                "_" + propName,
                "_" + char.ToLowerInvariant(propName[0]) + (propName.Length > 1 ? propName.Substring(1) : ""),
                "m_" + propName,
                propName
            };
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                foreach (var name in candidates)
                {
                    var f = t.GetField(name,
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (f != null && f.FieldType == typeof(string))
                    {
                        try { f.SetValue(target, value); return true; } catch { }
                    }
                }
                t = t.BaseType;
            }
            return false;
        }

        public static void TouchField(object field)
        {
            TrySetBoolField(field, "itemHasChanged", true);
            TrySetBoolField(field, "stored", true);
            TrySetBoolProp(field,  "Touched", true);
            TryInvokeNoArgs(field, "SetInFile");
        }

        public static void ForceMarkDirty(object dict, object viewContent, Result r)
        {
            TrySetBoolProp(dict,  "IsDirty", true);
            TrySetBoolField(dict, "isDirty", true);
            TryInvokeNoArgs(dict, "ChildListTouched");
            TryInvokeNoArgs(dict, "DoIsDirtyChanged");
            if (viewContent != null)
            {
                if (!TrySetBoolProp(viewContent, "IsDirty", true))
                    TrySetBoolField(viewContent, "isDirty", true);
                TryInvokeNoArgs(viewContent, "OnIsDirtyChanged");
            }
            r.Messages.Add("Dict + view marked dirty.");
        }

        static bool TrySetBoolProp(object target, string name, bool value)
        {
            if (target == null) return false;
            var p = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return false;
            try { p.SetValue(target, value, null); return true; } catch { return false; }
        }

        static bool TrySetBoolField(object target, string name, bool value)
        {
            if (target == null) return false;
            var t = target.GetType();
            while (t != null && t != typeof(object))
            {
                var f = t.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null && f.FieldType == typeof(bool))
                {
                    try { f.SetValue(target, value); return true; } catch { return false; }
                }
                t = t.BaseType;
            }
            return false;
        }

        static bool TryInvokeNoArgs(object target, string methodName)
        {
            if (target == null) return false;
            var m = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (m == null) return false;
            try { m.Invoke(target, null); return true; } catch { return false; }
        }

        public static IEnumerable<object> EnumerateFields(object table)
        {
            var en = DictModel.GetProp(table, "Fields") as IEnumerable;
            if (en == null) yield break;
            foreach (var f in en) if (f != null) yield return f;
        }

        public static IEnumerable<object> EnumerateTriggers(object table)
        {
            var en = DictModel.GetProp(table, "Triggers") as IEnumerable;
            if (en == null) yield break;
            foreach (var t in en) if (t != null) yield return t;
        }

        public static string GetTriggerBody(object trigger)
        {
            return DictModel.AsString(DictModel.GetProp(trigger, "Body"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "Code"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "Source"))
                ?? DictModel.AsString(DictModel.GetProp(trigger, "TriggerCode"))
                ?? "";
        }
    }
}
