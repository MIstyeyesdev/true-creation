using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalItemgen.Lookup;

namespace PalItemgen.Model
{
    public enum StationKind
    {
        /// <summary>
        /// The Production Manager itself: its own building id reusing a vanilla
        /// model (the basic Monitoring Stand by default). Its presence in a base
        /// switches automation on; no per-base limit, like the stand it copies.
        /// </summary>
        Manager,
        /// <summary>Optional command console: reuses a signboard so commands can be typed in game.</summary>
        ConsoleSign,
        /// <summary>Reuses a vanilla crafting bench: orders placed here become targets.</summary>
        Line,
        /// <summary>
        /// Generator item: an exact copy of any station, producer or storage
        /// building under its own id (rows, Pal work rows, item product row).
        /// The Lua side takes no part in it; only a model swap, if any, is listed.
        /// </summary>
        Copy,
    }

    public sealed class MaterialCost
    {
        public string ItemId = "Wood";
        public int Count = 10;
    }

    /// <summary>One building the package adds via PalSchema's buildings/ loader.</summary>
    public sealed class StationSpec
    {
        public StationKind Kind = StationKind.Line;

        /// <summary>Row key in the game's map object tables. Letters, digits and underscores.</summary>
        public string Id = "PM_Line_Handiwork";

        /// <summary>Line name the Lua side groups targets under (Line stations only).</summary>
        public string Line = "Handiwork";

        public string Name = "Production Line: Handiwork";
        public string Description = "Orders placed here become standing targets for this base.";

        /// <summary>Vanilla map object id whose blueprint (model, menu) this station reuses.</summary>
        public string ReuseMapObjectId = "AncientWorkBench";

        /// <summary>
        /// The vanilla building this item is a copy of: its options are mirrored
        /// on the "Manager item" tab. The basic Monitoring Stand for the manager.
        /// </summary>
        public string MirrorMapObjectId = "";

        /// <summary>
        /// The blueprint the row names. A manager keeps its stand functions: when
        /// the chosen model has another model class, the row keeps the mirrored
        /// stand's blueprint and the model becomes a runtime mesh + footprint swap
        /// (pm_config ModelSwap). Same-class models (the other stand tiers) are
        /// named directly.
        /// </summary>
        public string BlueprintSource(GameData data)
        {
            if ((Kind != StationKind.Manager && Kind != StationKind.Copy) || data == null) return ReuseMapObjectId;
            var reuse = data.Building(ReuseMapObjectId);
            var mirror = data.Building(MirrorId);
            if (reuse == null || mirror == null) return ReuseMapObjectId;
            return (reuse.Model ?? "") == (mirror.Model ?? "") ? ReuseMapObjectId : mirror.MapObjectId;
        }

        /// <summary>True when the chosen model is applied as a runtime swap on the stand's blueprint.</summary>
        public bool IsModelSwap(GameData data) => (Kind == StationKind.Manager || Kind == StationKind.Copy) && BlueprintSource(data) != ReuseMapObjectId;

        /// <summary>Why the chosen model cannot be put on the stand, or null when it can (or needs no swap).</summary>
        public string ModelSwapProblem(GameData data)
        {
            if (!IsModelSwap(data)) return null;
            var model = data.Building(ReuseMapObjectId);
            if (model == null) return $"{Id}: unknown model '{ReuseMapObjectId}'.";
            if (!model.Swappable) return $"{Id}: model '{model.Name ?? ReuseMapObjectId}' is an animated (skeletal) model; it cannot be put on the stand. Pick a model with a static mesh (see 'Onto the stand' in the table).";
            return null;
        }

        /// <summary>Vanilla map object id whose icon to show, or a custom PNG path.</summary>
        public string IconMapObjectId = "AncientWorkBench";
        public string CustomIconPng = "";

        /// <summary>
        /// Edits to the raw row fields the tab does not name (RowFieldCatalog):
        /// "Master.Hp_PVP" -> "4000". A field without an entry keeps the value of
        /// the building this item copies (MirrorMapObjectId).
        /// </summary>
        public Dictionary<string, string> RowFields = new Dictionary<string, string>();

        /// <summary>Written into the package. The Generator item starts off (kept in the project, not written).</summary>
        public bool Enabled = true;

        /// <summary>
        /// Generator copies only: also written to pm_config.lua Stations as kind
        /// "manager", so the copy is the production manager / chain station as well
        /// (F opens the board, the menu key finds it, automation runs where it
        /// stands). Meaningful for a copy of a Monitoring Stand tier; a Manager-kind
        /// item always has the role.
        /// </summary>
        public bool ActsAsManager = false;

