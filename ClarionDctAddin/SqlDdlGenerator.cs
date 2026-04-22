using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClarionDctAddin
{
    // Generates CREATE TABLE + CREATE INDEX statements for every table in an
    // open Clarion dictionary. Dialects: SQL Server, PostgreSQL, SQLite,
    // MySQL, MariaDB, Oracle, Firebird. Strictly read-only; no dictionary
    // mutation.
    internal static class SqlDdlGenerator
    {
        public enum Dialect { SqlServer, Postgres, SQLite, MySql, MariaDb, Oracle, Firebird }

        public sealed class Options
        {
            public Dialect Dialect          = Dialect.SqlServer;
            public bool    IncludeDropTable = true;
            public bool    IncludeIndexes   = true;
            public bool    IncludeComments  = true;
            public bool    UseFullPathName  = true;
        }

        public static string Generate(object dict, Options opt)
        {
            var sb = new StringBuilder();
            var dictName = DictModel.GetDictionaryName(dict);

            sb.AppendLine("-- ================================================================");
            sb.AppendLine("-- SQL DDL generated from Clarion dictionary '" + dictName + "'");
            sb.AppendLine("-- Generated:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("-- Dialect:     " + opt.Dialect);
            sb.AppendLine("-- ================================================================");
            sb.AppendLine();

            var tables = DictModel.GetTables(dict)
                .OrderBy(t => DictModel.AsString(DictModel.GetProp(t, "Name")), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var t in tables)
            {
                GenerateTable(sb, t, opt);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string GenerateForTable(object table, Options opt)
        {
            var label = DictModel.AsString(DictModel.GetProp(table, "Label")) ?? "?";
            var sb = new StringBuilder();
            sb.AppendLine("-- ================================================================");
            sb.AppendLine("-- SQL DDL for table '" + label + "'");
            sb.AppendLine("-- Generated:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("-- Dialect:     " + opt.Dialect);
            sb.AppendLine("-- ================================================================");
            sb.AppendLine();
            GenerateTable(sb, table, opt);
            return sb.ToString();
        }

        static void GenerateTable(StringBuilder sb, object table, Options opt)
        {
            var label   = DictModel.AsString(DictModel.GetProp(table, "Label")) ?? "?";
            var clName  = DictModel.AsString(DictModel.GetProp(table, "Name"))  ?? label;
            var fullPath = DictModel.AsString(DictModel.GetProp(table, "FullPathName")) ?? "";
            var sqlName = (opt.UseFullPathName && !string.IsNullOrEmpty(fullPath)) ? fullPath : label;

            if (opt.IncludeComments)
            {
                sb.AppendLine("-- ----------------------------------------------------------------");
                sb.AppendLine("-- " + label + (string.IsNullOrEmpty(fullPath) ? "" : "   (" + fullPath + ")"));
                var desc = DictModel.AsString(DictModel.GetProp(table, "Description")) ?? "";
                if (!string.IsNullOrEmpty(desc)) sb.AppendLine("-- " + desc.Replace("\r", " ").Replace("\n", " "));
                sb.AppendLine("-- ----------------------------------------------------------------");
            }

            if (opt.IncludeDropTable)
                sb.AppendLine(DropStatement(sqlName, opt.Dialect));

            sb.AppendLine("CREATE TABLE " + QuoteIdent(sqlName, opt.Dialect) + " (");

            var fields = DictModel.GetProp(table, "Fields") as IEnumerable;
            // Each line is kept as (code, trailing-comment) so the comma that
            // separates columns can be inserted BETWEEN them — otherwise it
            // ends up glued onto the end of the comment and the SQL parser
            // silently drops the field separator.
            var lines = new List<Tuple<string, string>>();
            var pkCols = GetKeyColumnLabels(GetPrimaryKeyObject(table));

            if (fields != null)
            {
                foreach (var f in fields)
                {
                    if (f == null) continue;
                    var flabel = DictModel.AsString(DictModel.GetProp(f, "Label")) ?? "?";
                    var code   = "  " + PadRight(QuoteIdent(flabel, opt.Dialect), 32) + " "
                               + PadRight(MapType(f, opt.Dialect), 18);

                    bool isPk = pkCols.Any(c => string.Equals(c, flabel, StringComparison.OrdinalIgnoreCase));
                    if (pkCols.Count == 1 && isPk) code += " NOT NULL";

                    string comment = "";
                    if (opt.IncludeComments)
                    {
                        var desc = DictModel.AsString(DictModel.GetProp(f, "Description")) ?? "";
                        if (!string.IsNullOrEmpty(desc))
                            comment = desc.Replace("\r", " ").Replace("\n", " ");
                    }
                    lines.Add(Tuple.Create(code, comment));
                }
            }

            if (pkCols.Count > 0)
            {
                var quoted = pkCols.Select(c => QuoteIdent(c, opt.Dialect));
                lines.Add(Tuple.Create(
                    "  CONSTRAINT " + QuoteIdent("PK_" + label, opt.Dialect)
                    + " PRIMARY KEY (" + string.Join(", ", quoted.ToArray()) + ")",
                    ""));
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var sep = (i < lines.Count - 1) ? "," : "";
                var commentPart = string.IsNullOrEmpty(lines[i].Item2) ? "" : "  -- " + lines[i].Item2;
                sb.AppendLine(lines[i].Item1 + sep + commentPart);
            }
            sb.AppendLine(");");

            if (opt.IncludeIndexes)
            {
                var keys = DictModel.GetProp(table, "Keys") as IEnumerable;
                if (keys != null)
                {
                    foreach (var k in keys)
                    {
                        if (k == null) continue;
                        var isPrimary = string.Equals(
                            DictModel.AsString(DictModel.GetProp(k, "AttributePrimary")),
                            "True", StringComparison.OrdinalIgnoreCase);
                        if (isPrimary) continue;

                        var kLabel = DictModel.AsString(DictModel.GetProp(k, "Label")) ?? "?";
                        var isUnique = string.Equals(
                            DictModel.AsString(DictModel.GetProp(k, "AttributeUnique")),
                            "True", StringComparison.OrdinalIgnoreCase);

                        var cols = GetKeyColumnLabels(k);
                        if (cols.Count == 0) continue;

                        var idxName = label + "_" + kLabel;
                        var stmt = (isUnique ? "CREATE UNIQUE INDEX " : "CREATE INDEX ")
                                 + QuoteIdent(idxName, opt.Dialect)
                                 + " ON " + QuoteIdent(sqlName, opt.Dialect)
                                 + " (" + string.Join(", ", cols.Select(c => QuoteIdent(c, opt.Dialect)).ToArray()) + ");";
                        sb.AppendLine(stmt);
                    }
                }
            }
        }

        // ---------------- type mapping ----------------
        static string MapType(object field, Dialect dialect)
        {
            var t        = DictModel.AsString(DictModel.GetProp(field, "DataType")) ?? "";
            var size     = ParseULong(DictModel.AsString(DictModel.GetProp(field, "FieldSize")));
            var chars    = ParseULong(DictModel.AsString(DictModel.GetProp(field, "Characters")));
            var places   = ParseInt(DictModel.AsString(DictModel.GetProp(field, "Places")));
            var picture  = DictModel.AsString(DictModel.GetProp(field, "ScreenPicture")) ?? "";

            string upperType = t.ToUpperInvariant();

            // LONG with a date picture == Clarion date
            if ((upperType == "LONG" || upperType == "ULONG") &&
                !string.IsNullOrEmpty(picture) && picture.StartsWith("@D", StringComparison.OrdinalIgnoreCase))
                return "DATE";

            bool isMySqlFamily = dialect == Dialect.MySql || dialect == Dialect.MariaDb;
            bool isOracle     = dialect == Dialect.Oracle;
            bool isFirebird   = dialect == Dialect.Firebird;

            // Oracle has no plain VARCHAR (it's deprecated / aliased),
            // Firebird keeps VARCHAR(n) but has a much smaller default limit.
            string varcharBase = isOracle ? "VARCHAR2" : "VARCHAR";

            switch (upperType)
            {
                case "STRING":
                    return varcharBase + "(" + Clamp(chars > 0 ? chars : size, 1, 8000) + ")";
                case "CSTRING":
                    return varcharBase + "(" + Clamp(size > 0 ? size - 1 : chars, 1, 8000) + ")";
                case "PSTRING":
                    return varcharBase + "(" + Clamp(size > 0 ? size - 1 : chars, 1, 8000) + ")";
                case "BYTE":
                    if (dialect == Dialect.SqlServer || isMySqlFamily) return "TINYINT";
                    if (isOracle) return "NUMBER(3)";
                    return "SMALLINT";
                case "SHORT":
                    return isOracle ? "NUMBER(5)" : "SMALLINT";
                case "USHORT":
                    if (isOracle) return "NUMBER(5)";
                    return "INT";
                case "LONG":
                    if (isOracle)               return "NUMBER(10)";
                    if (dialect == Dialect.SQLite) return "INTEGER";
                    if (isFirebird)             return "INTEGER";
                    return "INT";
                case "ULONG":
                    if (isOracle)   return "NUMBER(19)";
                    return "BIGINT";
                case "REAL":
                    if (dialect == Dialect.Postgres) return "DOUBLE PRECISION";
                    if (isMySqlFamily)               return "DOUBLE";
                    if (isOracle)                    return "BINARY_DOUBLE";
                    if (isFirebird)                  return "DOUBLE PRECISION";
                    return "FLOAT";
                case "SREAL":
                    if (isMySqlFamily) return "FLOAT";
                    if (isOracle)      return "BINARY_FLOAT";
                    if (isFirebird)    return "FLOAT";
                    return "REAL";
                case "BFLOAT4":
                    if (isMySqlFamily) return "FLOAT";
                    if (isOracle)      return "BINARY_FLOAT";
                    if (isFirebird)    return "FLOAT";
                    return "REAL";
                case "BFLOAT8":
                    if (dialect == Dialect.Postgres) return "DOUBLE PRECISION";
                    if (isMySqlFamily)               return "DOUBLE";
                    if (isOracle)                    return "BINARY_DOUBLE";
                    if (isFirebird)                  return "DOUBLE PRECISION";
                    return "FLOAT";
                case "DECIMAL":
                case "PDECIMAL":
                {
                    int precision = (int)(chars > 0 ? chars : 10);
                    int scale     = Math.Max(0, places);
                    if (scale > precision) scale = precision;
                    // Oracle uses NUMBER(p,s). Firebird keeps DECIMAL(p,s).
                    if (isOracle) return "NUMBER(" + precision + "," + scale + ")";
                    return "DECIMAL(" + precision + "," + scale + ")";
                }
                case "DATE":
                    // Oracle's DATE also holds a time component; it's the
                    // natural pick here because that's how Clarion programs
                    // treat it when stored as a real DATE.
                    return "DATE";
                case "TIME":
                    if (isOracle) return "TIMESTAMP";   // Oracle has no plain TIME
                    return "TIME";
                case "MEMO":
                    switch (dialect)
                    {
                        case Dialect.SqlServer: return "NVARCHAR(MAX)";
                        case Dialect.Postgres:  return "TEXT";
                        case Dialect.MySql:
                        case Dialect.MariaDb:   return "LONGTEXT";
                        case Dialect.Oracle:    return "CLOB";
                        case Dialect.Firebird:  return "BLOB SUB_TYPE TEXT";
                        default:                return "TEXT";
                    }
                case "BLOB":
                    switch (dialect)
                    {
                        case Dialect.SqlServer: return "VARBINARY(MAX)";
                        case Dialect.Postgres:  return "BYTEA";
                        case Dialect.MySql:
                        case Dialect.MariaDb:   return "LONGBLOB";
                        case Dialect.Oracle:    return "BLOB";
                        case Dialect.Firebird:  return "BLOB SUB_TYPE BINARY";
                        default:                return "BLOB";
                    }
                case "GROUP":
                {
                    var fallback = isOracle
                        ? "VARCHAR2(100)"
                        : (dialect == Dialect.SqlServer ? "NVARCHAR(100)" : "VARCHAR(100)");
                    return "/* GROUP - manual */ " + fallback;
                }
            }
            return "/* " + t + " */ " + varcharBase + "(50)";
        }

        // ---------------- helpers ----------------
        static string DropStatement(string tableName, Dialect dialect)
        {
            switch (dialect)
            {
                case Dialect.SqlServer:
                    return "IF OBJECT_ID(N'" + tableName.Replace("'", "''") + "', N'U') IS NOT NULL DROP TABLE "
                         + QuoteIdent(tableName, dialect) + ";";
                case Dialect.Postgres:
                case Dialect.SQLite:
                case Dialect.MySql:
                case Dialect.MariaDb:
                    return "DROP TABLE IF EXISTS " + QuoteIdent(tableName, dialect) + ";";
                case Dialect.Oracle:
                {
                    // Anonymous PL/SQL block: DROP TABLE ..., swallow the "table does not
                    // exist" error (-942), re-raise anything else. Works on every
                    // supported Oracle version; run with SQL*Plus using "/" as terminator.
                    return "BEGIN\r\n"
                         + "  EXECUTE IMMEDIATE 'DROP TABLE " + QuoteIdent(tableName, dialect) + " CASCADE CONSTRAINTS PURGE';\r\n"
                         + "EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF;\r\n"
                         + "END;\r\n"
                         + "/";
                }
                case Dialect.Firebird:
                {
                    // DROP TABLE IF EXISTS is Firebird 4.0+. For 2.5/3.0 compatibility
                    // wrap in an EXECUTE BLOCK that checks the system catalog first.
                    // isql needs "SET TERM" so the inner semicolons aren't parsed as
                    // statement ends — we restore ';' right after.
                    var qualified = QuoteIdent(tableName, dialect);
                    var bareUpper = BareForFirebirdCatalog(tableName);
                    return "SET TERM ^ ;\r\n"
                         + "EXECUTE BLOCK AS BEGIN\r\n"
                         + "  IF (EXISTS (SELECT 1 FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = '" + bareUpper + "')) THEN\r\n"
                         + "    EXECUTE STATEMENT 'DROP TABLE " + qualified + "';\r\n"
                         + "END^\r\n"
                         + "SET TERM ; ^";
                }
            }
            return "";
        }

        // Firebird stores relation names in upper-case when the source identifier
        // was unquoted; quoted identifiers preserve their case. For the catalog
        // lookup in the conditional DROP we need to present the same byte pattern
        // that's actually on disk — trim one outer level of quoting if present,
        // and uppercase the rest when it looks like a plain identifier.
        static string BareForFirebirdCatalog(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // Strip any schema/owner prefix — Firebird has no schemas per se.
            var dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1) name = name.Substring(dot + 1);
            if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"')
                return name.Substring(1, name.Length - 2).Replace("'", "''");
            return name.ToUpperInvariant().Replace("'", "''");
        }

        static string QuoteIdent(string name, Dialect dialect)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Split "schema.table" — quote parts separately so we get [dbo].[BITACORA]
            // rather than [dbo.BITACORA] on SQL Server.
            var dotIdx = name.IndexOf('.');
            if (dotIdx > 0 && dotIdx < name.Length - 1)
                return QuoteSingle(name.Substring(0, dotIdx), dialect) + "."
                     + QuoteSingle(name.Substring(dotIdx + 1), dialect);
            return QuoteSingle(name, dialect);
        }

        static string QuoteSingle(string name, Dialect dialect)
        {
            switch (dialect)
            {
                case Dialect.SqlServer: return "[" + name + "]";
                case Dialect.Postgres:
                case Dialect.SQLite:
                case Dialect.Oracle:
                case Dialect.Firebird:  return "\"" + name.Replace("\"", "\"\"") + "\"";
                case Dialect.MySql:
                case Dialect.MariaDb:   return "`" + name.Replace("`", "``") + "`";
            }
            return name;
        }

        static object GetPrimaryKeyObject(object table)
        {
            return DictModel.GetProp(table, "PrimaryKey")
                ?? DictModel.GetProp(table, "PrimaryOrUniqueKey");
        }

        static List<string> GetKeyColumnLabels(object key)
        {
            var result = new List<string>();
            if (key == null) return result;
            var comps = FindComponents(key);
            if (comps == null) return result;
            foreach (var c in comps)
            {
                if (c == null) continue;
                var fld = DictModel.GetProp(c, "Field") ?? DictModel.GetProp(c, "DDField");
                var lbl = fld != null
                    ? DictModel.AsString(DictModel.GetProp(fld, "Label"))
                    : DictModel.AsString(DictModel.GetProp(c, "Label"))
                      ?? DictModel.AsString(DictModel.GetProp(c, "Name"));
                if (!string.IsNullOrEmpty(lbl)) result.Add(lbl);
            }
            return result;
        }

        static IEnumerable FindComponents(object key)
        {
            string[] names = { "Components", "KeyComponents", "Fields", "KeyFields", "Segments" };
            foreach (var n in names)
            {
                var v = DictModel.GetProp(key, n) as IEnumerable;
                if (v != null && !(v is string)) return v;
            }
            return null;
        }

        static ulong ParseULong(string s)
        {
            ulong v; return ulong.TryParse(s, out v) ? v : 0UL;
        }
        static int ParseInt(string s)
        {
            int v; return int.TryParse(s, out v) ? v : 0;
        }
        static ulong Clamp(ulong v, ulong min, ulong max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        static string PadRight(string s, int width)
        {
            return s == null ? new string(' ', width) : (s.Length >= width ? s : s + new string(' ', width - s.Length));
        }
    }
}
