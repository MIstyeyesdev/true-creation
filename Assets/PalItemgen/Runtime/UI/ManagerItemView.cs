using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using PalItemgen.Lookup;
using PalItemgen.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// The third tab: the new item itself. It starts as a copy of the basic
    /// Monitoring Stand and follows the game's own rules for a building:
    ///
    ///   tier      which Monitoring Stand it copies (basic by default; compare
    ///             the higher tiers and load their presets),
    ///   identity  production type, names, index (SortId) and description,
    ///   options   the stand's row values (HP, defense, tech, ...),
    ///   BP model  the blueprint the game spawns for the item: static mesh,
    ///             collision footprint, menu - the "BPModel" a building pulls,
    ///   Pals      the assignment function chain the stand uses and the assign
    ///             rows that let a building take a Pal, made visible,
    ///   lines     every production line the manager controls.
    ///
    /// Everything edited here is the same StationSpec the Package tab's card
    /// shows, so the two never disagree.
    /// </summary>
    public sealed class ManagerItemView : VisualElement
    {
        private readonly GameData _data;
        private readonly Func<ModProject> _project;
        private readonly Action _changed;

        /// <summary>Host-supplied: opens the "all options" text in a real window. Null = inline overlay.</summary>
        public Action<string, string> OpenDump;

        /// <summary>Read at click time (the host assigns its window after this view is built).</summary>
        public Func<Action<string, string>> DumpHost;

        /// <summary>Host-supplied: opens the table browser (Creation Engine style) for a picker.</summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> ShowBrowser;

        private StationSpec _spec;
        private VisualElement _modelInfo;
        private VisualElement _tierTable;
        private Label _identityInfo;
        private VisualElement _overlay;

        public ManagerItemView(GameData data, Func<ModProject> project, Action changed)
        {
            _data = data;
            _project = project;
            _changed = changed;
            style.flexGrow = 1;
            Refresh();
        }

        public void Refresh()
        {
            Clear();
            _spec = _project().Stations.FirstOrDefault(s => s.Kind == StationKind.Manager);
            if (_spec == null)
            {
                var missing = UiKit.Section("Manager item", out var mb, "No Production Manager station in the project.");
                mb.Add(UiKit.Note("Add one on the Package & stations tab (+ Manager) and come back.", UiKit.Warning));
                Add(missing);
                return;
            }
            if (string.IsNullOrEmpty(_spec.MirrorMapObjectId)) _spec.MirrorMapObjectId = "BaseCampWorkHard";

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            Add(scroll);
            var columns = UiKit.Columns(UiKit.Column(), UiKit.Column());
            scroll.Add(columns);
            var left = (VisualElement)columns[0];
            var right = (VisualElement)columns[1];

            left.Add(BuildTier());
            left.Add(BuildIdentity());
            left.Add(BuildStandOptions());
            left.Add(Folded("Details: what the stand pulls from", BuildSources()));
            right.Add(BuildModel());
            right.Add(Folded("Details: the Pal-assignment function chain", BuildAssignment()));
            right.Add(BuildLines());
        }

        /// <summary>Reference text for the developer, collapsed by default: it says nothing a player needs.</summary>
        private static VisualElement Folded(string title, VisualElement content)
        {
            var f = new Foldout { text = title, value = false };
            f.style.marginTop = 4;
            f.Add(content);
            return f;
        }

        /// <summary>The stand tier this item copies (its options are mirrored).</summary>
        private ProducerInfo Source => _data.Building(_spec.MirrorMapObjectId);

        /// <summary>The building whose blueprint the item spawns (look, footprint, menu).</summary>
        private ProducerInfo Model => _data.Building(_spec.ReuseMapObjectId);

        private ProducerInfo BasicStand => _data.Building("BaseCampWorkHard");

        // ------------------------------------------------------------- tier

        private VisualElement BuildTier()
        {
            var section = UiKit.Section("Copy of: Monitoring Stand tier", out var body,
                "The item is a copy of the basic Monitoring Stand by default. Pick a higher tier to compare its numbers; " +
                "\"Load this tier's presets\" copies them into the item (build cost, HP, tech level, model, icon). Everything stays editable after.");
            var tiers = _data.StandTiers().ToList();
            if (tiers.Count == 0)
            {
                body.Add(UiKit.Note("no Monitoring Stand rows in the lookup data", UiKit.Danger));
                return section;
            }
            var labels = tiers.Select(TierLabel).ToList();
            var current = Math.Max(0, tiers.FindIndex(t => t.MapObjectId == _spec.MirrorMapObjectId));
            var dropdown = new DropdownField(labels, current);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var idx = labels.IndexOf(evt.newValue);
                if (idx < 0) return;
                _spec.MirrorMapObjectId = tiers[idx].MapObjectId;
                RefreshTierTable();
                Changed();
            });
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            dropdown.style.flexGrow = 1;
            line.Add(dropdown);
            line.Add(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.StandTiers(_data);
                ShowBrowser?.Invoke("Monitoring Stand tiers", cols, rows, v =>
                {
                    var idx = tiers.FindIndex(t => t.MapObjectId == v);
                    if (idx < 0) return;
                    _spec.MirrorMapObjectId = v;
                    dropdown.SetValueWithoutNotify(labels[idx]);
                    RefreshTierTable();
                    Changed();
                });
            }));
            body.Add(Row("Tier", line, "vanilla tiers: Monitoring Stand (level 7), High Quality (28), Ancient (71)"));

            _tierTable = new VisualElement();
            _tierTable.style.marginTop = 4;
            body.Add(_tierTable);
            RefreshTierTable();

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            buttons.Add(UiKit.SecondaryButton("Load this tier's presets into the item", LoadTierPresets));
            body.Add(buttons);
            body.Add(UiKit.Note("Loading presets sets: build cost, build work, HP, defense, material type, tech level and cost, the BP model and the icon. " +
                                "The stand's limit per base (none) is not copied: the manager keeps 1 per base.", UiKit.Muted));
            return section;
        }

        /// <summary>Everything the Monitoring Stand pulls from, and which of it the copy writes versus inherits.</summary>
        private VisualElement BuildSources()
        {
            var src = Source;
            var section = UiKit.Section("What the stand pulls from (and what the copy inherits)", out var body,
                "The whole copy is ONE item: this one. Rows are written under the new id; everything the blueprint carries is inherited by naming that blueprint.");
            var bp = src?.RawMaster?["BlueprintClassName"]?.ToString() ?? src?.Bp ?? "BP_BuildObject_BaseCampWorkHard";
            body.Add(KV("WRITTEN  master row", $"DT_MapObjectMasterDataTable[{src?.MapObjectId}]: BlueprintClassName {bp}, Hp {src?.Master?.Hp}, Defense {src?.Master?.Defense}, {src?.Master?.MaterialType}/{src?.Master?.MaterialSubType}"));
            body.Add(KV("WRITTEN  build row", $"DT_BuildObjectDataTable: {src?.Build?.TypeA}/{src?.Build?.TypeB}, wheel {src?.Build?.TypeUIDisplay}, SortId {src?.RawBuild?["SortId"]}, cost {(src?.Build == null ? "" : string.Join(", ", src.Build.Materials.Select(m => $"{m.Count} {_data.Item(m.Id)?.Name ?? m.Id}")))}, work {src?.Build?.RequiredBuildWorkAmount}, Palbox only"));
            body.Add(KV("WRITTEN  tech row", $"DT_TechnologyRecipeUnlock: level {src?.Tech?.LevelCap}, cost {src?.Tech?.Cost}"));
            body.Add(KV("WRITTEN  assign rows", $"DT_MapObjectAssignData: {GameData.AssignSummary(src)} (nobody works at the stand)"));
            body.Add(KV("WRITTEN  texts + icon", $"MAPOBJECT_NAME_/BUILDOBJECT_DESC_ texts from Name/Description; icon {src?.Icon ?? "?"}"));
            body.Add(KV("INHERITED blueprint", $"{bp}: StaticMesh {src?.Mesh}, CheckOverlapCollision {(src?.Overlap != null ? src.Overlap.Metres : "?")}, BP_InteractableBox (the interact prompt), VisualCtrl, DamageReaction, PalMapObjectBaseCampPassiveEffectWorkHardParameterComponent"));
            body.Add(KV("INHERITED model class", $"the blueprint's ConcreteModelClass = {src?.Model ?? "PalMapObjectBaseCampPassiveWorkHardModel"} (native C++). This is the pull that makes the item a stand: WorkHardType + RequestUpdateWorkHardType, and the three interaction entries below. The base camp lists it in WeakBaseCampWorkHardModelArray, so its work mode applies to the base like a real stand."));
            body.Add(KV("INHERITED F  Set Work Mode", "OpenWorkHardMenu -> WBP_IngameMenu_Monitoring (PalHUDDispatchParameter_MapObject); the mod replaces this screen with its menu"));
            body.Add(KV("INHERITED V  Set Work Preferences", "OpenWorkSuitabilityPreferenceMenu -> WBP_WorkSuitabilityPreferenceMenu (UPalHUDDispatchParameter_WorkSuitabilityPreference; per-Pal allow/prohibit per work type, WorkSuitabilityOptionInfo). Left vanilla: the mod does not rewrite it."));
            body.Add(KV("INHERITED C  Fixed assignment", "OpenFixedAssignManageMenu -> WBP_AssignBoard 'Assignment Board' (UPalHUDDispatchParameter_BaseCampWorkFixedAssignManage, UPalUIBaseCampWorkFixedAssignManageModel: GetFixedAssignableWorks, rows via GetAssignInfo/IsFixed/GetAssignableNum, icons via GetBuildObjectIconDataTableAccess). The mod's tab 1 copies this board and replaces it on interact."));
            body.Add(UiKit.Note("Nothing else is pulled: no extra items, no other buildings. The console sign and line stations are optional extras on the Package tab, off by default.", UiKit.Muted));
            return section;
        }

        private string TierLabel(ProducerInfo t) =>
            $"{(string.IsNullOrEmpty(t.Name) ? t.MapObjectId : t.Name)}  [{t.MapObjectId}]  level {t.Tech?.LevelCap ?? 0}";

        /// <summary>Field-by-field comparison: basic stand, the selected tier, and this item as it is now.</summary>
        private void RefreshTierTable()
        {
            if (_tierTable == null) return;
            _tierTable.Clear();
            var basic = BasicStand;
            var tier = Source;
            string Cost(ProducerInfo b) => b?.Build == null ? "" : string.Join(", ", b.Build.Materials.Select(m => $"{m.Count} {_data.Item(m.Id)?.Name ?? m.Id}"));
            string ItemCost() => string.Join(", ", (_spec.Materials ?? new List<MaterialCost>()).Select(m => $"{m.Count} {_data.Item(m.ItemId)?.Name ?? m.ItemId}"));
            var rows = new List<(string field, string basicV, string tierV, string itemV)>
            {
                ("HP", basic?.Master?.Hp.ToString(), tier?.Master?.Hp.ToString(), _spec.Hp.ToString()),
                ("Defense", basic?.Master?.Defense.ToString(), tier?.Master?.Defense.ToString(), _spec.Defense.ToString()),
                ("Material type", $"{basic?.Master?.MaterialType}/{basic?.Master?.MaterialSubType}", $"{tier?.Master?.MaterialType}/{tier?.Master?.MaterialSubType}", $"{_spec.MaterialType}/{_spec.MaterialSubType}"),
                ("Build work", F(basic?.Build?.RequiredBuildWorkAmount), F(tier?.Build?.RequiredBuildWorkAmount), F(_spec.BuildWorkAmount)),
                ("Build cost", Cost(basic), Cost(tier), ItemCost()),
                ("Tech level / cost", $"{basic?.Tech?.LevelCap} / {basic?.Tech?.Cost}", $"{tier?.Tech?.LevelCap} / {tier?.Tech?.Cost}", $"{_spec.TechLevel} / {_spec.TechCost}"),
                ("Deterioration", F(basic?.Master?.DeteriorationDamage), F(tier?.Master?.DeteriorationDamage), "0.12 (fixed)"),
                ("BP model", basic?.Bp, tier?.Bp, Model?.Bp ?? _spec.ReuseMapObjectId),
                ("Static mesh", basic?.Mesh, tier?.Mesh, Model?.Mesh),
                ("Footprint", basic?.Overlap?.Metres, tier?.Overlap?.Metres, Model?.Overlap?.Metres),
                ("Icon", basic?.MapObjectId, tier?.MapObjectId, _spec.IconMapObjectId),
                ("Limit per base", "none", "none", _spec.LimitPerBase == 0 ? "none" : _spec.LimitPerBase.ToString()),
            };
            _tierTable.Add(TableRow("", "Basic stand", tier == null || tier.MapObjectId == basic?.MapObjectId ? "Selected tier (= basic)" : "Selected: " + (tier.Name ?? tier.MapObjectId), "This item", true, false));
            foreach (var (field, b, t, i) in rows)
                _tierTable.Add(TableRow(field, b ?? "", t ?? "", i ?? "", false, (t ?? "") != (b ?? "")));
        }

        private static string F(float? v) => v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";

        private VisualElement TableRow(string field, string a, string b, string c, bool header, bool tierDiffers)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 1;
            row.style.paddingLeft = 2;
            if (header) row.style.backgroundColor = UiKit.Panel;
            VisualElement Cell(string text, float width, Color color, bool bold = false)
            {
                var l = new Label(text);
                l.style.width = width;
                l.style.flexShrink = 0;
                l.style.fontSize = 10;
                l.style.color = color;
                l.style.whiteSpace = WhiteSpace.Normal;
                if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
                return l;
            }
            row.Add(Cell(field, 100, UiKit.Muted, true));
            row.Add(Cell(a, 150, header ? UiKit.Text : UiKit.Muted, header));
            row.Add(Cell(b, 170, header ? UiKit.Text : (tierDiffers ? UiKit.Accent : UiKit.Muted), header));
            row.Add(Cell(c, 170, UiKit.Text, header));
            return row;
        }

        /// <summary>Copies the selected tier's row values into the item (the item then builds and unlocks like that tier).</summary>
        private void LoadTierPresets()
        {
            var tier = Source;
            if (tier == null) return;
            if (tier.Master != null)
            {
                _spec.Hp = tier.Master.Hp;
                _spec.Defense = tier.Master.Defense;
                _spec.MaterialType = tier.Master.MaterialType ?? _spec.MaterialType;
                _spec.MaterialSubType = tier.Master.MaterialSubType ?? _spec.MaterialSubType;
            }
            if (tier.Build != null)
            {
                _spec.BuildWorkAmount = tier.Build.RequiredBuildWorkAmount;
                _spec.Materials = tier.Build.Materials.Select(m => new MaterialCost { ItemId = m.Id, Count = m.Count }).ToList();
                if (!string.IsNullOrEmpty(tier.Build.TypeUIDisplay)) _spec.TypeUIDisplay = tier.Build.TypeUIDisplay;
            }
            if (tier.Tech != null)
            {
                _spec.TechLevel = tier.Tech.LevelCap;
                _spec.TechCost = tier.Tech.Cost;
                _spec.AncientTech = tier.Tech.IsBoss;
            }
            _spec.ReuseMapObjectId = tier.MapObjectId;
            if (_data.Icons.ContainsKey(tier.MapObjectId)) _spec.IconMapObjectId = tier.MapObjectId;
            Refresh();
            _changed?.Invoke();
        }

        // ------------------------------------------------------------- identity

        private VisualElement BuildIdentity()
        {
            var section = UiKit.Section("Item identity", out var body,
                "Mirrors the stand's row: production type is the build-wheel category, the display name is what players see, " +
                "the internal name is the row key in the game tables.");

            var uiTypes = _data.Enums.TypeUIDisplay.Count > 0 ? _data.Enums.TypeUIDisplay : new List<string> { _spec.TypeUIDisplay };
            var prodType = new DropdownField(uiTypes, Math.Max(0, uiTypes.IndexOf(_spec.TypeUIDisplay)));
            prodType.RegisterValueChangedCallback(evt => { _spec.TypeUIDisplay = evt.newValue; Changed(); });
            // Dropdown for a quick pick, plus the table browser (search, sort, how many
            // vanilla buildings use each category and which) for looking around.
            var typeLine = new VisualElement();
            typeLine.style.flexDirection = FlexDirection.Row;
            typeLine.style.alignItems = Align.Center;
            prodType.style.flexGrow = 1;
            typeLine.Add(prodType);
            typeLine.Add(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.ProductionTypes(_data);
                ShowBrowser?.Invoke("Production types (build-wheel categories)", cols, rows, v =>
                {
                    _spec.TypeUIDisplay = v;
                    prodType.SetValueWithoutNotify(v);
                    Changed();
                });
            }));
            body.Add(Row("Production type", typeLine, $"build-wheel category; the stand uses {Source?.Build?.TypeUIDisplay ?? "PalManagement"} ({Source?.Build?.TypeA}/{Source?.Build?.TypeB})"));

            var name = new TextField { value = _spec.Name };
            name.RegisterValueChangedCallback(evt => { _spec.Name = evt.newValue; Changed(); });
            body.Add(Row("Display name", name, "player-facing (MAPOBJECT_NAME text)"));

            var id = new TextField { value = _spec.Id };
            id.RegisterValueChangedCallback(evt => { _spec.Id = evt.newValue; Changed(); });
            body.Add(Row("Internal name", id, "row key in DT_MapObjectMasterDataTable / DT_BuildObjectDataTable; must not clash with a vanilla id"));

            var sortId = Source?.RawBuild?["SortId"]?.ToString() ?? "?";
            var bpClass = Source?.RawMaster?["BlueprintClassName"]?.ToString() ?? Source?.Bp ?? "?";
            body.Add(Row("Index (SortId)", Readonly($"stand: {sortId}; this item: next free slot, handed out by PalSchema at load"),
                "the index is only the build-wheel order. It is NOT tied to a blueprint: the blueprint is tied by " +
                $"BlueprintClassName ({bpClass}) in DT_MapObjectMasterDataTable - see BP model on the right."));

            var desc = new TextField { value = _spec.Description, multiline = true };
            desc.style.height = 46;
            desc.RegisterValueChangedCallback(evt => { _spec.Description = evt.newValue; Changed(); });
            body.Add(Row("Description", desc, "build wheel + technology text"));

            _identityInfo = UiKit.Note(IdentityInfo());
            body.Add(_identityInfo);
            return section;
        }

        private string IdentityInfo()
        {
            var display = _spec.Name ?? "";
            var internalName = _spec.Id ?? "";
            return display.Replace(" ", "").Equals(internalName, StringComparison.OrdinalIgnoreCase)
                ? "display and internal name match"
                : $"display name \"{display}\" differs from internal name \"{internalName}\" (that is normal: vanilla shows \"Monitoring Stand\" for BaseCampWorkHard)";
        }

        // ------------------------------------------------------------- stand options

        private VisualElement BuildStandOptions()
        {
            var src = Source;
            var section = UiKit.Section("Monitoring Stand options (as needed)", out var body,
                "The stand's row values, editable. Defaults come from the vanilla row of the selected tier; the full list of every field is behind the creative dev box.");
            body.Add(IntRow("HP", _spec.Hp, v => _spec.Hp = v, $"stand: {src?.Master?.Hp}"));
            body.Add(IntRow("Defense", _spec.Defense, v => _spec.Defense = v, $"stand: {src?.Master?.Defense}"));
            var mats = _data.Enums.MaterialType.Count > 0 ? _data.Enums.MaterialType : new List<string> { _spec.MaterialType };
            var mt = new DropdownField(mats, Math.Max(0, mats.IndexOf(_spec.MaterialType)));
            mt.RegisterValueChangedCallback(evt => { _spec.MaterialType = evt.newValue; Changed(); });
            body.Add(Row("Material type", mt, $"damage table; stand: {src?.Master?.MaterialType}"));
            var subs = _data.Enums.MaterialSubType.Count > 0 ? _data.Enums.MaterialSubType : new List<string> { _spec.MaterialSubType };
            var st = new DropdownField(subs, Math.Max(0, subs.IndexOf(_spec.MaterialSubType)));
            st.RegisterValueChangedCallback(evt => { _spec.MaterialSubType = evt.newValue; Changed(); });
            body.Add(Row("Material sub type", st, $"stand: {src?.Master?.MaterialSubType}"));
            body.Add(FloatRow("Build work", _spec.BuildWorkAmount, v => _spec.BuildWorkAmount = v, $"stand: {src?.Build?.RequiredBuildWorkAmount}"));
            body.Add(IntRow("Tech level", _spec.TechLevel, v => _spec.TechLevel = v, $"stand: {src?.Tech?.LevelCap} (1 = no requirement for testing)"));
            body.Add(IntRow("Tech cost", _spec.TechCost, v => _spec.TechCost = v, $"stand: {src?.Tech?.Cost}"));
            body.Add(ToggleRow("Ancient tech", _spec.AncientTech, v => _spec.AncientTech = v, "costs ancient technology points"));
            body.Add(IntRow("Limit per base", _spec.LimitPerBase, v => _spec.LimitPerBase = v, "1 = one manager per base; the stand itself has no limit"));
            body.Add(ToggleRow("Only near the Palbox", true, _ => { }, "bIsInstallOnlyHubAround, always on for this item (as the stand)"));

            var costLine = string.Join(", ", (_spec.Materials ?? new List<MaterialCost>()).Select(m => $"{m.Count} {_data.Item(m.ItemId)?.Name ?? m.ItemId}"));
            var standCost = src?.Build == null ? "" : string.Join(", ", src.Build.Materials.Select(m => $"{m.Count} {_data.Item(m.Id)?.Name ?? m.Id}"));
            body.Add(UiKit.Note($"Build cost: {costLine}   (stand: {standCost}). Edit the materials on the Package & stations tab, or load a tier's presets above."));

            var dev = new Toggle { value = false, text = "creative dev: list all options (opens its own window, read-only for now)" };
            dev.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue) return;
                dev.SetValueWithoutNotify(false);
                ShowDump();
            });
            dev.style.marginTop = 6;
            body.Add(dev);
            return section;
        }

        // ------------------------------------------------------------- BP model

        private VisualElement BuildModel()
        {
            var section = UiKit.Section("BP model (the blueprint the item spawns)", out var body,
                "Every building row names a blueprint (BlueprintClassName in DT_MapObjectMasterDataTable). The game spawns that blueprint for the item, " +
                "and the blueprint carries the static mesh (the skin), the collision box used for placement (the item's size in x/y/z), the components " +
                "and the menu that opens on interact. Pick which vanilla blueprint this item pulls. Listed: production and storage/transport buildings and the monitoring stands.");
            var candidates = _data.ModelCandidates().OrderBy(c => string.IsNullOrEmpty(c.Name) ? "~" + c.MapObjectId : c.Name).ToList();
            var picker = new UiKit.SearchPicker(() => candidates.Select(c => (c.MapObjectId, ModelLabel(c))), null, 400);
            var currentModel = candidates.FirstOrDefault(c => c.MapObjectId == _spec.ReuseMapObjectId);
            picker.SetDisplay(currentModel != null ? ModelLabel(currentModel) : _spec.ReuseMapObjectId);
            void Pick(string v)
            {
                _spec.ReuseMapObjectId = v;
                var chosen = candidates.FirstOrDefault(c => c.MapObjectId == v);
                picker.SetDisplay(chosen != null ? ModelLabel(chosen) : v);
                UpdateModelInfo();
                RefreshTierTable();
                Changed();
            }
            picker.OnSelected += Pick;
            picker.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Models(_data);
                ShowBrowser?.Invoke("Models - production and storage buildings, monitoring stands", cols, rows, Pick);
            }));
            body.Add(Row("Model", picker, "the caret opens the scrolling list, typing filters it, the table button opens the full table with footprint, mesh and class"));
            _modelInfo = new VisualElement();
            _modelInfo.style.marginTop = 4;
            body.Add(_modelInfo);
            UpdateModelInfo();
            return section;
        }

        private string ModelLabel(ProducerInfo c)
        {
            var name = string.IsNullOrEmpty(c.Name) ? c.MapObjectId : c.Name;
            var size = c.Overlap != null ? c.Overlap.Metres : "size ?";
            return $"{name}  [{c.MapObjectId}]  {c.Build?.TypeA}  {size}  {c.Mesh ?? ""}";
        }

        private void UpdateModelInfo()
        {
            _modelInfo.Clear();
            var m = Model;
            if (m == null)
            {
                _modelInfo.Add(UiKit.Note("unknown building: the game would show nothing", UiKit.Danger));
                return;
            }
            var bpClass = m.RawMaster?["BlueprintClassName"]?.ToString() ?? (m.Bp + "_C");
            var interacts = m.Model == "PalMapObjectBaseCampPassiveWorkHardModel" ? "the Monitoring Stand's menu: work mode + the assign-work list (what the manager's F7 menu mirrors on its first tab)"
                : m.Model == "PalMapObjectConvertItemModel" ? "a crafting menu: this item would then take orders like a line station"
                : m.Model == "PalMapObjectItemChestModel" ? "a chest inventory"
                : "that building's own menu";
            _modelInfo.Add(KV("In use", $"{(string.IsNullOrEmpty(m.Name) ? m.MapObjectId : m.Name)}  [{m.MapObjectId}]"));
            _modelInfo.Add(KV("Blueprint class", bpClass));
            _modelInfo.Add(KV("Blueprint path", m.BlueprintSoft ?? "?"));
            _modelInfo.Add(KV("Model class (C++)", (m.Model ?? "none") + "  - the object the Lua side finds in a base"));
            _modelInfo.Add(KV("Static mesh (skin)", m.Mesh ?? "(none exported: the blueprint builds its look from components)"));
            _modelInfo.Add(KV("Placement box", m.Overlap != null ? $"{m.Overlap.Metres}  (half extents {m.Overlap.X} x {m.Overlap.Y} x {m.Overlap.Z} cm, CheckOverlapCollision)" : "not in the export"));
            _modelInfo.Add(KV("Work area", m.Workable != null ? m.Workable.Metres + "  (BuildWorkableBounds: where assigned Pals stand)" : "none"));
            _modelInfo.Add(KV("Interact opens", interacts));
            _modelInfo.Add(KV("Pal requirement", GameData.AssignSummary(m) + "  (DT_MapObjectAssignData rows of this blueprint's building)"));
            _modelInfo.Add(KV("Tied to the index?", "no. SortId only orders the build wheel; the blueprint comes from the master row."));
            _modelInfo.Add(UiKit.Note($"Origin: the package writes only the new row that names {bpClass}; nothing is copied. Mesh, collision, components and menu all stay the vanilla blueprint's.", UiKit.Muted));
        }

        // ------------------------------------------------------------- Pal assignment

        private VisualElement BuildAssignment()
        {
            var section = UiKit.Section("Pal assignment (the stand's function, made visible)", out var body,
                "The stand itself is never worked (it has no assign rows). Its menu pins Pals to OTHER buildings' work: that menu and the calls under it " +
                "are what the manager reuses for \"best Pal to producer\". Names below come from the game's blueprint exports and the exe; the RPC pair is field-proven by other mods.");

            body.Add(SubHeader("Function chain the Monitoring Stand runs"));
            body.Add(Fn("BP_BuildObject_BaseCampWorkHard",
                "the stand's blueprint: StaticMesh SM_SurveillanceTable, BP_InteractableBox (the interact prompt), PalMapObjectBaseCampPassiveEffectWorkHardParameterComponent (work mode). Concrete model PalMapObjectBaseCampPassiveWorkHardModel.",
                "SOLID: blueprint export"));
            body.Add(Fn("Standing in front of it: BP_InteractableBox -> PalHUDDispatchParameter_MapObject -> WBP_IngameMenu_Monitoring:OnSetup(Param)",
                "the interact trigger is the blueprint's BP_InteractableBox component; the interaction dispatches a PalHUDDispatchParameter_MapObject carrying the concrete model, and the Monitoring menu widget reads it in OnSetup (GetParam -> cast to PalMapObjectBaseCampPassiveWorkHardModel -> GetPassiveEffectWorkHard). " +
                "The mod hooks that OnSetup: when the model's map object id is this item, it opens its own two-tab menu (and removes the vanilla one when 'Close vanilla menu' is on).",
                "SOLID: widget export; the hook itself is UNTESTED in game"));
            body.Add(Fn("WBP_BaseCampWorkFixedAssignManage",
                "the \"assign work\" screen the stand opens; HUD parameter UPalHUDDispatchParameter_BaseCampWorkFixedAssignManage, logic object UPalUIBaseCampWorkFixedAssignManageModel.",
                "SOLID: widget export"));
            body.Add(Fn("UPalUIBaseCampWorkFixedAssignManageModel::GetFixedAssignableWorks(TArray<FPalUIBaseCampWorkFixedAssignInfo>& OutWorks)",
                "lists every work in the base a Pal may be pinned to (one entry per bench, farm, ...); SortFixedAssignableWorks(OutArray) orders it.",
                "SOLID names; parameter types from the widget's call variables"));
            body.Add(Fn("::CanFixedAssign(...) -> bool, ::CanAssign -> CanAssign, ::AlreadyFixedAssign -> IsAlreadyFixedAssign, ::IsAssignedFixed, ::GetAssignedCharacters -> IndividualSlots",
                "the checks the screen makes before it sends the request (suitability, already pinned, slot free).",
                "SOLID names; parameters INFERRED"));
            body.Add(Fn("UPalNetworkBaseCampComponent::RequestFixedAssignWorkInBaseCamp_ToServer(FGuid BaseCampId, FGuid WorkId, FGuid IndividualId)",
                "THE assignment call: pins one Pal (IndividualId) to one work (WorkId = PalWorkBase:GetWorkId()) in a base. From Lua: PlayerController.Transmitter:GetBaseCamp():RequestFixedAssignWorkInBaseCamp_ToServer(...). The mod calls it in pm_game.lua fixed_assign.",
                "SOLID: field-verified by the Cyber Mendel mod on client and host"));
            body.Add(Fn("UPalNetworkBaseCampComponent::RequestUnassignWorkInBaseCamp_ToServer(FGuid BaseCampId, FGuid WorkId, FGuid IndividualId)",
                "releases the pin; the Pal goes back to base AI. The mod calls it when the bench idles (\"release when done\").",
                "SOLID: same source"));
            body.Add(Fn("UPalBaseCampWorkerDirector (server)",
                "BaseCampId, CharacterContainer, RequiredAssignWorks: TArray<FPalBaseCampWorkAssignRequest>, WaitingWorkerIndividualIds -> RegisterFixedAssignWork -> the bench's UPalWorkAssign slot gets bFixed = true (AssignedIndividualId, State).",
                "properties SOLID (mapping file); the order of calls INFERRED"));
            body.Add(Fn("Results the game reports",
                "SuccessFixedAssign; FailedFixedAssign NoSuitability / NoWorkInRange / NotAssignableOtomo / NotWantToDo / OtomoLackSuitabilityLevel / OverflowWorkers / WithTargetWork.",
                "SOLID: names in the exe"));

            body.Add(SubHeader("What lets a building take a Pal: DT_MapObjectAssignData rows"));
            body.Add(UiKit.Note("FPalMapObjectAssignData: WorkSuitability + WorkSuitabilityRank (what the Pal needs), bBaseCampWorkerWorkable / bPlayerWorkable, WorkType, WorkActionType, " +
                                "WorkerMaxNum (0 = the blueprint's slots), AffectSanityValue, AffectFullStomachValue, MultiWorkSuitability1/2 + MultiRequiredRank1/2 (either-or requirements).", UiKit.Muted));
            var model = Model;
            body.Add(KV("This item's BP model", $"{(model == null ? _spec.ReuseMapObjectId : (string.IsNullOrEmpty(model.Name) ? model.MapObjectId : model.Name))}: {GameData.AssignSummary(model)}"));
            if (model != null && model.RawAssignments.Count > 0)
            {
                foreach (var row in model.RawAssignments)
                    body.Add(UiKit.Note("  row: " + string.Join("  ", row.Properties().Where(p => p.Name != "GenusCategory" && p.Name != "ElementType" && p.Name != "WorkableTribeIDs" && p.Name != "WorkableSizeMin" && p.Name != "WorkableSizeMax")
                        .Select(p => $"{p.Name}={p.Value.ToString().Replace("EPalWorkSuitability::", "").Replace("EPalWorkType::", "").Replace("EPalActionType::", "")}")), UiKit.Muted));
            }
            else
            {
                body.Add(UiKit.Note("  none: nobody works at the stand; Pals are pinned to the benches below through the stand's menu.", UiKit.Muted));
            }

            body.Add(SubHeader("Pal requirement of this project's lines (from their benches' assign rows)"));
            foreach (var s in _project().Stations.Where(s => s.Kind == StationKind.Line))
            {
                var bench = _data.Building(s.ReuseMapObjectId);
                body.Add(KV(s.Name, $"{bench?.Name ?? s.ReuseMapObjectId}: {GameData.AssignSummary(bench)}"));
            }

            body.Add(SubHeader("How the manager uses it (pm_config.lua, same flags as the Package tab)"));
            var a = _project().Automation;
            body.Add(ToggleRow("Best Pal to producer", a.AssignBestWorker, v => a.AssignBestWorker = v, "after an order: fixed-assign the base Pal with the highest rank in the bench's work type (the stand's throw-to-assign, done by the mod)"));
            body.Add(ToggleRow("Release when done", a.ReleaseWorkerWhenDone, v => a.ReleaseWorkerWhenDone = v, "unassign that Pal when the bench idles; it returns to base AI"));
            body.Add(IntRow("Pals per order", a.WorkersPerOrder, v => a.WorkersPerOrder = v, "how many Pals to pin to one bench (capped by the bench's own slots)"));
            return section;
        }

        // ------------------------------------------------------------- production lines

        private VisualElement BuildLines()
        {
            var section = UiKit.Section("Production lines this manager controls", out var body,
                "The manager can order at every crafting facility in the base. Per line you choose what it may make: tick items on the Producers & item links tab " +
                "(\"Assembly Line may only make spheres\"). Nothing ticked = everything that line can make. In game the F7 menu also gives each facility a priority 0-5 and an allowed switch.");
            var project = _project();
            var list = new ScrollView();
            list.style.maxHeight = 300;
            foreach (var s in project.Stations.Where(s => s.Kind == StationKind.Line))
            {
                var bench = _data.Producer(s.ReuseMapObjectId);
                var count = bench != null ? _data.ItemsFor(bench).Count() : 0;
                var linked = s.AllowedItems?.Count ?? 0;
                list.Add(LineRow($"{s.Name}  [{s.Id}]", $"{bench?.Name ?? s.ReuseMapObjectId}: {(linked > 0 ? $"{linked} of {count} items linked" : $"all {count} items")}", UiKit.Accent));
            }
            foreach (var p in _data.Producers.Where(p => p.MapObjectId != null && p.Buildable).OrderBy(p => p.Name))
            {
                var count = _data.ItemsFor(p).Count();
                var linked = project.FacilityLinks.TryGetValue(p.MapObjectId, out var l) && l != null ? l.Count : 0;
                list.Add(LineRow($"{p.Name}  [{p.MapObjectId}]", $"rank {p.RankMax}: {(linked > 0 ? $"{linked} of {count} items linked" : $"all {count} items")}", linked > 0 ? UiKit.Accent : UiKit.Muted));
            }
            body.Add(list);
            return section;
        }

        private static VisualElement LineRow(string name, string detail, Color detailColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 1;
            var n = new Label(name);
            n.style.width = 260;
            n.style.flexShrink = 0;
            n.style.fontSize = 11;
            n.style.color = UiKit.Text;
            n.style.overflow = Overflow.Hidden;
            row.Add(n);
            var d = new Label(detail);
            d.style.fontSize = 11;
            d.style.color = detailColor;
            d.style.whiteSpace = WhiteSpace.Normal;
            row.Add(d);
            return row;
        }

        // ------------------------------------------------------------- dump (creative dev)

        private string DumpText()
        {
            var src = Source;
            var model = Model;
            var sb = new StringBuilder();
            sb.AppendLine($"ALL OPTIONS of the source building {_spec.MirrorMapObjectId} (read-only; every field of the shipped rows)");
            sb.AppendLine(new string('=', 78));
            void Section(string title, JObject o)
            {
                sb.AppendLine();
                sb.AppendLine(title);
                sb.AppendLine(new string('-', title.Length));
                if (o == null) { sb.AppendLine("  (none)"); return; }
                foreach (var p in o.Properties())
                    sb.AppendLine($"  {p.Name} = {p.Value.ToString(Newtonsoft.Json.Formatting.None)}");
            }
            Section("DT_MapObjectMasterDataTable row", src?.RawMaster);
            Section("DT_BuildObjectDataTable row", src?.RawBuild);
            if (src != null)
            {
                var i = 0;
                foreach (var a in src.RawAssignments) Section($"DT_MapObjectAssignData row {i++}", a);
                if (src.RawAssignments.Count == 0) Section("DT_MapObjectAssignData rows", null);
            }
            Section("DT_TechnologyRecipeUnlock row", src?.RawTech);
            sb.AppendLine();
            sb.AppendLine("Monitoring Stand work modes (BP_PalGameSetting.BaseCampPassiveEffectWorkHardInfoMap)");
            sb.AppendLine(new string('-', 70));
            foreach (var w in _data.WorkHard)
                sb.AppendLine($"  {w.Mode,-9} workSpeed x{w.WorkSpeedRate}  moveSpeed x{w.MoveSpeedRate}  sanity {w.AffectSanityRate:+0.00;-0.00}  hunger x{w.DecreaseFullStomachRate}");
            sb.AppendLine();
            sb.AppendLine("Monitoring Stand tiers");
            sb.AppendLine(new string('-', 22));
            foreach (var t in _data.StandTiers())
                sb.AppendLine($"  {t.MapObjectId,-20} {t.Name,-30} level {t.Tech?.LevelCap,3} cost {t.Tech?.Cost}  hp {t.Master?.Hp}  def {t.Master?.Defense}  work {t.Build?.RequiredBuildWorkAmount}  mesh {t.Mesh}");
            sb.AppendLine();
            sb.AppendLine($"BP model in use: {model?.Bp}  class {model?.RawMaster?["BlueprintClassName"]}  mesh {model?.Mesh}  overlap {(model?.Overlap != null ? model.Overlap.Metres : "?")}  model class {model?.Model}");
            sb.AppendLine($"Assign rows of that model: {GameData.AssignSummary(model)}");
            sb.AppendLine();
            sb.AppendLine("Only the options on the Manager item tab are written by the tool; everything else keeps the value the reused blueprint and the PalSchema loader give it.");
            return sb.ToString();
        }

        private void ShowDump()
        {
            var text = DumpText();
            var host = DumpHost?.Invoke() ?? OpenDump;
            if (host != null)
            {
                host($"All options - {_spec.MirrorMapObjectId}", text);
                return;
            }
            _overlay?.RemoveFromHierarchy();
            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.right = 0; _overlay.style.top = 0; _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0, 0, 0, 0.7f);
            var panel = UiKit.Section("All options (read-only)", out var body);
            panel.style.marginLeft = 40; panel.style.marginRight = 40; panel.style.marginTop = 30;
            var field = new TextField { value = text, multiline = true, isReadOnly = true };
            field.style.height = 520;
            body.Add(field);
            body.Add(UiKit.SecondaryButton("Close", () => { _overlay.RemoveFromHierarchy(); _overlay = null; }));
            _overlay.Add(panel);
            Add(_overlay);
        }

        // ------------------------------------------------------------- helpers

        private void Changed()
        {
            if (_identityInfo != null) _identityInfo.text = IdentityInfo();
            _changed?.Invoke();
        }

        private static Label Readonly(string text)
        {
            var l = new Label(text);
            l.style.color = UiKit.Text;
            l.style.fontSize = 12;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private static Label SubHeader(string text)
        {
            var l = new Label(text);
            l.style.color = UiKit.Accent;
            l.style.fontSize = 11;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 8;
            l.style.marginBottom = 2;
            return l;
        }

        /// <summary>A labelled fact: muted key on the left, wrapping value on the right.</summary>
        private static VisualElement KV(string key, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 1;
            var k = new Label(key);
            k.style.width = 140;
            k.style.flexShrink = 0;
            k.style.fontSize = 11;
            k.style.color = UiKit.Muted;
            row.Add(k);
            var v = new Label(value);
            v.style.fontSize = 11;
            v.style.color = UiKit.Text;
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.flexGrow = 1;
            v.style.flexShrink = 1;
            row.Add(v);
            return row;
        }

        /// <summary>One function of the chain: name, what it does, how sure we are.</summary>
        private static VisualElement Fn(string signature, string definition, string confidence)
        {
            var box = new VisualElement();
            box.style.marginBottom = 4;
            box.style.paddingLeft = 4;
            box.style.borderLeftWidth = 2;
            box.style.borderLeftColor = UiKit.Border;
            var s = new Label(signature);
            s.style.fontSize = 11;
            s.style.color = UiKit.Text;
            s.style.unityFontStyleAndWeight = FontStyle.Bold;
            s.style.whiteSpace = WhiteSpace.Normal;
            box.Add(s);
            var d = new Label(definition);
            d.style.fontSize = 10;
            d.style.color = UiKit.Text;
            d.style.whiteSpace = WhiteSpace.Normal;
            box.Add(d);
            var c = new Label(confidence);
            c.style.fontSize = 9;
            c.style.color = UiKit.Muted;
            c.style.whiteSpace = WhiteSpace.Normal;
            box.Add(c);
            return box;
        }

        private static VisualElement Row(string label, VisualElement field, string hint) => UiKit.Row(label, field, hint, 130, 0);

        private VisualElement IntRow(string label, int value, Action<int> set, string hint)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Changed(); });
            return UiKit.Row(label, f, hint, 130, 80);
        }

        private VisualElement FloatRow(string label, float value, Action<float> set, string hint)
        {
            var f = new FloatField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Changed(); });
            return UiKit.Row(label, f, hint, 130, 80);
        }

        private VisualElement ToggleRow(string label, bool value, Action<bool> set, string hint)
        {
            var f = new Toggle { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Changed(); });
            return UiKit.Row(label, f, hint, 130, 30);
        }
    }
}