        /// <summary>True when pm_config.lua lists this item as the production manager.</summary>
        [JsonIgnore]
        public bool IsManagerRole => Kind == StationKind.Manager || (Kind == StationKind.Copy && ActsAsManager);

        /// <summary>
        /// The item's Pal work rows (DT_MapObjectAssignData) as text, one
        /// dictionary per row. Null = copy the original's rows unchanged; the
        /// tab fills this in on the first edit, added row or removed row.
        /// </summary>
        public List<Dictionary<string, string>> AssignmentRows = null;

        /// <summary>The building this item copies: MirrorMapObjectId, else the reused model (Generator) or the basic stand.</summary>
        [JsonIgnore]
        public string MirrorId => !string.IsNullOrEmpty(MirrorMapObjectId) ? MirrorMapObjectId
            : Kind == StationKind.Copy ? (ReuseMapObjectId ?? "") : "BaseCampWorkHard";

        public List<MaterialCost> Materials = new List<MaterialCost> { new MaterialCost { ItemId = "Wood", Count = 20 }, new MaterialCost { ItemId = "Stone", Count = 10 } };
        public float BuildWorkAmount = 2000f;
        public int Hp = 4000;
        public int Defense = 2;
        public string MaterialType = "Wood";
        public string MaterialSubType = "Wood";
        public int TechLevel = 5;
        public int TechCost = 1;
        public bool AncientTech = false;
        public int LimitPerBase = 1;
        public string TypeUIDisplay = "PalManagement";
        public bool PlayerWorkable = true;

        /// <summary>
        /// Model size. A building's size is a mesh scale plus two boxes on its
        /// blueprint: the placement box (the grid size) and the work box (where
        /// Pals stand to work). Off = the chosen model's own values. On = the
        /// values below are applied to every placed copy at run time (the Lua
        /// side, per building id); the vanilla blueprint itself is never edited,
        /// so the original building keeps its size.
        /// </summary>
        public bool OverrideSize = false;
        public float MeshScale = 1f;
        /// <summary>Placement box half extents in cm (CheckOverlapCollision).</summary>
        public float OverlapX = 0f, OverlapY = 0f, OverlapZ = 0f;
        /// <summary>Work box half extents in cm (BuildWorkableBounds).</summary>
        public float WorkX = 0f, WorkY = 0f, WorkZ = 0f;
        /// <summary>Body box half extents in cm (VirtualMeshCollision): what players and Pals bump into.</summary>
        public float BodyX = 0f, BodyY = 0f, BodyZ = 0f;

        /// <summary>True when the Lua side must touch the placed actor: a mesh of another class, or a size override.</summary>
        public bool NeedsRuntimeSwap(GameData data) =>
            IsModelSwap(data) || (OverrideSize && (Kind == StationKind.Manager || Kind == StationKind.Copy));

        /// <summary>
        /// Item links for a line station: the items it manages. Empty = every
        /// item the reused bench can make. Set from the "Producers &amp; item
        /// links" tab; exported to pm_config.lua and used by the in-game menu
        /// and the order capture at that station.
        /// </summary>
        public List<string> AllowedItems = new List<string>();

        /// <summary>
        /// The exact copy of the basic Monitoring Stand under a new name: every
        /// value below is the stand's own row value (DT_MapObjectMasterDataTable,
        /// DT_BuildObjectDataTable, DT_TechnologyRecipeUnlock, verified 2026-09-05).
        /// Only the name, id and description are new.
        /// </summary>
        /// <summary>The Generator tab's item: nothing chosen yet, not written until it is enabled.</summary>
        public static StationSpec CopyItem() => new StationSpec
        {
            Kind = StationKind.Copy,
            Id = "",
            Line = "",
            Name = "",
            Description = "",
            ReuseMapObjectId = "",
            MirrorMapObjectId = "",
            IconMapObjectId = "",
            Materials = new List<MaterialCost>(),
            LimitPerBase = 0,
            TechLevel = 1,
            TechCost = 0,
            Enabled = false,
        };

