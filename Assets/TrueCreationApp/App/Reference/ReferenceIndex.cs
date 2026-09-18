// The 1.0.4 export index for the Reference tab: 18.7 MB / 78,596 lines, streamed one line at a time into a compact
// struct (only one parsed line is alive at a time). Loaded once per run; Load has no Unity calls, so the tab reads it
// on a worker thread and the window stays responsive.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TrueCreation.App
{
    public struct RefAsset
    {
        public string Rel;
        public string Cat;
        public string Sub;
        public long Bytes;
        public string Type;
        public int Rows;      // DataTable row count, -1 when not applicable
        public int Cols;      // column count of the first row
        public int CdoProps;  // Blueprint/DataAsset CDO property count

        public string Name
        {
            get
            {
                int i = Rel.LastIndexOf('/');
                string n = i >= 0 ? Rel.Substring(i + 1) : Rel;
                return n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 5) : n;
            }
        }

        public string SizeLabel =>
            Bytes >= 1048576 ? (Bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
          : Bytes >= 1024 ? (Bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB"
          : Bytes + " B";
    }

    public static class ReferenceIndex
    {
        public const string IndexFileName = "export_index_1.0.4.jsonl";
        private static readonly object Gate = new object();
        private static List<RefAsset> _all;
        private static Dictionary<string, int> _catCounts;
        private static string _error;

        public static bool Loaded => _all != null;
        public static string Error => _error;
        public static int Count => _all?.Count ?? 0;
        public static IReadOnlyList<RefAsset> All => _all;
        public static IReadOnlyDictionary<string, int> CategoryCounts => _catCounts;

        public static void Unload()
        {
            lock (Gate) { _all = null; _catCounts = null; _error = null; }
        }

        /// <summary>Reads the index at <paramref name="path"/>. Safe to call from a worker thread.</summary>
        public static bool Load(string path, bool force = false)
        {
            lock (Gate)
            {
                if (_all != null && !force) return true;
                _error = null;
                if (!File.Exists(path))
                {
                    _error = "Index not found: " + path;
                    return false;
                }

                var list = new List<RefAsset>(80000);
                var cats = new Dictionary<string, int>();
                try
                {
                    using (var sr = new StreamReader(path))
                    {
                        string s;
                        while ((s = sr.ReadLine()) != null)
                        {
                            if (s.Length < 2) continue;
                            var o = RefJson.Parse(s);
                            if (o == null) continue;
                            var r = new RefAsset
                            {
                                Rel = RefJson.Str(o, "rel"),
                                Cat = RefJson.Str(o, "cat"),
                                Sub = RefJson.Str(o, "sub"),
                                Bytes = (long)RefJson.Num(o, "bytes"),
                                Type = RefJson.Str(o, "type"),
                                Rows = -1,
                            };
                            if (o is Dictionary<string, object> d)
                            {
                                if (d.ContainsKey("rows")) r.Rows = (int)RefJson.Num(o, "rows", -1);
                                var cols = RefJson.Arr(o, "cols");
                                if (cols != null) r.Cols = cols.Count;
                                if (d.ContainsKey("cdo_props")) r.CdoProps = (int)RefJson.Num(o, "cdo_props");
                            }
                            if (string.IsNullOrEmpty(r.Rel)) continue;
                            list.Add(r);
                            if (!string.IsNullOrEmpty(r.Cat)) cats[r.Cat] = cats.TryGetValue(r.Cat, out int c) ? c + 1 : 1;
                        }
                    }
                }
                catch (Exception e)
                {
                    _error = "Failed reading the index: " + e.Message;
                    return false;
                }
                _catCounts = cats;
                _all = list;
                return true;
            }
        }

        /// <summary>Every record whose path contains <paramref name="query"/> (empty = all), in one category or all.</summary>
        public static List<RefAsset> Search(string query, string category)
        {
            var res = new List<RefAsset>();
            var all = _all;
            if (all == null) return res;
            bool anyCat = string.IsNullOrEmpty(category) || category == "(all)";
            bool anyQ = string.IsNullOrWhiteSpace(query);
            string q = anyQ ? null : query.Trim();
            foreach (var r in all)
            {
                if (!anyCat && r.Cat != category) continue;
                if (!anyQ && r.Rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                res.Add(r);
            }
            return res;
        }

        public static List<string> Categories()
        {
            var l = new List<string> { "(all)" };
            var counts = _catCounts;
            if (counts == null) return l;
            var keys = new List<string>(counts.Keys);
            keys.Sort((a, b) => counts[b].CompareTo(counts[a]));
            l.AddRange(keys);
            return l;
        }
    }
}
