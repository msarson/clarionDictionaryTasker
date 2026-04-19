using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClarionDctAddin
{
    // Pure structural diff of two DictSnapshots. Case-insensitive matching on
    // table names, field labels, key names, and relation names.
    internal static class DictDiff
    {
        public sealed class Result
        {
            public List<DictSnapshot.TableSnap> AddedTables   = new List<DictSnapshot.TableSnap>();
            public List<DictSnapshot.TableSnap> RemovedTables = new List<DictSnapshot.TableSnap>();
            public List<TableChange>            ChangedTables = new List<TableChange>();
            public int UnchangedTableCount;
        }

        public sealed class TableChange
        {
            public string Name;
            public DictSnapshot.TableSnap BeforeTable;
            public DictSnapshot.TableSnap AfterTable;
            public bool AttributesChanged;
            public List<DictSnapshot.FieldSnap> AddedFields     = new List<DictSnapshot.FieldSnap>();
            public List<DictSnapshot.FieldSnap> RemovedFields   = new List<DictSnapshot.FieldSnap>();
            public List<FieldChange>            ChangedFields   = new List<FieldChange>();
            public List<DictSnapshot.KeySnap>   AddedKeys       = new List<DictSnapshot.KeySnap>();
            public List<DictSnapshot.KeySnap>   RemovedKeys     = new List<DictSnapshot.KeySnap>();
            public List<KeyChange>              ChangedKeys     = new List<KeyChange>();
            public List<DictSnapshot.RelSnap>   AddedRelations   = new List<DictSnapshot.RelSnap>();
            public List<DictSnapshot.RelSnap>   RemovedRelations = new List<DictSnapshot.RelSnap>();
            public List<RelChange>              ChangedRelations = new List<RelChange>();

            public bool HasAnyChange
            {
                get
                {
                    return AttributesChanged
                        || AddedFields.Count    > 0 || RemovedFields.Count    > 0 || ChangedFields.Count    > 0
                        || AddedKeys.Count      > 0 || RemovedKeys.Count      > 0 || ChangedKeys.Count      > 0
                        || AddedRelations.Count > 0 || RemovedRelations.Count > 0 || ChangedRelations.Count > 0;
                }
            }
        }

        public sealed class FieldChange
        {
            public string Label;
            public DictSnapshot.FieldSnap Before, After;
        }

        public sealed class KeyChange
        {
            public string Name;
            public DictSnapshot.KeySnap Before, After;
        }

        public sealed class RelChange
        {
            public string Name;
            public DictSnapshot.RelSnap Before, After;
        }

        public static Result Compute(DictSnapshot before, DictSnapshot after)
        {
            var r = new Result();
            var bMap = before.Tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var aMap = after.Tables.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                   .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var kv in aMap)
                if (!bMap.ContainsKey(kv.Key)) r.AddedTables.Add(kv.Value);
            foreach (var kv in bMap)
                if (!aMap.ContainsKey(kv.Key)) r.RemovedTables.Add(kv.Value);

            foreach (var kv in bMap)
            {
                DictSnapshot.TableSnap a;
                if (!aMap.TryGetValue(kv.Key, out a)) continue;
                var ch = DiffTable(kv.Value, a);
                if (ch.HasAnyChange) r.ChangedTables.Add(ch);
                else r.UnchangedTableCount++;
            }
            return r;
        }

        static TableChange DiffTable(DictSnapshot.TableSnap b, DictSnapshot.TableSnap a)
        {
            var ch = new TableChange { Name = a.Name, BeforeTable = b, AfterTable = a };
            if (b.Prefix != a.Prefix || b.Driver != a.Driver || b.Description != a.Description)
                ch.AttributesChanged = true;

            var bf = b.Fields.GroupBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var af = a.Fields.GroupBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in af) if (!bf.ContainsKey(kv.Key)) ch.AddedFields.Add(kv.Value);
            foreach (var kv in bf) if (!af.ContainsKey(kv.Key)) ch.RemovedFields.Add(kv.Value);
            foreach (var kv in bf)
            {
                DictSnapshot.FieldSnap x;
                if (!af.TryGetValue(kv.Key, out x)) continue;
                if (kv.Value.Type != x.Type || kv.Value.Size != x.Size
                    || kv.Value.Picture != x.Picture || kv.Value.Description != x.Description)
                    ch.ChangedFields.Add(new FieldChange { Label = kv.Key, Before = kv.Value, After = x });
            }

            var bk = b.Keys.GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                           .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ak = a.Keys.GroupBy(k => k.Name, StringComparer.OrdinalIgnoreCase)
                           .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ak) if (!bk.ContainsKey(kv.Key)) ch.AddedKeys.Add(kv.Value);
            foreach (var kv in bk) if (!ak.ContainsKey(kv.Key)) ch.RemovedKeys.Add(kv.Value);
            foreach (var kv in bk)
            {
                DictSnapshot.KeySnap x;
                if (!ak.TryGetValue(kv.Key, out x)) continue;
                if (!SameKey(kv.Value, x))
                    ch.ChangedKeys.Add(new KeyChange { Name = kv.Key, Before = kv.Value, After = x });
            }

            var br = b.Relations.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ar = a.Relations.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ar) if (!br.ContainsKey(kv.Key)) ch.AddedRelations.Add(kv.Value);
            foreach (var kv in br) if (!ar.ContainsKey(kv.Key)) ch.RemovedRelations.Add(kv.Value);
            foreach (var kv in br)
            {
                DictSnapshot.RelSnap x;
                if (!ar.TryGetValue(kv.Key, out x)) continue;
                if (!string.Equals(kv.Value.RelatedTable, x.RelatedTable, StringComparison.OrdinalIgnoreCase))
                    ch.ChangedRelations.Add(new RelChange { Name = kv.Key, Before = kv.Value, After = x });
            }
            return ch;
        }

        static bool SameKey(DictSnapshot.KeySnap a, DictSnapshot.KeySnap b)
        {
            if (a.Type != b.Type)       return false;
            if (a.Unique != b.Unique)   return false;
            if (a.Primary != b.Primary) return false;
            if (a.Components.Count != b.Components.Count) return false;
            for (int i = 0; i < a.Components.Count; i++)
                if (!string.Equals(a.Components[i], b.Components[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        public static string RenderMarkdown(DictSnapshot before, DictSnapshot after, Result diff)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Dictionary diff");
            sb.AppendLine();
            sb.AppendLine("- **Before:** `" + before.DictName + "` (captured " + before.TakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine("- **After:** `"  + after.DictName  + "` (captured " + after.TakenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")  + ")");
            sb.AppendLine("- **Generated:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine("Tables: `+" + diff.AddedTables.Count + "` added, `-" + diff.RemovedTables.Count
                + "` removed, `~" + diff.ChangedTables.Count + "` changed, `=" + diff.UnchangedTableCount + "` unchanged.");
            sb.AppendLine();

            if (diff.AddedTables.Count > 0)
            {
                sb.AppendLine("## Added tables");
                sb.AppendLine();
                foreach (var t in diff.AddedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine("- `" + t.Name + "` (" + t.Fields.Count + " fields, "
                        + t.Keys.Count + " keys, " + t.Relations.Count + " relations)");
                sb.AppendLine();
            }
            if (diff.RemovedTables.Count > 0)
            {
                sb.AppendLine("## Removed tables");
                sb.AppendLine();
                foreach (var t in diff.RemovedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine("- `" + t.Name + "` (" + t.Fields.Count + " fields, "
                        + t.Keys.Count + " keys, " + t.Relations.Count + " relations)");
                sb.AppendLine();
            }
            if (diff.ChangedTables.Count > 0)
            {
                sb.AppendLine("## Changed tables");
                sb.AppendLine();
                foreach (var c in diff.ChangedTables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine("### `" + c.Name + "`");
                    sb.AppendLine();
                    if (c.AttributesChanged)
                    {
                        sb.AppendLine("Attribute changes:");
                        if (c.BeforeTable.Prefix != c.AfterTable.Prefix)
                            sb.AppendLine("- prefix: `" + c.BeforeTable.Prefix + "` → `" + c.AfterTable.Prefix + "`");
                        if (c.BeforeTable.Driver != c.AfterTable.Driver)
                            sb.AppendLine("- driver: `" + c.BeforeTable.Driver + "` → `" + c.AfterTable.Driver + "`");
                        if (c.BeforeTable.Description != c.AfterTable.Description)
                            sb.AppendLine("- description changed");
                        sb.AppendLine();
                    }
                    if (c.AddedFields.Count + c.RemovedFields.Count + c.ChangedFields.Count > 0)
                    {
                        sb.AppendLine("**Fields**");
                        sb.AppendLine();
                        foreach (var f in c.AddedFields)
                            sb.AppendLine("- `+` `" + f.Label + "` " + f.Type + " / " + f.Size
                                + (string.IsNullOrEmpty(f.Picture) ? "" : " / `" + f.Picture + "`"));
                        foreach (var f in c.RemovedFields)
                            sb.AppendLine("- `-` `" + f.Label + "` " + f.Type + " / " + f.Size
                                + (string.IsNullOrEmpty(f.Picture) ? "" : " / `" + f.Picture + "`"));
                        foreach (var ch in c.ChangedFields)
                            sb.AppendLine("- `~` `" + ch.Label + "` "
                                + ch.Before.Type + "/" + ch.Before.Size
                                + " → " + ch.After.Type + "/" + ch.After.Size);
                        sb.AppendLine();
                    }
                    if (c.AddedKeys.Count + c.RemovedKeys.Count + c.ChangedKeys.Count > 0)
                    {
                        sb.AppendLine("**Keys**");
                        sb.AppendLine();
                        foreach (var k in c.AddedKeys)
                            sb.AppendLine("- `+` `" + k.Name + "` on "
                                + string.Join(" + ", k.Components.ToArray()));
                        foreach (var k in c.RemovedKeys)
                            sb.AppendLine("- `-` `" + k.Name + "` on "
                                + string.Join(" + ", k.Components.ToArray()));
                        foreach (var ch in c.ChangedKeys)
                            sb.AppendLine("- `~` `" + ch.Name + "` "
                                + string.Join(" + ", ch.Before.Components.ToArray())
                                + " → " + string.Join(" + ", ch.After.Components.ToArray()));
                        sb.AppendLine();
                    }
                    if (c.AddedRelations.Count + c.RemovedRelations.Count + c.ChangedRelations.Count > 0)
                    {
                        sb.AppendLine("**Relations**");
                        sb.AppendLine();
                        foreach (var r in c.AddedRelations)
                            sb.AppendLine("- `+` `" + r.Name + "` → `" + r.RelatedTable + "`");
                        foreach (var r in c.RemovedRelations)
                            sb.AppendLine("- `-` `" + r.Name + "` → `" + r.RelatedTable + "`");
                        foreach (var ch in c.ChangedRelations)
                            sb.AppendLine("- `~` `" + ch.Name + "` `" + ch.Before.RelatedTable
                                + "` → `" + ch.After.RelatedTable + "`");
                        sb.AppendLine();
                    }
                }
            }
            if (diff.AddedTables.Count == 0 && diff.RemovedTables.Count == 0 && diff.ChangedTables.Count == 0)
                sb.AppendLine("_No structural differences._");
            return sb.ToString();
        }
    }
}