        public static StationSpec Manager() => new StationSpec
        {
            Kind = StationKind.Manager,
            Id = "PM_ManagerStation",
            Line = "",
            Name = "Production Manager",
            Description = "A Monitoring Stand of its own: set the work mode, manage fixed assignments, and keep items stocked from its menu.",
            ReuseMapObjectId = "BaseCampWorkHard",
            MirrorMapObjectId = "BaseCampWorkHard",
            IconMapObjectId = "BaseCampWorkHard",
            Materials = new List<MaterialCost> { new MaterialCost { ItemId = "Wood", Count = 30 }, new MaterialCost { ItemId = "Stone", Count = 10 } },
            BuildWorkAmount = 2000f,
            Hp = 4000,
            Defense = 2,
            TechLevel = 7,
            TechCost = 2,
            LimitPerBase = 0,   // no limit, like the Monitoring Stand it copies (the harness pins this)
            TypeUIDisplay = "PalManagement",
            MaterialType = "Wood",
            MaterialSubType = "Wood",
        };

        public static StationSpec Console() => new StationSpec
        {
            Kind = StationKind.ConsoleSign,
            Id = "PM_Console",
            Line = "",
            Name = "Production Console",
            Description = "Write commands on this sign: keep Ingot 1200 800 | drop Arrow | top Ingot | priority Ingot Arrow | follow on | pause | resume | status | help",
            ReuseMapObjectId = "Signboard",
            IconMapObjectId = "Signboard",
            Materials = new List<MaterialCost> { new MaterialCost { ItemId = "Wood", Count = 10 } },
            BuildWorkAmount = 1000f,
            TechLevel = 28,
            TechCost = 1,
            LimitPerBase = 0,
            TypeUIDisplay = "PalManagement",
            MaterialType = "Wood",
            MaterialSubType = "Wood",
        };

        public static StationSpec LineFrom(string line, string reuse, string icon, int techLevel, IEnumerable<MaterialCost> mats = null) => new StationSpec
        {
            Kind = StationKind.Line,
            Id = "PM_Line_" + line,
            Line = line,
            Name = "Production Line: " + line,
            Description = "Orders placed here become standing targets for this base (order 1 to remove a target, infinite to make it top priority). The station never crafts anything itself.",
            ReuseMapObjectId = reuse,
            IconMapObjectId = icon,
            Materials = mats?.ToList() ?? new List<MaterialCost> { new MaterialCost { ItemId = "Wood", Count = 20 }, new MaterialCost { ItemId = "Stone", Count = 10 } },
            BuildWorkAmount = 2000f,
            TechLevel = techLevel,
            TechCost = 1,
            LimitPerBase = 1,
            TypeUIDisplay = "Product_Repair",
            MaterialType = "Wood",
            MaterialSubType = "Wood",
        };
    }

    public sealed class PresetTarget
    {
        public string ItemId = "";
        public int Keep = 500;
        public int Trigger = 0;   // 0 = use DefaultTriggerPercent
        public int Priority = 5;
        public string Line = "";
        public bool Top;
        public int Weight = 1;   // lottery share among targets that want production (min 1)
        /// <summary>Bench type (map object id) this target is pinned to; "" = the strongest free maker. Kept apart from Line so the pin never changes the serve group.</summary>
        public string Only = "";
    }

    public sealed class AutomationSettings
    {
        public int ScanIntervalSeconds = 5;
        /// <summary>A scan runs in stages on the game thread: each step runs stages until this many ms are spent, the next step comes TickStepMs later.</summary>
        public int TickBudgetMs = 3;
        public int TickStepMs = 16;
        /// <summary>The menu view is rebuilt every this many scans while the menu is closed; fixed data is re-read from the game every FullRefreshTicks scans.</summary>
        public int ViewRefreshTicks = 6;
        public int FullRefreshTicks = 12;
        public bool RequireManagerStation = true;
        public int DefaultTriggerPercent = 66;
        public int DefaultKeep = 100;
        public int MaxOrderBatch = 50;
        public int IdleGraceSeconds = 20;
        public bool MaterialCheck = true;
        public bool AutoChainMaterials = false;
        public bool FollowPriorityOrder = false;
        public int AnnounceLevel = 1;
        public int IssueRepeatSeconds = 300;
        public int RemoveOrderCount = 1;
        public bool Diagnostics = true;
        /// <summary>Optional extra hotkey that announces a status line; empty = off.</summary>
        public string StatusKey = "";
        public bool WriteStatusToSign = false;
        /// <summary>In-game menu (host / single player) with the Monitoring and Production tabs.</summary>
        public bool ClientMenu = true;
        public string MenuKey = "F7";
        public string MenuFallbackKey = "F8";
        public int MenuWidth = 1320;
        public int MenuHeight = 780;
        public int MenuPollMs = 100;
        public int MenuSearchRows = 10;
        /// <summary>Fixed-assign the highest-ranked base Pal for the bench's work type after each order.</summary>
        public bool AssignBestWorker = true;
        public bool ReleaseWorkerWhenDone = true;
        public int WorkersPerOrder = 1;

