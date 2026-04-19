using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClarionDctAddin
{
    // Flat, serializable snapshot of a dictionary's structural shape.
    // Captured from the live reflection model, saved to a simple tab-delimited
    // text file, and reloaded later for diffing against another snapshot or
    // the live dictionary. Clarion can only hold one dictionary open at a time,
    // so "compare two dictionaries" is really "compare current vs. snapshot".
    internal sealed class DictSnapshot
    {
        public string DictName = "";
        public string FileName = "";
        public DateTime TakenUtc;
        public List<TableSnap> Tables = new List<TableSnap>();

        public sealed class TableSnap
        {
            public string Name = "", Prefix = "", Driver = "", Description = "";
            public List<FieldSnap> Fields    = new List<FieldSnap>();
            public List<KeySnap>   Keys      = new List<KeySnap>();
            public List<RelSnap>   Relations = new List<RelSnap>();
        }
        public sealed class FieldSnap
        {
            public string Label = "", Type = "", Size = "", Picture = "", Description = "";
        }
        public sealed class KeySnap
        {
            public string Name = "", Type = "";
            public bool Unique, Primary;
            public List<string> Components = new List<string>();
        }
        public sealed class RelSnap
        {
            public string Name = "", RelatedTable = "";
        }

        public static DictSnapshot CaptureFromLive(object dict)
        {
            var snap = new DictSnapshot
            {
                DictName = DictModel.GetDictionaryName(dict) ?? "",
                FileName = DictModel.GetDictionaryFileName(dict) ?? "",
                TakenUtc = DateTime.UtcNow
            };
            foreach (var t in DictModel.GetTables(dict))
            {
                var ts = new TableSnap
                {
                    Name        = DictModel.AsString(DictModel.GetProp(t, "Name"))           ?? "",
                    Prefix      = DictModel.AsString(DictModel.GetProp(t, "Prefix"))         ?? "",
                    Driver      = DictModel.AsString(DictModel.GetProp(t, "FileDriverName")) ?? "",
                    Description = DictModel.AsString(DictModel.GetProp(t, "Description"))    ?? ""
                };
                var fields = DictModel.GetProp(t, "Fields") as IEnumerable;
                if (fields != null) foreach (var f in fields)
                {
                    if (f == null) continue;
                    ts.Fields.Add(new FieldSnap
                    {
                        Label       = DictModel.AsString(DictModel.GetProp(f, "Label"))         ?? "",
                        Type        = DictModel.AsString(DictModel.GetProp(f, "DataType"))      ?? "",
                        Size        = DictModel.AsString(DictModel.GetProp(f, "FieldSize"))     ?? "",
                        Picture     = DictModel.AsString(DictModel.GetProp(f, "ScreenPicture")) ?? "",
                        Description = DictModel.AsString(DictModel.GetProp(f, "Description"))   ?? ""
                    });
                }
                var keys = DictModel.GetProp(t, "Keys") as IEnumerable;
                if (keys != null) foreach (var k in keys)
                {
                    if (k == null) continue;
                    var ks = new KeySnap
                    {
                        Name    = DictModel.AsString(DictModel.GetProp(k, "Name"))    ?? "",
                        Type    = DictModel.AsString(DictModel.GetProp(k, "KeyType")) ?? "",
                        Unique  = string.Equals(DictModel.AsString(DictModel.GetProp(k, "AttributeUnique")),  "True", StringComparison.OrdinalIgnoreCase),
                        Primary = string.Equals(DictModel.AsString(DictModel.GetProp(k, "AttributePrimary")), "True", StringComparison.OrdinalIgnoreCase)
                    };
                    foreach (var c in KeyComponents(k)) ks.Components.Add(c);
                    ts.Keys.Add(ks);
                }
                var rels = DictModel.GetProp(t, "Relations") as IEnumerable;
                if (rels != null) foreach (var r in rels)
                {
                    if (r == null) continue;
                    string relatedName = "";
                    string[] child = { "ChildFile", "RelatedFile", "Child", "ToFile", "To", "File", "DetailFile", "ForeignFile" };
                    foreach (var p in child)
                    {
                        var v = DictModel.GetProp(r, p);
                        if (v != null) { relatedName = DictModel.AsString(DictModel.GetProp(v, "Name")) ?? ""; break; }
                    }
                    ts.Relations.Add(new RelSnap
                    {
                        Name = DictModel.AsString(DictModel.GetProp(r, "Name")) ?? "",
                        RelatedTable = relatedName
                    });
                }
                snap.Tables.Add(ts);
            }
            return snap;
        }

        static IEnumerable<string> KeyComponents(object key)
        {
            string[] candidates = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            IEnumerable en = null;
            foreach (var c in candidates)
            {
                en = DictModel.GetProp(key, c) as IEnumerable;
                if (en != null && !(en is string)) break;
                en = null;
            }
            if (en == null) yield break;
            foreach (var comp in en)
            {
                if (comp == null) continue;
                var fld = DictModel.GetProp(comp, "Field") ?? DictModel.GetProp(comp, "DDField");
                var n = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(comp, "Label")) ?? DictModel.AsString(DictModel.GetProp(comp, "Name"));
                if (!string.IsNullOrEmpty(n)) yield return n;
            }
        }

        // Simple tab-delimited format. Escape rules: \ → \\  TAB → \t  LF → \n  CR → \r
        // First column is a section tag: SNAP (header), T (table), F (field), K (key), R (relation).

        static string Esc(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                if      (ch == '\\') sb.Append("\\\\");
                else if (ch == '\t') sb.Append("\\t");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                var n = s[++i];
                if      (n == '\\') sb.Append('\\');
                else if (n == 't')  sb.Append('\t');
                else if (n == 'n')  sb.Append('\n');
                else if (n == 'r')  sb.Append('\r');
                else                sb.Append(n);
            }
            return sb.ToString();
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SNAP\t1\t" + Esc(DictName) + "\t" + Esc(FileName) + "\t" + TakenUtc.ToString("o"));
            foreach (var t in Tables)
            {
                sb.AppendLine("T\t" + Esc(t.Name) + "\t" + Esc(t.Prefix) + "\t" + Esc(t.Driver) + "\t" + Esc(t.Description));
                foreach (var f in t.Fields)
                    sb.AppendLine("F\t" + Esc(f.Label) + "\t" + Esc(f.Type) + "\t" + Esc(f.Size) + "\t" + Esc(f.Picture) + "\t" + Esc(f.Description));
                foreach (var k in t.Keys)
                    sb.AppendLine("K\t" + Esc(k.Name) + "\t" + Esc(k.Type) + "\t" + (k.Unique ? "1" : "0") + "\t" + (k.Primary ? "1" : "0") + "\t" + Esc(string.Join(",", k.Components.ToArray())));
                foreach (var r in t.Relations)
                    sb.AppendLine("R\t" + Esc(r.Name) + "\t" + Esc(r.RelatedTable));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static DictSnapshot Load(string path)
        {
            var snap = new DictSnapshot();
            TableSnap current = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var parts = raw.Split('\t');
                switch (parts[0])
                {
                    case "SNAP":
                        if (parts.Length >= 5)
                        {
                            snap.DictName = Unesc(parts[2]);
                            snap.FileName = Unesc(parts[3]);
                            DateTime d;
                            if (DateTime.TryParse(parts[4], null, System.Globalization.DateTimeStyles.RoundtripKind, out d))
                                snap.TakenUtc = d;
                        }
                        break;
                    case "T":
                        current = new TableSnap
                        {
                            Name        = parts.Length > 1 ? Unesc(parts[1]) : "",
                            Prefix      = parts.Length > 2 ? Unesc(parts[2]) : "",
                            Driver      = parts.Length > 3 ? Unesc(parts[3]) : "",
                            Description = parts.Length > 4 ? Unesc(parts[4]) : ""
                        };
                        snap.Tables.Add(current);
                        break;
                    case "F":
                        if (current != null)
                            current.Fields.Add(new FieldSnap
                            {
                                Label       = parts.Length > 1 ? Unesc(parts[1]) : "",
                                Type        = parts.Length > 2 ? Unesc(parts[2]) : "",
                                Size        = parts.Length > 3 ? Unesc(parts[3]) : "",
                                Picture     = parts.Length > 4 ? Unesc(parts[4]) : "",
                                Description = parts.Length > 5 ? Unesc(parts[5]) : ""
                            });
                        break;
                    case "K":
                        if (current != null)
                        {
                            var k = new KeySnap
                            {
                                Name    = parts.Length > 1 ? Unesc(parts[1]) : "",
                                Type    = parts.Length > 2 ? Unesc(parts[2]) : "",
                                Unique  = parts.Length > 3 && parts[3] == "1",
                                Primary = parts.Length > 4 && parts[4] == "1"
                            };
                            if (parts.Length > 5)
                            {
                                var comps = Unesc(parts[5]);
                                if (!string.IsNullOrEmpty(comps))
                                    foreach (var c in comps.Split(',')) k.Components.Add(c);
                            }
                            current.Keys.Add(k);
                        }
                        break;
                    case "R":
                        if (current != null)
                            current.Relations.Add(new RelSnap
                            {
                                Name         = parts.Length > 1 ? Unesc(parts[1]) : "",
                                RelatedTable = parts.Length > 2 ? Unesc(parts[2]) : ""
                            });
                        break;
                }
            }
            return snap;
        }
    }
}
