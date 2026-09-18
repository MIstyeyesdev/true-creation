using System;
using System.Collections.Generic;
using System.Linq;
using PalItemgen.Export;
using PalItemgen.Lookup;
using PalItemgen.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// The fourth tab: the exact copy, original and new side by side.
    /// Left = the basic Monitoring Stand with the game's default settings,
    /// read-only. Right = this item, editable: name, model id, model. The
    /// blueprint, graphic, grid size, work area and model class on the right
    /// follow the chosen model. No prose: labels and values only. Every other
    /// row field of the stand is copied in the background by the package
    /// writer and is not shown here.
    /// </summary>
    public sealed class CopyCompareView : VisualElement
    {
        private readonly GameData _data;
        private readonly Func<ModProject> _project;
        private readonly Action _changed;

        /// <summary>Host-supplied table browser (Creation Engine style).</summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> ShowBrowser;

        private StationSpec _spec;

        /// <summary>
        /// Generator mode: the item is the project's Copy station (created on
        /// first use, off until a building is chosen), and the original may be
        /// any station, producer or storage building instead of a stand tier.
        /// </summary>
        private readonly bool _generator;

        public CopyCompareView(GameData data, Func<ModProject> project, Action changed, bool generator = false)
        {
            _data = data;
            _project = project;
            _changed = changed;
            _generator = generator;
            style.flexGrow = 1;
            Refresh();
        }

        /// <summary>The building the item copies: chosen on the left. Basic Monitoring Stand by default; nothing until chosen in Generator mode.</summary>
        private ProducerInfo Original
        {
            get
            {
                var id = _spec?.MirrorId ?? "";
                var b = string.IsNullOrEmpty(id) ? null : _data.Building(id);
                if (b != null) return b;
                return _generator ? null : _data.Building("BaseCampWorkHard");
            }
        }
        private ProducerInfo NewModel => _data.Building(_spec?.ReuseMapObjectId);

        private StationSpec GeneratorSpec()
        {
            var p = _project();
            var s = p.Stations.FirstOrDefault(x => x.Kind == StationKind.Copy);
            if (s == null)
            {
                s = StationSpec.CopyItem();
                p.Stations.Add(s);
            }
            return s;
        }

        public void Refresh()
        {
            Clear();
            _spec = _generator ? GeneratorSpec() : _project().Stations.FirstOrDefault(s => s.Kind == StationKind.Manager);
            if (_spec == null)
            {
                var missing = UiKit.Section("Exact copy", out var mb);
                mb.Add(UiKit.Note("No Production Manager station in the project. Add one on the Package & stations tab.", UiKit.Warning));
                Add(missing);
                return;
            }
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            Add(scroll);
            var columns = UiKit.Columns(UiKit.Column(), UiKit.Column());
            scroll.Add(columns);
            ((VisualElement)columns[0]).Add(BuildOriginal());
            ((VisualElement)columns[1]).Add(BuildNew());
            if (!_generator || Original != null) ((VisualElement)columns[1]).Add(BuildSize());
        }

        // ------------------------------------------------------------- original (default game settings, read-only)

        private VisualElement BuildOriginal()
        {
            if (!_generator && _spec != null && string.IsNullOrEmpty(_spec.MirrorMapObjectId)) _spec.MirrorMapObjectId = "BaseCampWorkHard";
            var o = Original;
            if (_generator && o == null)
            {
                var empty = UiKit.Section("Original: choose a building", out var emptyBody);
                emptyBody.Add(BuildOriginalPicker());
                emptyBody.Add(KV("Name", "(nothing chosen yet)", UiKit.Muted));
                return empty;
            }
            var isDefault = o == null || o.MapObjectId == "BaseCampWorkHard";
            var section = UiKit.Section(isDefault && !_generator ? "Original: Monitoring Stand (default)" : "Original: " + (o.Name ?? o.MapObjectId), out var body);
            body.Add(BuildOriginalPicker());
            body.Add(KV("Name", o?.Name ?? "Monitoring Stand"));
            body.Add(KV("Model id", o?.MapObjectId ?? "BaseCampWorkHard"));
            body.Add(KV("Model", o?.Bp ?? "BP_BuildObject_BaseCampWorkHard"));
            body.Add(KV("Model graphic", o?.GraphicLabel ?? "SM_SurveillanceTable"));
            body.Add(KV("Grid size", GridText(o)));
            body.Add(KV("Work area", o?.Workable != null ? o.Workable.Metres : "none"));
            body.Add(KV("Model class", o?.Model ?? "PalMapObjectBaseCampPassiveWorkHardModel"));
            // what the blueprint itself brings (not data rows): who works here, what it crafts, power
            body.Add(KV("Pal work", RowFieldCatalog.AssignmentSummary(o)));
            body.Add(KV("Crafts", o?.RankMax != null ? o.TypeSummary : "nothing (not a crafting bench)"));
            if (o?.WorkSpeed != null) body.Add(KV("Work speed", N(o.WorkSpeed.Value)));
            body.Add(KV("Needs power", o?.NeedsEnergyComponent == true ? "yes" : "no"));
            if (o?.RawItemProduct != null) body.Add(KV("Produces", ItemProductSummary(o)));
            if (o?.CropId != null) body.Add(KV("Grows", CropSummary(o)));
            body.Add(KV("Work spots", WorkSpotsText(o)));

            // The options the game does not show up front: the stand's own values, read-only.
            var more = Folded();
            more.Add(KV("Description", o?.Desc ?? ""));
            more.Add(KV("Build cost", CostText(o)));
            more.Add(KV("Build work", o?.Build != null ? N(o.Build.RequiredBuildWorkAmount) : "?"));
            more.Add(KV("HP", o?.Master != null ? N(o.Master.Hp) : "?"));
            more.Add(KV("Defense", o?.Master != null ? N(o.Master.Defense) : "?"));
            more.Add(KV("Material type", o?.Master != null ? $"{o.Master.MaterialType} / {o.Master.MaterialSubType}" : "?"));
            more.Add(KV("Tech level", o?.Tech != null ? N(o.Tech.LevelCap) : "?"));
            more.Add(KV("Tech cost", o?.Tech != null ? N(o.Tech.Cost) : "?"));
            more.Add(KV("Ancient tech", o?.Tech != null ? (o.Tech.IsBoss ? "yes" : "no") : "?"));
            more.Add(KV("Limit per base", o?.Build != null ? (o.Build.InstallMaxNumInBaseCamp == 0 ? "none" : N(o.Build.InstallMaxNumInBaseCamp)) : "?"));
            more.Add(KV("Palbox only", o?.Build != null ? (o.Build.InstallOnlyHubAround ? "yes" : "no") : "?"));
            more.Add(KV("Build category", o?.Build?.TypeUIDisplay ?? "?"));
            more.Add(KV("Icon", ShortIcon(o?.Icon)));
            var rows = new Foldout { text = "Every other row field", value = false };
            rows.style.marginTop = 4;
            RowTable? lastTable = null;
            foreach (var f in RowFieldCatalog.List(o))
            {
                if (lastTable != f.Table)
                {
                    lastTable = f.Table;
                    rows.Add(GroupHead(RowFieldCatalog.TableTitle(f.Table)));
                }
                rows.Add(KV(f.Key, f.DefaultText.Length == 0 ? "(empty)" : f.DefaultText, null, RowFieldLabelWidth, RowFieldCatalog.Describe(f.Table, f.Key)));
            }
            more.Add(rows);
            if (o?.HasPalWork == true)
            {
                // built when opened: a Palbox-sized building carries dozens of rows
                var work = new Foldout { text = $"Pal work rows ({o.RawAssignments.Count})", value = false };
                work.style.marginTop = 4;
                var builtWork = false;
                work.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue || builtWork) return;
                    builtWork = true;
                    var original = RowFieldCatalog.OriginalAssignmentRowsText(o);
                    for (var i = 0; i < original.Count; i++)
                    {
                        work.Add(GroupHead($"Row {i + 1}"));
                        foreach (var kv in original[i])
                            work.Add(KV(kv.Key, kv.Value.Length == 0 ? "(empty)" : kv.Value, null, RowFieldLabelWidth, RowFieldCatalog.DescribeAssignment(kv.Key)));
                    }
                });
                more.Add(work);
            }
            body.Add(more);
            return section;
        }

        private static Label GroupHead(string text)
        {
            var head = new Label(text);
            head.style.fontSize = 11;
            head.style.color = UiKit.Muted;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.marginTop = 4;
            return head;
        }

        /// <summary>Plantation output: the crop row the blueprint names (shared with the vanilla plantation, so read-only).</summary>
        private string CropSummary(ProducerInfo b)
        {
            var r = b?.RawFarmCrop;
            if (r == null) return b?.CropId != null ? $"crop '{b.CropId}' (no row found)" : "nothing";
            string T(string k) => RowFieldCatalog.Text(r[k]);
            var item = _data.Item(T("CropItemId"))?.Name ?? T("CropItemId");
            var seed = _data.Item(T("MaterialItem1_Id"))?.Name ?? T("MaterialItem1_Id");
            return $"{item} x{T("CropItemNum")} every {T("GrowupTime")} s; work seed {T("SeedingWorkAmount")} / water {T("WateringWorkAmount")} / harvest {T("HarvestWorkAmount")}; planting costs {seed} x{T("MaterialItem1_Num")}   (crop row '{b.CropId}', shared with the original)";
        }

        private static string WorkSpotsText(ProducerInfo b)
        {
            if (b == null) return "?";
            return b.WorkSpots > 0
                ? $"{b.WorkSpots} fixed spot(s) on the blueprint (WorkFacing); Pals beyond that queue"
                : "no fixed spots: Pals stand anywhere inside the work box";
        }

        private static string ItemProductSummary(ProducerInfo b)
        {
            var r = b?.RawItemProduct;
            if (r == null) return "nothing";
            return $"{RowFieldCatalog.Text(r["Product_Id"])}: {RowFieldCatalog.Text(r["RequiredWorkAmount"])} work per item, {RowFieldCatalog.Text(r["AutoWorkAmountBySec"])} auto work / s";
        }

        /// <summary>
        /// Right side of "Every other row field": each raw field of the original,
        /// prefilled with its value and editable. Text equal to the original's
        /// value is not stored, so the item follows the original until edited.
        /// </summary>
        private VisualElement BuildRowFields()
        {
            var rows = new Foldout { text = "Every other row field (filled from the original)", value = false };
            rows.style.marginTop = 4;
            var fields = RowFieldCatalog.List(Original);
            if (fields.Count == 0)
            {
                rows.Add(KV("rows", "no raw rows in the lookup data for this original", UiKit.Warning));
                return rows;
            }
            RowTable? last = null;
            foreach (var f in fields)
            {
                if (last != f.Table)
                {
                    last = f.Table;
                    var head = new Label(RowFieldCatalog.TableTitle(f.Table));
                    head.style.fontSize = 11;
                    head.style.color = UiKit.Muted;
                    head.style.unityFontStyleAndWeight = FontStyle.Bold;
                    head.style.marginTop = 4;
                    rows.Add(head);
                }
                var current = _spec.RowFields != null && _spec.RowFields.TryGetValue(f.Id, out var edited) ? edited : f.DefaultText;
                var tf = new TextField { value = current };
                var field = f;
                tf.RegisterValueChangedCallback(evt =>
                {
                    var text = (evt.newValue ?? "").Trim();
                    if (text == field.DefaultText) _spec.RowFields.Remove(field.Id);
                    else _spec.RowFields[field.Id] = text;
                    tf.style.color = _spec.RowFields.ContainsKey(field.Id) ? UiKit.Accent : UiKit.Text;
                    _changed?.Invoke();
                });
                tf.style.color = _spec.RowFields != null && _spec.RowFields.ContainsKey(f.Id) ? UiKit.Accent : UiKit.Text;
                rows.Add(Field(f.Key, tf, WithNote(f.DefaultText.Length == 0 ? "(empty)" : f.DefaultText, RowFieldCatalog.Describe(f.Table, f.Key)), RowFieldLabelWidth));
            }
            var reset = UiKit.SmallButton("Reset every field to the original", () =>
            {
                _spec.RowFields.Clear();
                Refresh();
                _changed?.Invoke();
            });
            reset.style.marginTop = 4;
            reset.style.alignSelf = Align.FlexStart;
            rows.Add(reset);
            return rows;
        }

        /// <summary>
        /// Which stand the item copies: the three vanilla Monitoring Stand tiers
        /// (dropdown, or the table browser). Choosing one loads its values into
        /// the new item (cost, work, HP, defense, material, tech, model, icon),
        /// which stays editable on the right.
        /// </summary>
        private VisualElement BuildOriginalPicker()
        {
            if (_generator)
            {
                // every station, producer and storage building; typing filters, the caret lists all
                var sources = _data.GeneratorSources().ToList();
                string SourceLabel(ProducerInfo b) => $"{(string.IsNullOrEmpty(b.Name) ? b.MapObjectId : b.Name)}  [{b.MapObjectId}]  {b.Build?.TypeA} / {b.Build?.TypeUIDisplay}";
                var picker = new UiKit.SearchPicker(() => sources.Select(b => (b.MapObjectId, SourceLabel(b))), null, 400);
                var chosen = sources.FirstOrDefault(b => b.MapObjectId == _spec.MirrorId);
                picker.SetDisplay(chosen != null ? SourceLabel(chosen) : (_spec.MirrorId ?? ""));
                void PickSource(string id)
                {
                    var b = _data.Building(id);
                    if (b != null) UseOriginal(b);
                }
                picker.OnSelected += PickSource;
                picker.AddTrailing(UiKit.BrowseButton(() =>
                {
                    var (cols, rows) = BrowserDatasets.GeneratorSources(_data);
                    ShowBrowser?.Invoke("Stations, producers and storage", cols, rows, PickSource);
                }));
                return Field("Copy of", picker);
            }
            var tiers = _data.StandTiers().ToList();
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            if (tiers.Count == 0)
            {
                line.Add(new Label("no Monitoring Stand rows in the lookup data") { style = { color = UiKit.Danger, fontSize = 12 } });
                return Field("Copy of", line);
            }
            string TierLabel(ProducerInfo t) => $"{(string.IsNullOrEmpty(t.Name) ? t.MapObjectId : t.Name)}  [{t.MapObjectId}]  level {t.Tech?.LevelCap ?? 0}";
            var labels = tiers.Select(TierLabel).ToList();
            var current = Math.Max(0, tiers.FindIndex(t => t.MapObjectId == _spec.MirrorMapObjectId));
            var dropdown = new DropdownField(labels, current);
            dropdown.style.flexGrow = 1;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var idx = labels.IndexOf(evt.newValue);
                if (idx >= 0) UseOriginal(tiers[idx]);
            });
            line.Add(dropdown);
            line.Add(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.StandTiers(_data);
                ShowBrowser?.Invoke("Monitoring Stand tiers", cols, rows, v =>
                {
                    var t = tiers.FirstOrDefault(x => x.MapObjectId == v);
                    if (t != null) UseOriginal(t);
                });
            }));
            return Field("Copy of", line);
        }

        /// <summary>Makes `tier` the original and copies its values into the new item.</summary>
        private static string AutoId(string mapObjectId) => ModPackager.SafeId((mapObjectId ?? "") + "_Copy");
        private static string AutoName(ProducerInfo b) => (string.IsNullOrEmpty(b?.Name) ? b?.MapObjectId : b.Name) + " Copy";

        private void UseOriginal(ProducerInfo tier)
        {
            if (tier == null || _spec == null) return;
            if (_generator)
            {
                // name and id follow the original until edited by hand
                var prev = string.IsNullOrEmpty(_spec.MirrorId) ? null : _data.Building(_spec.MirrorId);
                if (string.IsNullOrEmpty(_spec.Id) || (prev != null && _spec.Id == AutoId(prev.MapObjectId))) _spec.Id = AutoId(tier.MapObjectId);
                if (string.IsNullOrEmpty(_spec.Name) || (prev != null && _spec.Name == AutoName(prev))) _spec.Name = AutoName(tier);
                _spec.Enabled = true;
            }
            _spec.MirrorMapObjectId = tier.MapObjectId;
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
            if (!string.IsNullOrEmpty(tier.Desc)) _spec.Description = tier.Desc;
            _spec.RowFields.Clear();
            _spec.AssignmentRows = null;   // the new original's Pal work rows apply
            _spec.ReuseMapObjectId = tier.MapObjectId;
            if (_data.Icons.ContainsKey(tier.MapObjectId) || _generator) _spec.IconMapObjectId = tier.MapObjectId;
            Refresh();
            _changed?.Invoke();
        }

        // ------------------------------------------------------------- new (this item, editable)

        private VisualElement BuildNew()
        {
            var section = UiKit.Section("New: this item", out var body);

            if (_generator)
            {
                var include = new Toggle { value = _spec.Enabled };
                var includeHint = new Label(_spec.Enabled ? "written on Generate" : "kept in the project, not written");
                includeHint.style.fontSize = 10;
                includeHint.style.color = UiKit.Muted;
                includeHint.style.marginLeft = 8;
                include.RegisterValueChangedCallback(evt =>
                {
                    _spec.Enabled = evt.newValue;
                    includeHint.text = evt.newValue ? "written on Generate" : "kept in the project, not written";
                    _changed?.Invoke();
                });
                var includeRow = Field("Include in package", include);
                includeRow.Add(includeHint);
                body.Add(includeRow);
            }

            var name = new TextField { value = _spec.Name };
            name.RegisterValueChangedCallback(evt => { _spec.Name = evt.newValue; _changed?.Invoke(); });
            body.Add(Field("Name", name));

            var id = new TextField { value = _spec.Id };
            id.RegisterValueChangedCallback(evt =>
            {
                // a row key: letters, digits, underscores (a typed space is dropped)
                var safe = ModPackager.SafeId(evt.newValue);
                if (safe != evt.newValue) id.SetValueWithoutNotify(safe);
                _spec.Id = safe;
                _changed?.Invoke();
            });
            body.Add(Field("Model id", id));

            // the values that follow the chosen model (and the item's two roles) live here
            var live = new VisualElement();
            if (_spec.Kind == StationKind.Copy)
            {
                // a Generator copy is a look, not a role, unless this is on: then it is
                // written to pm_config.lua Stations (kind manager) like the Exact copy item
                var asManager = new Toggle { value = _spec.ActsAsManager };
                asManager.RegisterValueChangedCallback(evt =>
                {
                    _spec.ActsAsManager = evt.newValue;
                    RefreshLive(live);
                    _changed?.Invoke();
                });
                body.Add(Field("Production manager", asManager, "on = this copy is the chain station too: listed in pm_config.lua Stations, F opens the board, automation runs where it stands. Needs a Monitoring Stand copy.", 110, ""));
            }

            // Model: a scrolling list (the caret opens all of it, typing filters it) plus the table browser.
            var candidates = _data.ModelCandidates().OrderBy(c => string.IsNullOrEmpty(c.Name) ? "~" + c.MapObjectId : c.Name).ToList();
            string LabelOf(ProducerInfo c) => $"{(string.IsNullOrEmpty(c.Name) ? c.MapObjectId : c.Name)}  [{c.MapObjectId}]";
            var picker = new UiKit.SearchPicker(() => candidates.Select(c => (c.MapObjectId, LabelOf(c))), null, 400);
            var currentModel = candidates.FirstOrDefault(c => c.MapObjectId == _spec.ReuseMapObjectId);
            picker.SetDisplay(currentModel != null ? LabelOf(currentModel) : _spec.ReuseMapObjectId);
            void Pick(string mapObjectId)
            {
                _spec.ReuseMapObjectId = mapObjectId;
                var chosen = candidates.FirstOrDefault(c => c.MapObjectId == mapObjectId);
                picker.SetDisplay(chosen != null ? LabelOf(chosen) : mapObjectId);
                Refresh();   // the size section reads the model too; a live-only refresh left it on the old building
                _changed?.Invoke();
            }
            picker.OnSelected += Pick;
            picker.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Models(_data);
                ShowBrowser?.Invoke("Models", cols, rows, Pick);
            }));
            picker.AddTrailing(UiKit.SmallButton("default", () =>
            {
                // back to the original's own model, no size override
                var orig = Original;
                if (orig == null) return;
                _spec.ReuseMapObjectId = orig.MapObjectId;
                _spec.OverrideSize = false;   // the size numbers stay; 'Reload the model's values' replaces them on purpose
                Refresh();
                _changed?.Invoke();
            }));
            body.Add(Field("Model", picker, "EXPERIMENTAL: another model is applied at run time per placed item; untested in game. 'default' = the original's own model", 110, ""));

            body.Add(live);
            RefreshLive(live);

            // The same options, editable; each hint names the stand's value.
            var o = Original;
            var more = Folded();
            var desc = new TextField { value = _spec.Description ?? "", multiline = true };
            desc.style.height = 44;
            desc.RegisterValueChangedCallback(evt => { _spec.Description = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Description", desc));
            more.Add(BuildMaterials());
            more.Add(FloatField("Build work", _spec.BuildWorkAmount, v => _spec.BuildWorkAmount = v, o?.Build != null ? N(o.Build.RequiredBuildWorkAmount) : null));
            more.Add(IntField("HP", _spec.Hp, v => _spec.Hp = v, o?.Master != null ? N(o.Master.Hp) : null));
            more.Add(IntField("Defense", _spec.Defense, v => _spec.Defense = v, o?.Master != null ? N(o.Master.Defense) : null));
            var mats = _data.Enums.MaterialType.Count > 0 ? _data.Enums.MaterialType : new List<string> { _spec.MaterialType };
            var mt = new DropdownField(mats, Math.Max(0, mats.IndexOf(_spec.MaterialType)));
            mt.RegisterValueChangedCallback(evt => { _spec.MaterialType = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Material type", mt, o?.Master?.MaterialType));
            var subs = _data.Enums.MaterialSubType.Count > 0 ? _data.Enums.MaterialSubType : new List<string> { _spec.MaterialSubType };
            var st = new DropdownField(subs, Math.Max(0, subs.IndexOf(_spec.MaterialSubType)));
            st.RegisterValueChangedCallback(evt => { _spec.MaterialSubType = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Material sub type", st, o?.Master?.MaterialSubType));
            more.Add(IntField("Tech level", _spec.TechLevel, v => _spec.TechLevel = v, o?.Tech != null ? N(o.Tech.LevelCap) + "  (1 = no gate for testing)" : null));
            more.Add(IntField("Tech cost", _spec.TechCost, v => _spec.TechCost = v, (o?.Tech != null ? N(o.Tech.Cost) : "?") + "  (0 is unverified: no vanilla technology costs 0; use 1 if the unlock never shows in the tree)"));
            var ancient = new Toggle { value = _spec.AncientTech };
            ancient.RegisterValueChangedCallback(evt => { _spec.AncientTech = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Ancient tech", ancient, o?.Tech != null ? (o.Tech.IsBoss ? "yes" : "no") : null));
            more.Add(IntField("Limit per base", _spec.LimitPerBase, v => _spec.LimitPerBase = v,
                o?.Build != null ? (o.Build.InstallMaxNumInBaseCamp == 0 ? "none (0)" : N(o.Build.InstallMaxNumInBaseCamp)) : null));
            var cats = _data.Enums.TypeUIDisplay.Count > 0 ? _data.Enums.TypeUIDisplay : new List<string> { _spec.TypeUIDisplay };
            var cat = new DropdownField(cats, Math.Max(0, cats.IndexOf(_spec.TypeUIDisplay)));
            cat.RegisterValueChangedCallback(evt => { _spec.TypeUIDisplay = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Build category", cat, o?.Build?.TypeUIDisplay));
            var iconPick = new UiKit.SearchPicker(() => _data.Icons.OrderBy(k => k.Value.Name).Select(k => (k.Key, string.IsNullOrEmpty(k.Value.Name) ? k.Key : k.Value.Name + "  (" + k.Key + ")")), null, 400)
            { Value = _spec.IconMapObjectId };
            iconPick.OnSelected += v => { _spec.IconMapObjectId = v; _changed?.Invoke(); };
            iconPick.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Icons(_data);
                ShowBrowser?.Invoke("Build icons", cols, rows, v => { _spec.IconMapObjectId = v; iconPick.SetValueSilently(v); _changed?.Invoke(); });
            }));
            more.Add(Field("Icon", iconPick, o?.MapObjectId));
            var png = new TextField { value = _spec.CustomIconPng ?? "" };
            png.RegisterValueChangedCallback(evt => { _spec.CustomIconPng = evt.newValue; _changed?.Invoke(); });
            more.Add(Field("Custom icon PNG", png, "none"));
            more.Add(BuildRowFields());
            if (_generator || Original?.HasPalWork == true) more.Add(BuildAssignmentRows());
            body.Add(more);
            return section;
        }

        /// <summary>
        /// The item's Pal work rows (DT_MapObjectAssignData): the original's rows
        /// prefilled and editable, rows can be added or removed; built when the
        /// fold is opened. Until something is edited the item copies the
        /// original's rows as they are.
        /// </summary>
        private VisualElement BuildAssignmentRows()
        {
            var o = Original;
            var fold = new Foldout { text = "Pal work rows (who can work here)", value = false };
            fold.style.marginTop = 4;
            var built = false;
            void BuildBody()
            {
                fold.contentContainer.Clear();
                var follow = RowFieldCatalog.AssignmentsFollowOriginal(_spec);
                var rows = RowFieldCatalog.AssignmentRowsText(_spec, o);
                var original = RowFieldCatalog.OriginalAssignmentRowsText(o);
                fold.Add(KV("State", follow ? "copied from the original" : "this item's own rows (edited)", follow ? UiKit.Muted : UiKit.Accent, RowFieldLabelWidth));
                for (var i = 0; i < rows.Count; i++)
                {
                    var index = i;
                    var row = rows[i];
                    var head = new VisualElement();
                    head.style.flexDirection = FlexDirection.Row;
                    head.style.alignItems = Align.Center;
                    head.style.marginTop = 6;
                    var title = GroupHead($"Row {i + 1}");
                    title.style.marginTop = 0;
                    title.style.marginRight = 8;
                    head.Add(title);
                    head.Add(UiKit.SmallButton("remove row", () =>
                    {
                        RowFieldCatalog.MaterialiseAssignments(_spec, o);
                        if (index < _spec.AssignmentRows.Count) _spec.AssignmentRows.RemoveAt(index);
                        BuildBody();
                        _changed?.Invoke();
                    }));
                    fold.Add(head);
                    var keys = RowFieldCatalog.AssignmentKeys.Concat(row.Keys.Where(k => Array.IndexOf(RowFieldCatalog.AssignmentKeys, k) < 0)).ToList();
                    foreach (var key in keys)
                    {
                        if (!row.ContainsKey(key)) continue;
                        var k = key;
                        var orig = index < original.Count && original[index].TryGetValue(k, out var ov) ? ov : null;
                        var tf = new TextField { value = row[k] };
                        tf.style.color = orig != null && orig == row[k] ? UiKit.Text : UiKit.Accent;
                        tf.RegisterValueChangedCallback(evt =>
                        {
                            var text = (evt.newValue ?? "").Trim();
                            RowFieldCatalog.MaterialiseAssignments(_spec, o);
                            if (index < _spec.AssignmentRows.Count) _spec.AssignmentRows[index][k] = text;
                            tf.style.color = orig != null && orig == text ? UiKit.Text : UiKit.Accent;
                            _changed?.Invoke();
                        });
                        fold.Add(Field(k, tf, WithNote(orig == null ? "(new row)" : (orig.Length == 0 ? "(empty)" : orig), RowFieldCatalog.DescribeAssignment(k)), RowFieldLabelWidth));
                    }
                }
                if (rows.Count == 0) fold.Add(KV("rows", "none: no Pal works here", UiKit.Muted, RowFieldLabelWidth));
                var buttons = new VisualElement();
                buttons.style.flexDirection = FlexDirection.Row;
                buttons.style.marginTop = 6;
                buttons.Add(UiKit.SmallButton("+ row", () =>
                {
                    RowFieldCatalog.MaterialiseAssignments(_spec, o);
                    _spec.AssignmentRows.Add(RowFieldCatalog.NewAssignmentRowText(_spec, o));
                    BuildBody();
                    _changed?.Invoke();
                }));
                var reset = UiKit.SmallButton("Reset rows to the original", () =>
                {
                    _spec.AssignmentRows = null;
                    BuildBody();
                    _changed?.Invoke();
                });
                reset.style.marginLeft = 6;
                buttons.Add(reset);
                fold.Add(buttons);
            }
            fold.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue || built) return;
                built = true;
                BuildBody();
            });
            return fold;
        }

        /// <summary>Build cost rows: item picker, count, remove; plus an add button.</summary>
        private VisualElement BuildMaterials()
        {
            var box = new VisualElement();
            var list = new VisualElement();
            void Rebuild()
            {
                list.Clear();
                for (var i = 0; i < _spec.Materials.Count; i++)
                {
                    var m = _spec.Materials[i];
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 2;
                    var cap = new Label(i == 0 ? "Build cost" : "");
                    cap.style.width = 110;
                    cap.style.flexShrink = 0;
                    cap.style.fontSize = 12;
                    cap.style.color = UiKit.Muted;
                    row.Add(cap);
                    var pick = new UiKit.SearchPicker(() => _data.Items.Select(it => (it.Id, it.Display)), null, 400) { Value = m.ItemId };
                    pick.style.flexGrow = 1;
                    pick.OnSelected += v => { m.ItemId = v; _changed?.Invoke(); };
                    pick.AddTrailing(UiKit.BrowseButton(() =>
                    {
                        var (cols, rows) = BrowserDatasets.Items(_data, false);
                        ShowBrowser?.Invoke("Items - build material", cols, rows, v => { m.ItemId = v; pick.SetValueSilently(v); _changed?.Invoke(); });
                    }));
                    row.Add(pick);
                    var count = new IntegerField { value = m.Count };
                    count.style.width = 60;
                    count.style.marginLeft = 4;
                    count.RegisterValueChangedCallback(evt => { m.Count = evt.newValue; _changed?.Invoke(); });
                    row.Add(count);
                    var remove = m;
                    row.Add(UiKit.SmallButton("x", () => { _spec.Materials.Remove(remove); Rebuild(); _changed?.Invoke(); }));
                    list.Add(row);
                }
                if (_spec.Materials.Count == 0) list.Add(KV("Build cost", "none (add one)", UiKit.Warning));
            }
            Rebuild();
            box.Add(list);
            var add = UiKit.SmallButton("+ material", () =>
            {
                if (_spec.Materials.Count >= 4) return;
                _spec.Materials.Add(new MaterialCost { ItemId = "Wood", Count = 10 });
                Rebuild();
                _changed?.Invoke();
            });
            add.style.marginLeft = 110;
            add.style.alignSelf = Align.FlexStart;
            box.Add(add);
            var hint = new Label("original: " + CostText(Original));
            hint.style.marginLeft = 110;
            hint.style.fontSize = 10;
            hint.style.color = UiKit.Muted;
            box.Add(hint);
            return box;
        }

        /// <summary>The values that follow the chosen model; highlighted when they differ from the original.</summary>
        private void RefreshLive(VisualElement live)
        {
            live.Clear();
            var m = NewModel;
            var o = Original;
            if (_generator && o == null)
            {
                live.Add(KV("Model graphic", "choose the original on the left first", UiKit.Muted));
                return;
            }
            if (m == null)
            {
                live.Add(KV("Model graphic", "unknown model", UiKit.Danger));
                return;
            }
            // what the item IS in the game: its two roles, read from the blueprint the row
            // names and from what pm_config.lua will say about it
            var hostBp = _data.Building(_spec.BlueprintSource(_data)) ?? o;
            var standClass = (hostBp?.Model ?? "") == "PalMapObjectBaseCampPassiveWorkHardModel";
            live.Add(KV("Monitoring Stand", standClass
                ? $"yes: the row names {hostBp.Bp}; its model class gives F (work mode), V (work preferences), C (Assignment Board) and the base-wide work mode"
                : $"no: a copy of {hostBp?.Name ?? hostBp?.MapObjectId ?? "?"} ({(string.IsNullOrEmpty(hostBp?.Model) ? "no model class" : hostBp.Model)})",
                standClass ? UiKit.Success : UiKit.Text));
            live.Add(KV("Production manager", ManagerRoleText(standClass), _spec.IsManagerRole ? UiKit.Success : UiKit.Warning));
            live.Add(KV("Written rows", WrittenRowsText(hostBp, o)));
            live.Add(KV("Model graphic", m.GraphicLabel, (m.Mesh ?? "") == (o?.Mesh ?? "") ? UiKit.Text : UiKit.Accent));
            if (_spec.OverrideSize)
            {
                live.Add(KV("Grid size", Metres(_spec.OverlapX, _spec.OverlapY, _spec.OverlapZ) + "   (override, see Model size)", UiKit.Accent));
                live.Add(KV("Work area", (_spec.WorkX > 0 ? Metres(_spec.WorkX, _spec.WorkY, _spec.WorkZ) : "none") + "   (override)", UiKit.Accent));
                if (Math.Abs(_spec.MeshScale - 1f) > 0.0001f) live.Add(KV("Mesh scale", N(_spec.MeshScale) + "x   (override)", UiKit.Accent));
            }
            else
            {
                live.Add(KV("Grid size", GridText(m), GridText(m) == GridText(o) ? UiKit.Text : UiKit.Accent));
                live.Add(KV("Work area", m.Workable != null ? m.Workable.Metres : "none", (m.Workable?.Metres ?? "") == (o?.Workable?.Metres ?? "") ? UiKit.Text : UiKit.Accent));
            }
            var sameClass = (m.Model ?? "") == (o?.Model ?? "");
            live.Add(KV("Model class", sameClass ? (m.Model ?? "none") : (o?.Model ?? "stand") + "  (kept)", UiKit.Text));
            if (!sameClass && m.Swappable) live.Add(KV("Applied as", "graphic + grid swap on the stand's blueprint: all stand functions stay", UiKit.Accent));
            if (!sameClass && !m.Swappable) live.Add(KV("Applied as", "cannot: this is an animated model (skeletal mesh); the stand keeps its own look. Pick a static model.", UiKit.Danger));
            // what the blueprint the row names brings along (the original's, kept through a swap)
            var host = _data.Building(_spec.BlueprintSource(_data)) ?? o;
            var edited = !RowFieldCatalog.AssignmentsFollowOriginal(_spec);
            var standNote = standClass && host?.HasPalWork != true ? "  (correct for a stand: nobody works AT it; its menu pins Pals to other buildings)" : "";
            live.Add(KV("Pal work", RowFieldCatalog.AssignmentSummary(host) + (edited ? "  (rows edited below)" : standNote), edited ? UiKit.Accent : UiKit.Text));
            live.Add(KV("Crafts", host?.RankMax != null ? host.TypeSummary : (standClass ? "nothing (correct for a stand)" : "nothing (not a crafting bench)")));
            if (host?.WorkSpeed != null) live.Add(KV("Work speed", N(host.WorkSpeed.Value)));
            live.Add(KV("Needs power", host?.NeedsEnergyComponent == true ? "yes" : "no"));
            if (o?.RawItemProduct != null) live.Add(KV("Produces", ItemProductSummary(o) + "  (item product row, editable below)"));
            if (host?.CropId != null) live.Add(KV("Grows", CropSummary(host)));
            live.Add(KV("Work spots", WorkSpotsText(host)));
        }

        /// <summary>The production-manager role line: what pm_config.lua will say and what F / C / the menu key do on it.</summary>
        private string ManagerRoleText(bool standClass)
        {
            var p = _project();
            if (!_spec.IsManagerRole)
                return _spec.Kind == StationKind.Copy
                    ? "NO: written as a plain copy (a look, not a role): not in pm_config.lua Stations, F opens the stand's own screen, the mod idles in a base that has only this item. Switch 'Production manager' on above to change that."
                    : "NO";
            var tab = p.Chain?.InteractTab ?? "chain";
            var fOpens = !p.Automation.OpenMenuOnInteract ? "the stand's own work-mode screen"
                : tab == "board" ? "the game's Assignment Board, producers only"
                : tab == "targets" ? "our targets list (keep items stocked)"
                : tab == "prod" ? "our Production tab"
                : tab == "monitor" ? "our Monitoring tab" : "our Chain screen";
            var def = p.Chain?.DefaultTab ?? "chain";
            var keyOpens = def == "board" ? "the Assignment Board for the station in your base" : def == "targets" ? "our targets list (keep items stocked)" : def == "prod" ? "our Production tab" : def == "monitor" ? "our Monitoring tab" : "our Chain screen";
            var where = p.Automation.RequireManagerStation ? "automation runs in a base where it stands" : "automation runs in every base ('Require this station' is off)";
            var ct = p.Chain?.CTab ?? "targets";
            var cOpens = ct == "vanilla" ? "the vanilla Assignment Board" : ct == "targets" ? "our targets list (keep items stocked)" : ct == "prod" ? "our Production tab" : ct == "monitor" ? "our Monitoring tab" : "our Chain screen";
            var text = $"yes: written to pm_config.lua Stations as kind manager, found in a base by id and model class. F opens {fOpens}; C opens {cOpens}; {p.Automation.MenuKey ?? "F7"} opens {keyOpens}; {where}";
            if (!standClass) text += ". WARNING: not a Monitoring Stand copy, so F cannot open the Assignment Board on it";
            return text;
        }

        /// <summary>How much of the original's data the package writes under the new id.</summary>
        private string WrittenRowsText(ProducerInfo host, ProducerInfo o)
        {
            var src = o ?? host;
            if (src == null) return "master, build, technology, icon and text rows";
            static int Count(JObject raw, params string[] skip) => raw == null ? 0 : raw.Properties().Count(p => !skip.Contains(p.Name));
            var master = Count(src.RawMaster, "Editor_RowNameHash");
            var build = Count(src.RawBuild, "Editor_RowNameHash", "MapObjectId");
            var tech = Count(src.RawTech, "Editor_RowNameHash");
            var rows = RowFieldCatalog.AssignmentRowsText(_spec, src).Count;
            var original = src.RawAssignments?.Count ?? 0;
            return $"master {master} fields, build {build} (+ MapObjectId from the key), technology {tech}, the icon row, name / description / technology texts, Pal work rows {rows} (original {original})";
        }

        // ------------------------------------------------------------- model size (mesh scale + boxes)

        private static readonly (float Value, string Label)[] ScaleReferences =
        {
            (0.5f, "0.5 - half size"),
            (0.75f, "0.75 - three quarters"),
            (1.0f, "1.0 - unchanged (the model's own size)"),
            (1.25f, "1.25 - a quarter bigger"),
            (1.5f, "1.5 - half again"),
            (2.0f, "2.0 - double"),
            (3.0f, "3.0 - triple"),
        };

        private static string Metres(float x, float y, float z) => new BoxHalf { X = x, Y = y, Z = z }.Metres;

        private void LoadSizeFromModel(ProducerInfo model)
        {
            _spec.MeshScale = 1f;
            _spec.OverlapX = model?.Overlap?.X ?? 0; _spec.OverlapY = model?.Overlap?.Y ?? 0; _spec.OverlapZ = model?.Overlap?.Z ?? 0;
            _spec.WorkX = model?.Workable?.X ?? 0; _spec.WorkY = model?.Workable?.Y ?? 0; _spec.WorkZ = model?.Workable?.Z ?? 0;
            _spec.BodyX = model?.VirtualMesh?.X ?? 0; _spec.BodyY = model?.VirtualMesh?.Y ?? 0; _spec.BodyZ = model?.VirtualMesh?.Z ?? 0;
        }

        /// <summary>
        /// Model size, as in the Pal Creation Engine: the visible size is a mesh
        /// scale, and the two boxes (placement grid, work area) have to move
        /// with it. Off = the chosen model's own values. On = applied to every
        /// placed copy at run time; the vanilla building keeps its own size.
        /// </summary>
        private VisualElement BuildSize()
        {
            var model = NewModel ?? Original;
            var section = UiKit.Section("Model size  -  SUPER EXPERIMENTAL, testing to come", out var body);
            var toggle = new Toggle { value = _spec.OverrideSize };
            toggle.RegisterValueChangedCallback(evt =>
            {
                _spec.OverrideSize = evt.newValue;
                if (evt.newValue && _spec.OverlapX <= 0 && _spec.OverlapY <= 0 && _spec.OverlapZ <= 0) LoadSizeFromModel(model);
                Refresh();
                _changed?.Invoke();
            });
            body.Add(Field("Override these values", toggle, "EXPERIMENTAL: off = mirror the chosen model exactly; on = applied at run time, untested in game", 160, ""));
            var loaded = new Label(model == null
                ? "no model chosen"
                : (_spec.OverrideSize ? "Override ON: the fields below are what the mod writes (typed, or loaded earlier; 'Reload the model's values' re-reads them). " : "")
                  + $"{model.MapObjectId}: mesh scale 1, placement box {(model.Overlap != null ? $"{N(model.Overlap.X)} / {N(model.Overlap.Y)} / {N(model.Overlap.Z)}" : "none")}, work box {(model.Workable != null ? $"{N(model.Workable.X)} / {N(model.Workable.Y)} / {N(model.Workable.Z)}" : "none")}, body box {(model.VirtualMesh != null ? $"{N(model.VirtualMesh.X)} / {N(model.VirtualMesh.Y)} / {N(model.VirtualMesh.Z)}" : "none")} (cm half extents); {WorkSpotsText(model)}.");
            loaded.style.fontSize = 11;
            loaded.style.color = UiKit.Accent;
            loaded.style.whiteSpace = WhiteSpace.Normal;
            loaded.style.marginBottom = 4;
            body.Add(loaded);

            var fields = new VisualElement();
            body.Add(fields);
            var on = _spec.OverrideSize;
            var result = new VisualElement();

            void ShowResult()
            {
                result.Clear();
                var sx = on ? _spec.OverlapX : (model?.Overlap?.X ?? 0);
                var sy = on ? _spec.OverlapY : (model?.Overlap?.Y ?? 0);
                var sz = on ? _spec.OverlapZ : (model?.Overlap?.Z ?? 0);
                var wx = on ? _spec.WorkX : (model?.Workable?.X ?? 0);
                var wy = on ? _spec.WorkY : (model?.Workable?.Y ?? 0);
                var wz = on ? _spec.WorkZ : (model?.Workable?.Z ?? 0);
                result.Add(KV("Grid size", sx > 0 ? Metres(sx, sy, sz) : "none", on ? UiKit.Accent : UiKit.Text, 160));
                result.Add(KV("Work area", wx > 0 ? Metres(wx, wy, wz) : "none", on ? UiKit.Accent : UiKit.Text, 160));
                result.Add(KV("Mesh scale", N(on ? _spec.MeshScale : 1f) + "x", on ? UiKit.Accent : UiKit.Text, 160));
            }

            var scale = new UnityEngine.UIElements.FloatField { value = on ? _spec.MeshScale : 1f };
            scale.style.width = 80;
            scale.style.flexGrow = 0;
            scale.RegisterValueChangedCallback(evt => { _spec.MeshScale = Math.Max(0.05f, evt.newValue); ShowResult(); _changed?.Invoke(); });
            fields.Add(Field("Mesh scale", scale, "the model's mesh components are scaled by this (1 = as shipped)", 160, ""));

            VisualElement Triple(string key, float x, float y, float z, Action<float> sx, Action<float> sy, Action<float> sz, string hint)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                foreach (var (v, set) in new[] { (x, sx), (y, sy), (z, sz) })
                {
                    var f = new UnityEngine.UIElements.FloatField { value = v };
                    f.style.width = 70;
                    f.style.marginRight = 4;
                    var setter = set;
                    f.RegisterValueChangedCallback(evt => { setter(Math.Max(0f, evt.newValue)); ShowResult(); _changed?.Invoke(); });
                    row.Add(f);
                }
                return Field(key, row, hint, 160, "");
            }
            fields.Add(Triple("Placement box (cm)", on ? _spec.OverlapX : (model?.Overlap?.X ?? 0), on ? _spec.OverlapY : (model?.Overlap?.Y ?? 0), on ? _spec.OverlapZ : (model?.Overlap?.Z ?? 0),
                v => _spec.OverlapX = v, v => _spec.OverlapY = v, v => _spec.OverlapZ = v, "half extents X / Y / Z: the grid size, doubled, in metres"));
            fields.Add(Triple("Work box (cm)", on ? _spec.WorkX : (model?.Workable?.X ?? 0), on ? _spec.WorkY : (model?.Workable?.Y ?? 0), on ? _spec.WorkZ : (model?.Workable?.Z ?? 0),
                v => _spec.WorkX = v, v => _spec.WorkY = v, v => _spec.WorkZ = v, "half extents X / Y / Z: where Pals stand to work"));
            fields.Add(Triple("Body box (cm)", on ? _spec.BodyX : (model?.VirtualMesh?.X ?? 0), on ? _spec.BodyY : (model?.VirtualMesh?.Y ?? 0), on ? _spec.BodyZ : (model?.VirtualMesh?.Z ?? 0),
                v => _spec.BodyX = v, v => _spec.BodyY = v, v => _spec.BodyZ = v, "half extents X / Y / Z: what players and Pals bump into (none on some models)"));

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.alignItems = Align.Center;
            buttons.style.marginLeft = 160;
            buttons.style.marginBottom = 4;
            buttons.Add(UiKit.SmallButton("Scale collision to match", () =>
            {
                // the model's own boxes times the mesh scale
                var k = Math.Max(0.05f, _spec.MeshScale);
                _spec.OverlapX = (model?.Overlap?.X ?? 0) * k; _spec.OverlapY = (model?.Overlap?.Y ?? 0) * k; _spec.OverlapZ = (model?.Overlap?.Z ?? 0) * k;
                _spec.WorkX = (model?.Workable?.X ?? 0) * k; _spec.WorkY = (model?.Workable?.Y ?? 0) * k; _spec.WorkZ = (model?.Workable?.Z ?? 0) * k;
                _spec.BodyX = (model?.VirtualMesh?.X ?? 0) * k; _spec.BodyY = (model?.VirtualMesh?.Y ?? 0) * k; _spec.BodyZ = (model?.VirtualMesh?.Z ?? 0) * k;
                Refresh();
                _changed?.Invoke();
            }));
            var reload = UiKit.SmallButton("Reload the model's values", () => { LoadSizeFromModel(model); Refresh(); _changed?.Invoke(); });
            reload.style.marginLeft = 6;
            buttons.Add(reload);
            fields.Add(buttons);

            var labels = ScaleReferences.Select(r => r.Label).ToList();
            var current = 2;
            for (var i = 0; i < ScaleReferences.Length; i++)
                if (Math.Abs(ScaleReferences[i].Value - (on ? _spec.MeshScale : 1f)) < 0.001f) current = i;
            var reference = new DropdownField(labels, current);
            reference.RegisterValueChangedCallback(evt =>
            {
                var idx = labels.IndexOf(evt.newValue);
                if (idx < 0) return;
                _spec.MeshScale = ScaleReferences[idx].Value;
                scale.SetValueWithoutNotify(_spec.MeshScale);
                ShowResult();
                _changed?.Invoke();
            });
            fields.Add(Field("Reference", reference, "sets the mesh scale; then 'Scale collision to match'", 160, ""));
            fields.SetEnabled(on);

            body.Add(result);
            ShowResult();
            return section;
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Raw field names (bInstallableNoObstacleFromCamera...) need a wider label column.</summary>
        private const float RowFieldLabelWidth = 240;

        private static VisualElement Folded()
        {
            var f = new Foldout { text = "More options (all of it is written to the package)", value = false };
            f.style.marginTop = 6;
            return f;
        }

        private static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static string N(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private string CostText(ProducerInfo b) =>
            b?.Build == null ? "?" : string.Join(", ", b.Build.Materials.Select(m => $"{m.Count} {_data.Item(m.Id)?.Name ?? m.Id}"));

        private static string ShortIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            var i = path.LastIndexOf('/');
            var name = i >= 0 ? path.Substring(i + 1) : path;
            var dot = name.IndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private VisualElement IntField(string key, int value, Action<int> set, string standValue)
        {
            var f = new IntegerField { value = value };
            f.style.width = 80;
            f.style.flexGrow = 0;
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return Field(key, f, standValue);
        }

        private VisualElement FloatField(string key, float value, Action<float> set, string standValue)
        {
            var f = new UnityEngine.UIElements.FloatField { value = value };
            f.style.width = 80;
            f.style.flexGrow = 0;
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return Field(key, f, standValue);
        }

        private static string GridText(ProducerInfo b)
        {
            if (b?.Overlap == null) return "?";
            return $"{b.Overlap.Metres}   ({b.Overlap.X:0} x {b.Overlap.Y:0} x {b.Overlap.Z:0} cm half extents)";
        }

        /// <summary>Label + editable field; when the stand's value is given it shows beside the field as "stand: ...".</summary>
        private static VisualElement Field(string key, VisualElement field, string standValue = null, float labelWidth = 110, string prefix = "original: ")
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            var k = new Label(key);
            k.style.width = labelWidth;
            k.style.flexShrink = 0;
            k.style.fontSize = 12;
            k.style.color = UiKit.Muted;
            row.Add(k);
            if (field.style.flexGrow.value == 0 && field.style.width.value.value > 0) { /* fixed-width field keeps its width */ }
            else field.style.flexGrow = 1;
            row.Add(field);
            if (standValue != null)
            {
                var hint = new Label(prefix + standValue);
                hint.style.fontSize = 10;
                hint.style.color = UiKit.Muted;
                hint.style.marginLeft = 8;
                hint.style.flexShrink = 1;
                hint.style.whiteSpace = WhiteSpace.Normal;
                row.Add(hint);
            }
            return row;
        }

        /// <summary>"original: value" plus, when known, what the field does.</summary>
        private static string WithNote(string value, string note) =>
            string.IsNullOrEmpty(note) ? value : value + "   -   " + note;

        private static VisualElement KV(string key, string value, Color? color = null, float labelWidth = 110, string note = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;
            row.style.minHeight = 20;
            row.style.alignItems = Align.Center;
            var k = new Label(key);
            k.style.width = labelWidth;
            k.style.flexShrink = 0;
            k.style.fontSize = 12;
            k.style.color = UiKit.Muted;
            row.Add(k);
            var v = new Label(value);
            v.style.fontSize = 12;
            v.style.color = color ?? UiKit.Text;
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.flexShrink = 1;
            if (string.IsNullOrEmpty(note)) v.style.flexGrow = 1;
            else
            {
                // value in a fixed column, the note fills the rest of the line
                v.style.width = 180;
                v.style.flexShrink = 0;
            }
            row.Add(v);
            if (!string.IsNullOrEmpty(note))
            {
                var n = new Label(note);
                n.style.fontSize = 10;
                n.style.color = UiKit.Muted;
                n.style.marginLeft = 8;
                n.style.flexGrow = 1;
                n.style.flexShrink = 1;
                n.style.whiteSpace = WhiteSpace.Normal;
                row.Add(n);
            }
            return row;
        }
    }
}