        /// <summary>
        /// When a player stops (or takes over) a bench the manager ordered at,
        /// that item's target is held this long before automation tries again.
        /// 0 = hold until resumed from the menu.
        /// </summary>
        public int InterruptHoldSeconds = 600;

        /// <summary>An order that vanished with at least this many crafts left counts as stopped by hand.</summary>
        public int InterruptMinRemaining = 3;

        /// <summary>
        /// Interacting with the manager item (standing in front of it, the
        /// blueprint's BP_InteractableBox) opens the mod's menu with both tabs,
        /// by hooking the Monitoring menu's OnSetup for our building id.
        /// </summary>
        public bool OpenMenuOnInteract = true;

        /// <summary>Also remove the vanilla Monitoring menu the interaction opened, so only ours is on screen.</summary>
        public bool CloseVanillaMenuOnInteract = true;

        /// <summary>F on the item: the stand's own screen is closed after this many ms (0 = the next frame) and the board opens BoardOpenDelayMs later.</summary>
        public int InteractCloseDelayMs = 0;
        public int BoardOpenDelayMs = 60;
    }

    /// <summary>What the item does as a production-chain station (pm_config.lua 'Chain' block).</summary>
    public sealed class ChainSettings
    {
        public string InteractTab = "chain";        // chain | monitor | prod | board (the game's own Assignment Board, producers only) | targets
        public string DefaultTab = "chain";         // chain | monitor | prod | board | targets
        /// <summary>What C on the item opens: vanilla (the game's Assignment Board) | targets (our keep list) | chain | monitor | prod.</summary>
        public string CTab = "targets";
        /// <summary>Older key the Lua still reads: true = C keeps the vanilla board. Written as CTab == "vanilla".</summary>
        public bool KeepAssignBoard = false;
        public bool AllowCancelFromList = true;
        public bool ShowMakeNow = true;
        public bool ProductSlotOpensBench = false;
        /// <summary>The bench interaction the 'make' control fires: 8 = Production Menu (OpenConverterMenu), 7 = craft menu, 23 = recipe select.</summary>
        public int ProductSlotIndicator = 8;
        /// <summary>Producers-only Assignment Board ("board" above): a heading per production family between the groups.</summary>
        public bool BoardHeaders = true;
        /// <summary>Producers-only Assignment Board: producers the tool switched off are listed too (false = hidden like non-producers).</summary>
        public bool BoardShowUntracked = true;
        /// <summary>Producers-only board: when the stand's own C action cannot be fired, open the board through the HUD service instead (untested).</summary>
        public bool BoardHudFallback = false;
        /// <summary>Rename the F and C prompts on our item (PromptF / PromptC); false keeps the vanilla texts.</summary>
        public bool PromptText = true;
        public string PromptF = "Fixed work assignments";
        public string PromptC = "Production targets";
        /// <summary>Our station within this many cm of the player is the one a prompt belongs to.</summary>
        public int PromptRangeCm = 350;
        public bool OneProducerPerTarget = true;
        public string RankBy = "rank";              // rank | speed
        public string TargetOrder = "lottery";      // lottery | weight | priority
        /// <summary>Family ids (TypeUIDisplay) switched OFF; empty = all seven tracked.</summary>
        public List<string> DisabledFamilies = new List<string>();
        /// <summary>Map object ids never tracked.</summary>
        public List<string> ExcludedBenches = new List<string>();
        public bool FamilyEnabled(string id) => DisabledFamilies == null || !DisabledFamilies.Contains(id);
        public void SetFamily(string id, bool on)
        {
            DisabledFamilies ??= new List<string>();
            if (on) DisabledFamilies.Remove(id); else if (!DisabledFamilies.Contains(id)) DisabledFamilies.Add(id);
        }
    }

    /// <summary>Where an extra file belongs. Each kind has its own folder in the package and its own install target.</summary>
    public enum ExtraFileKind
    {
        /// <summary>.pak -> Paks/ -> Pal\Content\Paks\~WorkshopMods\&lt;Package&gt; (manual: Pal\Content\Paks\~mods).</summary>
        Paks,
        /// <summary>.pak -> LogicMods/ -> Pal\Content\Paks\LogicMods.</summary>
        LogicMods,
        /// <summary>PalSchema table patch .json -> PalSchema/raw/.</summary>
        PalSchemaRaw,
        /// <summary>PalSchema blueprint edit .json -> PalSchema/blueprints/.</summary>
        PalSchemaBlueprints,
    }

