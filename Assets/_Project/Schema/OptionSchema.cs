using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace PalPanel.Schema
{
    /// <summary>How an option relates to the game's own Min/Max clamp table.</summary>
    public enum OptionTier
    {
        /// <summary>Clamped in PalOptionWorldStaticSettings and rewritten by ExpandedWorldSettings
        /// (which LOWERS three vanilla maxima and raises the two drop limits to 10000, see ModNarrows): a slider in the in-game World Settings UI.</summary>
        UiSliderClamped,

        /// <summary>Clamped, but the mod leaves it alone. (Name kept for the schema string.)</summary>
        ClampedNotWidened,

        /// <summary>No clamp entry. Not slider-driven -- a toggle, a dropdown, or a
        /// server-only key with no in-game UI at all. Not by itself proof of being hidden.</summary>
        NoStaticClamp,

        /// <summary>In the usmap struct but absent from DefaultPalWorldSettings.ini.</summary>
        NotInIni,
    }

    /// <summary>Coarse value kind, derived from the Unreal type in the usmap dump.</summary>
    public enum OptionValueKind { Float, Int, Bool, String, Enum, EnumArray, NameArray, Unknown }

    /// <summary>One console cap from WorldSettingThreshold (consoles only; no Windows entry).</summary>
    [Serializable]
    public sealed class ConsoleCap
    {
        [JsonProperty("platform")] public string Platform;
        [JsonProperty("value")] public double Value;
    }

    [Serializable]
    public sealed class PalOptionField
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("iniKey")] public string IniKey;
        [JsonProperty("unrealType")] public string UnrealType;
        [JsonProperty("enumType")] public string EnumType;
        [JsonProperty("enumValues")] public List<string> EnumValues;
        [JsonProperty("default")] public string DefaultRaw;
        [JsonProperty("current")] public string CurrentRaw;
        [JsonProperty("category")] public string Category;
        [JsonProperty("iniPresence")] public string IniPresence;
        [JsonProperty("hasStaticClamp")] public bool HasStaticClamp;
        /// <summary>True only when the mod's range is wider at one end than vanilla (since 2026-09-18; it used
        /// to mean "the mod touches this field", which is ModRewritesClamp).</summary>
        [JsonProperty("modWidensClamp")] public bool ModWidensClamp;

        // ---- F-0012: vanilla slider range from the BP_PalOptionSubsystem CDO, console caps, and what
        // ---- ExpandedWorldSettings rewrites the range to. min/max are 0 when HasMin/HasMax is false.
        [JsonProperty("hasMin")] public bool HasMin;
        [JsonProperty("hasMax")] public bool HasMax;
        [JsonProperty("min")] public double Min;
        [JsonProperty("max")] public double Max;
        /// <summary>In the clamp struct but the CDO stores no Min/Max: a native default we cannot read (6 fields).</summary>
        [JsonProperty("clampedRangeUnknown")] public bool ClampedRangeUnknown;
        [JsonProperty("consoleCaps")] public List<ConsoleCap> ConsoleCaps = new();
        [JsonProperty("modRewritesClamp")] public bool ModRewritesClamp;
        [JsonProperty("modMin")] public double ModMin;
        [JsonProperty("modMax")] public double ModMax;
        [JsonProperty("modWidens")] public bool ModWidens;
        /// <summary>The mod's ceiling (or floor) is tighter than vanilla: "expanded" is a misnomer here.</summary>
        [JsonProperty("modNarrows")] public bool ModNarrows;
        [JsonProperty("tier")] public string TierRaw;
        [JsonProperty("isSecret")] public bool IsSecret;
        [JsonProperty("casingDiffersFromIni")] public bool CasingDiffersFromIni;

        [JsonIgnore]
        public OptionTier Tier => TierRaw switch
        {
            "ui_slider_clamped" => OptionTier.UiSliderClamped,
            "clamped_not_widened" => OptionTier.ClampedNotWidened,
            "no_static_clamp" => OptionTier.NoStaticClamp,
            _ => OptionTier.NotInIni,
        };

        [JsonIgnore]
        public OptionValueKind Kind
        {
            get
            {
                if (string.IsNullOrEmpty(UnrealType)) return OptionValueKind.Unknown;
                if (UnrealType.StartsWith("Array<enum")) return OptionValueKind.EnumArray;
                if (UnrealType.StartsWith("Array<Name")) return OptionValueKind.NameArray;
                if (UnrealType.StartsWith("enum")) return OptionValueKind.Enum;
                return UnrealType switch
                {
                    "Float" => OptionValueKind.Float,
                    "Int" => OptionValueKind.Int,
                    "Bool" => OptionValueKind.Bool,
                    "Str" => OptionValueKind.String,
                    "Name" => OptionValueKind.String,
                    _ => OptionValueKind.Unknown,
                };
            }
        }

        [JsonIgnore] public bool HasVanillaRange => HasMin || HasMax;

        /// <summary>Vanilla slider range as text ("0.1 - 20", "max 5000"), or null when none is stored.</summary>
        [JsonIgnore]
        public string VanillaRangeText => !HasVanillaRange ? null
            : HasMin && HasMax ? $"{Min:0.###} - {Max:0.###}" : HasMax ? $"max {Max:0.###}" : $"min {Min:0.###}";

        /// <summary>False when a numeric value lies outside the vanilla slider range. Ranges are the in-game slider's,
        /// not a proven server limit (clamp-at-load UNTESTED, F-0012): a caller warns, it does not refuse.</summary>
        public bool InVanillaRange(double v) => (!HasMin || v >= Min) && (!HasMax || v <= Max);

        /// <summary>True when the live server value differs from the stock default.</summary>
        [JsonIgnore]
        public bool DiffersFromDefault =>
            !string.Equals(CurrentRaw ?? DefaultRaw, DefaultRaw, StringComparison.Ordinal);
    }

    // Not [Serializable]: this class is only ever read through Newtonsoft (Load), and the panel
    // reloads it whenever its field is null, so Unity never needs to serialize it. The attribute
    // only earned the UAC1009 warning for the Dictionary field (2026-09-16).
    public sealed class OptionSchema
    {
        [JsonProperty("generatedUtc")] public string GeneratedUtc;
        [JsonProperty("structFieldCount")] public int StructFieldCount;
        [JsonProperty("tierMeaning")] public Dictionary<string, string> TierMeaning;
        [JsonProperty("modName")] public string ModName;
        [JsonProperty("modGlobalMin")] public double ModGlobalMin;
        [JsonProperty("modGlobalMax")] public double ModGlobalMax;
        [JsonProperty("options")] public List<PalOptionField> Options = new();

        [JsonIgnore] private Dictionary<string, PalOptionField> _byKey;

        public const string FileName = "palworld_option_schema.json";

        /// <summary>Load the generated schema from StreamingAssets.</summary>
        public static OptionSchema Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"{FileName} not found. Run Tools/PalworldServer/generate_schema.py to build it.", path);

            var schema = JsonConvert.DeserializeObject<OptionSchema>(File.ReadAllText(path));
            schema.BuildIndex();
            return schema;
        }

        private void BuildIndex()
        {
            _byKey = new Dictionary<string, PalOptionField>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Options)
            {
                // Index both spellings: the struct name and the INI's own key.
                _byKey[o.Name] = o;
                if (!string.IsNullOrEmpty(o.IniKey)) _byKey[o.IniKey] = o;
            }
        }

        public PalOptionField Find(string key) =>
            _byKey != null && _byKey.TryGetValue(key, out var f) ? f : null;

        public IEnumerable<string> Categories =>
            Options.Select(o => o.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal);

        public IEnumerable<PalOptionField> InCategory(string category) =>
            Options.Where(o => string.Equals(o.Category, category, StringComparison.Ordinal));

        /// <summary>Case-insensitive substring match over name and category.</summary>
        public IEnumerable<PalOptionField> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Options;
            return Options.Where(o =>
                o.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (o.Category?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        }
    }
}
