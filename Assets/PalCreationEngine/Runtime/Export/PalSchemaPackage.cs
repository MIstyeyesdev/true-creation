using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// A complete PalSchema mod in memory, one member per loader folder, and the writer that
    /// turns it into files. Layout and file shapes follow the verified working mod
    /// (PalVariantPandemonium, Workshop 3800182837):
    ///
    /// <code>
    /// PalSchema/enums/&lt;Mod&gt;.json          { "EPalTribeID": [ "NewId" ] }          bare member names
    /// PalSchema/pals/&lt;Mod&gt;.json           { "NewId": { 90 fields }, "BOSS_NewId": {...} }
    /// PalSchema/raw/&lt;Table&gt;.json          { "&lt;Table&gt;": { "RowKey": {...} } }   one file per table
    /// PalSchema/translations/&lt;lang&gt;/&lt;Mod&gt;.json   { "DT_PalNameText": { "PAL_NAME_NewId": "..." }, ... }
    /// PalSchema/spawns/&lt;Mod&gt;.json         [ { Sheet entry }, ... ]                an ARRAY
    /// PalSchema/blueprints/&lt;Mod&gt;.json     { "/Game/.../BP_X.BP_X_C": { component edits } }
    /// PalSchema/items/&lt;Mod&gt;.json          typed items loader (gear chain)
    /// </code>
    ///
    /// Free of UnityEngine so the console harness can exercise it.
    /// </summary>
    public sealed class PalSchemaPackage
    {
        /// <summary>enums/: enum name -> bare member names to add.</summary>
        public readonly Dictionary<string, List<string>> Enums = new Dictionary<string, List<string>>();

        /// <summary>pals/: row key -> full row.</summary>
        public readonly Dictionary<string, PalMonsterParameterRow> Pals = new Dictionary<string, PalMonsterParameterRow>();

        /// <summary>raw/: table name -> (row key -> row). Rows are JObjects so cloned vanilla rows keep their exact shape.</summary>
        public readonly Dictionary<string, Dictionary<string, JObject>> Raw = new Dictionary<string, Dictionary<string, JObject>>();

        /// <summary>translations/: language -> text table section -> text key -> text.</summary>
        public readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Translations =
            new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

        /// <summary>spawns/: Sheet entries.</summary>
        public readonly List<SheetSpawnEntry> Spawns = new List<SheetSpawnEntry>();

        /// <summary>blueprints/: component edits keyed by full object path.</summary>
        public readonly PalSchemaBlueprints Blueprints = new PalSchemaBlueprints();

        /// <summary>items/: the typed items loader document (gear chain), or null.</summary>
        public JObject Items;

        /// <summary>Legacy single raw/ file (DT_PalWildSpawner edits etc.), written only when non-empty.</summary>
        public PalSchemaMod Legacy;

        /// <summary>Text table sections in the order the working mod writes them.</summary>
        public static class TextSections
        {
            public const string PalName = "DT_PalNameText";
            public const string NamePrefix = "DT_NamePrefixText_Common";
            public const string SkillName = "DT_SkillNameText_Common";
            public const string FirstActivated = "DT_PalFirstActivatedInfoText";
            public const string LongDescription = "DT_PalLongDescriptionText";
            public static readonly string[] Order = { PalName, NamePrefix, SkillName, FirstActivated, LongDescription };
        }

        public Dictionary<string, JObject> RawTable(string table)
        {
            if (!Raw.TryGetValue(table, out var rows))
            {
                rows = new Dictionary<string, JObject>();
                Raw[table] = rows;
            }
            return rows;
        }

        public void AddRaw(string table, string rowKey, JObject row) => RawTable(table)[rowKey] = row;

        public void AddRaw(string table, string rowKey, object typedRow) =>
            RawTable(table)[rowKey] = JObject.FromObject(typedRow, JsonSerializer.Create(PalSchemaMod.SerializerSettings));

        public void AddText(string language, string section, string key, string text)
        {
            if (!Translations.TryGetValue(language, out var sections))
            {
                sections = new Dictionary<string, Dictionary<string, string>>();
                Translations[language] = sections;
            }
            if (!sections.TryGetValue(section, out var entries))
            {
                entries = new Dictionary<string, string>();
                sections[section] = entries;
            }
            entries[key] = text ?? "";
        }

        public void AddEnumMember(string enumName, string member)
        {
            if (!Enums.TryGetValue(enumName, out var list))
            {
                list = new List<string>();
                Enums[enumName] = list;
            }
            if (!list.Contains(member)) list.Add(member);
        }

        /// <summary>Every text key the package defines, across languages ("PAL_NAME_X" form, no table prefix).</summary>
        public HashSet<string> TextKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lang in Translations.Values)
                foreach (var section in lang.Values)
                    foreach (var key in section.Keys)
                        keys.Add(key);
            return keys;
        }

        /// <summary>
        /// Relative path (under PalSchema/) -> file text, in the working mod's order. Empty
        /// members produce no file. <paramref name="modName"/> is the file stem.
        /// </summary>
        public Dictionary<string, string> ToFiles(string modName)
        {
            if (string.IsNullOrWhiteSpace(modName)) throw new ArgumentException("modName is required", nameof(modName));
            var stem = new string(modName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (stem.Length == 0) stem = "Mod";
            var settings = PalSchemaMod.SerializerSettings;
            var files = new Dictionary<string, string>();

            if (Enums.Count > 0)
                files["enums/" + stem + ".json"] = JsonConvert.SerializeObject(Enums, settings);

            if (Pals.Count > 0)
                files["pals/" + stem + ".json"] = JsonConvert.SerializeObject(Pals, settings);

            // raw/: one file per table, the working mod's table order first, then any other table A-Z
            var tables = RawTables.NewPalOrder.Where(t => Raw.ContainsKey(t) && Raw[t].Count > 0)
                .Concat(Raw.Keys.Where(t => !RawTables.NewPalOrder.Contains(t) && Raw[t].Count > 0).OrderBy(t => t, StringComparer.Ordinal));
            foreach (var table in tables)
            {
                var doc = new JObject { [table] = JObject.FromObject(Raw[table]) };
                files["raw/" + table + ".json"] = JsonConvert.SerializeObject(doc, settings);
            }

            foreach (var lang in Translations.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var sections = Translations[lang];
                var ordered = new JObject();
                foreach (var name in TextSections.Order)
                    if (sections.TryGetValue(name, out var entries) && entries.Count > 0)
                        ordered[name] = JObject.FromObject(entries);
                foreach (var name in sections.Keys.Where(n => !TextSections.Order.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
                    if (sections[name].Count > 0) ordered[name] = JObject.FromObject(sections[name]);
                if (ordered.Count > 0)
                    files["translations/" + lang + "/" + stem + ".json"] = JsonConvert.SerializeObject(ordered, settings);
            }

            if (Spawns.Count > 0)
                files["spawns/" + stem + ".json"] = JsonConvert.SerializeObject(Spawns, settings);

            if (!Blueprints.IsEmpty)
                files["blueprints/" + stem + ".json"] = Blueprints.ToJson();

            if (Items != null && Items.Count > 0)
                files["items/" + stem + ".json"] = JsonConvert.SerializeObject(Items, settings);

            if (Legacy != null && (Legacy.WildSpawners.Count > 0 || Legacy.ItemLottery.Count > 0 || Legacy.FieldLotteryNames.Count > 0))
                files["raw/" + stem + "_legacy.json"] = Legacy.ToJson();

            return files;
        }

        /// <summary>The loader targets this package ships (folder or raw table), for comparison with a working mod's footprint.</summary>
        public HashSet<string> LoaderTargets()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Enums.Count > 0) set.Add("enums");
            if (Pals.Count > 0) set.Add("pals");
            foreach (var t in Raw.Where(kv => kv.Value.Count > 0)) set.Add(t.Key);
            if (Translations.Count > 0) set.Add("translations");
            if (Spawns.Count > 0) set.Add("spawns");
            if (!Blueprints.IsEmpty) set.Add("blueprints");
            if (Items != null && Items.Count > 0) set.Add("items");
            return set;
        }
    }
}
