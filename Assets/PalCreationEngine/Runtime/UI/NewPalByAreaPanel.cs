using System.Globalization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PalCreationEngine.Export;
using PalCreationEngine.Lookup;
using PalCreationEngine.UI;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalCreationEngine.UI
{
    /// <summary>
    /// New Pal by Area: builds the complete PalSchema footprint of one new Pal from a base Pal
    /// into a chosen in-game map area, the way the verified working mod (PalVariantPandemonium)
    /// ships each of its 50 - own rows, tribe enum member, texts, spawns copied from vanilla
    /// placements inside the area's trigger volumes, and a Paldex habitat row. Validate checks
    /// every key and reference against the full-export scan index before Export writes anything.
    /// Nothing is ever installed into the game from here.
    ///
    /// A plain panel: the True Engine shell hosts it as a tab, in the Unity editor and in the exe. Dialogs, the
    /// Palworld-folder setting and Explorer go through AppHost; the table browser opens in the host's window when
    /// OpenBrowser is set, otherwise as an overlay. The exe's default folders are in Documents\True Creation.
    /// </summary>
    public sealed class NewPalByAreaPanel : VisualElement
    {
        private readonly GameData _data;
        private TextField _id, _display, _bossPrefix, _partner, _first, _long, _out, _author, _version;
        private DropdownField _baseLetter, _tribe;
        private UiKit.SearchPicker _base, _area;
        private int _baseNumber;
        private string _areaId;
        /// <summary>The Creation Engine's create-Pal sections, embedded; its row is the regular row that ships.</summary>
        private PalEditorView _editor;
        private Label _seedNote;
        private Toggle _boss, _habitat;
        private IntegerField _bossSpawns, _regularSpawns, _bossLvMin, _bossLvMax, _regLvMin, _regLvMax, _numMin, _numMax;
        private VisualElement _extraAreaList;
        private readonly List<(UiKit.SearchPicker Picker, Label Hint, VisualElement Row, string[] Id)> _extraAreas = new List<(UiKit.SearchPicker, Label, VisualElement, string[])>();
        private Label _status, _idHint, _areaHint;
        private VisualElement _report;
        private List<int> _baseNumbers = new List<int>();
        private List<string> _baseEntryKeys = new List<string>();
        private List<string> _areaIds = new List<string>();

        // ---- model (2026-09-18): vanilla by default; "Include mods on this PC" adds the new Pal blueprints of
        // the mods installed on the PC running the tool. Read-only and memory only: nothing about any PC's mods is
        // written into the project, so another person sees only the mods they have (contract section 4 rule 10).
        private const string InstallPref = "PalCreationEngine.PalworldInstall";
        private Toggle _useMods;
        private Label _modelLabel, _modsNote;
        private VisualElement _modsRow;
        private LocalModScan _scan;
        private ModModelChoice _modModel;

        /// <summary>
        /// The host's table browser: the editor sets its DataBrowserWindow; unset (the exe) = an overlay over this panel.
        /// The embedded Creation Engine sections use the same one.
        /// </summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> OpenBrowser;

        private void ShowBrowser(string title, List<BrowserColumn> columns, List<string[]> rows, Action<string> onPick)
        {
            if (OpenBrowser != null) { OpenBrowser(title, columns, rows, onPick); return; }
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = UiKit.Background;
            overlay.Add(new DataBrowser(title, columns, rows,
                picked => { onPick?.Invoke(picked); overlay.RemoveFromHierarchy(); },
                () => overlay.RemoveFromHierarchy()));
            Add(overlay);
            overlay.BringToFront();
        }

        public NewPalByAreaPanel() : this(GameData.Load(DataPaths.LookupDirectory)) { }

        public NewPalByAreaPanel(GameData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            foreach (var error in _data.LoadErrors) Debug.LogWarning($"[PalCreationEngine] {error}");
            style.flexGrow = 1;
            style.backgroundColor = UiKit.Background;
            style.color = UiKit.Text;
            Build();
        }

        private void Build()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 10; scroll.style.paddingRight = 10; scroll.style.paddingTop = 8;
            Add(scroll);

            var title = new Label("New Pal by Area");
            title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.marginBottom = 4;
            scroll.Add(title);
            scroll.Add(Note("Route A: the new Pal shares the base Pal's blueprint through its own DT_PalBPClass row (as 33 shipped Pals do); the vanilla Pal is never patched. " +
                            "Spawns copy vanilla placements inside the area's trigger volumes. Everything is checked against the export scan before anything is written.", UiKit.Muted));
            if (_data.LoadErrors.Count > 0)
                scroll.Add(Note("Lookup load problems: " + string.Join(" | ", _data.LoadErrors), UiKit.Danger));
            if (_data.MapAreas.Count == 0)
                scroll.Add(Note("map_areas.json is missing or empty in True Creation's data folder, so spawns by area cannot be built. " +
                                "Reinstall True Creation to restore it (developers: python Tools/PalCreationEngine/generate_index_lookups.py).", UiKit.Danger));
            if (_data.BaseRows.Count == 0)
                scroll.Add(Note("base_rows.json is missing or empty in True Creation's data folder: partner-skill, camera, randomizer and drop rows cannot be cloned (recommended pieces). " +
                                "Reinstall True Creation to restore it (developers: python Tools/PalCreationEngine/generate_lookups.py --base-rows).", UiKit.Warning));

            // ---- identity
            scroll.Add(UiKit.Section("Identity", out var identity));
            _id = new TextField { value = "MyNewPal" };
            _id.RegisterValueChangedCallback(_ => { RefreshIdHint(); SyncEditorIdentity(); });
            identity.Add(UiKit.Row("Internal id", _id, "row key, enum member and text-key suffix; letters, digits, underscores", 150, 260));
            _idHint = Note("", UiKit.Muted); identity.Add(_idHint);

            // Pal first, then that Pal's own letters (asked for five times): the base is picked by its
            // Paldex number, and the second list holds only the rows at that number - none = the base
            // form, each letter = what it is AT THAT NUMBER. The chosen row is the model source.
            _baseNumbers = _data.ZukanSlots.Keys.Select(k => int.Parse(k, CultureInfo.InvariantCulture)).OrderBy(n => n).ToList();
            var badCat = _data.Pals.TryGetValue("BadCatgirl", out var bc) ? bc.ZukanIndex : 0;
            string NumberLabel(int n) => $"{n} - {_data.LabelForPal(_data.ZukanSlot(n).Primary)}".Replace("/", " + ");
            _baseNumber = badCat;
            _base = new UiKit.SearchPicker(() => _baseNumbers.Select(NumberLabel), "type a name or number, or open the list; the table button browses every Pal");
            _base.OnSelected += label => ApplyBaseNumber(label);
            _base.OnCommitted += label => ApplyBaseNumber(label);
            _base.AddTrailing(BrowseButton("Browse every Pal in a table", () =>
            {
                var (columns, rows) = BrowserDatasets.PaldexSlots(_data);
                ShowBrowser("Paldex - pick the base Pal", columns, rows, picked => ApplyBaseNumber(picked));
            }));
            _base.SetValueSilently(NumberLabel(_baseNumber));
            identity.Add(UiKit.Row("Base Pal (number)", _base, "the Pal this one is a variant of; its blueprint, icon, partner skill, learnset, drops and texts are the starting point", 150, 360));
            _baseLetter = new DropdownField(new List<string> { "(none)" }, 0);
            _baseLetter.RegisterValueChangedCallback(_ => { RefreshBaseHints(); SyncEditorIdentity(); });
            identity.Add(UiKit.Row("Base form (letter)", _baseLetter, "the rows at that number: none = base form, each letter = that Pal's own variant; the picked row is the model source", 150, 360));
            RebuildBaseLetters();
            _modelLabel = Note("", UiKit.Text);
            identity.Add(UiKit.Row("Model", _modelLabel, null, 150, 620));
            _useMods = new Toggle { value = false };
            _useMods.RegisterValueChangedCallback(e => SetModsEnabled(e.newValue));
            identity.Add(UiKit.Row("Include mods on this PC", _useMods, "off: the base Pal's own vanilla blueprint. On: also offers the new Pal blueprints of the mods installed on THIS PC (read-only, nothing saved)", 150, 24));
            _modsRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginLeft = 150, display = DisplayStyle.None } };
            _modsRow.Add(UiKit.SecondaryButton("Pick a mod model", PickModModel));
            _modsRow.Add(UiKit.SecondaryButton("Use the base Pal's blueprint", () => SetModModel(null)));
            _modsRow.Add(UiKit.SecondaryButton("Paks and patches", ShowModFiles));
            _modsRow.Add(UiKit.SecondaryButton("Palworld folder", PickInstall));
            _modsRow.Add(UiKit.SecondaryButton("Re-detect", () => { AppHost.Current.DeleteKey(InstallPref); SetModsEnabled(true); }));
            identity.Add(_modsRow);
            _modsNote = Note("Mods off: the model is the base Pal's own vanilla blueprint.", UiKit.Muted);
            identity.Add(_modsNote);
            _tribe = new DropdownField(new List<string> { "Own tribe - a new species family, the working mod's way", "Base's tribe - counts as the base Pal (untested)" }, 0);
            _tribe.RegisterValueChangedCallback(evt => _editor?.SetTribeMode(_tribe.index == 0));
            identity.Add(UiKit.Row("Tribe", _tribe, "which species family this Pal belongs to - read the note below", 150, 360));
            identity.Add(Note(
                "TRIBE is the species family the game files this Pal under. It is not shown in game, but three things depend on it. " +
                "1) Fixed breeding pairs are matched by the parents' tribes. 2) Two parents of the same tribe always breed that tribe's base Pal. " +
                "3) Some base objects only let certain tribes work them. " +
                "OWN TRIBE: this Pal is its own family (that is what every one of the working mod's 100 Pals does). It is in no breeding pair until you add one below, " +
                "two of them together breed this Pal, and no tribe-restricted object accepts it unless a mod lists it. " +
                "BASE'S TRIBE: the game treats this Pal as the base Pal wherever tribe matters. It inherits the base's pairs but those pairs produce the base's child, not this Pal; " +
                "two of these together breed the BASE Pal, never this one; it may work whatever the base may work. Nobody has tested this in game.", UiKit.Text));

            // ---- create Pal: the Creation Engine's own sections, embedded (2026-09-16: "new pal by
            // area should also pull and list the create pal part from other tabs"). Name and Borrow-model are
            // driven by Internal id and Base Pal above; the row starts as the base Pal's own row and what these
            // sections show is what ships (NewPalPlan.RowTemplate). Model Size and Companion are not offered on
            // Route A (they patch the borrowed vanilla class, pce-01).
            scroll.Add(UiKit.Section("Create Pal - elements, Paldex slot, stats, work suitability, passives, loot (the Creation Engine sections)", out var create));
            _seedNote = Note("", UiKit.Muted); create.Add(_seedNote);
            _editor = new PalEditorView(_data, Debug.Log, embedded: true) { OpenBrowser = ShowBrowser };
            _editor.RowRebuilt += RefreshSeedNote;
            create.Add(_editor);

            // ---- texts
            scroll.Add(UiKit.Section("Names and text (empty = copy the base Pal's)", out var texts));
            _display = new TextField(); texts.Add(UiKit.Row("Display name", _display, "PAL_NAME_<id>", 150, 420));
            _bossPrefix = new TextField(); texts.Add(UiKit.Row("Alpha title", _bossPrefix, "BOSS_NAME_<id>, shown before the name on the alpha", 150, 420));
            _partner = new TextField(); texts.Add(UiKit.Row("Partner skill name", _partner, "PARTNERSKILL_<id>", 150, 420));
            _first = new TextField { multiline = true }; _first.style.minHeight = 44;
            texts.Add(UiKit.Row("First-spawn text", _first, "PAL_FIRST_SPAWN_DESC_<id>", 150, 520));
            _long = new TextField { multiline = true }; _long.style.minHeight = 60;
            texts.Add(UiKit.Row("Paldex description", _long, "PAL_LONG_DESC_<id>", 150, 520));

            // ---- world
            scroll.Add(UiKit.Section("World: where it spawns", out var world));
            _areaIds = _data.MapAreas.Values
                .OrderBy(a => a.Family ?? "", StringComparer.Ordinal)
                .ThenBy(a => a.Name ?? "", StringComparer.Ordinal)
                .Select(a => a.Id).ToList();
            _areaId = _data.MapAreas.ContainsKey("Grass_001") ? "Grass_001" : _areaIds.FirstOrDefault();
            _area = new UiKit.SearchPicker(() => _areaIds.Select(id => AreaLabel(_data.MapAreas[id])), "type an area name, id or zone, or open the list; the table button browses all 124");
            _area.OnSelected += label => ApplyArea(label);
            _area.OnCommitted += label => ApplyArea(label);
            _area.AddTrailing(BrowseButton("Browse every area in a table", () =>
            {
                var columns = new List<BrowserColumn> { new BrowserColumn("In-game name", 220), new BrowserColumn("Row id", 200), new BrowserColumn("Zone", 60), new BrowserColumn("Group", 110), new BrowserColumn("Common", 70, true), new BrowserColumn("Alpha", 60, true), new BrowserColumn("Common lv", 90), new BrowserColumn("Alpha lv", 80) };
                var rows = _areaIds.Select(id => _data.MapAreas[id]).Select(a => new[]
                {
                    a.Name ?? "", a.Id, a.Zones.Count > 0 ? a.Zones[0].ZoneId : "", a.Family ?? "", a.Counts.Common.ToString(), a.Counts.FieldBoss.ToString(),
                    $"{a.Levels.CommonP10 ?? a.Levels.CommonMin}-{a.Levels.CommonP90 ?? a.Levels.CommonMax}", $"{a.Levels.FieldBossMin?.ToString() ?? "-"}-{a.Levels.FieldBossMax?.ToString() ?? "-"}",
                }).ToList();
                ShowBrowser("Map areas - pick where it spawns", columns, rows, picked => ApplyArea(picked));
            }));
            if (_areaId != null) _area.SetValueSilently(AreaLabel(_data.MapAreas[_areaId]));
            world.Add(UiKit.Row("Map area", _area, "in-game name on entering  [row id, trigger zone, island group]  placements inside", 150, 560));
            _areaHint = Note("", UiKit.Muted); world.Add(_areaHint);
            // More areas: Cattiva spawns in several; each extra area gets the same spawns below.
            _extraAreaList = new VisualElement(); world.Add(_extraAreaList);
            var addArea = UiKit.SecondaryButton("Add another area", () => AddExtraArea(null));
            addArea.style.marginBottom = 6; world.Add(addArea);
            _boss = new Toggle { value = true }; world.Add(UiKit.Row("Alpha (boss) row + spawn", _boss, "BOSS_<id> row and a FieldBoss spawn copied from a vanilla alpha placement", 150, 24));
            world.Add(Note("What a spawn entry is (spawns/ file, the working mod's shape): Type Sheet; Location = the exact coordinates of one vanilla spawner placement inside the area; " +
                           "SpawnerName = <area prefix>_FBOSS_<id> or _Common_<id>; SpawnerType FieldBoss (alpha) or Common; one group with Weight 100 (the only group, so it is always picked); " +
                           "PalList = this Pal with Level..Level_Max and Num..Num_Max. Blank level or count fields = the area's own vanilla values shown in the area line.", UiKit.Muted));
            _bossSpawns = new IntegerField { value = 1 }; world.Add(UiKit.Row("Alpha spawns per area", _bossSpawns, "0-3 FieldBoss entries, each at a vanilla alpha placement in the area; BOSS_<id> row, Num 1", 150, 60));
            var bossLv = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _bossLvMin = new IntegerField { value = 0 }; _bossLvMin.style.width = 60; _bossLvMax = new IntegerField { value = 0 }; _bossLvMax.style.width = 60; _bossLvMax.style.marginLeft = 6;
            bossLv.Add(_bossLvMin); bossLv.Add(_bossLvMax);
            world.Add(UiKit.Row("Alpha level min, max", bossLv, "Level..Level_Max of the alpha; 0 = the area's vanilla alpha range", 150, 130));
            _regularSpawns = new IntegerField { value = 2 }; world.Add(UiKit.Row("Regular spawns per area", _regularSpawns, "0-3 Common entries, each at a vanilla common placement; <id> row. Common through spawns/ is UNTESTED in game", 150, 60));
            var regLv = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _regLvMin = new IntegerField { value = 0 }; _regLvMin.style.width = 60; _regLvMax = new IntegerField { value = 0 }; _regLvMax.style.width = 60; _regLvMax.style.marginLeft = 6;
            regLv.Add(_regLvMin); regLv.Add(_regLvMax);
            world.Add(UiKit.Row("Regular level min, max", regLv, "Level..Level_Max; 0 = the area's vanilla common range (10th to 90th percentile)", 150, 130));
            var num = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _numMin = new IntegerField { value = 1 }; _numMin.style.width = 60; _numMax = new IntegerField { value = 2 }; _numMax.style.width = 60; _numMax.style.marginLeft = 6;
            num.Add(_numMin); num.Add(_numMax);
            world.Add(UiKit.Row("Group size min, max", num, "Num..Num_Max: how many appear per regular spawn; vanilla packs are 1-2 for most, alphas always 1", 150, 130));
            _habitat = new Toggle { value = true }; world.Add(UiKit.Row("Paldex habitat map", _habitat, "DT_PaldexDistributionData row from the spawns, the area's placements and the base's points", 150, 24));

            // ---- output
            scroll.Add(UiKit.Section("Output", out var output));
            _out = new TextField { value = DefaultOutputFolder };
            var browse = UiKit.SecondaryButton("Browse", () =>
            {
                var picked = AppHost.Current.OpenFolderPanel("Package output folder", _out.value);
                if (!string.IsNullOrEmpty(picked)) _out.value = picked;
            });
            var outRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _out.style.flexGrow = 1; outRow.Add(_out); outRow.Add(browse);
            output.Add(UiKit.Row("Folder", outRow, "the package is written to <folder>/<id>/; nothing is installed into the game", 150, 520));
            // the editor fills in this PC's account name; the exe starts empty, so nobody's name goes into a package unasked
            _author = new TextField { value = Application.isEditor ? Environment.UserName : "" }; output.Add(UiKit.Row("Author", _author, null, 150, 260));
            _version = new TextField { value = "1.0.0" }; output.Add(UiKit.Row("Version", _version, "any change of this string makes the loader reinstall the package", 150, 120));

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8, marginBottom = 8 } };
            buttons.Add(UiKit.PrimaryButton("Validate", () => Validate(out _, out _)));
            buttons.Add(UiKit.SecondaryButton("Export package", Export));
            buttons.Add(UiKit.SecondaryButton("Save design", SaveDesign));
            buttons.Add(UiKit.SecondaryButton("Load design", LoadDesign));
            scroll.Add(buttons);
            scroll.Add(Note("Save design writes one <id>.pcepal.json with everything on this page (identity, the row, Paldex slot, texts, area, spawns, loot, breeding pairs) so it can be loaded and exported again. " +
                            "Export package writes the mod folder: <folder>/<id>/Info.json + PalSchema/{enums,pals,raw,translations,spawns}.", UiKit.Muted));
            _status = Note("", UiKit.Muted); _status.style.whiteSpace = WhiteSpace.Normal; scroll.Add(_status);
            _report = new VisualElement(); scroll.Add(_report);

            RefreshIdHint(); RefreshBaseHints(); RefreshAreaHint(); SyncEditorIdentity();
        }

        private void SyncEditorIdentity()
        {
            if (_editor == null) return;
            _editor.SetIdentity((_id.value ?? "").Trim(), BaseId);
            _editor.SetTribeMode(_tribe == null || _tribe.index == 0);
            RefreshSeedNote();
        }

        private void RefreshSeedNote()
        {
            if (_seedNote == null || _editor == null) return;
            _seedNote.text = $"Row seeded from {_editor.SeededFrom}, then your edits below; what these sections show is what ships in pals/. " +
                             "Changing Base Pal re-seeds and discards edits. Name comes from Internal id; the model from Base Pal or the Model row above.";
        }

        /// <summary>
        /// One line per area with no "/" in it - Unity's DropdownField reads "/" as a submenu
        /// separator, which is what split the old "241 common / 3 alpha" labels into nested menus.
        /// What it says: the in-game name the player sees on entering, the DT_WorldMapAreaData
        /// row id, the trigger zone id, the island group, and the vanilla placements inside the
        /// volume that a spawn can be copied from.
        /// </summary>
        private static string AreaLabel(MapArea a)
        {
            var zone = a.Zones.Count > 0 ? string.Join("+", a.Zones.Select(z => z.ZoneId)) : "no zone";
            var placements = a.Counts.Common + a.Counts.FieldBoss == 0
                ? "no placements inside"
                : $"{a.Counts.Common} common + {a.Counts.FieldBoss} alpha";
            return $"{a.Name}   [{a.Id}  zone {zone}  {a.Family}]   {placements}".Replace("/", "+");
        }

        private static Label Note(string text, Color color)
        {
            var label = new Label(text) { style = { color = color, whiteSpace = WhiteSpace.Normal, marginTop = 2, marginBottom = 4 } };
            label.style.fontSize = 11;
            return label;
        }

        private string BaseId => _baseEntryKeys.Count > 0 && _baseLetter.index >= 0 && _baseLetter.index < _baseEntryKeys.Count ? _baseEntryKeys[_baseLetter.index] : null;

        private void ApplyBaseNumber(string label)
        {
            var digits = new string((label ?? "").TrimStart().TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || _data.ZukanSlot(n) == null) return;
            _baseNumber = n;
            _base.SetValueSilently($"{n} - {_data.LabelForPal(_data.ZukanSlot(n).Primary)}".Replace("/", " + "));
            RebuildBaseLetters(); RefreshBaseHints(); SyncEditorIdentity();
        }

        private static Button BrowseButton(string tooltip, Action onClick)
        {
            var b = new Button(onClick) { text = "\u2261", tooltip = tooltip };
            b.style.width = 22; b.style.height = 18; b.style.marginLeft = 2;
            return b;
        }

        /// <summary>Fills the letter list with the rows at the chosen number and nothing else.</summary>
        private void RebuildBaseLetters()
        {
            if (_baseLetter == null) return;
            var number = _baseNumber;
            var slot = _data.ZukanSlot(number);
            var entries = (slot?.Entries ?? new List<ZukanEntry>())
                .Where(e => _data.Pals.TryGetValue(e.RowKey, out var p) && !p.IsBoss && !p.IsRaidBoss && !p.IsTowerBoss)
                .OrderBy(e => e.Suffix ?? "", StringComparer.Ordinal).ThenBy(e => e.RowKey, StringComparer.Ordinal).ToList();
            _baseEntryKeys = entries.Select(e => e.RowKey).ToList();
            var labels = entries.Select(e => $"{(string.IsNullOrEmpty(e.Suffix) ? "(none)" : e.Suffix)} - {_data.LabelForPal(e.RowKey)}".Replace("/", " + ")).ToList();
            if (labels.Count == 0) { labels.Add("(none) - no row at this number"); _baseEntryKeys = new List<string> { null }; }
            _baseLetter.choices = labels;
            _baseLetter.SetValueWithoutNotify(labels[0]);
        }
        private string AreaId => _areaId;

        /// <summary>Primary area first, then every extra area that resolved, no duplicates.</summary>
        private List<string> AllAreaIds()
        {
            var ids = new List<string>();
            if (_areaId != null) ids.Add(_areaId);
            foreach (var e in _extraAreas) if (e.Id[0] != null && !ids.Contains(e.Id[0])) ids.Add(e.Id[0]);
            return ids;
        }

        private string ResolveArea(string picked)
        {
            var text = (picked ?? "").Trim();
            if (text.Length == 0) return null;
            return _areaIds.FirstOrDefault(i => AreaLabel(_data.MapAreas[i]) == text)
                   ?? _areaIds.FirstOrDefault(i => string.Equals(_data.MapAreas[i].Id, text, StringComparison.OrdinalIgnoreCase))
                   ?? _areaIds.FirstOrDefault(i => string.Equals(_data.MapAreas[i].Name, text, StringComparison.OrdinalIgnoreCase))
                   ?? _areaIds.FirstOrDefault(i => _data.MapAreas[i].Zones.Any(z => string.Equals(z.ZoneId, text, StringComparison.OrdinalIgnoreCase)))
                   ?? _areaIds.FirstOrDefault(i => text.StartsWith(_data.MapAreas[i].Name ?? "\0", StringComparison.OrdinalIgnoreCase));
        }

        private void AddExtraArea(string areaId)
        {
            var idBox = new[] { areaId };
            var picker = new UiKit.SearchPicker(() => _areaIds.Select(id => AreaLabel(_data.MapAreas[id])), "another area this Pal spawns in");
            var hint = Note("", UiKit.Muted);
            var row = new VisualElement();
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart } };
            picker.style.width = 560;
            var remove = new Button { text = "x" }; remove.style.width = 22; remove.style.marginLeft = 6;
            line.Add(picker); line.Add(remove); row.Add(line); row.Add(hint);
            void Apply(string label)
            {
                var id = ResolveArea(label);
                if (id == null) { hint.text = $"'{label}' is not an area name, row id or zone id"; hint.style.color = UiKit.Danger; return; }
                idBox[0] = id; picker.SetValueSilently(AreaLabel(_data.MapAreas[id]));
                var (text, color) = AreaHintText(id); hint.text = text; hint.style.color = color;
            }
            picker.OnSelected += Apply; picker.OnCommitted += Apply;
            picker.AddTrailing(BrowseButton("Browse every area in a table", () => ShowBrowser("Map areas - pick another area", AreaColumns(), AreaRows(), Apply)));
            var entry = (picker, hint, row, idBox);
            remove.clicked += () => { row.RemoveFromHierarchy(); _extraAreas.Remove(entry); };
            _extraAreas.Add(entry);
            _extraAreaList.Add(row);
            if (areaId != null) Apply(areaId);
        }

        private static List<BrowserColumn> AreaColumns() => new List<BrowserColumn> { new BrowserColumn("In-game name", 220), new BrowserColumn("Row id", 200), new BrowserColumn("Zone", 60), new BrowserColumn("Group", 110), new BrowserColumn("Common", 70, true), new BrowserColumn("Alpha", 60, true), new BrowserColumn("Common lv", 90), new BrowserColumn("Alpha lv", 80) };
        private List<string[]> AreaRows() => _areaIds.Select(id => _data.MapAreas[id]).Select(a => new[]
        {
            a.Name ?? "", a.Id, a.Zones.Count > 0 ? a.Zones[0].ZoneId : "", a.Family ?? "", a.Counts.Common.ToString(), a.Counts.FieldBoss.ToString(),
            $"{a.Levels.CommonP10 ?? a.Levels.CommonMin}-{a.Levels.CommonP90 ?? a.Levels.CommonMax}", $"{a.Levels.FieldBossMin?.ToString() ?? "-"}-{a.Levels.FieldBossMax?.ToString() ?? "-"}",
        }).ToList();

        /// <summary>Accepts a picker label, a browser row (first column = name), a row id, a zone id, or an in-game name.</summary>
        private void ApplyArea(string picked)
        {
            var text = (picked ?? "").Trim();
            if (text.Length == 0) return;
            var id = _areaIds.FirstOrDefault(i => AreaLabel(_data.MapAreas[i]) == text)
                     ?? _areaIds.FirstOrDefault(i => string.Equals(_data.MapAreas[i].Id, text, StringComparison.OrdinalIgnoreCase))
                     ?? _areaIds.FirstOrDefault(i => string.Equals(_data.MapAreas[i].Name, text, StringComparison.OrdinalIgnoreCase))
                     ?? _areaIds.FirstOrDefault(i => _data.MapAreas[i].Zones.Any(z => string.Equals(z.ZoneId, text, StringComparison.OrdinalIgnoreCase)))
                     ?? _areaIds.FirstOrDefault(i => text.StartsWith(_data.MapAreas[i].Name ?? "\0", StringComparison.OrdinalIgnoreCase));
            if (id == null) { _areaHint.text = $"'{text}' is not an area name, row id or zone id"; _areaHint.style.color = UiKit.Danger; return; }
            _areaId = id;
            _area.SetValueSilently(AreaLabel(_data.MapAreas[id]));
            RefreshAreaHint();
        }

        private void RefreshIdHint()
        {
            var id = _id.value ?? "";
            if (string.IsNullOrWhiteSpace(id)) { _idHint.text = "enter an id"; _idHint.style.color = UiKit.Warning; return; }
            var problems = new List<string>();
            if (!id.All(c => char.IsLetterOrDigit(c) || c == '_')) problems.Add("letters, digits and underscores only");
            if (_data.Pals.ContainsKey(id) || _data.Scan.TableRowExists("DT_PalMonsterParameter", id)) problems.Add("already a shipped Pal row");
            if (_data.Scan.TableRowExists("DT_PalHumanParameter", id)) problems.Add("already a shipped human/NPC row");
            if (_data.Scan.Exists("enum_member", "EPalTribeID::" + id) || _data.EnumValues("EPalTribeID").Contains(id)) problems.Add("already an EPalTribeID member");
            if (_data.Scan.Exists("text_key", "DT_PalNameText_Common::PAL_NAME_" + id)) problems.Add("PAL_NAME_" + id + " already exists");
            _idHint.text = problems.Count == 0 ? $"'{id}' collides with nothing in the export scan" : $"'{id}': " + string.Join("; ", problems);
            _idHint.style.color = problems.Count == 0 ? UiKit.Success : UiKit.Danger;
        }

        private void RefreshBaseHints()
        {
            var baseId = BaseId;
            if (baseId == null) return;
            var link = _data.BaseLinks.TryGetValue(baseId, out var l) ? l : null;
            string T(string k, string fallback) => link != null && link.Text.TryGetValue(k, out var t) && !string.IsNullOrEmpty(t) ? t : fallback;
            _display.textEdition.placeholder = T("PAL_NAME", _data.Names?.Pals != null && _data.Names.Pals.TryGetValue(baseId, out var n) ? n : baseId);
            _bossPrefix.textEdition.placeholder = T("BOSS_NAME", "Alpha");
            _partner.textEdition.placeholder = T("PARTNERSKILL", "");
            _first.textEdition.placeholder = T("PAL_FIRST_SPAWN_DESC", "");
            _long.textEdition.placeholder = T("PAL_LONG_DESC", _data.PalDescription(baseId) ?? "");
            RefreshModelLabel();
        }

        private void RefreshAreaHint()
        {
            if (AreaId == null) { _areaHint.text = ""; return; }
            var (text, color) = AreaHintText(AreaId);
            _areaHint.text = text; _areaHint.style.color = color;
        }

        /// <summary>What this area is, in the words the game and the tables use, and what can be copied from it.</summary>
        private (string Text, Color Color) AreaHintText(string areaId)
        {
            if (areaId == null || !_data.MapAreas.TryGetValue(areaId, out var a)) return ("", UiKit.Muted);
            var lv = a.Levels;
            var zone = a.Zones.Count > 0 ? string.Join(", ", a.Zones.Select(z => $"{z.ZoneId} ({z.Shape})")) : "none";
            var placeholder = string.IsNullOrEmpty(a.Name) || a.Name.StartsWith("REGION_", StringComparison.Ordinal) || a.Name.Contains("INFERRED");
            var none = a.Counts.Common + a.Counts.FieldBoss == 0;
            var text =
                (placeholder
                    ? $"Trigger {a.Id} has no DT_WorldMapAreaData row: the game shows no area name when the player enters it. "
                    : $"In game, a player walking into this volume sees \"{a.Name}\". ") +
                $"Area row id {a.Id} (DT_WorldMapAreaData); trigger zone {zone}; island group {a.Family}. " +
                $"Spawns are not keyed by the area: they copy the coordinates of vanilla placements inside this volume - " +
                $"{a.Counts.Common} common, {a.Counts.FieldBoss} alpha ({a.Placements.Count} core); " +
                $"vanilla levels here: common {lv.CommonP10 ?? lv.CommonMin}-{lv.CommonP90 ?? lv.CommonMax}, alpha {lv.FieldBossMin?.ToString() ?? "-"}-{lv.FieldBossMax?.ToString() ?? "-"}; spawner prefix '{a.TopPrefix}'." +
                (none ? " NO vanilla placement lies inside this volume, so there is nothing to copy a spawn position from - pick another area." : "");
            return (text, none ? UiKit.Danger : placeholder ? UiKit.Warning : UiKit.Muted);
        }

        // ---- model: vanilla by default, mods on this PC on request (rule 10) -------------------------------------

        private void SetModsEnabled(bool on)
        {
            if (_modsRow == null) return;
            if (!on)
            {
                _scan = null;
                SetModModel(null);
                _modsRow.style.display = DisplayStyle.None;
                _modsNote.text = "Mods off: the model is the base Pal's own vanilla blueprint.";
                _modsNote.style.color = UiKit.Muted;
                return;
            }
            _modsRow.style.display = DisplayStyle.Flex;
            var install = PalworldInstall.Find(AppHost.Current.GetString(InstallPref, ""));
            if (!install.Found)
            {
                _scan = null;
                SetModModel(null);
                _modsNote.text = "Palworld was not found on this PC (tried " + string.Join("; ", install.Tried.Take(6)) + "). Use Palworld folder to point at it.";
                _modsNote.style.color = UiKit.Danger;
                return;
            }
            _scan = LocalModScan.Run(install.Path);
            var mods = _scan.Blueprints.Select(b => b.Mod).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _modsNote.text = $"Palworld on this PC: {install.Path} (found via {install.How}). Read only; kept in memory, never saved into the project, so another PC sees only its own mods. " +
                             $"{_scan.Blueprints.Count} new Pal blueprint(s) from {mods} mod(s) can be picked; {_scan.Paks.Count} pak(s) and {_scan.Patches.Count} blueprint-patch set(s) are listed for information." +
                             (_scan.Problems.Count > 0 ? $" {_scan.Problems.Count} file(s) could not be read: " + string.Join(" | ", _scan.Problems.Take(3)) : "");
            _modsNote.style.color = _scan.Problems.Count > 0 ? UiKit.Warning : UiKit.Muted;
            if (_modModel != null && !_scan.Blueprints.Any(b => string.Equals(b.Mod, _modModel.Mod, StringComparison.OrdinalIgnoreCase)
                                                             && string.Equals(b.RowKey, _modModel.RowKey, StringComparison.OrdinalIgnoreCase)))
                SetModModel(null);
        }

        private void PickModModel()
        {
            if (_scan == null || _scan.Blueprints.Count == 0)
            {
                _modsNote.text = "No mod on this PC adds a Pal blueprint (DT_PalBPClass rows in a PalSchema mod's raw/ folder).";
                _modsNote.style.color = UiKit.Warning;
                return;
            }
            var columns = new List<BrowserColumn> { new BrowserColumn("Mod : row", 280), new BrowserColumn("Class path", 430), new BrowserColumn("Alpha twin", 80), new BrowserColumn("Icon", 50), new BrowserColumn("Workshop package", 170) };
            var rows = _scan.Blueprints.Select(b => new[] { b.PickLabel, b.ClassPath, _scan.HasBoss(b) ? "yes" : "", _scan.IconFor(b) != null ? "yes" : "", b.Package ?? "(not a Workshop package)" }).ToList();
            ShowBrowser("Mod models on this PC - pick one", columns, rows, picked =>
            {
                var bp = _scan?.Blueprints.FirstOrDefault(b => b.PickLabel == picked);
                if (bp != null) SetModModel(_scan.Choose(bp));
            });
        }

        private void ShowModFiles()
        {
            if (_scan == null) return;
            var columns = new List<BrowserColumn> { new BrowserColumn("Kind", 120), new BrowserColumn("Name", 330), new BrowserColumn("Folder", 170), new BrowserColumn("Workshop package", 180), new BrowserColumn("Detail", 200) };
            var rows = _scan.Paks.Select(p => new[] { "pak", Path.GetFileName(p.RelativePath), p.Folder, p.Package ?? "(not a Workshop package)", (p.IsLink ? "link, " : "") + "contents not opened" })
                .Concat(_scan.Patches.Select(s => new[] { "blueprint patches", s.Mod, "PalSchema blueprints", s.Package ?? "(not a Workshop package)", s.Keys + " class(es) patched; not a model" }))
                .ToList();
            ShowBrowser("Mods on this PC - paks and blueprint patches (information only)", columns, rows, _ => { });
        }

        private void PickInstall()
        {
            var picked = AppHost.Current.OpenFolderPanel("Palworld install folder (the one holding Pal\\Content\\Paks)", AppHost.Current.GetString(InstallPref, ""));
            if (string.IsNullOrEmpty(picked)) return;
            if (!PalworldInstall.LooksLikeInstall(picked))
            {
                _modsNote.text = picked + " has no Pal\\Content\\Paks folder; it is not a Palworld install.";
                _modsNote.style.color = UiKit.Danger;
                return;
            }
            AppHost.Current.SetString(InstallPref, picked);   // a per-user setting (EditorPrefs / settings.json), never part of the project
            if (_useMods.value) SetModsEnabled(true); else _useMods.value = true;
        }

        private void SetModModel(ModModelChoice choice)
        {
            _modModel = choice;
            RefreshModelLabel();
        }

        private void RefreshModelLabel()
        {
            if (_modelLabel == null) return;
            if (_modModel == null)
            {
                var baseId = BaseId;
                string path = null;
                if (baseId != null)
                    path = _data.BaseLinks != null && _data.BaseLinks.TryGetValue(baseId, out var link) && !string.IsNullOrEmpty(link.BpClassPath)
                        ? link.BpClassPath
                        : _data.BlueprintPathFor(_data.Pals.TryGetValue(baseId, out var pal) ? pal.BpClass : null);
                _modelLabel.text = "Base Pal's own blueprint (vanilla)" + (string.IsNullOrEmpty(path) ? "" : ": " + path);
                _modelLabel.style.color = UiKit.Text;
                return;
            }
            _modelLabel.text = $"{_modModel.Label} -> {_modModel.ClassPath}. From a mod on this PC: the package lists {_modModel.Requirement} as a dependency" +
                               (_modModel.Package == null ? " (not a Workshop package here, so Info.json cannot list it; users must install it themselves)" : "") +
                               " and the Pal renders only where it is installed." +
                               (string.IsNullOrEmpty(_modModel.IconPath) ? " The icon stays the base Pal's." : "");
            _modelLabel.style.color = UiKit.Warning;
        }

        private NewPalPlan BuildPlan()
        {
            var id = (_id.value ?? "").Trim();
            return new NewPalPlan
            {
                PalId = id,
                BasePalId = BaseId,
                ModName = id,
                Tribe = _tribe.index == 1 ? TribeMode.BorrowBase : TribeMode.NewMember,
                Seed = SeedMode.CloneBase,
                ModModel = _modModel,
                // The embedded sections' row is the regular row; its Paldex slot and loot come with it.
                RowTemplate = _editor?.CurrentRow,
                ZukanIndex = _editor?.ChosenZukanIndex,
                ZukanIndexSuffix = _editor?.CurrentRow?.ZukanIndexSuffix,
                RegularDrops = _editor?.BuildDropRow(),
                CombiPairs = _editor?.CombiPairs ?? new List<CombiPair>(),
                SelfBreedPair = _editor?.SelfBreedPair ?? true,
                Texts = new NewPalTexts
                {
                    DisplayName = _display.value ?? "", BossPrefix = _bossPrefix.value ?? "", PartnerSkillName = _partner.value ?? "",
                    FirstSpawnDesc = _first.value ?? "", LongDesc = _long.value ?? "",
                },
                IncludeBoss = _boss.value,
                AreaIds = AllAreaIds(),
                BossSpawnsPerArea = Mathf.Clamp(_bossSpawns.value, 0, 3),
                RegularSpawnsPerArea = Mathf.Clamp(_regularSpawns.value, 0, 3),
                BossLevelMin = _bossLvMin.value > 0 ? _bossLvMin.value : (int?)null,
                BossLevelMax = _bossLvMax.value > 0 ? _bossLvMax.value : (int?)null,
                RegularLevelMin = _regLvMin.value > 0 ? _regLvMin.value : (int?)null,
                RegularLevelMax = _regLvMax.value > 0 ? _regLvMax.value : (int?)null,
                RegularNumMin = _numMin.value > 0 ? _numMin.value : (int?)null,
                RegularNumMax = _numMax.value > 0 ? _numMax.value : (int?)null,
                Habitat = _habitat.value,
            };
        }

        // ---- design file -------------------------------------------------------

        /// <summary>Everything on the page, as one JSON file the page can load back.</summary>
        private sealed class Design
        {
            public string Format = "PalCreationEngine.NewPalByArea.design/1";
            public string PalId, BasePalId, Author, Version, OutputFolder, AreaId;
            public int TribeMode;
            public NewPalTexts Texts;
            public bool IncludeBoss, Habitat, SelfBreedPair;
            public int BossSpawns, RegularSpawns, BossLvMin, BossLvMax, RegLvMin, RegLvMax, NumMin, NumMax;
            public List<string> ExtraAreas;
            public PalCreationEngine.Data.PalMonsterParameterRow Row;
            public PalCreationEngine.Data.PalDropItemRow Drops;
            public List<CombiPair> Pairs;
            /// <summary>The mod model picked with "Include mods on this PC" (mod folder and row key only; resolved again on load).</summary>
            public string ModModelMod, ModModelRowKey;
        }

        // the editor keeps its Desktop folders; the exe uses Documents\True Creation, the user's own place
        private static string DefaultOutputFolder => Application.isEditor
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PalCreationEngine Output")
            : AppPaths.UserFolder("New Pal Output");
        private static string DesignFolder => Application.isEditor
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PalCreationEngine Designs")
            : AppPaths.UserFolder("New Pal Designs");

        private void SaveDesign()
        {
            var id = (_id.value ?? "").Trim();
            if (id.Length == 0) { _status.text = "Give the Pal an Internal id first."; _status.style.color = UiKit.Danger; return; }
            Directory.CreateDirectory(DesignFolder);
            var path = AppHost.Current.SaveFilePanel("Save design", DesignFolder, id + ".pcepal", "json");
            if (string.IsNullOrEmpty(path)) return;
            var d = new Design
            {
                PalId = id, BasePalId = BaseId, Author = _author.value, Version = _version.value, OutputFolder = _out.value, AreaId = AreaId,
                TribeMode = _tribe.index,
                Texts = new NewPalTexts { DisplayName = _display.value ?? "", BossPrefix = _bossPrefix.value ?? "", PartnerSkillName = _partner.value ?? "", FirstSpawnDesc = _first.value ?? "", LongDesc = _long.value ?? "" },
                IncludeBoss = _boss.value, Habitat = _habitat.value, BossSpawns = _bossSpawns.value, RegularSpawns = _regularSpawns.value,
                BossLvMin = _bossLvMin.value, BossLvMax = _bossLvMax.value, RegLvMin = _regLvMin.value, RegLvMax = _regLvMax.value, NumMin = _numMin.value, NumMax = _numMax.value,
                ExtraAreas = AllAreaIds().Skip(1).ToList(),
                Row = _editor?.CurrentRow, Drops = _editor?.BuildDropRow(), Pairs = _editor?.CombiPairs ?? new List<CombiPair>(), SelfBreedPair = _editor?.SelfBreedPair ?? true,
                ModModelMod = _modModel?.Mod, ModModelRowKey = _modModel?.RowKey,
            };
            try
            {
                File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(d, Newtonsoft.Json.Formatting.Indented, PalSchemaMod.SerializerSettings));
                _status.text = "Design saved: " + path + "\nLoad design brings every field on this page back; Export package writes the mod folder.";
                _status.style.color = UiKit.Success;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _status.text = "Could not save the design: " + e.Message; _status.style.color = UiKit.Danger;
            }
        }

        private void LoadDesign()
        {
            var path = AppHost.Current.OpenFilePanel("Load design", Directory.Exists(DesignFolder) ? DesignFolder : "", "json");
            if (string.IsNullOrEmpty(path)) return;
            Design d;
            try { d = Newtonsoft.Json.JsonConvert.DeserializeObject<Design>(File.ReadAllText(path), PalSchemaMod.SerializerSettings); }
            catch (Exception e) { _status.text = "Not a design file: " + e.Message; _status.style.color = UiKit.Danger; return; }
            if (d == null || string.IsNullOrEmpty(d.PalId)) { _status.text = "Not a design file (no PalId)."; _status.style.color = UiKit.Danger; return; }

            _id.SetValueWithoutNotify(d.PalId);
            if (!string.IsNullOrEmpty(d.BasePalId) && _data.Pals.TryGetValue(d.BasePalId, out var basePal))
            {
                ApplyBaseNumber(basePal.ZukanIndex.ToString(CultureInfo.InvariantCulture));
                var idx = _baseEntryKeys.IndexOf(d.BasePalId);
                if (idx >= 0) _baseLetter.SetValueWithoutNotify(_baseLetter.choices[idx]);
            }
            _tribe.SetValueWithoutNotify(_tribe.choices[Math.Max(0, Math.Min(_tribe.choices.Count - 1, d.TribeMode))]);
            _display.SetValueWithoutNotify(d.Texts?.DisplayName ?? ""); _bossPrefix.SetValueWithoutNotify(d.Texts?.BossPrefix ?? "");
            _partner.SetValueWithoutNotify(d.Texts?.PartnerSkillName ?? ""); _first.SetValueWithoutNotify(d.Texts?.FirstSpawnDesc ?? ""); _long.SetValueWithoutNotify(d.Texts?.LongDesc ?? "");
            if (!string.IsNullOrEmpty(d.AreaId)) ApplyArea(d.AreaId);
            _boss.SetValueWithoutNotify(d.IncludeBoss); _habitat.SetValueWithoutNotify(d.Habitat);
            _bossSpawns.SetValueWithoutNotify(d.BossSpawns); _regularSpawns.SetValueWithoutNotify(d.RegularSpawns);
            _bossLvMin.SetValueWithoutNotify(d.BossLvMin); _bossLvMax.SetValueWithoutNotify(d.BossLvMax); _regLvMin.SetValueWithoutNotify(d.RegLvMin); _regLvMax.SetValueWithoutNotify(d.RegLvMax);
            _numMin.SetValueWithoutNotify(d.NumMin > 0 ? d.NumMin : 1); _numMax.SetValueWithoutNotify(d.NumMax > 0 ? d.NumMax : 2);
            _extraAreaList.Clear(); _extraAreas.Clear();
            foreach (var extra in d.ExtraAreas ?? new List<string>()) AddExtraArea(extra);
            if (!string.IsNullOrEmpty(d.Author)) _author.SetValueWithoutNotify(d.Author);
            if (!string.IsNullOrEmpty(d.Version)) _version.SetValueWithoutNotify(d.Version);
            if (!string.IsNullOrEmpty(d.OutputFolder)) _out.SetValueWithoutNotify(d.OutputFolder);
            RefreshIdHint(); RefreshBaseHints(); SyncEditorIdentity();
            _editor?.ApplyDesign(d.Row, d.Drops, d.Pairs, d.SelfBreedPair);
            var modNote = "";
            if (!string.IsNullOrEmpty(d.ModModelMod))
            {
                if (!_useMods.value) _useMods.value = true;   // scans THIS PC's mod folders
                var bp = _scan?.Blueprints.FirstOrDefault(b => string.Equals(b.Mod, d.ModModelMod, StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(b.RowKey, d.ModModelRowKey, StringComparison.OrdinalIgnoreCase));
                if (bp != null) SetModModel(_scan.Choose(bp));
                else { SetModModel(null); modNote = $"\nThe design used {d.ModModelMod} : {d.ModModelRowKey}, which is not installed on this PC; the model is the base Pal's own blueprint."; }
            }
            else SetModModel(null);
            _status.text = "Design loaded: " + path + modNote; _status.style.color = modNote.Length > 0 ? UiKit.Warning : UiKit.Success;
        }

        private bool Validate(out PalSchemaPackage package, out NewPalPlan plan)
        {
            plan = BuildPlan();
            package = NewPalBuilder.Build(plan, _data, out var report);
            var problems = report.Ok ? PackageValidator.Validate(package, plan, _data) : new List<PackageProblem>();
            var errors = problems.Where(p => p.Severity == PackageProblem.Error).ToList();
            var warnings = problems.Where(p => p.Severity != PackageProblem.Error).ToList();

            _report.Clear();
            foreach (var e in report.Errors) _report.Add(Note("builder: " + e, UiKit.Danger));
            foreach (var e in errors) _report.Add(Note(e.ToString(), UiKit.Danger));
            foreach (var w in warnings) _report.Add(Note(w.ToString(), UiKit.Warning));
            foreach (var line in report.Lines) _report.Add(Note(line, UiKit.Muted));
            if (report.Untested.Count > 0)
            {
                _report.Add(Note("UNTESTED until you run it in game:", UiKit.Warning));
                foreach (var u in report.Untested) _report.Add(Note("  - " + u, UiKit.Warning));
            }
            var ok = report.Ok && errors.Count == 0;
            var files = ok ? package.ToFiles(plan.ModName) : new Dictionary<string, string>();
            _status.text = ok
                ? $"Valid: {files.Count} PalSchema files ({string.Join(", ", files.Keys.Select(k => k.Split('/')[0]).Distinct())}), {warnings.Count} warning(s)."
                : $"{report.Errors.Count + errors.Count} problem(s) must be fixed before export.";
            _status.style.color = ok ? UiKit.Success : UiKit.Danger;
            return ok;
        }

        private void Export()
        {
            if (!Validate(out var package, out var plan)) return;
            try
            {
                var root = Path.Combine(_out.value, plan.PalId);
                var displayName = string.IsNullOrWhiteSpace(_display.value) ? plan.PalId : _display.value;
                var info = ModPackager.BuildInfo(displayName, plan.PalId, _author.value, string.IsNullOrWhiteSpace(_version.value) ? "1.0.0" : _version.value,
                    logicMods: plan.Route == ModelRoute.B_CustomMeshPak);
                if (plan.ModModel?.Package != null && !info.Dependencies.Contains(plan.ModModel.Package))
                    info.Dependencies.Add(plan.ModModel.Package);   // the model comes from that mod (rule 10)
                var written = ModPackager.Write(root, info, package, plan.ModName);
                _status.text = $"Exported {written.Count} files. Mod folder: {root}\n" + string.Join("\n", written) +
                               "\nAdd a thumbnail.png, then upload with Palworld Mod Uploader, or copy PalSchema/ to Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\" + plan.PalId + "\\ to test. Nothing was installed by this tool." +
                               (plan.ModModel == null ? "" : plan.ModModel.Package != null
                                   ? $"\nModel from {plan.ModModel.Label}: Info.json lists {plan.ModModel.Package} as a dependency."
                                   : $"\nModel from {plan.ModModel.Label}: {plan.ModModel.Mod} is not a Workshop package here; tell users to install it.");
                _status.style.color = UiKit.Success;
                Debug.Log($"[PalCreationEngine] New Pal by Area exported {plan.PalId} to {root}");
                AppHost.Current.Reveal(root);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _status.text = "Could not write the package: " + e.Message;
                _status.style.color = UiKit.Danger;
            }
        }
    }
}
