using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TrueServer
{
    // Flat by design: Unity's JsonUtility cannot deserialise dictionaries or nullable numbers,
    // so Tools/build_world_options.py flattens everything before it gets here.

    [Serializable]
    public sealed class OptionDef
    {
        public string name;
        public string iniKey;
        public string category;
        public string type;
        public string @default;
        public string[] enumValues;
        public bool isBool;
        public bool isSecret;
        public bool hasMin;
        public bool hasMax;
        public float min;
        public float max;
        public bool clampedRangeUnknown;
        public string when;             // "creation" | "restart"
        public bool casingDiffersFromIni;

        // What ExpandedWorldSettings would rewrite this field's range to. The mod's only job is
        // to overwrite Min/Max on PalOptionSubsystem -- it adds no options and changes no values.
        public bool modTouches;
        public float modMin;
        public float modMax;
        /// <summary>The mod's max is LOWER than vanilla. "Expanded" is a misnomer for these.</summary>
        public bool modNarrows;

        /// <summary>False for the three fields a Lua mod cannot write (Name and Array properties).</summary>
        public bool luaSettable;

        /// <summary>Array&lt;...&gt; -- holds several values at once, not one of several.</summary>
        public bool isArray;

        /// <summary>False for the one struct-only field that appears in no ini.</summary>
        public bool inIni;
        /// <summary>Str and Name values are written quoted.</summary>
        public bool quoted;

        /// <summary>Readable name, from the PalServerSettings config or our own table.</summary>
        public string title;
        /// <summary>What the option actually does.</summary>
        public string desc;
        /// <summary>What it accepts, as the source phrased it.</summary>
        public string valuesHint;

        /// <summary>The default parsed as a set -- "(Steam,Xbox,PS5,Mac)" becomes four entries.</summary>
        public string[] DefaultList()
        {
            var raw = (@default ?? string.Empty).Trim().Trim('(', ')');
            if (raw.Length == 0) return Array.Empty<string>();
            return raw.Split(',')
                      .Select(x => x.Trim().Trim('"'))
                      .Where(x => x.Length > 0)
                      .ToArray();
        }

        public string ModScaleText =>
            modTouches ? Num(modMin) + " - " + Num(modMax) : string.Empty;

        /// <summary>What the game publishes as the slider range -- shown, never enforced.</summary>
        public string ScaleText
        {
            get
            {
                if (hasMin && hasMax) return Num(min) + " - " + Num(max);
                if (hasMax) return "max " + Num(max);
                if (hasMin) return "min " + Num(min);
                return clampedRangeUnknown ? "clamped, range unknown" : "-";
            }
        }

        public bool HasScale => hasMin || hasMax;

        public string AcceptsText
        {
            get
            {
                if (enumValues != null && enumValues.Length > 0) return string.Join(", ", enumValues);
                return isBool ? "True / False" : string.Empty;
            }
        }

        private static string Num(float v) =>
            Mathf.Approximately(v, Mathf.Round(v)) ? ((int)Mathf.Round(v)).ToString() : v.ToString("0.###");
    }

    [Serializable] public sealed class KeyVal { public string key; public string value; }

    [Serializable]
    public sealed class PresetDef
    {
        public string name;             // Easy | Normal | Hard | Hardcore
        public string difficulty;
        public KeyVal[] fields;
    }

    [Serializable]
    public sealed class ModeDef
    {
        public string name;             // Single | ListenMulti | Dedicated_PvE | Dedicated_PvP
        public KeyVal[] fields;
    }

    [Serializable]
    public sealed class OptionPayload
    {
        public string generatedUtc;
        public int structFieldCount;
        public int rangedCount;
        public int clampUnknownCount;
        public int unrangedCount;
        public string modName;
        public float modGlobalMin;
        public float modGlobalMax;
        public int modFieldCount;
        public OptionDef[] options;
        public PresetDef[] presets;
        public ModeDef[] modes;
    }

    /// <summary>
    /// The 123 world options with their published scales, generated from the game's own data by
    /// Tools/build_world_options.py. Nothing is filtered: options the in-game World Settings
    /// screen never shows are here too.
    /// </summary>
    public static class OptionCatalog
    {
        private static OptionPayload _payload;
        public static string LoadError { get; private set; }

        public static string JsonPath => Path.Combine(Application.streamingAssetsPath, "trueserver_options.json");

        public static OptionPayload Payload
        {
            get { if (_payload == null) Load(); return _payload; }
        }

        public static bool IsLoaded => Payload != null && Payload.options != null && Payload.options.Length > 0;

        public static void Reload() { _payload = null; LoadError = null; Load(); }

        private static void Load()
        {
            try
            {
                if (!File.Exists(JsonPath))
                {
                    LoadError = "Not found: " + JsonPath + (Application.isEditor
                                    ? "\nRun:  python Tools/build_world_options.py"
                                    : "\nIt ships with the program; reinstall to restore it.");
                    return;
                }
                _payload = JsonUtility.FromJson<OptionPayload>(File.ReadAllText(JsonPath));
                if (_payload?.options == null || _payload.options.Length == 0)
                    LoadError = "Parsed but empty: " + JsonPath;
            }
            catch (Exception e)
            {
                LoadError = e.Message;
            }
        }

        public static IEnumerable<OptionDef> All =>
            IsLoaded ? Payload.options : Enumerable.Empty<OptionDef>();

        public static IEnumerable<string> Categories =>
            All.Select(o => o.category).Distinct().OrderBy(c => c);

        public static IEnumerable<OptionDef> InCategory(string cat) =>
            All.Where(o => o.category == cat).OrderBy(o => o.name);

        public static OptionDef Find(string name) =>
            All.FirstOrDefault(o => string.Equals(o.name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The 14 fields that differ between difficulty presets. If ApplyWorldPreset runs at load
        /// (F-0022, unproven) editing any of these alongside a non-Custom Difficulty loses the edit.
        /// </summary>
        public static HashSet<string> PresetGovernedFields()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Payload?.presets == null || Payload.presets.Length < 2) return set;

            var first = Payload.presets[0];
            foreach (var f in first.fields ?? Array.Empty<KeyVal>())
            {
                foreach (var other in Payload.presets.Skip(1))
                {
                    var match = other.fields?.FirstOrDefault(x => x.key == f.key);
                    if (match != null && match.value != f.value) { set.Add(f.key); break; }
                }
            }
            return set;
        }

        /// <summary>The six bools the world mode sets. Treat as not settable from the ini (F-0022).</summary>
        public static HashSet<string> ModeGovernedFields()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mode = Payload?.modes?.FirstOrDefault();
            foreach (var f in mode?.fields ?? Array.Empty<KeyVal>()) set.Add(f.key);
            return set;
        }

        public static ModeDef Mode(string name) =>
            Payload?.modes?.FirstOrDefault(m => m.name == name);

        public static PresetDef Preset(string name) =>
            Payload?.presets?.FirstOrDefault(p => p.name == name);
    }
}
