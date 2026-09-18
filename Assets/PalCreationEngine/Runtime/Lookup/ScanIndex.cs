using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace PalCreationEngine.Lookup
{
    /// <summary>
    /// The slice of the full-export scan a package must validate against.
    ///
    /// <c>scan_index.json</c> is <c>{ "kind": [ "key", ... ] }</c> for the kinds the tool
    /// writes or references: <c>enum_member</c>, <c>text_key</c>, <c>table_row</c>
    /// ("DT_Table::RowKey"), <c>spawner_name</c>, <c>placed_spawner</c> and
    /// <c>object_path</c>. It is built by Tools/PalCreationEngine/generate_lookups.py from
    /// the index in Docs/ExportTOC, never from the export folder itself.
    ///
    /// Every lookup is case-insensitive and returns the vanilla spelling, because the game
    /// matches FNames case-insensitively: 60 vanilla rows only resolve that way (the icon
    /// row for <c>BadCatgirl</c> is <c>BadCatGirl</c>, the drop row for <c>Boss_Anubis</c>
    /// is <c>BOSS_Anubis</c>). A writer must emit the vanilla spelling it gets back.
    /// </summary>
    public sealed class ScanIndex
    {
        private readonly Dictionary<string, Dictionary<string, string>> _kinds =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static ScanIndex Empty => new ScanIndex();

        public bool IsEmpty => _kinds.Count == 0;

        public IEnumerable<string> Kinds => _kinds.Keys;

        public int Count(string kind) =>
            kind != null && _kinds.TryGetValue(kind, out var d) ? d.Count : 0;

        public static ScanIndex Load(string path)
        {
            var index = new ScanIndex();
            var root = JObject.Parse(File.ReadAllText(path));
            foreach (var prop in root.Properties())
            {
                if (!(prop.Value is JArray arr)) continue;
                foreach (var token in arr)
                {
                    if (token.Type != JTokenType.String) continue;
                    index.Add(prop.Name, (string)token);
                }
            }
            return index;
        }

        /// <summary>Adds one key; the first spelling seen for a key is the canonical one.</summary>
        public void Add(string kind, string key)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key)) return;
            if (!_kinds.TryGetValue(kind, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _kinds[kind] = dict;
            }
            if (!dict.ContainsKey(key)) dict[key] = key;
        }

        /// <summary>The vanilla spelling of <paramref name="key"/> in <paramref name="kind"/>, or null.</summary>
        public string Resolve(string kind, string key)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(key)) return null;
            return _kinds.TryGetValue(kind, out var dict) && dict.TryGetValue(key, out var canonical)
                ? canonical
                : null;
        }

        public bool Exists(string kind, string key) => Resolve(kind, key) != null;

        public bool ObjectPathExists(string objectPath) => Exists("object_path", objectPath);

        /// <summary>
        /// "DT_X" and "DT_X_Common" name the same table (the composite and its parent), so a
        /// row is looked up under both spellings. Returns the full "Table::Row" key or null.
        /// </summary>
        public string ResolveTableRow(string table, string rowKey)
        {
            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(rowKey)) return null;
            var hit = Resolve("table_row", table + "::" + rowKey);
            if (hit != null) return hit;
            var alt = table.EndsWith("_Common", StringComparison.OrdinalIgnoreCase)
                ? table.Substring(0, table.Length - "_Common".Length)
                : table + "_Common";
            return Resolve("table_row", alt + "::" + rowKey);
        }

        public bool TableRowExists(string table, string rowKey) => ResolveTableRow(table, rowKey) != null;

        /// <summary>The vanilla spelling of a row key (without the table prefix), or null when absent.</summary>
        public string CanonicalRowKey(string table, string rowKey)
        {
            var hit = ResolveTableRow(table, rowKey);
            if (hit == null) return null;
            var i = hit.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? hit.Substring(i + 2) : hit;
        }
    }
}
