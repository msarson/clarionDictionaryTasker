using System;
using System.Collections;
using System.Linq;
using System.Text;

namespace ClarionDctAddin
{
    // Generates a single-document Markdown reference for the open dictionary.
    // Read-only. Output is browseable as-is or render-able via any Markdown
    // viewer (GitHub, VS Code, pandoc, etc.).
    internal static class MarkdownGenerator
    {
        public sealed class Options
        {
            public bool IncludeFields    = true;
            public bool IncludeKeys      = true;
            public bool IncludeRelations = true;
            public bool IncludeTOC       = true;
        }

        public static string Generate(object dict, Options opt)
        {
            var sb = new StringBuilder();
            var name = DictModel.GetDictionaryName(dict);
            var file = DictModel.GetDictionaryFileName(dict);
            var tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();

            sb.AppendLine("# Dictionary `" + name + "`");
            sb.AppendLine();
            sb.AppendLine("- **File:** `" + file + "`");
            sb.AppendLine("- **Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("- **Table count:** " + tables.Count);
            sb.AppendLine();

            if (opt.IncludeTOC && tables.Count > 0)
            {
                sb.AppendLine("## Contents");
                sb.AppendLine();
                foreach (var t in tables)
                {
                    var tname = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
                    sb.AppendLine("- [" + tname + "](#" + Anchor(tname) + ")");
                }
                sb.AppendLine();
            }

            foreach (var t in tables)
            {
                GenerateTable(sb, t, opt);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string GenerateForTable(object table, Options opt)
        {
            var sb = new StringBuilder();
            GenerateTable(sb, table, opt);
            return sb.ToString();
        }

        static void GenerateTable(StringBuilder sb, object t, Options opt)
        {
            var name = DictModel.AsString(DictModel.GetProp(t, "Name")) ?? "?";
            sb.AppendLine("## " + name);
            sb.AppendLine();

            var desc   = DictModel.AsString(DictModel.GetProp(t, "Description"))    ?? "";
            var prefix = DictModel.AsString(DictModel.GetProp(t, "Prefix"))         ?? "";
            var driver = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "";
            var full   = DictModel.AsString(DictModel.GetProp(t, "FullPathName"))   ?? "";

            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine("> " + desc.Replace("\r", " ").Replace("\n", " "));
                sb.AppendLine();
            }

            sb.AppendLine("| Attribute | Value |");
            sb.AppendLine("|-----------|-------|");
            sb.AppendLine("| Prefix    | `" + prefix + "` |");
            sb.AppendLine("| Driver    | `" + driver + "` |");
            if (!string.IsNullOrEmpty(full))
                sb.AppendLine("| Full path | `" + full + "` |");
            sb.AppendLine("| Fields    | " + DictModel.CountEnumerable(t, "Fields")    + " |");
            sb.AppendLine("| Keys      | " + DictModel.CountEnumerable(t, "Keys")      + " |");
            sb.AppendLine("| Relations | " + DictModel.CountEnumerable(t, "Relations") + " |");
            sb.AppendLine();

            if (opt.IncludeFields)
            {
                sb.AppendLine("### Fields");
                sb.AppendLine();
                sb.AppendLine("| Name | Type | Size | Picture | Description |");
                sb.AppendLine("|------|------|------|---------|-------------|");
                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields != null)
                {
                    foreach (var f in fields)
                    {
                        if (f == null) continue;
                        var flabel = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "";
                        var dt     = DictModel.AsString(DictModel.GetProp(f, "DataType")) ?? "";
                        var size   = DictModel.AsString(DictModel.GetProp(f, "FieldSize")) ?? "";
                        var pic    = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "";
                        var fdesc  = (DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "")
                                          .Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                        sb.AppendLine("| `" + flabel + "` | " + dt + " | " + size + " | "
                            + (string.IsNullOrEmpty(pic) ? "" : "`" + pic + "`") + " | " + fdesc + " |");
                    }
                }
                sb.AppendLine();
            }

            if (opt.IncludeKeys)
            {
                sb.AppendLine("### Keys");
                sb.AppendLine();
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                bool any = false;
                if (keys != null)
                {
                    sb.AppendLine("| Name | Type | Unique | Primary | Components |");
                    sb.AppendLine("|------|------|--------|---------|------------|");
                    foreach (var k in keys)
                    {
                        if (k == null) continue;
                        any = true;
                        var kname  = DictModel.AsString(DictModel.GetProp(k, "Name")) ?? "";
                        var ktype  = DictModel.AsString(DictModel.GetProp(k, "KeyType")) ?? "Key";
                        var uniq   = DictModel.AsString(DictModel.GetProp(k, "AttributeUnique"));
                        var prim   = DictModel.AsString(DictModel.GetProp(k, "AttributePrimary"));
                        var comps  = ComponentSummary(k);
                        sb.AppendLine("| `" + kname + "` | " + ktype + " | " + Yn(uniq) + " | " + Yn(prim) + " | " + comps + " |");
                    }
                }
                if (!any) sb.AppendLine("_No keys._");
                sb.AppendLine();
            }

            if (opt.IncludeRelations)
            {
                sb.AppendLine("### Relations");
                sb.AppendLine();
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                bool any = false;
                if (rels != null)
                {
                    sb.AppendLine("| Name | Related table |");
                    sb.AppendLine("|------|---------------|");
                    foreach (var r in rels)
                    {
                        if (r == null) continue;
                        any = true;
                        var rname = DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "";
                        string relatedName = "";
                        string[] child = { "ChildFile", "RelatedFile", "Child", "ToFile", "To", "File", "DetailFile", "ForeignFile" };
                        foreach (var p in child)
                        {
                            var v = DictModel.GetProp(r, p);
                            if (v != null) { relatedName = DictModel.AsString(DictModel.GetProp(v, "Name")) ?? ""; break; }
                        }
                        sb.AppendLine("| `" + rname + "` | `" + relatedName + "` |");
                    }
                }
                if (!any) sb.AppendLine("_No relations._");
                sb.AppendLine();
            }
        }

        static string Yn(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return string.Equals(v, "True", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
        }

        static string ComponentSummary(object key)
        {
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) return "";
            var names = new System.Collections.Generic.List<string>();
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n)) names.Add("`" + n + "`");
            }
            return string.Join(" + ", names.ToArray());
        }

        static string Anchor(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '_') sb.Append('-');
            }
            return sb.ToString();
        }
    }
}