    public sealed class ExtraFile
    {
        public ExtraFileKind Kind = ExtraFileKind.Paks;
        public string Path = "";
    }

    public enum OutputLayout
    {
        /// <summary>Info.json + Scripts/ + PalSchema/ + thumbnail: what the Mod Uploader and the in-game loader expect.</summary>
        SteamWorkshop,
        /// <summary>Files already placed the way the game folder expects them, for copying by hand.</summary>
        ManualInstall,
    }

    /// <summary>Everything the single screen edits. Serialisable so a project can be saved and reopened.</summary>
    public sealed class ModProject
    {
        public string ModName = "Pal Production Manager";
        public string PackageName = "PalProductionManager";
        public string Author = "";
        public string Version = "0.1.0";
        public int MinRevision = 82182;
        public bool DebugMode = false;
        public string SteamItemId = "";
        public bool IncludeServerRules = true;
        public string ThumbnailPng = "";
        public string OutputFolder = "";
        public OutputLayout Layout = OutputLayout.SteamWorkshop;

        /// <summary>
        /// The Palworld install (the folder holding Pal\ and Mods\). When set,
        /// Generate also hard-copies the package's PalSchema and Scripts parts
        /// into the game's mod folders, so the game runs what was just written.
        /// </summary>
        public string GameFolder = "";
        // Off by default: installing into the game is a write the user must choose
        // (2026-09-15 audit: every saved project had this on and Management Station mode
        // installed silently on Generate).
        public bool InstallAfterGenerate = false;

        public List<StationSpec> Stations = new List<StationSpec>();
        public AutomationSettings Automation = new AutomationSettings();
        public List<PresetTarget> Presets = new List<PresetTarget>();

        /// <summary>
        /// Item links for vanilla production lines: map object id (WorkBench,
        /// Factory_Hard_03, ...) to the items that line may make under the
        /// manager. Missing or empty = everything the line can make. Exported to
        /// pm_config.lua as Facilities; the planner and the F7 menu honour it.
        /// </summary>
        public Dictionary<string, List<string>> FacilityLinks = new Dictionary<string, List<string>>();

        /// <summary>The Chain tab: how the item behaves as a production-chain station (pm_config.lua Chain block).</summary>
        public ChainSettings Chain = new ChainSettings();

        /// <summary>The allowed-item list for a vanilla line, created on first use.</summary>
        public List<string> LinksFor(string mapObjectId)
        {
            if (!FacilityLinks.TryGetValue(mapObjectId, out var list) || list == null)
            {
                list = new List<string>();
                FacilityLinks[mapObjectId] = list;
            }
            return list;
        }
        /// <summary>Optional paks or PalSchema raw/blueprint files shipped alongside, each routed to its own folder.</summary>
        public List<ExtraFile> ExtraFiles = new List<ExtraFile>();

        /// <summary>Where this project was last saved or loaded from; not part of the file itself.</summary>
        [JsonIgnore] public string ProjectPath = "";

        /// <summary>
        /// File format of a saved project. 0 = written by the first build (no
        /// field at all), which is the only format that needs its stations
        /// swapped on load. Set to CurrentFormat whenever a file is written.
        /// </summary>
        public int FormatVersion = 0;
        public const int CurrentFormat = 2;

        public static readonly Regex PackageNameRule = new Regex("^[A-Za-z0-9]+$");
        public static readonly Regex StationIdRule = new Regex("^[A-Za-z][A-Za-z0-9_]*$");

        /// <summary>
        /// Defaults for the testing phase: no tech requirements (level 1, cost 0),
        /// the manager on the basic Monitoring Stand model, one console sign and
        /// the six production lines. Tech levels are raised in the tool later
        /// (28 = High Quality Monitoring Stand tier was the agreed release value).
        /// </summary>
        /// <summary>
        /// The whole copy is ONE item: the Production Manager, a duplicate of the
        /// basic Monitoring Stand under its own name. Targets are set from its
        /// in-game menu; the console sign and line stations are optional extras
        /// (see DefaultFull and the + buttons on the Package tab).
        /// </summary>
        public static ModProject Default()
        {
            var p = new ModProject { FormatVersion = CurrentFormat };
            p.Stations.Add(StationSpec.Manager());
            return p;
        }

