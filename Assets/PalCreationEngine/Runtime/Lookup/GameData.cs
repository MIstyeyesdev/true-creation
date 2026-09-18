using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PalCreationEngine.Lookup
{
    public sealed class PalSummary
    {
        [JsonProperty("rowKey")] public string RowKey;
        [JsonProperty("bpClass")] public string BpClass;
        [JsonProperty("tribe")] public string Tribe;
        [JsonProperty("zukanIndex")] public int ZukanIndex;
        [JsonProperty("zukanIndexSuffix")] public string ZukanIndexSuffix;
        [JsonProperty("size")] public string Size;
        [JsonProperty("rarity")] public int Rarity;
        [JsonProperty("element1")] public string Element1;
        [JsonProperty("element2")] public string Element2;
        [JsonProperty("genus")] public string Genus;
        [JsonProperty("isBoss")] public bool IsBoss;
        [JsonProperty("isTowerBoss")] public bool IsTowerBoss;
        [JsonProperty("isRaidBoss")] public bool IsRaidBoss;

        /// <summary>
        /// True for the Boss/Raid/Predator/Gym/Summon/Quest variants that share
        /// a Paldex slot with a catchable Pal. Worth hiding by default: they
        /// outnumber the ordinary Pals and clutter a picker.
        /// </summary>
        public bool IsVariant =>
            IsBoss || IsTowerBoss || IsRaidBoss ||
            RowKey != null && (RowKey.StartsWith("BOSS_", StringComparison.Ordinal)
                               || RowKey.StartsWith("RAID_", StringComparison.Ordinal)
                               || RowKey.StartsWith("PREDATOR_", StringComparison.Ordinal)
                               || RowKey.StartsWith("GYM_", StringComparison.Ordinal)
                               || RowKey.StartsWith("SUMMON_", StringComparison.Ordinal)
                               || RowKey.StartsWith("POLICE_", StringComparison.Ordinal)
                               || RowKey.StartsWith("Quest_", StringComparison.Ordinal));
    }

    public sealed class ItemSummary
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("typeA")] public string TypeA;
        [JsonProperty("typeB")] public string TypeB;
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("rarity")] public int Rarity;
        [JsonProperty("weight")] public float Weight;
        [JsonProperty("price")] public int Price;
    }

    public sealed class PassiveSkillEffect
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("value")] public float Value;
        [JsonProperty("target")] public string Target;
    }

    public sealed class PassiveSkillSummary
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("lotteryWeight")] public int LotteryWeight;
        [JsonProperty("effects")] public List<PassiveSkillEffect> Effects = new List<PassiveSkillEffect>();

        /// <summary>Which entity kinds this can roll on: AddPal, AddRarePal, AddWorldTreePal...</summary>
        [JsonProperty("scope")] public List<string> Scope = new();

        /// <summary>When it applies: Always, InOtomo, Riding, Worker, InBaseCamp...</summary>
        [JsonProperty("invoke")] public List<string> Invoke = new();

        /// <summary>Who it reaches: ToSelf, ToTrainer, ToSelfAndTrainer, ToBuildObject...</summary>
        [JsonProperty("targets")] public List<string> Targets = new();

        /// <summary>True when any effect reaches the player rather than the Pal.</summary>
        [JsonProperty("affectsPlayer")] public bool AffectsPlayer;

        [JsonProperty("category")] public string Category;

        public string Describe()
        {
            if (Effects == null || Effects.Count == 0) return Id;
            var parts = Effects.Select(e => $"{e.Type} {e.Value:+0.##;-0.##;0}");
            return $"{Id}  (rank {Rank}) - {string.Join(", ", parts)}";
        }
    }

    public sealed class FieldSchema
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("realMin")] public double? RealMin;
        [JsonProperty("realMax")] public double? RealMax;
        [JsonProperty("constant")] public bool Constant;
        [JsonProperty("realValues")] public List<string> RealValues;
    }

    public sealed class BlueprintPath
    {
        [JsonProperty("objectPath")] public string ObjectPath;

        /// <summary>
        /// "ground-truth" (taken from a working mod or export), "derived" (from
        /// the validated folder rule), or "derived-case-insensitive" (the rule
        /// only matched ignoring case, because the shipped data is inconsistent
        /// there -- worth surfacing rather than trusting silently).
        /// </summary>
        [JsonProperty("source")] public string Source;

        public bool IsCertain => Source == "ground-truth";
    }

    public sealed class SizeDefaults
    {
        [JsonProperty("sampleCount")] public int SampleCount;
        [JsonProperty("fields")] public Dictionary<string, JToken> Fields = new Dictionary<string, JToken>();
    }

    public sealed class ZukanEntry
    {
        [JsonProperty("rowKey")] public string RowKey;
        [JsonProperty("suffix")] public string Suffix;
        [JsonProperty("bpClass")] public string BpClass;
        [JsonProperty("element1")] public string Element1;
        [JsonProperty("element2")] public string Element2;
        [JsonProperty("isVariant")] public bool IsVariant;

        /// <summary>
        /// What this variant is, from its own row key: at index 5, suffix "B"
        /// is BluePlatypus_Fire, so the tag is "Fire". Empty for the base form.
        /// </summary>
        [JsonProperty("variantTag")] public string VariantTag;

        [JsonProperty("elementLabel")] public string ElementLabel;

        /// <summary>
        /// "B - Fire" or "(none) - base form", for a picker.
        ///
        /// Forward slashes are swapped for "+": Unity's DropdownField reads "/"
        /// as a submenu separator, so a dual-element label like "Dark/Water"
        /// would silently become a nested "Dark" menu containing "Water".
        /// </summary>
        public string SuffixLabel
        {
            get
            {
                var key = string.IsNullOrEmpty(Suffix) ? "(none)" : Suffix;
                var what = !string.IsNullOrEmpty(VariantTag) ? VariantTag
                    : !string.IsNullOrEmpty(ElementLabel) ? ElementLabel
                    : "base form";
                return $"{key} - {what}".Replace("/", " + ");
            }
        }
    }

    /// <summary>
    /// What occupies one Paldex index. A suffix only means something inside its
    /// own index -- "B" at index 5 is BluePlatypus_Fire, "B" at index 7 is
    /// FlyingManta_Thunder -- so a suffix has to be described per index rather
    /// than labelled globally.
    /// </summary>
    public sealed class ZukanSlot
    {
        [JsonProperty("primary")] public string Primary;
        [JsonProperty("suffixesInUse")] public List<string> SuffixesInUse = new List<string>();
        [JsonProperty("entries")] public List<ZukanEntry> Entries = new List<ZukanEntry>();

        /// <summary>
        /// The letter a NEW Pal gets at this number: the first of B..Z that no
        /// row here uses. Vanilla uses '' (base) and 'B' (one variant) only; the
        /// verified working mod (PalVariantPandemonium) gives its variants 'C'
        /// where the base and 'B' already exist. Null when B..Z are all taken.
        /// </summary>
        public string NextFreeSuffix(params string[] alsoUsed)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in SuffixesInUse) used.Add(s ?? "");
            foreach (var e in Entries) used.Add(e.Suffix ?? "");
            foreach (var s in alsoUsed ?? Array.Empty<string>()) used.Add(s ?? "");
            return "BCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).FirstOrDefault(s => !used.Contains(s));
        }

        public string DescribeSuffix(string suffix)
        {
            var match = Entries.FirstOrDefault(e => (e.Suffix ?? "") == (suffix ?? ""));
            if (match == null)
                return string.IsNullOrEmpty(suffix)
                    ? "no suffix - free at this index"
                    : $"'{suffix}' is unused at this index - free to take";

            var elements = match.Element2 is "None" or null
                ? match.Element1
                : $"{match.Element1}/{match.Element2}";
            return $"'{suffix}' is taken by {match.RowKey} ({elements})";
        }
    }

    public sealed class StatTier
    {
        [JsonProperty("label")] public string Label;
        [JsonProperty("percentile")] public int Percentile;
        [JsonProperty("exampleRowKey")] public string ExampleRowKey;
        [JsonProperty("statTotal")] public int StatTotal;
        [JsonProperty("stats")] public Dictionary<string, int> Stats = new Dictionary<string, int>();
        [JsonProperty("movement")] public Dictionary<string, int> Movement = new Dictionary<string, int>();
        [JsonProperty("size")] public string Size;
        [JsonProperty("rarity")] public int Rarity;
    }

    public sealed class StatBenchmarks
    {
        [JsonProperty("sampleCount")] public int SampleCount;
        [JsonProperty("tiers")] public List<StatTier> Tiers = new List<StatTier>();
        [JsonProperty("percentiles")] public Dictionary<string, Dictionary<string, float>> Percentiles
            = new Dictionary<string, Dictionary<string, float>>();

        /// <summary>
        /// Where a value sits against shipped Pals, phrased for a user rather
        /// than as a raw number. Returns null when the field has no ladder.
        /// </summary>
        public string Describe(string field, double value)
        {
            if (!Percentiles.TryGetValue(field, out var ladder)) return null;

            var ordered = ladder.OrderBy(kv => int.Parse(kv.Key)).ToList();
            if (value < ordered.First().Value) return "below every shipped Pal";
            if (value > ordered.Last().Value) return "above every shipped Pal";

            string previous = null;
            foreach (var (pct, threshold) in ordered.Select(kv => (kv.Key, kv.Value)))
            {
                if (value <= threshold)
                    return previous == null
                        ? $"bottom {pct}% of shipped Pals"
                        : $"around the {pct}th percentile";
                previous = pct;
            }
            return null;
        }
    }

    public sealed class DisplayNames
    {
        [JsonProperty("pals")] public Dictionary<string, string> Pals = new Dictionary<string, string>();
        [JsonProperty("items")] public Dictionary<string, string> Items = new Dictionary<string, string>();

        [JsonProperty("itemDescriptions")] public Dictionary<string, string> ItemDescriptions = new();
        [JsonProperty("palDescriptions")] public Dictionary<string, string> PalDescriptions = new();

        /// <summary>Null until the localization text tables have been exported.</summary>
        [JsonProperty("resolvedFrom")] public string ResolvedFrom;

        /// <summary>
        /// Game description text carries inline markup the engine resolves at
        /// display time -- &lt;itemName id=|Arrow|/&gt; becomes the item's name.
        /// This unwraps it to the id so a description reads as a sentence, and
        /// collapses the CRLFs the tables use.
        /// </summary>
        public static string Readable(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var text = System.Text.RegularExpressions.Regex.Replace(
                raw, @"<[A-Za-z]+ id=\|([^|]*)\|/>", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]*>", "");
            // Element/UI keys leak through as COMMON_ELEMENT_NAME_Fire; trim the
            // prefix rather than showing the raw key.
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"COMMON_ELEMENT_NAME_", "");
            return text.Replace((char)13, (char)32).Replace((char)10, (char)32).Trim();
        }

        public string ItemDescription(string itemId) =>
            itemId != null && ItemDescriptions.TryGetValue(itemId, out var d) ? Readable(d) : "";

        public string PalDescription(string rowKey) =>
            rowKey != null && PalDescriptions.TryGetValue(rowKey, out var d) ? Readable(d) : "";

        public bool Available => !string.IsNullOrEmpty(ResolvedFrom);
    }

    public sealed class NpcSummary
    {
        [JsonProperty("rowKey")] public string RowKey;
        [JsonProperty("bpClass")] public string BpClass;
        [JsonProperty("aiResponse")] public string AiResponse;
        [JsonProperty("aiSightResponse")] public string AiSightResponse;
        [JsonProperty("organization")] public string Organization;

        /// <summary>
        /// Whether this NPC has a DT_NPCTalkFlow row. That row is the only
        /// thing that decides whether the in-game [F] Talk prompt appears --
        /// not the Blueprint, and not the AI settings.
        /// </summary>
        [JsonProperty("canTalk")] public bool CanTalk;
    }

    public sealed class TalkFlowEntry
    {
        [JsonProperty("assetPath")] public string AssetPath;
        [JsonProperty("graph")] public string Graph;
    }

    public sealed class ShopStockEntry
    {
        [JsonProperty("itemId")] public string ItemId;
        [JsonProperty("productType")] public string ProductType;
        [JsonProperty("overridePrice")] public int OverridePrice;
        [JsonProperty("productNum")] public int ProductNum;
        [JsonProperty("stock")] public int Stock;
    }

    public sealed class ShopLotteryEntry
    {
        [JsonProperty("shopGroupName")] public string ShopGroupName;
        [JsonProperty("weight")] public float Weight;
    }

    public sealed class QuestSummary
    {
        [JsonProperty("questType")] public string QuestType;
        [JsonProperty("asset")] public string Asset;
    }

    public sealed class SpawnSlot
    {
        [JsonProperty("slot")] public int Slot;
        [JsonProperty("pal")] public string Pal;
        [JsonProperty("npc")] public string Npc;
        [JsonProperty("lvMin")] public int LvMin;
        [JsonProperty("lvMax")] public int LvMax;
        [JsonProperty("numMin")] public int NumMin;
        [JsonProperty("numMax")] public int NumMax;
    }

    public sealed class SpawnRow
    {
        [JsonProperty("rowKey")] public string RowKey;
        [JsonProperty("spawnerName")] public string SpawnerName;
        [JsonProperty("spawnerType")] public string SpawnerType;
        [JsonProperty("weight")] public float Weight;
        [JsonProperty("freeSlots")] public int FreeSlots;
        [JsonProperty("slots")] public List<SpawnSlot> Slots = new();
    }

    /// <summary>
    /// One wild spawn group. PlacementCount is how many times it is placed in
    /// the world -- a group with zero placements (dungeon or event spawners)
    /// will never produce an overworld spawn no matter what is added to it.
    /// </summary>
    public sealed class SpawnGroup
    {
        [JsonProperty("placementCount")] public int PlacementCount;
        [JsonProperty("layers")] public List<string> Layers = new();
        [JsonProperty("rows")] public List<SpawnRow> Rows = new();
    }

    /// <summary>
    /// What a borrowed model actually is. Size is a per-row field, but the MODEL
    /// is fixed by BPClass -- so offering all five sizes for any model is
    /// misleading. A model family ships 1-3 sizes in practice (523 of 731 ship
    /// exactly two: the Pal and its Alpha).
    /// </summary>
    public sealed class ModelInfo
    {
        [JsonProperty("bpClass")] public string BpClass;
        [JsonProperty("family")] public string Family;

        /// <summary>Sizes this exact model is used at in the shipped data.</summary>
        [JsonProperty("sizes")] public List<string> Sizes = new();

        /// <summary>Sizes anything in the same Blueprint folder is used at.</summary>
        [JsonProperty("familySizes")] public List<string> FamilySizes = new();

        /// <summary>Null when the Blueprint has no override, i.e. it renders at 1.0.</summary>
        [JsonProperty("meshScale")] public List<float> MeshScale;

        [JsonProperty("capsuleHalfHeight")] public float? CapsuleHalfHeight;
        [JsonProperty("capsuleRadius")] public float? CapsuleRadius;
        [JsonProperty("blueprintFound")] public bool BlueprintFound;

        public float ScaleX => MeshScale != null && MeshScale.Count > 0 ? MeshScale[0] : 1f;
    }

    public sealed class FunnelWazaInfo
    {
        [JsonProperty("element")] public string Element;
        [JsonProperty("category")] public string Category;
        [JsonProperty("power")] public int? Power;
        [JsonProperty("coolTime")] public float? CoolTime;
        [JsonProperty("minRange")] public int? MinRange;
        [JsonProperty("maxRange")] public int? MaxRange;
    }

    /// <summary>
    /// A funnel character class -- the helper that appears beside the player.
    /// The class fixes the element and attack, because it selects the AI action
    /// which selects the waza. There is no element field to set.
    /// </summary>
    public sealed class FunnelClass
    {
        [JsonProperty("class")] public string Class;
        [JsonProperty("assetPath")] public string AssetPath;
        [JsonProperty("skillAIAction")] public string SkillAIAction;
        [JsonProperty("skillModule")] public string SkillModule;
        [JsonProperty("canBeDamaged")] public bool? CanBeDamaged;
        [JsonProperty("waza")] public string Waza;
        [JsonProperty("wazaInfo")] public FunnelWazaInfo WazaInfo;
        [JsonProperty("usedBy")] public List<string> UsedBy = new();

        /// <summary>"Dark, power 20, 5s" - or a note when the waza is inherited.</summary>
        public string Describe()
        {
            if (WazaInfo == null)
                return UsedBy.Count > 0
                    ? $"used by {string.Join(", ", UsedBy)} - attack inherited"
                    : "attack inherited from the base class";

            return $"{WazaInfo.Element}, power {WazaInfo.Power}, {WazaInfo.CoolTime}s"
                   + (UsedBy.Count > 0 ? $" - as used by {UsedBy[0]}" : "");
        }
    }

    public sealed class FunnelPal
    {
        [JsonProperty("blueprint")] public string Blueprint;
        [JsonProperty("characterClass")] public string CharacterClass;
        [JsonProperty("controllerClass")] public string ControllerClass;
        [JsonProperty("attackWazaID")] public string AttackWazaID;
        [JsonProperty("noAutoSpawnCharacterClass")] public string NoAutoSpawnCharacterClass;
    }

    /// <summary>Funnel companion catalogue. Empty when companions.json is absent.</summary>
    public sealed class CompanionData
    {
        [JsonProperty("classes")] public Dictionary<string, FunnelClass> Classes = new();
        [JsonProperty("pals")] public Dictionary<string, FunnelPal> Pals = new();

        public bool Available => Classes.Count > 0;
    }

    /// <summary>NPC-side lookups. Empty when npc.json has not been generated.</summary>
    public sealed class NpcData
    {
        [JsonProperty("npcs")] public Dictionary<string, NpcSummary> Npcs = new();
        [JsonProperty("talkFlows")] public Dictionary<string, TalkFlowEntry> TalkFlows = new();
        [JsonProperty("shopLotteries")] public Dictionary<string, List<ShopLotteryEntry>> ShopLotteries = new();
        [JsonProperty("shopStock")] public Dictionary<string, List<ShopStockEntry>> ShopStock = new();
        [JsonProperty("shopCurrencies")] public Dictionary<string, string> ShopCurrencies = new();
        [JsonProperty("quests")] public Dictionary<string, QuestSummary> Quests = new();
        [JsonProperty("availableGraphs")] public List<string> AvailableGraphs = new();

        /// <summary>
        /// Enums taken from the NPC table rather than the Pal one. No Pal is a
        /// VillageNPC or in the City organization, so the Pal enums carry none
        /// of the values an NPC field actually takes.
        /// </summary>
        [JsonProperty("enums")] public Dictionary<string, List<string>> Enums = new();

        public bool Available => Npcs.Count > 0;

        public int TalkableCount => Npcs.Values.Count(n => n.CanTalk);

        /// <summary>The graph a given NPC uses, for "copy what this one does".</summary>
        public string GraphFor(string npcId) =>
            TalkFlows.TryGetValue(npcId ?? "", out var entry) ? entry.Graph : null;

        public string AssetPathForGraph(string graph) =>
            TalkFlows.Values.FirstOrDefault(t => t.Graph == graph)?.AssetPath;
    }

    /// <summary>
    /// Loads the generated lookup data. Everything here comes from
    /// Tools/PalCreationEngine/generate_lookups.py reading the real exported tables, so it is
    /// only ever as current as the last export.
    ///
    /// Takes an explicit directory rather than reaching for
    /// Application.streamingAssetsPath, so the same class is usable from a
    /// plain console harness. Unity callers pass the streaming-assets path.
    /// </summary>
    public sealed class GameData
    {
        public Dictionary<string, PalSummary> Pals { get; private set; }
        public Dictionary<string, ItemSummary> Items { get; private set; }
        public Dictionary<string, PassiveSkillSummary> PassiveSkills { get; private set; }
        public Dictionary<string, BlueprintPath> BlueprintPaths { get; private set; }
        public Dictionary<string, SizeDefaults> SizeDefaultsBySize { get; private set; }
        public Dictionary<string, List<string>> Enums { get; private set; }
        public Dictionary<string, ZukanSlot> ZukanSlots { get; private set; }
        public StatBenchmarks Benchmarks { get; private set; }
        public NpcData Npc { get; private set; }
        public Dictionary<string, SpawnGroup> SpawnGroups { get; private set; }
        public Dictionary<string, ModelInfo> Models { get; private set; }
        public CompanionData Companions { get; private set; }
        public DisplayNames Names { get; private set; }
        public List<FieldSchema> Schema { get; private set; }

        // New-Pal contract (2026-09-16). All optional: a data folder generated before them still loads.
        /// <summary>DT_PalBPClass rows: row key -> class path (bp_class_rows.json).</summary>
        public Dictionary<string, BpClassInfo> BpClassRows { get; private set; } = new Dictionary<string, BpClassInfo>();
        /// <summary>Per base Pal: the rows and texts a new Pal copies or points at (base_links.json).</summary>
        public Dictionary<string, BaseLink> BaseLinks { get; private set; } = new Dictionary<string, BaseLink>();
        /// <summary>Verbatim vanilla rows to clone: table -> row key -> row (base_rows.json).</summary>
        public Dictionary<string, Dictionary<string, JObject>> BaseRows { get; private set; } = new Dictionary<string, Dictionary<string, JObject>>();
        /// <summary>In-game map areas with their zones and vanilla placements (map_areas.json).</summary>
        public Dictionary<string, MapArea> MapAreas { get; private set; } = new Dictionary<string, MapArea>();
        /// <summary>Paldex habitat points per Pal (paldex_points.json).</summary>
        public Dictionary<string, PaldexPointSet> PaldexPoints { get; private set; } = new Dictionary<string, PaldexPointSet>();
        /// <summary>Keys of the full-export scan the tool validates against (scan_index.json).</summary>
        public ScanIndex Scan { get; private set; } = ScanIndex.Empty;

        public string SourceDirectory { get; private set; }

        /// <summary>Files that failed to load, with the reason. Empty on a clean load.</summary>
        public IReadOnlyList<string> LoadErrors => _loadErrors;
        private readonly List<string> _loadErrors = new List<string>();

        public static GameData Load(string directory)
        {
            var data = new GameData { SourceDirectory = directory };

            data.Pals = data.ReadDictionary<PalSummary>(directory, "pals.json");
            data.Items = data.ReadDictionary<ItemSummary>(directory, "items.json");
            data.PassiveSkills = data.ReadDictionary<PassiveSkillSummary>(directory, "passive_skills.json");
            data.BlueprintPaths = data.ReadDictionary<BlueprintPath>(directory, "bp_paths.json");
            data.SizeDefaultsBySize = data.ReadDictionary<SizeDefaults>(directory, "size_defaults.json");
            data.Enums = data.ReadDictionary<List<string>>(directory, "enums.json");
            data.ZukanSlots = data.ReadDictionary<ZukanSlot>(directory, "zukan_index.json");
            data.Benchmarks = data.ReadObject<StatBenchmarks>(directory, "stat_benchmarks.json")
                              ?? new StatBenchmarks();
            data.Names = data.ReadObject<DisplayNames>(directory, "display_names.json")
                         ?? new DisplayNames();
            // Optional: the NPC tab degrades to an explanatory notice when this
            // has not been generated yet, rather than failing the whole load.
            data.Npc = data.ReadObjectOptional<NpcData>(directory, "npc.json")
                       ?? new NpcData();
            data.SpawnGroups = data.ReadDictionaryOptional<SpawnGroup>(directory, "wild_spawners.json");
            data.Models = data.ReadDictionaryOptional<ModelInfo>(directory, "model_sizes.json");
            data.Companions = data.ReadObjectOptional<CompanionData>(directory, "companions.json")
                              ?? new CompanionData();

            data.BpClassRows = data.ReadDictionaryOptional<BpClassInfo>(directory, "bp_class_rows.json");
            data.BaseLinks = data.ReadDictionaryOptional<BaseLink>(directory, "base_links.json");
            data.BaseRows = data.ReadDictionaryOptional<Dictionary<string, JObject>>(directory, "base_rows.json");
            data.MapAreas = data.ReadDictionaryOptional<MapArea>(directory, "map_areas.json");
            foreach (var kv in data.MapAreas)
                if (kv.Value != null && string.IsNullOrEmpty(kv.Value.Id)) kv.Value.Id = kv.Key;
            data.PaldexPoints = data.ReadDictionaryOptional<PaldexPointSet>(directory, "paldex_points.json");
            var scanPath = Path.Combine(directory, "scan_index.json");
            if (File.Exists(scanPath))
            {
                try { data.Scan = ScanIndex.Load(scanPath); }
                catch (Exception e) { data._loadErrors.Add("scan_index.json could not be parsed: " + e.Message); data.Scan = ScanIndex.Empty; }
            }

            var schemaPath = Path.Combine(directory, "pal_schema.json");
            if (File.Exists(schemaPath))
            {
                // A corrupt or empty schema file used to throw out of Load and take the whole
                // window down (audit pce-13); it is now a load error like every other file.
                try
                {
                    var parsed = JObject.Parse(File.ReadAllText(schemaPath));
                    data.Schema = parsed["fields"]?.ToObject<List<FieldSchema>>() ?? new List<FieldSchema>();
                    if (parsed["fields"] == null) data._loadErrors.Add("pal_schema.json has no \"fields\" array");
                }
                catch (Exception e) when (e is JsonException || e is InvalidCastException || e is ArgumentException)
                {
                    data.Schema = new List<FieldSchema>();
                    data._loadErrors.Add("pal_schema.json could not be parsed: " + e.Message);
                }
            }
            else
            {
                data.Schema = new List<FieldSchema>();
                data._loadErrors.Add("pal_schema.json not found in " + directory);
            }

            return data;
        }

        private Dictionary<string, T> ReadDictionary<T>(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                _loadErrors.Add($"{fileName} not found in {directory}");
                return new Dictionary<string, T>();
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, T>>(File.ReadAllText(path))
                       ?? new Dictionary<string, T>();
            }
            catch (JsonException e)
            {
                _loadErrors.Add($"{fileName} could not be parsed: {e.Message}");
                return new Dictionary<string, T>();
            }
        }

        private T ReadObject<T>(string directory, string fileName) where T : class
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                _loadErrors.Add($"{fileName} not found in {directory}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (JsonException e)
            {
                _loadErrors.Add($"{fileName} could not be parsed: {e.Message}");
                return null;
            }
        }

        /// <summary>Like ReadDictionary, but a missing file is not an error.</summary>
        private Dictionary<string, T> ReadDictionaryOptional<T>(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return new Dictionary<string, T>();

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, T>>(File.ReadAllText(path))
                       ?? new Dictionary<string, T>();
            }
            catch (JsonException e)
            {
                _loadErrors.Add($"{fileName} could not be parsed: {e.Message}");
                return new Dictionary<string, T>();
            }
        }

        /// <summary>Like ReadObject, but a missing file is not recorded as an error.</summary>
        private T ReadObjectOptional<T>(string directory, string fileName) where T : class
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            }
            catch (JsonException e)
            {
                _loadErrors.Add($"{fileName} could not be parsed: {e.Message}");
                return null;
            }
        }

        public bool IsLoaded => Pals != null && Pals.Count > 0;

        /// <summary>
        /// "Cattiva (PinkCat)" once the text tables are available, otherwise
        /// just "PinkCat". Internal IDs and player-facing names diverge often
        /// enough that showing only one of them makes a picker hard to use.
        /// </summary>
        public string LabelForPal(string rowKey)
        {
            if (rowKey == null) return string.Empty;
            return Names != null && Names.Pals.TryGetValue(rowKey, out var display) && display != rowKey
                ? $"{display}  ({rowKey})"
                : rowKey;
        }

        public string LabelForItem(string itemId)
        {
            if (itemId == null) return string.Empty;
            return Names != null && Names.Items.TryGetValue(itemId, out var display) && display != itemId
                ? $"{display}  ({itemId})"
                : itemId;
        }

        /// <summary>
        /// Player-facing description of an item, with the game's inline markup
        /// unwrapped. Empty when the text tables have not been exported.
        /// </summary>
        public string ItemDescription(string itemId) =>
            Names != null ? Names.ItemDescription(itemId) : string.Empty;

        public string PalDescription(string rowKey) =>
            Names != null ? Names.PalDescription(rowKey) : string.Empty;

        /// <summary>Recovers the internal ID from a "Display  (Internal)" label.</summary>
        public static string IdFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            var open = label.LastIndexOf('(');
            var close = label.LastIndexOf(')');
            return open >= 0 && close > open
                ? label.Substring(open + 1, close - open - 1).Trim()
                : label.Trim();
        }

        public ZukanSlot ZukanSlot(int index) =>
            ZukanSlots != null && ZukanSlots.TryGetValue(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var slot)
                ? slot
                : null;

        /// <summary>The first Paldex number nobody holds: one past the highest known index (vanilla 204 -> 205).</summary>
        public int NextFreeZukanIndex()
        {
            var max = 0;
            if (ZukanSlots != null)
                foreach (var key in ZukanSlots.Keys)
                    if (int.TryParse(key, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > max)
                        max = n;
            return max + 1;
        }

        /// <summary>
        /// The ONE rule for a new Pal's Paldex letter, shared by the editor and
        /// NewPalBuilder: "" when the number is unused (the Pal is the base form
        /// there), otherwise that slot's next free letter. A letter only means
        /// something inside its own number, so it is never chosen globally.
        /// Null when the number is held and B..Z are all taken.
        /// </summary>
        public string NextFreeZukanSuffix(int index, params string[] alsoUsed)
        {
            var slot = ZukanSlot(index);
            if (slot != null) return slot.NextFreeSuffix(alsoUsed);
            // No slot known: only a caller-supplied base suffix can occupy it.
            return alsoUsed != null && alsoUsed.Any(s => string.IsNullOrEmpty(s)) ? "B" : "";
        }

        public IEnumerable<string> EnumValues(string enumName) =>
            Enums != null && Enums.TryGetValue(enumName, out var values)
                ? values
                : Enumerable.Empty<string>();

        public FieldSchema Field(string name) =>
            Schema?.FirstOrDefault(f => f.Name == name);

        /// <summary>
        /// Ordinary catchable Pals, boss and raid variants excluded. This is the
        /// list a "which model should my Pal use" picker should show.
        /// </summary>
        public IEnumerable<PalSummary> BasePals =>
            Pals.Values.Where(p => !p.IsVariant).OrderBy(p => p.RowKey);

        /// <summary>
        /// Sizes worth offering for a borrowed model. Falls back to every size
        /// when the model is unknown, so an unrecognised BPClass is never a dead
        /// end.
        /// </summary>
        public List<string> SizesForModel(string bpClass)
        {
            if (Models != null && bpClass != null
                && Models.TryGetValue(bpClass, out var model)
                && model.FamilySizes is { Count: > 0 })
                return model.FamilySizes;

            return SizeDefaultsBySize.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// The model in <paramref name="family"/> that is actually used at
        /// <paramref name="size"/>.
        ///
        /// A family ships the same mesh at more than one size, and the variants
        /// carry different collision and scale -- Bastet is XS at 40/16 while
        /// BOSS_Bastet is S at 62/24 with a 1.5 mesh scale. Reading the family's
        /// base entry for every size would hand back the wrong numbers.
        /// </summary>
        public ModelInfo ModelForFamilySize(string family, string size)
        {
            if (Models == null || family == null) return null;

            return Models.Values.FirstOrDefault(
                       m => m.Family == family && m.Sizes.Contains(size))
                   ?? Models.Values.FirstOrDefault(m => m.Family == family);
        }

        public ModelInfo Model(string bpClass) =>
            Models != null && bpClass != null && Models.TryGetValue(bpClass, out var m) ? m : null;

        /// <summary>
        /// The class path a DT_PalBPClass row key renders with. Prefers the table itself
        /// (bp_class_rows.json, 940 rows) over the filename-derived bp_paths.json, and matches
        /// the key case-insensitively like the game does.
        /// </summary>
        public string BlueprintPathFor(string bpClass)
        {
            if (string.IsNullOrEmpty(bpClass)) return null;
            if (BpClassRows != null && BpClassRows.Count > 0)
            {
                if (BpClassRows.TryGetValue(bpClass, out var row) && !string.IsNullOrEmpty(row.Path)) return row.Path;
                var ci = BpClassRows.FirstOrDefault(kv => string.Equals(kv.Key, bpClass, StringComparison.OrdinalIgnoreCase));
                if (ci.Value != null && !string.IsNullOrEmpty(ci.Value.Path)) return ci.Value.Path;
            }
            if (BlueprintPaths != null && BlueprintPaths.TryGetValue(bpClass, out var entry)) return entry.ObjectPath;
            var alt = BlueprintPaths?.FirstOrDefault(kv => string.Equals(kv.Key, bpClass, StringComparison.OrdinalIgnoreCase));
            return alt?.Value?.ObjectPath;
        }

        /// <summary>The vanilla spelling of a Pal row key, matched case-insensitively (FName rule).</summary>
        public bool TryCanonicalPalKey(string id, out string canonical)
        {
            canonical = null;
            if (string.IsNullOrEmpty(id) || Pals == null) return false;
            if (Pals.ContainsKey(id)) { canonical = id; return true; }
            canonical = Pals.Keys.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
            return canonical != null;
        }

        /// <summary>
        /// A verbatim vanilla row from base_rows.json, or null when the table or row is not
        /// available. Table and row key match case-insensitively; DT_X and DT_X_Common are the same table.
        /// </summary>
        public JObject BaseRow(string table, string rowKey)
        {
            if (BaseRows == null || string.IsNullOrEmpty(table) || string.IsNullOrEmpty(rowKey)) return null;
            var alt = table.EndsWith("_Common", StringComparison.OrdinalIgnoreCase) ? table.Substring(0, table.Length - 7) : table + "_Common";
            foreach (var name in new[] { table, alt })
            {
                var t = BaseRows.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
                if (t == null) continue;
                if (t.TryGetValue(rowKey, out var row)) return row;
                var ci = t.FirstOrDefault(kv => string.Equals(kv.Key, rowKey, StringComparison.OrdinalIgnoreCase));
                if (ci.Value != null) return ci.Value;
            }
            return null;
        }
    }
}
