using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PalItemgen.Lookup
{
    /// <summary>One row of DT_ItemDataTable plus the English name and which vanilla benches can craft it.</summary>
    public sealed class ItemInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("desc")] public string Desc;
        /// <summary>Icon texture path from DT_ItemIconDataTable, or null when the game has none.</summary>
        [JsonProperty("icon")] public string Icon;
        [JsonProperty("sortId")] public int SortId;
        [JsonProperty("typeA")] public string TypeA;
        [JsonProperty("typeB")] public string TypeB;
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("rarity")] public int Rarity;
        [JsonProperty("maxStack")] public int MaxStack;
        [JsonProperty("craftable")] public bool Craftable;
        [JsonProperty("recipes")] public List<string> Recipes = new List<string>();
        [JsonProperty("benches")] public List<string> Benches = new List<string>();

        public string Display => string.IsNullOrEmpty(Name) ? Id : $"{Name}  ({Id})";
    }

    public sealed class RecipeMaterial
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("count")] public int Count;
    }

    /// <summary>One row of DT_ItemRecipeDataTable.</summary>
    public sealed class RecipeInfo
    {
        [JsonProperty("product")] public string Product;
        [JsonProperty("count")] public int Count = 1;
        [JsonProperty("work")] public float Work;
        [JsonProperty("materials")] public List<RecipeMaterial> Materials = new List<RecipeMaterial>();
        [JsonProperty("energyType")] public string EnergyType;
        [JsonProperty("energyAmount")] public int EnergyAmount;
        /// <summary>Technology row that unlocks this recipe, or null when none governs it.</summary>
        [JsonProperty("tech")] public string Tech;
    }

    public sealed class BuildRowInfo
    {
        [JsonProperty("typeA")] public string TypeA;
        [JsonProperty("typeB")] public string TypeB;
        [JsonProperty("typeUIDisplay")] public string TypeUIDisplay;
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("requiredBuildWorkAmount")] public float RequiredBuildWorkAmount;
        [JsonProperty("requiredEnergyType")] public string RequiredEnergyType;
        [JsonProperty("consumeEnergySpeed")] public float ConsumeEnergySpeed;
        [JsonProperty("materials")] public List<RecipeMaterial> Materials = new List<RecipeMaterial>();
        [JsonProperty("bIsInstallOnlyHubAround")] public bool InstallOnlyHubAround;
        [JsonProperty("installMaxNumInBaseCamp")] public int InstallMaxNumInBaseCamp;
    }

    public sealed class MasterRowInfo
    {
        [JsonProperty("hp")] public int Hp;
        [JsonProperty("defense")] public int Defense;
        [JsonProperty("materialType")] public string MaterialType;
        [JsonProperty("materialSubType")] public string MaterialSubType;
        [JsonProperty("deteriorationDamage")] public float DeteriorationDamage;
    }

    public sealed class TechInfo
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("levelCap")] public int LevelCap;
        [JsonProperty("cost")] public int Cost;
        [JsonProperty("isBoss")] public bool IsBoss;
    }

    public sealed class AssignmentInfo
    {
        [JsonProperty("row")] public string Row;
        [JsonProperty("workSuitability")] public string WorkSuitability;
        [JsonProperty("workSuitabilityRank")] public int WorkSuitabilityRank;
        [JsonProperty("bPlayerWorkable")] public bool PlayerWorkable;
        [JsonProperty("bBaseCampWorkerWorkable")] public bool BaseCampWorkerWorkable;
        [JsonProperty("workType")] public string WorkType;
        [JsonProperty("workActionType")] public string WorkActionType;
        [JsonProperty("workerMaxNum")] public int WorkerMaxNum;
        [JsonProperty("affectSanityValue")] public float AffectSanityValue;
        [JsonProperty("affectFullStomachValue")] public float AffectFullStomachValue;
    }

    /// <summary>Half extents of a blueprint collision box, in centimetres.</summary>
    public sealed class BoxHalf
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;
        /// <summary>The box centre relative to the actor (cm); null in lookup files made before 2026-09-05.</summary>
        [JsonProperty("at")] public BoxPoint At;

        /// <summary>Full size in metres as "L x W x H".</summary>
        public string Metres => $"{X * 2 / 100f:0.0} x {Y * 2 / 100f:0.0} x {Z * 2 / 100f:0.0} m";
    }

    /// <summary>A point relative to the actor, in centimetres.</summary>
    public sealed class BoxPoint
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;
    }

    /// <summary>
    /// A vanilla building whose blueprint can be reused for a station: either
    /// a crafting bench (PalMapObjectConvertItemModel with converter types) or
    /// a signboard (text input UI), or any buildable building at all.
    /// </summary>
    public sealed class ProducerInfo
    {
        [JsonProperty("bp")] public string Bp;
        [JsonProperty("mapObjectId")] public string MapObjectId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("desc")] public string Desc;
        [JsonProperty("blueprintSoft")] public string BlueprintSoft;
        [JsonProperty("buildable")] public bool Buildable;
        [JsonProperty("model")] public string Model;
        [JsonProperty("typesA")] public List<string> TypesA = new List<string>();
        [JsonProperty("typesB")] public List<string> TypesB = new List<string>();
        [JsonProperty("rankMax")] public int? RankMax;
        [JsonProperty("workSpeed")] public float? WorkSpeed;
        [JsonProperty("meshes")] public List<string> Meshes = new List<string>();
        /// <summary>Runtime asset paths of the meshes (/Game/.../SM_X.SM_X), what LoadAsset takes.</summary>
        [JsonProperty("meshPaths")] public List<string> MeshPaths = new List<string>();
        /// <summary>Animated (skeletal) meshes; these cannot be put on the stand's static mesh component.</summary>
        [JsonProperty("skeletalMeshes")] public List<string> SkeletalMeshes = new List<string>();
        /// <summary>Placement footprint (CheckOverlapCollision box, half extents) and the box Pals work in.</summary>
        [JsonProperty("overlap")] public BoxHalf Overlap;
        [JsonProperty("workable")] public BoxHalf Workable;
        [JsonProperty("needsEnergyComponent")] public bool NeedsEnergyComponent;
        [JsonProperty("icon")] public string Icon;
        [JsonProperty("master")] public MasterRowInfo Master;
        [JsonProperty("build")] public BuildRowInfo Build;
        [JsonProperty("tech")] public TechInfo Tech;
        [JsonProperty("assignments")] public List<AssignmentInfo> Assignments = new List<AssignmentInfo>();
        /// <summary>Every field of the source rows, untouched, for the "list all options" view.</summary>
        [JsonProperty("rawMaster")] public JObject RawMaster;
        [JsonProperty("rawBuild")] public JObject RawBuild;
        [JsonProperty("rawAssignments")] public List<JObject> RawAssignments = new List<JObject>();
        [JsonProperty("rawTech")] public JObject RawTech;
        /// <summary>DT_MapObjectItemProductDataTable row: quarries, logging sites and oil pumps only (what they produce, how fast).</summary>
        [JsonProperty("rawItemProduct")] public JObject RawItemProduct;
        /// <summary>The body box players and Pals bump into (VirtualMeshCollision, half extents cm); null when the blueprint has none.</summary>
        [JsonProperty("virtualMesh")] public BoxHalf VirtualMesh;
        /// <summary>The prompt volume (BP_InteractableBox: where F / V / C are offered), half extents cm; null when the blueprint has none.</summary>
        [JsonProperty("interact")] public BoxHalf Interact;
        /// <summary>Fixed spots Pals stand on to work (PalWorkFacingComponent count): benches 1-4, plantations 0.</summary>
        [JsonProperty("workSpots")] public int WorkSpots;
        /// <summary>Plantations: the DT_MapObjectFarmCrop row the blueprint grows (CropDataId).</summary>
        [JsonProperty("cropId")] public string CropId;
        [JsonProperty("rawFarmCrop")] public JObject RawFarmCrop;

        /// <summary>True when Pals can be assigned here (the building has DT_MapObjectAssignData rows).</summary>
        public bool HasPalWork => RawAssignments != null && RawAssignments.Count > 0;

        public string Mesh => Meshes != null && Meshes.Count > 0 ? Meshes[0] : null;
        public string MeshPath => MeshPaths != null && MeshPaths.Count > 0 ? MeshPaths[0] : null;
        public string SkeletalMesh => SkeletalMeshes != null && SkeletalMeshes.Count > 0 ? SkeletalMeshes[0] : null;
        /// <summary>What the building looks like: its static mesh, or its animated mesh marked as such.</summary>
        public string GraphicLabel => Mesh ?? (SkeletalMesh != null ? SkeletalMesh + "  (animated)" : "(no mesh exported)");
        /// <summary>True when this graphic can be put onto the stand: a static mesh with a placement box.</summary>
        public bool Swappable => !string.IsNullOrEmpty(MeshPath) && Overlap != null;

        public string Display => string.IsNullOrEmpty(Name) ? Bp : $"{Name}  [{MapObjectId ?? Bp}]";

        public string TypeSummary =>
            RankMax.HasValue
                ? $"rank {RankMax}: {string.Join(", ", TypesB.Take(6))}{(TypesB.Count > 6 ? ", ..." : "")}"
                : "sign (text input)";
    }

    /// <summary>A Pal species' work suitability ranks and base craft speed (DT_PalMonsterParameter).</summary>
    public sealed class PalInfo
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("craftSpeed")] public int CraftSpeed;
        [JsonProperty("ranks")] public Dictionary<string, int> Ranks = new Dictionary<string, int>();
        [JsonProperty("isBoss")] public bool IsBoss;
    }

    /// <summary>One Monitoring Stand work mode and what it does to base Pals (BP_PalGameSetting).</summary>
    public sealed class WorkHardInfo
    {
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("workSpeedRate")] public float WorkSpeedRate;
        [JsonProperty("moveSpeedRate")] public float MoveSpeedRate;
        [JsonProperty("affectSanityRate")] public float AffectSanityRate;
        [JsonProperty("decreaseFullStomachRate")] public float DecreaseFullStomachRate;
    }

    public sealed class IconInfo
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("name")] public string Name;
    }

    public sealed class EnumLists
    {
        [JsonProperty("itemTypeA")] public List<string> ItemTypeA = new List<string>();
        [JsonProperty("itemTypeB")] public List<string> ItemTypeB = new List<string>();
        [JsonProperty("buildTypeA")] public List<string> BuildTypeA = new List<string>();
        [JsonProperty("buildTypeB")] public List<string> BuildTypeB = new List<string>();
        [JsonProperty("typeUIDisplay")] public List<string> TypeUIDisplay = new List<string>();
        [JsonProperty("workSuitability")] public List<string> WorkSuitability = new List<string>();
        [JsonProperty("materialType")] public List<string> MaterialType = new List<string>();
        [JsonProperty("materialSubType")] public List<string> MaterialSubType = new List<string>();
        [JsonProperty("energyType")] public List<string> EnergyType = new List<string>();
    }

    /// <summary>
    /// Everything the tool knows about the game, loaded from the JSON files
    /// Tools/PalItemgen/generate_lookups.py writes. Free of UnityEngine so the console
    /// verification harness can load it too.
    /// </summary>
    public sealed class GameData
    {
        public readonly List<ItemInfo> Items = new List<ItemInfo>();
        public readonly Dictionary<string, ItemInfo> ItemsById = new Dictionary<string, ItemInfo>(StringComparer.Ordinal);
        public readonly Dictionary<string, RecipeInfo> Recipes = new Dictionary<string, RecipeInfo>(StringComparer.Ordinal);
        public readonly List<ProducerInfo> Producers = new List<ProducerInfo>();
        public readonly List<ProducerInfo> Signboards = new List<ProducerInfo>();
        /// <summary>Every buildable vanilla building (any model), for stations that reuse a non-bench model.</summary>
        public readonly List<ProducerInfo> Buildings = new List<ProducerInfo>();
        public readonly List<PalInfo> Pals = new List<PalInfo>();
        public readonly List<WorkHardInfo> WorkHard = new List<WorkHardInfo>();
        public readonly Dictionary<string, IconInfo> Icons = new Dictionary<string, IconInfo>(StringComparer.Ordinal);
        public EnumLists Enums = new EnumLists();
        public readonly List<string> LoadErrors = new List<string>();

        public bool IsLoaded => Items.Count > 0 && Recipes.Count > 0 && Producers.Count > 0;

        public static GameData Load(string directory)
        {
            var data = new GameData();
            T Read<T>(string file) where T : class
            {
                var path = Path.Combine(directory, file);
                if (!File.Exists(path))
                {
                    data.LoadErrors.Add($"missing {path} (reinstall True Creation to restore it; developers: python Tools/PalItemgen/generate_lookups.py)");
                    return null;
                }
                try
                {
                    return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    data.LoadErrors.Add($"{file}: {e.Message}");
                    return null;
                }
            }

            var items = Read<List<ItemInfo>>("items.json");
            if (items != null)
            {
                data.Items.AddRange(items);
                foreach (var i in items) data.ItemsById[i.Id] = i;
            }
            var recipes = Read<Dictionary<string, RecipeInfo>>("recipes.json");
            if (recipes != null) foreach (var kv in recipes) data.Recipes[kv.Key] = kv.Value;
            var producers = Read<List<ProducerInfo>>("producers.json");
            if (producers != null) data.Producers.AddRange(producers);
            var signs = Read<List<ProducerInfo>>("signboards.json");
            if (signs != null) data.Signboards.AddRange(signs);
            var buildings = Read<List<ProducerInfo>>("buildings.json");
            if (buildings != null) data.Buildings.AddRange(buildings);
            var pals = Read<List<PalInfo>>("pals.json");
            if (pals != null) data.Pals.AddRange(pals);
            var workhard = Read<List<WorkHardInfo>>("workhard.json");
            if (workhard != null) data.WorkHard.AddRange(workhard);
            var icons = Read<Dictionary<string, IconInfo>>("icons.json");
            if (icons != null) foreach (var kv in icons) data.Icons[kv.Key] = kv.Value;
            var enums = Read<EnumLists>("enums.json");
            if (enums != null) data.Enums = enums;
            return data;
        }

        public ItemInfo Item(string id) => id != null && ItemsById.TryGetValue(id, out var i) ? i : null;

        public ProducerInfo Producer(string mapObjectIdOrBp) =>
            Producers.FirstOrDefault(p => p.MapObjectId == mapObjectIdOrBp || p.Bp == mapObjectIdOrBp)
            ?? Signboards.FirstOrDefault(p => p.MapObjectId == mapObjectIdOrBp || p.Bp == mapObjectIdOrBp);

        /// <summary>Any vanilla building by map object id (benches and signs included).</summary>
        public ProducerInfo Building(string mapObjectIdOrBp)
        {
            if (string.IsNullOrEmpty(mapObjectIdOrBp)) return null;
            return Buildings.FirstOrDefault(b => b.MapObjectId == mapObjectIdOrBp)
                   ?? Producer(mapObjectIdOrBp)
                   ?? Buildings.FirstOrDefault(b => b.Bp == mapObjectIdOrBp);
        }

        /// <summary>
        /// Buildings whose model may be reused by the manager item: production
        /// and storage/transport buildings, plus the monitoring stands the item
        /// mirrors. Each carries a blueprint, a static mesh and a footprint.
        /// </summary>
        public IEnumerable<ProducerInfo> ModelCandidates()
        {
            // Every building that has a graphic and a placement box: the stand tiers
            // first (same model class, named directly), then everything by category.
            return Buildings.Where(b => b.Build != null && b.Overlap != null && (b.Mesh != null || b.SkeletalMesh != null))
                .OrderBy(b => (b.MapObjectId ?? "").StartsWith("BaseCampWorkHard") ? 0 : 1)
                .ThenBy(b => b.Build.TypeA)
                .ThenBy(b => string.IsNullOrEmpty(b.Name) ? "~" + b.MapObjectId : b.Name);
        }

        private static readonly string[] GeneratorTypeA = { "Pal", "Product", "Storage", "Food", "Infrastructure", "Other" };

        /// <summary>
        /// Buildings the Generator tab may copy: every station, producer, storage
        /// and Pal building with a graphic and a placement box, grouped by type
        /// (Pal management first, then production, storage, food, infrastructure,
        /// other). Foundations, defences, lights and furniture are left out.
        /// </summary>
        public IEnumerable<ProducerInfo> GeneratorSources()
        {
            return Buildings.Where(b => b.Build != null && b.Overlap != null && (b.Mesh != null || b.SkeletalMesh != null)
                                        && Array.IndexOf(GeneratorTypeA, b.Build.TypeA ?? "") >= 0)
                .OrderBy(b => Array.IndexOf(GeneratorTypeA, b.Build.TypeA ?? ""))
                .ThenBy(b => b.Build.TypeUIDisplay ?? "")
                .ThenBy(b => string.IsNullOrEmpty(b.Name) ? "~" + b.MapObjectId : b.Name);
        }

        /// <summary>The Monitoring Stand tiers (basic, High Quality, Ancient), lowest tech level first.</summary>
        public IEnumerable<ProducerInfo> StandTiers() =>
            Buildings.Where(b => (b.MapObjectId ?? "").StartsWith("BaseCampWorkHard"))
                .OrderBy(b => b.Tech?.LevelCap ?? 0).ThenBy(b => b.MapObjectId);

        /// <summary>
        /// The Pal requirement of a building, read from its DT_MapObjectAssignData
        /// rows: "Handcraft rank 1" or "Handcraft rank 6, ProductMedicine rank 6"
        /// for multi rows, "none" when nothing ever works at it (signs, the stands).
        /// </summary>
        public static string AssignSummary(ProducerInfo b)
        {
            if (b?.RawAssignments == null || b.RawAssignments.Count == 0) return "none";
            var parts = new List<string>();
            foreach (var row in b.RawAssignments)
            {
                string EnumName(string key) => (row[key]?.ToString() ?? "").Split(new[] { "::" }, StringSplitOptions.None).Last();
                var suit = EnumName("WorkSuitability");
                if (suit != "" && suit != "None") parts.Add($"{suit} rank {row["WorkSuitabilityRank"]}");
                for (var i = 1; i <= 2; i++)
                {
                    var multi = EnumName("MultiWorkSuitability" + i);
                    if (multi != "" && multi != "None") parts.Add($"{multi} rank {row["MultiRequiredRank" + i]}");
                }
                var max = row["WorkerMaxNum"]?.ToString();
                if (!string.IsNullOrEmpty(max) && max != "0") parts.Add($"max {max} Pals");
                if (string.Equals(row["bPlayerWorkable"]?.ToString(), "True", StringComparison.OrdinalIgnoreCase)) parts.Add("player can work");
            }
            return parts.Count == 0 ? "any Pal (no suitability asked)" : string.Join(", ", parts.Distinct());
        }

        /// <summary>Recipe used for an item: the row named like the item when there is one.</summary>
        public RecipeInfo RecipeFor(string itemId)
        {
            if (itemId == null) return null;
            if (Recipes.TryGetValue(itemId, out var direct) && direct.Product == itemId) return direct;
            return Recipes.Values.FirstOrDefault(r => r.Product == itemId);
        }

        public IEnumerable<ItemInfo> CraftableItems() => Items.Where(i => i.Craftable);

        /// <summary>Items a given vanilla bench blueprint can craft, by type pair and rank.</summary>
        public IEnumerable<ItemInfo> ItemsFor(ProducerInfo bench)
        {
            if (bench == null || !bench.RankMax.HasValue) return Enumerable.Empty<ItemInfo>();
            var a = new HashSet<string>(bench.TypesA);
            var b = new HashSet<string>(bench.TypesB);
            return Items.Where(i => i.Craftable && a.Contains(i.TypeA) && b.Contains(i.TypeB) && i.Rank <= bench.RankMax.Value);
        }
    }
}