        /// <summary>The manager plus the console sign and the six production lines (the earlier default).</summary>
        public static ModProject DefaultFull()
        {
            var p = Default();
            var console = StationSpec.Console();
            console.TechLevel = 1;
            console.TechCost = 0;
            p.Stations.Add(console);
            foreach (var line in new[]
                     {
                         StationSpec.LineFrom("Handiwork", "AncientWorkBench", "AncientWorkBench", 1),
                         StationSpec.LineFrom("Refinery", "AncientBlastFurnace", "AncientBlastFurnace", 1),
                         StationSpec.LineFrom("Kitchen", "AncientCookingStove", "AncientCookingStove", 1),
                         StationSpec.LineFrom("Medicine", "MedicineFacility_03", "MedicineFacility_03", 1),
                         StationSpec.LineFrom("Spheres", "SphereFactory_Black_04", "SphereFactory_Black_04", 1),
                         StationSpec.LineFrom("Weapons", "WeaponFactory_Dirty_04", "WeaponFactory_Dirty_04", 1),
                     })
            {
                line.TechCost = 0;
                p.Stations.Add(line);
            }
            return p;
        }

        /// <summary>
        /// Replace, not append: Newtonsoft's default (Auto) adds deserialized
        /// items to a list that already has default entries from its initializer,
        /// so every load doubled a station's build materials. Replace makes a
        /// loaded project exactly what was saved.
        /// </summary>
        private static readonly JsonSerializerSettings FileSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <summary>A fresh project with no name: the tool asks for one before it saves or generates.</summary>
        public static ModProject NewUnnamed()
        {
            var p = Default();
            p.ModName = "";
            p.PackageName = "";
            return p;
        }

        public string ToJson()
        {
            FormatVersion = CurrentFormat;
            return JsonConvert.SerializeObject(this, FileSettings);
        }

        public static ModProject FromJson(string json)
        {
            var p = JsonConvert.DeserializeObject<ModProject>(json, FileSettings) ?? Default();
            // A file may carry "Chain": null or null lists (ObjectCreationHandling.Replace keeps the null); treat as 'all on'.
            p.Chain ??= new ChainSettings();
            p.Chain.DisabledFamilies ??= new List<string>();
            p.Chain.ExcludedBenches ??= new List<string>();
            // a file from before CTab existed: C follows the old KeepAssignBoard switch
            try
            {
                var chainJson = JObject.Parse(json)["Chain"] as JObject;
                if (chainJson != null && chainJson["CTab"] == null && chainJson["KeepAssignBoard"] != null)
                    p.Chain.CTab = chainJson["KeepAssignBoard"].Value<bool>() ? "vanilla" : "monitor";
            }
            catch (Exception) { /* keep the defaults */ }
            p.Chain.KeepAssignBoard = p.Chain.CTab == "vanilla";
            return p;
        }

        public static string KindLabel(StationKind k) => k switch
        {
            StationKind.Manager => "Production Manager (any model)",
            StationKind.ConsoleSign => "Console (sign, typed commands)",
            StationKind.Copy => "Copy of a building (Generator)",
            _ => "Production line (bench, orders = targets)",
        };

        /// <summary>Problems that would make the package fail to load or misbehave.</summary>
        public List<string> Validate(GameData data)
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(ModName)) problems.Add("Mod name is required.");
            if (string.IsNullOrWhiteSpace(PackageName) || !PackageNameRule.IsMatch(PackageName))
                problems.Add("Package name must contain only letters and digits (A-Z a-z 0-9).");
            if (string.IsNullOrWhiteSpace(Version)) problems.Add("Version is required (any change triggers a reinstall).");
            if (!string.IsNullOrEmpty(SteamItemId) && !Regex.IsMatch(SteamItemId, "^[0-9]+$"))
                problems.Add("Steam item id must be digits only.");
            if (Stations.Count == 0) problems.Add("Add at least one station.");
            if (!Stations.Any(s => s.Enabled && s.IsManagerRole) && Automation.RequireManagerStation)
                problems.Add("No Production Manager station, but 'Require manager station' is on: nothing would ever run.");
            if (!Stations.Any(s => s.Enabled && (s.Kind == StationKind.Line || s.Kind == StationKind.ConsoleSign)) && Presets.Count == 0 && !Automation.ClientMenu)
                problems.Add("No way to set targets: turn the in-game menu on, or add a Production Line, a Console sign or preset targets.");
            var ids = new HashSet<string>();
            foreach (var s in Stations.Where(st => st.Enabled))
            {
                if (string.IsNullOrWhiteSpace(s.Id) || !StationIdRule.IsMatch(s.Id))
                    problems.Add($"Station id '{s.Id}' must be letters, digits and underscores, starting with a letter.");
                else if (!ids.Add(s.Id)) problems.Add($"Station id '{s.Id}' is used twice.");
                if (data != null && data.IsLoaded)
                {
                    if (data.Building(s.Id) != null || data.Icons.ContainsKey(s.Id))
                        problems.Add($"Station id '{s.Id}' already exists in the game; pick another.");
                    var reuse = data.Building(s.ReuseMapObjectId);
                    if (reuse == null) problems.Add($"{s.Id}: unknown building to reuse '{s.ReuseMapObjectId}'.");
                    else if (s.Kind == StationKind.ConsoleSign && !(reuse.Model ?? "").Contains("Signboard"))
                        problems.Add($"{s.Id}: a console must reuse a signboard building, not '{s.ReuseMapObjectId}'.");
                    else if (s.Kind == StationKind.Line && !ChainCatalog.IsTrueProducer(data.Producer(s.ReuseMapObjectId)))
                        problems.Add($"{s.Id}: a line station must reuse a crafting bench, not '{s.ReuseMapObjectId}'.");
                    else if (s.Kind == StationKind.Manager && string.IsNullOrEmpty(data.Building(s.BlueprintSource(data))?.Model))
                        problems.Add($"{s.Id}: '{s.BlueprintSource(data)}' has no concrete model class; the Lua side could not find it in a base.");
                    var swapProblem = s.ModelSwapProblem(data);
                    if (swapProblem != null) problems.Add(swapProblem);
                    if (string.IsNullOrEmpty(s.CustomIconPng) && !data.Icons.ContainsKey(s.IconMapObjectId ?? ""))
                        problems.Add($"{s.Id}: icon '{s.IconMapObjectId}' is not a known build icon.");
                    foreach (var m in s.Materials)
                        if (data.Item(m.ItemId) == null) problems.Add($"{s.Id}: build material '{m.ItemId}' is not an item.");
                }
                if (s.Kind == StationKind.Line && string.IsNullOrWhiteSpace(s.Line))
                    problems.Add($"{s.Id}: line stations need a line name.");
                if (s.Kind == StationKind.Line && data != null && data.IsLoaded && s.AllowedItems != null && s.AllowedItems.Count > 0)
                {
                    var bench = data.Producer(s.ReuseMapObjectId);
                    if (bench != null)
                    {
                        var canMake = new HashSet<string>(data.ItemsFor(bench).Select(i => i.Id));
                        foreach (var id in s.AllowedItems.Where(id => !canMake.Contains(id)))
                            problems.Add($"{s.Id}: linked item '{id}' cannot be made by {bench.Name}; unlink it or reuse another bench.");
                    }
                }
                if (s.Materials.Count == 0) problems.Add($"{s.Id}: needs at least one build material.");
                if (s.Materials.Count > 4) problems.Add($"{s.Id}: at most 4 build materials.");
                if (s.LimitPerBase < 0) problems.Add($"{s.Id}: limit per base cannot be negative.");
            }
            if (data != null && data.IsLoaded)
            {
                foreach (var kv in FacilityLinks)
                {
                    if (kv.Value == null || kv.Value.Count == 0) continue;
                    var bench = data.Producer(kv.Key);
                    if (bench == null) { problems.Add($"Facility links: '{kv.Key}' is not a known production line."); continue; }
                    var canMake = new HashSet<string>(data.ItemsFor(bench).Select(i => i.Id));
                    foreach (var id in kv.Value.Where(id => !canMake.Contains(id)))
                        problems.Add($"Facility links: {bench.Name} cannot make '{id}'; unlink it.");
                }
            }
            foreach (var s in Stations.Where(st => st.Enabled))
            {
                if (s.Kind == StationKind.Copy && s.ActsAsManager && data != null && data.IsLoaded)
                {
                    var host = data.Building(s.BlueprintSource(data));
                    if (host != null && host.Model != "PalMapObjectBaseCampPassiveWorkHardModel")
                        problems.Add($"{s.Id}: acts as the production manager but copies {host.Name ?? host.MapObjectId}, not a Monitoring Stand: F cannot open the Assignment Board on it.");
                }
            }
            var c = Chain ?? new ChainSettings();
            var lineNames = new HashSet<string>(Stations.Where(s => s.Kind == StationKind.Line).Select(s => s.Line ?? ""));
            foreach (var p in Presets)
            {
                if (data != null && data.IsLoaded)
                {
                    var item = data.Item(p.ItemId);
                    if (item == null) problems.Add($"Preset '{p.ItemId}' is not an item.");
                    else if (!item.Craftable) problems.Add($"Preset {item.Name} has no recipe, it can never be produced.");
                    // The producer pin: Only, or (older projects) a Line that names a bench type.
                    var legacyPin = !string.IsNullOrEmpty(p.Line) && p.Line != "default" && !lineNames.Contains(p.Line) && data.Producer(p.Line) != null;
                    var pinId = !string.IsNullOrEmpty(p.Only) ? p.Only : (legacyPin ? p.Line : null);
                    if (pinId != null)
                    {
                        var pin = data.Producer(pinId);
                        if (pin == null) problems.Add($"Preset '{p.ItemId}': producer '{pinId}' is not a bench type.");
                        else if (!ChainCatalog.MakersOf(data, p.ItemId).Any(m => m.MapObjectId == pinId)) problems.Add($"Preset '{p.ItemId}': {pin.Name} cannot make it.");
                        else if ((c.ExcludedBenches ?? new List<string>()).Contains(pinId) || !c.FamilyEnabled(ChainCatalog.FamilyOf(pin))) problems.Add($"Preset '{p.ItemId}': producer {pin.Name} is not tracked.");
                    }
                }
                if (p.Keep < 1) problems.Add($"Preset '{p.ItemId}': keep must be at least 1.");
                if (p.Trigger >= p.Keep && p.Trigger != 0) problems.Add($"Preset '{p.ItemId}': trigger must be below keep.");
                if (p.Weight < 1) problems.Add($"Preset '{p.ItemId}': weight must be at least 1.");
            }
            foreach (var f in ExtraFiles ?? new List<ExtraFile>())
            {
                if (string.IsNullOrWhiteSpace(f.Path)) { problems.Add("Extra file with an empty path."); continue; }
                var ext = System.IO.Path.GetExtension(f.Path).ToLowerInvariant();
                if ((f.Kind == ExtraFileKind.Paks || f.Kind == ExtraFileKind.LogicMods) && ext != ".pak")
                    problems.Add($"Extra file '{System.IO.Path.GetFileName(f.Path)}': Paks/LogicMods entries must be .pak files.");
                if ((f.Kind == ExtraFileKind.PalSchemaRaw || f.Kind == ExtraFileKind.PalSchemaBlueprints) && ext != ".json" && ext != ".jsonc")
                    problems.Add($"Extra file '{System.IO.Path.GetFileName(f.Path)}': PalSchema entries must be .json files.");
            }
            if (Automation.ScanIntervalSeconds < 1) problems.Add("Scan interval must be at least 1 second.");
            if (Automation.DefaultTriggerPercent < 0 || Automation.DefaultTriggerPercent > 99) problems.Add("Trigger percent must be 0-99.");
            if (Automation.MaxOrderBatch < 1) problems.Add("Max order batch must be at least 1.");
            var tabs = new[] { "chain", "monitor", "prod", "board", "targets" };
            if (!tabs.Contains(c.InteractTab ?? "")) problems.Add($"F opens: unknown tab '{c.InteractTab}'.");
            if (!new[] { "vanilla", "targets", "chain", "monitor", "prod" }.Contains(c.CTab ?? "")) problems.Add($"C opens: unknown choice '{c.CTab}'.");
            if (!tabs.Contains(c.DefaultTab ?? "")) problems.Add($"Menu opens on: unknown tab '{c.DefaultTab}'.");
            if (c.RankBy != "rank" && c.RankBy != "speed") problems.Add("Producer order must be rank or speed.");
            if (c.TargetOrder != "lottery" && c.TargetOrder != "weight" && c.TargetOrder != "priority") problems.Add("Serve order must be lottery, weight or priority.");
            foreach (var id in c.DisabledFamilies ?? new List<string>())
                if (ChainCatalog.Order(id) < 0) problems.Add($"Chain: unknown family '{id}'.");
            if (data != null && data.IsLoaded)
                foreach (var id in c.ExcludedBenches ?? new List<string>())
                    if (!ChainCatalog.IsTrueProducer(data.Producer(id))) problems.Add($"Chain: '{id}' is not a production bench.");
            if (Stations.Any(s => s.Enabled && s.IsManagerRole && s.AssignmentRows != null && s.AssignmentRows.Count > 0))
                problems.Add("Chain station must not have Pal work rows (they add a '[Hold] Work' prompt).");
            return problems;
        }
    }
}
