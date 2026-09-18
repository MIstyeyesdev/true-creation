using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Export;
using PalCreationEngine.Lookup;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalCreationEngine.UI
{
    /// <summary>
    /// The Pal editor, built as a plain VisualElement tree with no dependency
    /// on UnityEditor, so the editor window and the runtime app host the same
    /// view rather than two that drift apart.
    /// </summary>
    public sealed class PalEditorView : VisualElement
    {
        private readonly GameData _data;
        private readonly Action<string> _log;

        /// <summary>
        /// Supplied by the host to open a browser in a real separate window.
        /// The editor sets this to DataBrowserWindow.Show; when nothing sets it
        /// the browser opens as a modal overlay instead, so the runtime app
        /// still works.
        /// </summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> OpenBrowser;

        private PalMonsterParameterRow _row;
        private string _palName = "NewPal";
        private string _size = "M";
        private string _bpClass = "";
        /// <summary>Row key of the Pal whose model (and, when base_rows.json has it, own row) is borrowed.</summary>
        private string _borrowedPalId;
        /// <summary>What RebuildRow last seeded from, for the status line and the host.</summary>
        private string _seededFrom = "";
        /// <summary>
        /// True when hosted inside another panel (New Pal by Area): no header, no own
        /// ScrollView, no NPC/Export pages, no Model Size/Companion sections, and the
        /// Name and Borrow-model rows are hidden and driven by the host through SetIdentity.
        /// </summary>
        private readonly bool _embedded;
        private Action<string> _applyBorrowedModel;
        private VisualElement _nameRow;
        // Mirrors of row fields that used to be write-only controls: after a re-seed from a base
        // Pal's own row they must show the row's values, not a fixed Normal/None/FourLegged.
        private DropdownField _element1Drop, _element2Drop, _genusDrop, _bestJobDrop;
        private readonly List<(IntegerField Field, string Name)> _boundInts = new();
        private Toggle _ignoreCombi, _selfPair;
        private VisualElement _pairList, _pairsBlock;
        private Label _tribeConsequence;
        private bool _ownTribe = true;
        private readonly List<PairUi> _pairs = new();

        private sealed class PairUi
        {
            public VisualElement Row;
            public UiKit.SearchPicker TribeA, TribeB;
            public DropdownField GenderA, GenderB;
        }
        private readonly List<(UiKit.SearchPicker Picker, VisualElement Row)> _passivePickers = new();

        private readonly Dictionary<string, SliderInt> _workSliders = new();
        private readonly List<VisualElement> _statRows = new();
        private readonly Dictionary<string, IntegerField> _statFields = new();
        private readonly List<LootSlotUi> _lootSlots = new();

        private Label _statusLabel;
        private VisualElement _problemList;
        private VisualElement _suffixHintRow;
        private VisualElement _indexRow;
        private UiKit.SearchPicker _indexPicker;
        private DropdownField _suffixDropdown;
        /// <summary>The letter picked from the selected number's slot list; null = that number's next free letter.</summary>
        private string _chosenZukanSuffix;
        /// <summary>
        /// The Paldex number chosen in the editor. Kept outside the row because
        /// RebuildRow (any size or model change) builds a fresh row, which would
        /// otherwise silently reset the number the field still shows.
        /// </summary>
        private int? _chosenZukanIndex;
        private TextField _outputPath;
        private Label _statSummary;
        private readonly PalScale _scale = new PalScale();
        private readonly CompanionSetup _companion = new CompanionSetup();
        private Label _companionAdvisories;
        private Label _companionBehaviour;
        private Toggle _companionEnabled, _companionAutoSpawn;
        private Label _scaleAdvisories;
        private FloatField _capsuleHeightField, _capsuleRadiusField;
        private DropdownField _sizeField;
        private VisualElement _sizeRow;
        private Toggle _unlockSizes;

        private sealed class LootSlotUi
        {
            public UiKit.SearchPicker Item;
            public FloatField Rate;
            public IntegerField Min;
            public IntegerField Max;
        }

        /// <summary>
        /// The 13 work-suitability fields paired with in-game names. The suffixes
        /// are not guessable from the labels -- "Handling" is MonsterFarm,
        /// "Planting" is Seeding -- so the mapping is spelled out.
        ///
        /// Oil Extraction exists in the table but is absent from the Pal detail
        /// screen in game and is 0 on every shipped Pal, so it is marked rather
        /// than presented as an ordinary job.
        /// </summary>
        /// <summary>
        /// Highest WorkSuitabilityRank any base object requires (DT_MapObjectAssignData: 9 objects
        /// at 10, e.g. Lab_Fire_10). Vanilla Pals ship at most 8; condensing and the work handbooks
        /// make up the difference in game.
        /// </summary>
        private const int GameWorkRankCeiling = 10;

        private static readonly (string Display, string Field, bool PlayerFacing)[] WorkJobs =
        {
            ("Kindling", "EmitFlame", true),
            ("Watering", "Watering", true),
            ("Planting", "Seeding", true),
            ("Generating Elec.", "GenerateElectricity", true),
            ("Handiwork", "Handcraft", true),
            ("Gathering", "Collection", true),
            ("Lumbering", "Deforest", true),
            ("Mining", "Mining", true),
            ("Medicine Prod.", "ProductMedicine", true),
            ("Cooling", "Cool", true),
            ("Transporting", "Transport", true),
            ("Farming (Ranch)", "MonsterFarm", true),
            ("Oil Extraction", "OilExtraction", false),
        };

        private static readonly (string Field, string Label)[] EditableStats =
        {
            ("Hp", "HP"),
            ("MeleeAttack", "Melee Attack"),
            ("ShotAttack", "Shot Attack"),
            ("Defense", "Defense"),
            ("Support", "Support"),
            ("Stamina", "Stamina"),
            ("WalkSpeed", "Walk Speed"),
            ("RunSpeed", "Run Speed"),
            ("RideSprintSpeed", "Ride Sprint"),
            ("TransportSpeed", "Transport Speed"),
            ("Price", "Price"),
        };

        public PalEditorView(GameData data, Action<string> log = null, bool embedded = false)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _log = log ?? Debug.Log;
            _embedded = embedded;

            style.flexGrow = embedded ? 0 : 1;
            style.backgroundColor = UiKit.Background;

            if (!_data.IsLoaded)
            {
                Add(BuildDataMissingNotice());
                return;
            }

            RebuildRow();

            // Hosted inside another panel's ScrollView there is no scroll view of its
            // own: a nested one collapses to nothing or fights the outer one for the wheel.
            VisualElement host = this;
            if (!embedded)
            {
                var scroll = new ScrollView { style = { flexGrow = 1 } };
                scroll.contentContainer.style.paddingLeft = 12;
                scroll.contentContainer.style.paddingRight = 12;
                scroll.contentContainer.style.paddingTop = 10;
                scroll.contentContainer.style.paddingBottom = 10;
                Add(scroll);
                scroll.Add(BuildHeader());
                host = scroll;
            }

            var tabs = new UiKit.Tabs();
            tabs.AddPage("Pal", BuildPalPage());
            tabs.AddPage("Loot Drops", BuildLootPage());
            if (!embedded)
            {
                // The host exports through the new-Pal route (NewPalBuilder); the legacy raw/
                // export and the NPC page (nothing exported yet) stay with the standalone tab.
                tabs.AddPage("NPC", BuildNpcPage());
                tabs.AddPage("Export", BuildExportPage());
            }
            host.Add(tabs);
        }

        // ---- host API: New Pal by Area embeds these sections ---------------

        /// <summary>The live row: the borrowed Pal's own row with the edits made in the sections.</summary>
        public PalMonsterParameterRow CurrentRow => _row;

        /// <summary>The Paldex number chosen here (or followed from the borrowed Pal); null before any.</summary>
        public int? ChosenZukanIndex => _chosenZukanIndex;

        /// <summary>What the current row was seeded from ("BadCatgirl's own row" or "the M-size medians").</summary>
        public string SeededFrom => _seededFrom;

        /// <summary>Raised after the row is re-seeded (size or model change), so a host can refresh its notes.</summary>
        public event Action RowRebuilt;

        /// <summary>
        /// Host-driven identity. New Pal by Area's Internal id and Base Pal fields replace the Name
        /// and Borrow-model rows here. Changing the id only renames; changing the base Pal re-seeds
        /// the row from that Pal (stats, elements, size, Paldex number), which discards edits.
        /// </summary>
        /// <summary>
        /// The host's tribe choice. Own tribe: the pairs below are what makes this Pal breedable and
        /// two of them breed true. Base's tribe: the game already has the base's pairs and same-tribe
        /// rule, both of which produce the BASE, so the pair list is switched off and says why.
        /// </summary>
        public void SetTribeMode(bool ownTribe)
        {
            _ownTribe = ownTribe;
            RefreshTribeConsequence();
        }

        private void RefreshTribeConsequence()
        {
            if (_tribeConsequence == null) return;
            var bp = BorrowedPal();
            var baseName = bp != null ? _data.LabelForPal(bp.RowKey) : "the base Pal";
            if (_ownTribe)
            {
                _tribeConsequence.text =
                    $"THIS PAL'S BREEDING WITH ITS OWN TRIBE: two {_palName} together breed {_palName}. " +
                    $"Any other way to breed it must be a fixed pair below; without one it can only come from CombiRank breeding (if Ignore normal breeding is off). " +
                    $"Default pair = {baseName} + a partner, the way every vanilla variant is bred (81 of 85 lettered Pals).";
                _tribeConsequence.style.color = UiKit.Text;
            }
            else
            {
                _tribeConsequence.text =
                    $"THIS PAL'S BREEDING WITH {baseName.ToUpperInvariant()}'S TRIBE: two {_palName} together breed {baseName}, not {_palName}. " +
                    $"{baseName}'s fixed pairs apply to it but they produce {baseName}'s children. A fixed pair naming this tribe would also name {baseName}, so the list below is off. " +
                    $"The only way to get {_palName} is CombiRank breeding, and only if Ignore normal breeding is off. UNTESTED in game.";
                _tribeConsequence.style.color = UiKit.Warning;
            }
            _pairsBlock?.SetEnabled(_ownTribe);
            if (!_ownTribe) _selfPair?.SetValueWithoutNotify(false);
            else if (_selfPair != null && !_selfPair.value) _selfPair.SetValueWithoutNotify(true);
        }

        public void SetIdentity(string palId, string basePalId)
        {
            if (!string.IsNullOrWhiteSpace(palId))
            {
                _palName = palId.Trim();
                if (_row != null) _row.Tribe = PalFactory.Prefixed("EPalTribeID", _palName);
            }
            if (string.IsNullOrEmpty(basePalId) || basePalId == _borrowedPalId || !_data.Pals.ContainsKey(basePalId)) return;
            _applyBorrowedModel?.Invoke(basePalId);
            ResetDefaultPair();
            RefreshTribeConsequence();
            RefreshBreedingPreview();
        }

        /// <summary>Fixed breeding pairs entered in the Tribe, breeding and gender section (NewPalPlan.CombiPairs).</summary>
        public List<CombiPair> CombiPairs => _pairs
            .Select(p => new CombiPair
            {
                ParentTribeA = TribeFromPick(p.TribeA.Value),
                ParentTribeB = TribeFromPick(p.TribeB.Value),
                ParentGenderA = p.GenderA.value,
                ParentGenderB = p.GenderB.value,
            })
            .Where(p => p.ParentTribeA.Length > 0 || p.ParentTribeB.Length > 0)
            .ToList();

        /// <summary>Whether "this Pal + this Pal -> this Pal" is also written (breeds true).</summary>
        public bool SelfBreedPair => _selfPair == null || _selfPair.value;

        /// <summary>
        /// Restores a saved design into the sections: the row (all 90 fields), its Paldex slot, the loot
        /// slots and the fixed breeding pairs. Call after SetIdentity so the model is already borrowed.
        /// </summary>
        public void ApplyDesign(PalMonsterParameterRow row, PalDropItemRow drops, List<CombiPair> pairs, bool selfPair)
        {
            if (row != null)
            {
                _row = PalFactory.Clone(row);
                _chosenZukanIndex = row.ZukanIndex;
                _chosenZukanSuffix = row.ZukanIndexSuffix ?? "";
                SyncAllFields();
                if (_indexPicker != null && _chosenZukanIndex.HasValue) _indexPicker.SetValueSilently(IndexLabel(_chosenZukanIndex.Value));
                RebuildSuffixChoices();
                RefreshZukanHints();
            }
            for (var i = 0; i < _lootSlots.Count; i++)
            {
                var n = i + 1;
                var slot = _lootSlots[i];
                var item = drops == null ? null : typeof(PalDropItemRow).GetField("ItemId" + n)?.GetValue(drops) as string;
                slot.Item.SetValueSilently(string.IsNullOrEmpty(item) || item == "None" ? "" : _data.LabelForItem(item));
                slot.Rate.SetValueWithoutNotify(drops == null ? 0f : (typeof(PalDropItemRow).GetField("Rate" + n)?.GetValue(drops) as float?) ?? 0f);
                slot.Min.SetValueWithoutNotify(drops == null ? 0 : (typeof(PalDropItemRow).GetField("min" + n)?.GetValue(drops) as int?) ?? 0);
                slot.Max.SetValueWithoutNotify(drops == null ? 0 : (typeof(PalDropItemRow).GetField("Max" + n)?.GetValue(drops) as int?) ?? 0);
            }
            if (_pairList != null)
            {
                _pairList.Clear();
                _pairs.Clear();
                foreach (var p in pairs ?? new List<CombiPair>())
                {
                    AddPairRow(PickLabelForTribe(p.ParentTribeA), PickLabelForTribe(p.ParentTribeB));
                    var ui = _pairs[_pairs.Count - 1];
                    ui.GenderA.SetValueWithoutNotify(Bare(p.ParentGenderA, "None"));
                    ui.GenderB.SetValueWithoutNotify(Bare(p.ParentGenderB, "None"));
                }
                _selfPair?.SetValueWithoutNotify(selfPair);
            }
        }

        /// <summary>The "27B - Bastet_Ice" label for a tribe, "new - <this Pal>" for its own, or the bare tribe.</summary>
        private string PickLabelForTribe(string tribe)
        {
            var bare = Bare(tribe, "");
            if (bare.Length == 0) return "";
            if (bare == _palName) return $"new - {_palName}";
            var pal = _data.Pals.Values.FirstOrDefault(p => Bare(p.Tribe, "") == bare && !p.IsBoss && !p.IsRaidBoss && !p.IsTowerBoss && p.ZukanIndex > 0);
            return pal == null ? bare : $"{pal.ZukanIndex}{pal.ZukanIndexSuffix} - {_data.LabelForPal(pal.RowKey)}".Replace("/", " + ");
        }

        /// <summary>The Loot Drops page as one DT_PalDropItem row, or null when every slot is blank.</summary>
        public PalDropItemRow BuildDropRow()
        {
            var drops = new PalDropItemRow();
            var used = 0;
            for (var i = 0; i < _lootSlots.Count; i++)
            {
                var slot = _lootSlots[i];
                var itemId = GameData.IdFromLabel(slot.Item.Value);
                if (string.IsNullOrWhiteSpace(itemId)) continue;

                used++;
                var n = i + 1;
                typeof(PalDropItemRow).GetField("ItemId" + n)?.SetValue(drops, itemId);
                typeof(PalDropItemRow).GetField("Rate" + n)?.SetValue(drops, slot.Rate.value);
                typeof(PalDropItemRow).GetField("min" + n)?.SetValue(drops, slot.Min.value);
                typeof(PalDropItemRow).GetField("Max" + n)?.SetValue(drops, slot.Max.value);
            }
            return used > 0 ? drops : null;
        }

        /// <summary>The Pal whose model is borrowed, or null before one is picked.</summary>
        private PalSummary BorrowedPal() =>
            _borrowedPalId != null && _data.Pals.TryGetValue(_borrowedPalId, out var pal) ? pal : null;

        // ---- pages -----------------------------------------------------

        /// <summary>
        /// Opens a dataset browser. Uses the host's window if one was supplied,
        /// otherwise covers this view with a modal overlay -- either way the
        /// caller just gets the picked ID back.
        /// </summary>
        private void ShowBrowser(
            string title, List<BrowserColumn> columns, List<string[]> rows, Action<string> onPick)
        {
            if (OpenBrowser != null)
            {
                OpenBrowser(title, columns, rows, onPick);
                return;
            }

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.backgroundColor = UiKit.Background;

            overlay.Add(new DataBrowser(title, columns, rows,
                picked => { onPick?.Invoke(picked); overlay.RemoveFromHierarchy(); },
                () => overlay.RemoveFromHierarchy()));

            Add(overlay);
            overlay.BringToFront();
        }

        /// <summary>A compact button that opens a dataset browser beside a field.</summary>
        private Button BrowseButton(string tooltip, Action onClick)
        {
            var button = new Button(onClick) { text = "≡", tooltip = tooltip };
            button.style.width = 22;
            button.style.height = 18;
            button.style.marginLeft = 2;
            button.style.marginRight = 0;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            button.style.flexShrink = 0;
            button.style.backgroundColor = UiKit.Panel;
            button.style.color = UiKit.Text;
            UiKit.SetBorder(button, UiKit.Border, 1);
            return button;
        }

        private VisualElement BuildPalPage()
        {
            var page = UiKit.Column();
            page.Add(UiKit.Columns(BuildIdentitySection(), BuildStatsSection()));
            page.Add(UiKit.Columns(BuildWorkSection(), BuildPassiveSection()));
            page.Add(BuildTribeBreedingSection());
            if (_embedded)
            {
                // Route A never patches the borrowed vanilla class (pce-01), and both of these
                // sections are blueprint edits keyed by that class - so they are not offered here.
                var note = new Label("Model Size and Companion are not offered in New Pal by Area: they patch the borrowed vanilla blueprint class, which Route A never does (pce-01). A custom mesh needs its own class in a pak (Route B).");
                note.style.color = UiKit.Muted;
                note.style.fontSize = 10;
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginBottom = 6;
                page.Add(note);
            }
            else
            {
                page.Add(UiKit.Columns(BuildScaleSection(), BuildCompanionSection()));
            }
            return page;
        }

        private VisualElement BuildIdentitySection()
        {
            var section = UiKit.Section("Identity", out var body);

            var nameField = new TextField { value = _palName };
            nameField.RegisterValueChangedCallback(evt =>
            {
                _palName = evt.newValue;
                // Legacy raw/ route only: it writes no translations/, so it keeps writing the
                // literal name (a known defect of that route - ModCompare). The new-Pal route
                // keeps "None" and writes PAL_NAME_<id> in translations/, as vanilla (323 of
                // 323 rows) and the working mod (50 of 50) do.
                if (!_embedded) _row.OverrideNameTextID = _palName;
                _row.Tribe = PalFactory.Prefixed("EPalTribeID", _palName);
            });
            _nameRow = UiKit.Row("Name", nameField, "row key + in-game name");
            if (_embedded) _nameRow.style.display = DisplayStyle.None; // driven by the host's Internal id
            body.Add(_nameRow);

            var bpPicker = new UiKit.SearchPicker(
                () => _data.BasePals.Select(p => _data.LabelForPal(p.RowKey)),
                _data.Names.Available
                    ? "search by in-game or internal name"
                    : "internal IDs only (in-game names not loaded from display_names.json: reinstall True Creation)");

            var bpRow = UiKit.Row("Borrow model", bpPicker, "required", fieldWidth: 178);
            if (_embedded) bpRow.style.display = DisplayStyle.None; // driven by the host's Base Pal

            void ApplyBorrowedModel(string rowKey)
            {
                if (!_data.Pals.TryGetValue(rowKey, out var pal)) return;

                _bpClass = pal.BpClass;
                _borrowedPalId = rowKey;
                _size = pal.Size;
                // The scale edit targets the BORROWED model's Blueprint -- that
                // is the asset whose mesh component actually gets resized.
                _scale.BlueprintObjectPath = _data.BlueprintPathFor(pal.BpClass);
                RefreshSizeChoices();
                RefreshScaleLock();
                bpPicker.SetValueSilently(_data.LabelForPal(rowKey));
                RebuildRow();
                SyncAllFields();
                UiKit.SetRowHint(bpRow, $"{pal.BpClass} - size {pal.Size}", UiKit.Success);
                // The Paldex number follows the borrowed Pal: a new Pal on
                // BluePlatypus's model is a BluePlatypus variant in the Paldex,
                // so it takes 5 and 5's next free letter - the working mod's
                // rule (BadCatgirl_Ground = BadCatgirl's number + 'C'). The
                // number can still be changed by hand afterwards.
                if (pal.ZukanIndex > 0) ApplyIndex(pal.ZukanIndex);
            }

            _applyBorrowedModel = ApplyBorrowedModel;
            bpPicker.OnSelected += label => ApplyBorrowedModel(GameData.IdFromLabel(label));

            bpPicker.AddTrailing(BrowseButton("Browse all Pals in a table", () =>
            {
                var (columns, rows) = BrowserDatasets.Pals(_data, includeVariants: true);
                ShowBrowser("Pals - pick a model to borrow", columns, rows, ApplyBorrowedModel);
            }));
            body.Add(bpRow);

            // Only the sizes this model is actually used at, unless test mode
            // unlocks the rest. A model family ships 1-3 sizes; offering all five
            // implies a choice the model does not support.
            _sizeField = new DropdownField(AvailableSizes(), 0);
            _sizeField.RegisterValueChangedCallback(evt =>
            {
                _size = evt.newValue;
                RebuildRow();
                SyncAllFields();
                // The variant behind a family changes with size, and so do its
                // scale and collision -- re-mirror rather than keep the old one.
                RefreshScaleLock();
                SetStatus($"Re-seeded from {_seededFrom}.", UiKit.Muted);
            });
            _sizeRow = UiKit.Row("Size", _sizeField, "pick a model to see its sizes");
            body.Add(_sizeRow);

            // Foundation for later custom-model work: the constraint is real for
            // in-game assets, but a user importing their own mesh would need it
            // lifted. Off by default, and labelled as untested.
            _unlockSizes = new Toggle { value = false };
            _unlockSizes.RegisterValueChangedCallback(_ => RefreshSizeChoices());
            body.Add(UiKit.Row("Test mode: all sizes", _unlockSizes,
                "ignores what the model ships at - untested"));

            // Initial values come from the seeded row (the borrowed Pal's own row when known)
            // and SyncAllFields re-reads them after every re-seed, so the dropdowns show what
            // the row holds and ships.
            _element1Drop = EnumDropdown("EPalElementType", Bare(_row.ElementType1, "Normal"),
                v => _row.ElementType1 = PalFactory.Prefixed("EPalElementType", v));
            body.Add(UiKit.Row("Element 1", _element1Drop));
            _element2Drop = EnumDropdown("EPalElementType", Bare(_row.ElementType2, "None"),
                v => _row.ElementType2 = PalFactory.Prefixed("EPalElementType", v));
            body.Add(UiKit.Row("Element 2", _element2Drop));
            _genusDrop = EnumDropdown("EPalGenusCategoryType", Bare(_row.GenusCategory, "FourLegged"),
                v => _row.GenusCategory = PalFactory.Prefixed("EPalGenusCategoryType", v));
            body.Add(UiKit.Row("Genus", _genusDrop));
            body.Add(StatRow("Rarity", "Rarity"));

            // A Paldex slot is a number plus a letter, and the letter has no
            // meaning of its own: B at 5 is BluePlatypus_Fire, B at 7 is
            // FlyingManta_Thunder. So only the NUMBER is chosen here; the letter
            // is derived from what that number already holds (vanilla: base ''
            // plus one 'B' variant; the verified working mod's variants take 'C').
            var zukanNote = new Label(
                "ZUKAN is the row's own name for the Paldex: ZukanIndex is the Paldex number, ZukanIndexSuffix the letter (zukan = picture book). " +
                "Paldex slot = number + letter: 5 is BluePlatypus, 5B is its Fire variant. " +
                "The letter means nothing by itself - B at one number is a different Pal from B at another. " +
                "Pick the number of the Pal yours is a variant of; the suffix list then holds only that Pal's slots: " +
                "the letters already in use there (with who holds them) and this Pal's next free letter, selected by default. " +
                "An unused number makes it a new Paldex entry with no letter.");
            zukanNote.style.color = UiKit.Muted;
            zukanNote.style.fontSize = 10;
            zukanNote.style.whiteSpace = WhiteSpace.Normal;
            zukanNote.style.marginTop = 4;
            zukanNote.style.marginBottom = 3;
            body.Add(zukanNote);

            _indexPicker = new UiKit.SearchPicker(
                () => _data.ZukanSlots
                    .Select(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
                    .OrderBy(i => i)
                    .Select(IndexLabel)
                    .Append(IndexLabel(_data.NextFreeZukanIndex())),
                "\u25be lists every number and who holds it; or type a number and press Enter");
            _indexRow = UiKit.Row("Paldex Index (ZukanIndex)", _indexPicker, string.Empty, fieldWidth: 178);
            _indexPicker.OnSelected += label => ApplyIndex(ParseLeadingInt(label));
            _indexPicker.OnCommitted += text => ApplyIndex(ParseLeadingInt(text));
            _indexPicker.AddTrailing(BrowseButton("Browse every Paldex slot and who holds it", () =>
            {
                var (columns, rows) = BrowserDatasets.PaldexSlots(_data);
                ShowBrowser("Paldex slots", columns, rows,
                    picked => ApplyIndex(ParseLeadingInt(picked)));
            }));
            body.Add(_indexRow);

            // The SELECTED Pal's slots, filled per number: every letter already in use
            // there with who holds it, then this Pal's next free letter (selected by
            // default). A letter from any other number never appears in this list.
            _suffixDropdown = new DropdownField(new List<string> { "(none)" }, 0);
            _suffixDropdown.RegisterValueChangedCallback(evt => ApplySuffixChoice(evt.newValue));
            _suffixHintRow = UiKit.Row("Paldex Suffix (ZukanIndexSuffix)", _suffixDropdown, string.Empty, fieldWidth: 300);
            body.Add(_suffixHintRow);

            // First number: the borrowed model's Pal when one is set (variant of
            // it), otherwise the first unused number (new Paldex entry).
            var modelPal = BorrowedPal();
            var modelIndex = modelPal != null && modelPal.ZukanIndex > 0 ? modelPal.ZukanIndex : (int?)null;
            ApplyIndex(_chosenZukanIndex ?? modelIndex ?? _row.ZukanIndex ?? 0);

            return section;
        }

        private static int ParseLeadingInt(string label)
        {
            var text = (label ?? "").Trim();
            var negative = text.StartsWith("-", StringComparison.Ordinal);
            var digits = new string(text.Skip(negative ? 1 : 0).TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return 0;
            return negative ? -value : value;
        }

        /// <summary>"5 - BluePlatypus" for a held number, "205 - (unused) new Paldex entry" for a free one.</summary>
        private string IndexLabel(int index)
        {
            if (index == -1) return "-1 - no Paldex entry";
            var slot = _data.ZukanSlot(index);
            return slot == null
                ? $"{index} - (unused) new Paldex entry"
                : $"{index} - {_data.LabelForPal(slot.Primary)}";
        }

        /// <summary>
        /// Sets the Paldex number; the letter is derived, never chosen. 0 (blank
        /// or non-numeric text) means the first unused number. -1 is the vanilla
        /// boss convention: no Paldex entry at all.
        /// </summary>
        private void ApplyIndex(int index)
        {
            if (index == 0) index = _data.NextFreeZukanIndex();
            if (index < -1) index = -1;
            _chosenZukanIndex = index;
            _chosenZukanSuffix = null; // a new number: back to that number's free letter
            ApplyZukanToRow();
            // The field shows the slot it actually holds. Filtering by that text
            // is no longer a problem: the caret opens the full list regardless.
            _indexPicker?.SetValueSilently(IndexLabel(index));
            RebuildSuffixChoices();
            RefreshZukanHints();
        }

        /// <summary>
        /// Writes the chosen number and letter into the row (the letter picked from
        /// the slot list, or that number's next free letter). Called from ApplyIndex
        /// and from RebuildRow, because a re-seed replaces the row on every size or
        /// model change.
        /// </summary>
        private void ApplyZukanToRow()
        {
            if (_row == null || !_chosenZukanIndex.HasValue) return;
            var index = _chosenZukanIndex.Value;
            _row.ZukanIndex = index;
            _row.ZukanIndexSuffix = index == -1 ? "" : _chosenZukanSuffix ?? _data.NextFreeZukanSuffix(index) ?? "";
        }

        /// <summary>
        /// Fills the suffix list with the selected number's slots and nothing else:
        /// each letter in use there, grouped, with the rows that hold it; then this
        /// Pal's next free letter. The row's current letter is the selected entry.
        /// </summary>
        private void RebuildSuffixChoices()
        {
            if (_suffixDropdown == null || _row == null) return;
            var index = _row.ZukanIndex ?? 0;
            var choices = new List<string>();
            if (index == -1)
            {
                choices.Add("(none) - no Paldex entry");
            }
            else
            {
                var slot = _data.ZukanSlot(index);
                if (slot != null)
                    foreach (var g in slot.Entries.GroupBy(e => e.Suffix ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
                        choices.Add($"{(g.Key == "" ? "(none)" : g.Key)} - {string.Join(", ", g.Select(e => e.RowKey))} (taken)");
                var free = slot == null ? "" : _data.NextFreeZukanSuffix(index);
                if (free != null)
                    choices.Add(free == "" ? $"(none) - free: first Pal at {index}" : $"{free} - free: this Pal's next letter");
            }
            if (choices.Count == 0) choices.Add("(none) - every letter at this number is taken");
            // DropdownField reads "/" as a submenu separator.
            choices = choices.Select(c => c.Replace("/", " + ")).ToList();
            var current = _row.ZukanIndexSuffix ?? "";
            var selected = choices.FirstOrDefault(c => SuffixFromLabel(c) == current) ?? choices[choices.Count - 1];
            _suffixDropdown.choices = choices;
            _suffixDropdown.SetValueWithoutNotify(selected);
        }

        private static string SuffixFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            var dash = label.IndexOf(" - ", StringComparison.Ordinal);
            var key = (dash > 0 ? label.Substring(0, dash) : label).Trim();
            return key == "(none)" ? "" : key;
        }

        private void ApplySuffixChoice(string label)
        {
            if (_row == null) return;
            _chosenZukanSuffix = SuffixFromLabel(label);
            _row.ZukanIndexSuffix = _chosenZukanSuffix;
            RefreshZukanHints();
        }

        private void RefreshZukanHints()
        {
            if (_indexRow == null || _suffixHintRow == null || _row == null) return;

            var index = _row.ZukanIndex ?? 0;
            var suffix = _row.ZukanIndexSuffix ?? "";

            if (index == -1)
            {
                UiKit.SetRowHint(_indexRow, "no Paldex entry (vanilla boss rows use -1)", UiKit.Warning);
                UiKit.SetRowHint(_suffixHintRow, "no letter: there is no number to order against", UiKit.Muted);
                return;
            }

            var slot = _data.ZukanSlot(index);
            if (slot == null)
            {
                UiKit.SetRowHint(_indexRow, $"{index} is unused - this Pal is a new Paldex entry", UiKit.Success);
                UiKit.SetRowHint(_suffixHintRow, $"no letter: the first Pal at {index} is its base form", UiKit.Success);
                return;
            }

            // What the letters mean AT THIS NUMBER, so "B" is never read as a
            // global option: at 5 it is the Fire variant, at 7 the Thunder one.
            var taken = string.Join(", ", slot.Entries
                .OrderBy(e => e.Suffix ?? "", StringComparer.Ordinal)
                .Select(e => $"{index}{e.Suffix} = {e.RowKey}"));
            UiKit.SetRowHint(_indexRow,
                $"held by {_data.LabelForPal(slot.Primary)} - taken: {taken}", UiKit.Warning);

            var holders = slot.Entries.Where(e => (e.Suffix ?? "") == suffix).Select(e => e.RowKey).ToList();
            var free = _data.NextFreeZukanSuffix(index);
            if (holders.Count > 0)
            {
                UiKit.SetRowHint(_suffixHintRow,
                    $"{index}{suffix} is the slot of {string.Join(", ", holders)} - two rows in one Paldex slot, UNTESTED in game" +
                    (free != null ? $"; the free letter here is {free}" : "; every letter B-Z here is taken"),
                    UiKit.Danger);
                return;
            }

            var letters = string.Join(", ", slot.Entries
                .Select(e => string.IsNullOrEmpty(e.Suffix) ? "none" : e.Suffix)
                .Distinct());
            UiKit.SetRowHint(_suffixHintRow,
                $"{index}{suffix} is free at {index} ({letters} taken)", UiKit.Success);
        }

        /// <summary>
        /// Model size. Separate from the Size enum on purpose: Size drives VFX
        /// scale and trap eligibility, while what actually resizes the Pal is
        /// RelativeScale3D on the borrowed Blueprint's mesh component.
        /// </summary>
        /// <summary>Sizes offered for the current model, or all of them in test mode.</summary>
        private List<string> AvailableSizes()
        {
            if (_unlockSizes != null && _unlockSizes.value)
                return _data.SizeDefaultsBySize.Keys.OrderBy(k => k).ToList();

            var sizes = _data.SizesForModel(_bpClass);
            return sizes is { Count: > 0 }
                ? sizes
                : _data.SizeDefaultsBySize.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// Rebuilds the size list for the borrowed model and keeps the current
        /// pick if the model still supports it.
        /// </summary>
        private void RefreshSizeChoices()
        {
            if (_sizeField == null) return;

            var choices = AvailableSizes();
            _sizeField.choices = choices;

            if (!choices.Contains(_size))
            {
                _size = choices.Count > 0 ? choices[0] : "M";
                RebuildRow();
                SyncAllFields();
            }
            _sizeField.SetValueWithoutNotify(_size);

            var model = _data.Model(_bpClass);
            UiKit.SetRowHint(_sizeRow,
                model == null
                    ? "pick a model to see its sizes"
                    : $"{model.Family} ships at {string.Join(", ", model.FamilySizes)}"
                      + (_unlockSizes != null && _unlockSizes.value ? " - unlocked" : ""),
                _unlockSizes != null && _unlockSizes.value ? UiKit.Warning : UiKit.Muted);
        }

        /// <summary>Seeds the scale panel from the borrowed model's real values.</summary>
        /// <summary>
        /// Mirrors the selected model's real values into the panel. This is the
        /// default state: the fields show what the game actually ships for that
        /// model at that size, and stay locked until the user overrides them.
        /// </summary>
        private void SyncScaleToModel()
        {
            if (_scaleField == null) return;

            var model = CurrentModel();

            if (model == null)
            {
                // Blank rather than 1.0/0/0, which would read as real data.
                _scaleField.SetValueWithoutNotify(0f);
                _capsuleHeightField?.SetValueWithoutNotify(0f);
                _capsuleRadiusField?.SetValueWithoutNotify(0f);
                _scale.SetUniform(1f);
                _scale.CapsuleHalfHeight = null;
                _scale.CapsuleRadius = null;
                if (_modelSizeSource != null)
                    _modelSizeSource.text = "Pick a model above to load its real values.";
                UiKit.SetRowHint(_meshScaleRow, string.Empty, UiKit.Muted);
                return;
            }

            _scale.SetUniform(model.ScaleX);
            _scaleField.SetValueWithoutNotify(model.ScaleX);

            _scale.CapsuleHalfHeight = model.CapsuleHalfHeight;
            _scale.CapsuleRadius = model.CapsuleRadius;
            _capsuleHeightField?.SetValueWithoutNotify(model.CapsuleHalfHeight ?? 0f);
            _capsuleRadiusField?.SetValueWithoutNotify(model.CapsuleRadius ?? 0f);

            if (model.CapsuleHalfHeight.HasValue)
                _scale.MeshOffsetZ = -model.CapsuleHalfHeight.Value;

            var capsule = model.CapsuleHalfHeight.HasValue
                ? $"capsule {model.CapsuleHalfHeight:0.#} / {model.CapsuleRadius:0.#}"
                : "no capsule on its Blueprint";

            if (_modelSizeSource != null)
            {
                _modelSizeSource.text =
                    $"Loaded from {model.BpClass} at size {_size}: mesh scale {model.ScaleX:0.##}, {capsule}." +
                    (model.BlueprintFound ? "" : "  (Blueprint not in the export - values may be incomplete.)");
                _modelSizeSource.style.color = model.BlueprintFound ? UiKit.Success : UiKit.Warning;
            }

            UiKit.SetRowHint(_meshScaleRow,
                Math.Abs(model.ScaleX - 1f) < 0.0001f
                    ? "no scale override on this Blueprint"
                    : $"{model.BpClass} is authored at {model.ScaleX:0.##}",
                UiKit.Muted);

            if (_scaleReference != null)
            {
                var match = PalScale.References
                    .Select((r, i) => (r, i))
                    .FirstOrDefault(x => Math.Abs(x.r.Value - model.ScaleX) < 0.0001f);
                _scaleReference.SetValueWithoutNotify(
                    match.r.Label ?? $"(model's own - {model.ScaleX:0.##})");
            }

            RefreshScaleAdvisories();
        }

        private VisualElement BuildScaleSection()
        {
            var section = UiKit.Section("Model Size", out var body);

            var intro = new Label(
                "The Size dropdown above does NOT resize the model - it scales particle " +
                "effects and decides which traps work. Real size is a mesh scale on the " +
                "borrowed Pal's Blueprint.");
            intro.style.color = UiKit.Muted;
            intro.style.fontSize = 10;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            body.Add(intro);

            // Locked to the real file by default. Everything below shows what the
            // chosen model ACTUALLY ships with, per size, until the user
            // deliberately takes over.
            _overrideModelSize = new Toggle { value = false };
            _overrideModelSize.RegisterValueChangedCallback(_ => RefreshScaleLock());
            body.Add(UiKit.Row("Override these values", _overrideModelSize,
                "off = mirror the selected model exactly"));

            _modelSizeSource = new Label("Pick a model above to load its real values.");
            _modelSizeSource.style.color = UiKit.Muted;
            _modelSizeSource.style.fontSize = 10;
            _modelSizeSource.style.whiteSpace = WhiteSpace.Normal;
            _modelSizeSource.style.marginBottom = 4;
            body.Add(_modelSizeSource);

            _scaleField = new FloatField();
            _scaleField.RegisterValueChangedCallback(evt => ApplyScale(evt.newValue));
            _meshScaleRow = UiKit.Row("Mesh scale", _scaleField, string.Empty);
            body.Add(_meshScaleRow);

            _capsuleHeightField = new FloatField();
            _capsuleHeightField.RegisterValueChangedCallback(evt =>
                _scale.CapsuleHalfHeight = evt.newValue > 0 ? evt.newValue : (float?)null);
            body.Add(UiKit.Row("Capsule half-height", _capsuleHeightField, "collision"));

            _capsuleRadiusField = new FloatField();
            _capsuleRadiusField.RegisterValueChangedCallback(evt =>
                _scale.CapsuleRadius = evt.newValue > 0 ? evt.newValue : (float?)null);
            var radiusRow = UiKit.Row("Capsule radius", _capsuleRadiusField, string.Empty);
            body.Add(radiusRow);

            var fit = UiKit.SecondaryButton("Scale collision to match", () =>
            {
                var model = CurrentModel();
                if (model?.CapsuleHalfHeight == null || model.CapsuleRadius == null)
                {
                    SetStatus(
                        string.IsNullOrWhiteSpace(_bpClass)
                            ? "Borrow a model first - there is no capsule to scale from."
                            : $"{_bpClass} has no capsule in its Blueprint, so there is nothing to scale from.",
                        UiKit.Warning);
                    return;
                }

                var (hh, r, z) = PalScaleBuilder.SuggestCollision(
                    model.CapsuleHalfHeight.Value, model.CapsuleRadius.Value, _scale.ScaleX);
                _scale.CapsuleHalfHeight = hh;
                _scale.CapsuleRadius = r;
                _scale.MeshOffsetZ = z;
                _capsuleHeightField.SetValueWithoutNotify(hh);
                _capsuleRadiusField.SetValueWithoutNotify(r);
                SetStatus($"Capsule scaled from {model.BpClass}: {hh:0.#} / {r:0.#}", UiKit.Success);
                RefreshScaleAdvisories();
            });
            fit.style.height = 20;
            fit.style.minWidth = 0;
            fit.style.marginLeft = 6;
            fit.style.marginRight = 0;
            radiusRow.Add(fit);

            // Recommendations, for a Pal being built from scratch rather than
            // mirrored from an existing one. Only usable once override is on.
            var refLabels = PalScale.References.Select(r => r.Label).ToList();
            _scaleReference = new DropdownField(refLabels, 3);
            _scaleReference.RegisterValueChangedCallback(evt =>
            {
                var match = PalScale.References[refLabels.IndexOf(evt.newValue)];
                _scale.SetUniform(match.Value);
                _scaleField.SetValueWithoutNotify(match.Value);
                ApplyScale(match.Value);
            });
            body.Add(UiKit.Row("Reference", _scaleReference,
                "suggestions for a Pal built from scratch", fieldWidth: 230));

            _scaleAdvisories = new Label(string.Empty);
            _scaleAdvisories.style.color = UiKit.Warning;
            _scaleAdvisories.style.fontSize = 10;
            _scaleAdvisories.style.whiteSpace = WhiteSpace.Normal;
            _scaleAdvisories.style.marginTop = 6;
            body.Add(_scaleAdvisories);

            RefreshScaleLock();
            return section;
        }

        /// <summary>The model variant matching the borrowed family AND chosen size.</summary>
        private ModelInfo CurrentModel()
        {
            var direct = _data.Model(_bpClass);
            if (direct == null) return null;

            // Bastet is XS at 40/16; BOSS_Bastet is the same mesh at S, 62/24 and
            // 1.5x. Which one applies depends on the size picked above.
            return _data.ModelForFamilySize(direct.Family, _size) ?? direct;
        }

        /// <summary>Enables the fields only when the user has taken over.</summary>
        private void RefreshScaleLock()
        {
            if (_embedded) return; // no Model Size section when hosted (pce-01)
            var unlocked = _overrideModelSize != null && _overrideModelSize.value;

            _scaleField?.SetEnabled(unlocked);
            _capsuleHeightField?.SetEnabled(unlocked);
            _capsuleRadiusField?.SetEnabled(unlocked);
            _scaleReference?.SetEnabled(unlocked);

            if (!unlocked) SyncScaleToModel();
            RefreshScaleAdvisories();
        }

        private FloatField _scaleField;
        private VisualElement _meshScaleRow;
        private Toggle _overrideModelSize;
        private Label _modelSizeSource;
        private DropdownField _scaleReference;

        private void ApplyScale(float value)
        {
            _scale.SetUniform(value);
            RefreshScaleAdvisories();
        }

        private void RefreshScaleAdvisories()
        {
            if (_scaleAdvisories == null) return;
            var notes = PalScaleBuilder.Advisories(_scale);
            _scaleAdvisories.text = notes.Count == 0
                ? string.Empty
                : string.Join("\n", notes.Select(n => "- " + n));
        }

        /// <summary>
        /// Funnel companion -- the helper that appears beside the player while
        /// the Pal is in the party. A third in-party system, independent of both
        /// passive skills and the activated partner skill.
        /// </summary>
        private VisualElement BuildCompanionSection()
        {
            var section = UiKit.Section("Companion (funnel)", out var body);

            if (!_data.Companions.Available)
            {
                body.Add(new Label("companions.json is missing or empty in True Creation's data folder: reinstall True Creation to restore it "
                                   + "(developers: python Tools/PalCreationEngine/generate_lookups.py)")
                {
                    style = { color = UiKit.Warning, fontSize = 10, whiteSpace = WhiteSpace.Normal },
                });
                return section;
            }

            var intro = new Label(
                "Daedream's mechanic: a helper spawns beside you and acts on its own while " +
                "the Pal is in your party. Configured on the Pal's Blueprint, so no .pak. " +
                "The class you pick fixes the element and attack - there is no element field.");
            intro.style.color = UiKit.Muted;
            intro.style.fontSize = 10;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            body.Add(intro);

            _companionEnabled = new Toggle { value = false };
            _companionEnabled.RegisterValueChangedCallback(_ => RefreshCompanionAdvisories());
            body.Add(UiKit.Row("Give a companion", _companionEnabled,
                $"{_data.Companions.Classes.Count} funnel classes exist"));

            var picker = new UiKit.SearchPicker(
                () => _data.Companions.Classes.Keys.OrderBy(k => k),
                "pick a funnel character class");
            var classRow = UiKit.Row("Funnel class", picker, string.Empty, fieldWidth: 178);
            picker.OnSelected += cls =>
            {
                if (!_data.Companions.Classes.TryGetValue(cls, out var info)) return;
                _companion.FunnelCharacterClass = info.AssetPath;
                UiKit.SetRowHint(classRow, info.Describe(), UiKit.Muted);
                DescribeCompanion(info);
                RefreshCompanionAdvisories();
            };
            picker.AddTrailing(BrowseButton("Browse funnel classes and what they fire", () =>
            {
                var (columns, rows) = BrowserDatasets.FunnelClasses(_data);
                ShowBrowser("Funnel companions", columns, rows, picked =>
                {
                    picker.SetValueSilently(picked);
                    if (_data.Companions.Classes.TryGetValue(picked, out var info))
                    {
                        _companion.FunnelCharacterClass = info.AssetPath;
                        UiKit.SetRowHint(classRow, info.Describe(), UiKit.Muted);
                    }
                    RefreshCompanionAdvisories();
                });
            }));
            body.Add(classRow);

            _companionAutoSpawn = new Toggle { value = true };
            _companionAutoSpawn.RegisterValueChangedCallback(evt =>
            {
                _companion.AutoSpawn = evt.newValue;
                RefreshCompanionAdvisories();
            });
            body.Add(UiKit.Row("Auto-spawn in party", _companionAutoSpawn,
                "off = the Centaurs' on-command form"));

            _companionBehaviour = new Label(string.Empty);
            _companionBehaviour.style.color = UiKit.Text;
            _companionBehaviour.style.fontSize = 11;
            _companionBehaviour.style.whiteSpace = WhiteSpace.Normal;
            _companionBehaviour.style.marginTop = 6;
            body.Add(_companionBehaviour);

            _companionAdvisories = new Label(string.Empty);
            _companionAdvisories.style.color = UiKit.Warning;
            _companionAdvisories.style.fontSize = 10;
            _companionAdvisories.style.whiteSpace = WhiteSpace.Normal;
            _companionAdvisories.style.marginTop = 6;
            body.Add(_companionAdvisories);
            RefreshCompanionAdvisories();

            return section;
        }

        /// <summary>
        /// Plain-language account of what the chosen companion will do, since a
        /// class name alone says nothing.
        /// </summary>
        private void DescribeCompanion(FunnelClass info)
        {
            if (_companionBehaviour == null) return;

            var w = info.WazaInfo;
            var module = info.SkillModule ?? "";

            string behaviour;
            if (module.Contains("CollectItem"))
                behaviour = "Picks up nearby items automatically. Does not fight.";
            else if (w != null)
                behaviour = $"Targets what you aim at and fires a {w.Element} {w.Category} attack " +
                            $"for {w.Power} power every {w.CoolTime}s, from {w.MinRange}-{w.MaxRange} units.";
            else
                behaviour = "Attack inherited from the base funnel class - the exported data does " +
                            "not say which, so behaviour will match whatever that class does.";

            var invulnerable = info.CanBeDamaged == false
                ? " The companion itself cannot be damaged."
                : "";

            _companionBehaviour.text = behaviour + invulnerable;
        }

        private void RefreshCompanionAdvisories()
        {
            if (_companionAdvisories == null) return;

            if (_companionEnabled == null || !_companionEnabled.value)
            {
                _companionAdvisories.text = string.Empty;
                return;
            }

            var notes = CompanionBuilder.Advisories(_companion);
            _companionAdvisories.text = string.Join("\n", notes.Select(n => "- " + n));
        }

        private VisualElement BuildStatsSection()
        {
            var section = UiKit.Section("Stats", out var body);

            // Named reference points, so a raw number can be read as "roughly
            // this shipped Pal" instead of meaning nothing on its own.
            var tierLabels = _data.Benchmarks.Tiers
                .Select(t => $"{t.Label} - like {_data.LabelForPal(t.ExampleRowKey)}")
                .ToList();

            if (tierLabels.Count > 0)
            {
                var preset = new DropdownField(tierLabels, 2);
                preset.RegisterValueChangedCallback(evt =>
                    ApplyTier(_data.Benchmarks.Tiers[tierLabels.IndexOf(evt.newValue)]));
                body.Add(UiKit.Row("Auto-fill from", preset, "copies that Pal's profile", fieldWidth: 200));
            }

            foreach (var (field, label) in EditableStats)
                body.Add(StatRow(label, field));

            _statSummary = new Label(string.Empty);
            _statSummary.style.color = UiKit.Muted;
            _statSummary.style.fontSize = 11;
            _statSummary.style.marginTop = 6;
            _statSummary.style.whiteSpace = WhiteSpace.Normal;
            body.Add(_statSummary);
            RefreshStatSummary();

            return section;
        }

        private void ApplyTier(StatTier tier)
        {
            foreach (var kv in tier.Stats) SetRowField(kv.Key, kv.Value);
            foreach (var kv in tier.Movement) SetRowField(kv.Key, kv.Value);

            SyncAllFields();
            SetStatus(
                $"Filled from {_data.LabelForPal(tier.ExampleRowKey)} - a {tier.Label.ToLowerInvariant()} " +
                $"tier Pal (stat total {tier.StatTotal}).", UiKit.Success);
        }

        private VisualElement BuildWorkSection()
        {
            var section = UiKit.Section("Work Suitability", out var body);

            // Best job leads: it is the icon the game shows, so it reads as the
            // headline rather than a footnote under thirteen sliders.
            var bestJobs = WorkJobs.Where(j => j.PlayerFacing).Select(j => j.Display).ToList();
            var best = _bestJobDrop = new DropdownField(bestJobs, 4);
            best.RegisterValueChangedCallback(evt =>
            {
                var match = WorkJobs.First(j => j.Display == evt.newValue);
                _row.BestWorkSuitability = PalFactory.Prefixed("EPalWorkSuitability", match.Field);
            });
            body.Add(UiKit.Row("Best job", best, "icon shown in game", labelWidth: 116, fieldWidth: 150));

            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = UiKit.Border;
            divider.style.marginTop = 4;
            divider.style.marginBottom = 6;
            body.Add(divider);

            foreach (var (display, field, playerFacing) in WorkJobs)
            {
                var fieldName = "WorkSuitability_" + field;
                var schema = _data.Field(fieldName);
                // shippedMax is the highest value any vanilla row carries (8 for ten work
                // types, 7 Transport, 4 Ranch). The game itself goes to 10: base objects in
                // DT_MapObjectAssignData require WorkSuitabilityRank up to 10 (Lab_Fire_10),
                // reached from the shipped value by condensing and the handbook items. The
                // slider allows the game's ceiling; the hint says what vanilla Pals ship at.
                var shippedMax = (int)(schema?.RealMax ?? 5);
                var sliderMax = Math.Max(shippedMax, GameWorkRankCeiling);

                // showInputField gives a typed box beside the slider, so a value
                // can be entered exactly rather than dragged to.
                var slider = new SliderInt(0, sliderMax) { value = 0, showInputField = true };
                slider.RegisterValueChangedCallback(evt => SetRowField(fieldName, evt.newValue));
                _workSliders[field] = slider;

                var hint = !playerFacing
                    ? "not shown in game's Pal screen"
                    : $"vanilla Pals ship 0-{shippedMax}; base objects need up to {GameWorkRankCeiling}";

                var row = UiKit.Row(display, slider, hint, labelWidth: 116, fieldWidth: 150);
                if (!playerFacing) row.style.opacity = 0.6f;
                body.Add(row);
            }

            return section;
        }

        private VisualElement BuildPassiveSection()
        {
            var section = UiKit.Section("Passive Skills", out var body);

            for (var slot = 1; slot <= 4; slot++)
            {
                var index = slot;
                var picker = new UiKit.SearchPicker(
                    () => _data.PassiveSkills.Keys,
                    $"search {_data.PassiveSkills.Count} passive skills");

                var row = UiKit.Row($"Passive {slot}", picker, string.Empty,
                    labelWidth: 80, fieldWidth: 178);

                void ApplyPassive(string selected)
                {
                    picker.SetValueSilently(selected);
                    SetRowField("PassiveSkill" + index, selected);
                    UiKit.SetRowHint(row,
                        _data.PassiveSkills.TryGetValue(selected, out var skill)
                            ? skill.Describe()
                            : "not a known skill ID",
                        _data.PassiveSkills.ContainsKey(selected) ? UiKit.Muted : UiKit.Warning);
                }

                picker.OnSelected += ApplyPassive;
                _passivePickers.Add((picker, row));

                picker.AddTrailing(BrowseButton("Browse passive skills by rank and effect", () =>
                {
                    var (columns, rows) = BrowserDatasets.PassiveSkills(_data);
                    ShowBrowser($"Passive skills - slot {index}", columns, rows, ApplyPassive);
                }));

                body.Add(row);
            }

            return section;
        }

        // ---- tribe, breeding, gender ------------------------------------

        private static Label SectionNote(string text)
        {
            var note = new Label(text);
            note.style.color = UiKit.Muted;
            note.style.fontSize = 10;
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 5;
            return note;
        }

        /// <summary>An IntegerField bound to a row field, clamped, re-read by SyncAllFields after a re-seed.</summary>
        private VisualElement IntRow(string label, string fieldName, string hint, int min, int max)
        {
            var field = new IntegerField { value = GetRowField(fieldName) is int i ? i : 0 };
            field.RegisterValueChangedCallback(evt =>
            {
                var v = Math.Max(min, Math.Min(max, evt.newValue));
                if (v != evt.newValue) field.SetValueWithoutNotify(v);
                SetRowField(fieldName, v);
            });
            _boundInts.Add((field, fieldName));
            if (fieldName == "CombiRank" || fieldName == "CombiDuplicatePriority") field.RegisterValueChangedCallback(_ => RefreshBreedingPreview());
            return UiKit.Row(label, field, hint, labelWidth: 132, fieldWidth: 80);
        }

        private static Label Heading(string text)
        {
            var l = new Label(text);
            l.style.color = UiKit.Text;
            l.style.fontSize = 11;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 6;
            l.style.marginBottom = 2;
            return l;
        }

        private Label _breedingPreview;

        /// <summary>
        /// Tribe, breeding and gender, written for the person making the Pal. Every number is
        /// measured from the index; the breeding rule is Cyber Mendel's (Palworld 1.0 datamine).
        /// </summary>
        private VisualElement BuildTribeBreedingSection()
        {
            var section = UiKit.Section("Tribe, breeding and gender", out var body);

            body.Add(Heading("Tribe = the species family this Pal is filed under"));
            body.Add(SectionNote("Three things use it: fixed breeding pairs match the parents' tribes; two parents of one tribe always breed that tribe's base Pal; some base objects accept only listed tribes."));
            body.Add(SectionNote("Own tribe (the working mod's way): in no pair until you add one; two of this Pal breed this Pal. Base's tribe: counts as the base everywhere - two of this Pal breed the BASE."));

            body.Add(Heading("How a child is decided (1.0 rule, in this order)"));
            body.Add(SectionNote("1. A fixed pair for the two parent tribes wins.  2. Same tribe: the tribe's base Pal.  3. Otherwise: target = (rankA + rankB + 1) / 2 rounded down, child = the Pal whose CombiRank is nearest; a tie goes to the higher Duplicate priority. A Pal with Ignore normal breeding on is never a rank result."));
            body.Add(IntRow("CombiRank", "CombiRank", "vanilla 10 (WorldTreeDragon) to 9999; the working mod keeps the base's", 0, 99999));
            body.Add(IntRow("Duplicate priority", "CombiDuplicatePriority", "tie-breaker; vanilla = CombiRank x 100 (291 of 300)", 0, 9999999));
            _ignoreCombi = new Toggle { value = _row?.IgnoreCombi == true };
            _ignoreCombi.RegisterValueChangedCallback(evt => { if (_row != null) _row.IgnoreCombi = evt.newValue; RefreshBreedingPreview(); });
            body.Add(UiKit.Row("Ignore normal breeding", _ignoreCombi, "on = only fixed pairs can produce it (vanilla: the 10 legendaries)", labelWidth: 132, fieldWidth: 24));
            _breedingPreview = SectionNote("");
            body.Add(_breedingPreview);

            body.Add(Heading("What the child inherits, and from what"));
            body.Add(SectionNote("From THIS row (the species): stats, elements, size, work suitability, partner skill, learnset, drops, Paldex slot - everything on this page. " +
                                 "From the PARENTS: passive skills (the game rolls the child's up to 4 from the parents' passives) and gender odds from this row's Male probability. " +
                                 "Nothing else on the parents carries over: level, IVs and condensing are the child's own."));

            body.Add(Heading("Gender"));
            body.Add(SectionNote("Rolled per individual from Male probability below; the spawner cannot set it (its entry holds only PalId, level range and count). A fixed pair below can require a parent's gender."));
            body.Add(IntRow("Male probability %", "MaleProbability", "vanilla 50 for 255 of 300; 10 QueenBee and SoldierBee; 90 KingAlpaca", 0, 100));

            body.Add(Heading("Fixed breeding pairs: parent A + parent B always give the child"));
            if (_embedded)
            {
                _tribeConsequence = SectionNote("");
                _tribeConsequence.style.marginTop = 2;
                body.Add(_tribeConsequence);
                _pairsBlock = new VisualElement();
                _pairList = new VisualElement();
                _pairsBlock.Add(_pairList);
                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
                buttons.Add(UiKit.SecondaryButton("Add pair", () => AddPairRow(null, null)));
                _pairsBlock.Add(buttons);
                _selfPair = new Toggle { value = true };
                _pairsBlock.Add(UiKit.Row("Breeds true", _selfPair, "also writes this Pal + this Pal -> this Pal", labelWidth: 132, fieldWidth: 24));
                body.Add(_pairsBlock);
                ResetDefaultPair();
                RefreshTribeConsequence();
            }
            else
            {
                body.Add(SectionNote("Written by New Pal by Area. This tab's legacy Export writes the row only, so no pairs are offered here."));
            }
            RefreshBreedingPreview();

            return section;
        }

        /// <summary>
        /// Who breeds into this Pal by rank, computed with the 1.0 rule over the vanilla base-form
        /// Pals (fixed vanilla pairs are not in the lookups, so a pair they override is not excluded).
        /// </summary>
        private void RefreshBreedingPreview()
        {
            if (_breedingPreview == null || _row == null) return;
            var rank = _row.CombiRank ?? 0;
            if (_row.IgnoreCombi == true)
            {
                _breedingPreview.text = $"Outcome preview: Ignore normal breeding is on, so no rank pairing produces {_palName}; only the fixed pairs below do.";
                _breedingPreview.style.color = UiKit.Warning;
                return;
            }
            var pool = _data.Pals.Values
                .Where(p => p.ZukanIndex > 0 && !p.IsVariant && _data.BaseRow(RawTables.PalMonsterParameterTable, p.RowKey) is { } r && r["IgnoreCombi"]?.ToObject<bool?>() != true)
                .Select(p => (Id: p.RowKey, Rank: _data.BaseRow(RawTables.PalMonsterParameterTable, p.RowKey)["CombiRank"]?.ToObject<int?>() ?? 0,
                              Dup: _data.BaseRow(RawTables.PalMonsterParameterTable, p.RowKey)["CombiDuplicatePriority"]?.ToObject<int?>() ?? 0))
                .Where(x => x.Rank > 0).ToList();
            if (pool.Count == 0) { _breedingPreview.text = "Outcome preview needs base_rows.json."; return; }
            var mine = (Id: _palName, Rank: rank, Dup: _row.CombiDuplicatePriority ?? 0);
            var all = pool.Concat(new[] { mine }).OrderBy(x => x.Rank).ToList();
            (string Id, int Rank, int Dup) Nearest(int target)
            {
                var best = all[0]; var bestD = Math.Abs(best.Rank - target);
                foreach (var c in all)
                {
                    var d = Math.Abs(c.Rank - target);
                    if (d < bestD || (d == bestD && c.Dup > best.Dup)) { best = c; bestD = d; }
                }
                return best;
            }
            var hits = new List<string>();
            var count = 0;
            for (var i = 0; i < pool.Count; i++)
                for (var j = i; j < pool.Count; j++)
                {
                    var target = (pool[i].Rank + pool[j].Rank + 1) / 2;
                    if (Nearest(target).Id != _palName) continue;
                    count++;
                    if (hits.Count < 6) hits.Add($"{_data.LabelForPal(pool[i].Id)} + {_data.LabelForPal(pool[j].Id)}");
                }
            var neighbours = all.Where(x => x.Id != _palName).OrderBy(x => Math.Abs(x.Rank - rank)).Take(2).Select(x => $"{_data.LabelForPal(x.Id)} {x.Rank}").ToList();
            _breedingPreview.text = count == 0
                ? $"Outcome preview at rank {rank}: no vanilla pair lands on {_palName} (nearest ranks: {string.Join(", ", neighbours)}); only fixed pairs below produce it."
                : $"Outcome preview at rank {rank}: {count} vanilla pairings breed {_palName}, e.g. {string.Join("; ", hits)}{(count > 6 ? "; ..." : "")}. Nearest ranks: {string.Join(", ", neighbours)}.";
            _breedingPreview.style.color = count == 0 ? UiKit.Warning : UiKit.Success;
        }

        /// <summary>"27 - Mau (Bastet)", "27B - Bastet_Ice", ... plus "new - <this Pal>".</summary>
        private IEnumerable<string> PalPickLabels()
        {
            yield return $"new - {_palName}";
            foreach (var n in _data.ZukanSlots.Keys.Select(k => int.Parse(k, CultureInfo.InvariantCulture)).OrderBy(x => x))
                foreach (var e in _data.ZukanSlot(n).Entries.OrderBy(e => e.Suffix ?? "", StringComparer.Ordinal))
                    if (_data.Pals.TryGetValue(e.RowKey, out var p) && !p.IsVariant)
                        yield return $"{n}{e.Suffix} - {_data.LabelForPal(e.RowKey)}".Replace("/", " + ");
        }

        /// <summary>The tribe of the picked row; the new Pal's own name for the "new" entry; a bare typed tribe passes through.</summary>
        private string TribeFromPick(string label)
        {
            label = (label ?? "").Trim();
            if (label.Length == 0) return "";
            if (label.StartsWith("new - ", StringComparison.Ordinal)) return _palName;
            var dash = label.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                var id = GameData.IdFromLabel(label.Substring(dash + 3));
                if (_data.Pals.TryGetValue(id, out var p)) return Bare(p.Tribe, id);
            }
            return label;
        }

        /// <summary>
        /// The vanilla-shaped default pair: the base Pal plus a partner of this Pal's first element
        /// (Bastet + Penguin gives Bastet_Ice; PinkCat + ElecCat gives the Electric variant). The
        /// partner is the lowest-numbered base-form Pal of that element that is not the base itself.
        /// </summary>
        private void ResetDefaultPair()
        {
            if (_pairList == null) return;
            _pairList.Clear();
            _pairs.Clear();
            var bp = BorrowedPal();
            if (bp == null) { AddPairRow(null, null); return; }
            var element = Bare(_row?.ElementType1, "None");
            var partner = _data.Pals.Values
                .Where(p => p.ZukanIndex > 0 && string.IsNullOrEmpty(p.ZukanIndexSuffix) && !p.IsVariant && p.RowKey != bp.RowKey
                            && string.Equals(Bare(p.Element1, ""), element, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.ZukanIndex).FirstOrDefault();
            AddPairRow($"{bp.ZukanIndex}{bp.ZukanIndexSuffix} - {_data.LabelForPal(bp.RowKey)}",
                partner != null ? $"{partner.ZukanIndex} - {_data.LabelForPal(partner.RowKey)}" : null);
        }

        private void AddPairRow(string tribeA, string tribeB)
        {
            // Parents are picked as Pals - number, then that Pal's own letter - never as bare
            // tribe names; the row's tribe is looked up from the picked row.
            IEnumerable<string> Tribes() => PalPickLabels();
            var ui = new PairUi();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 2;

            ui.TribeA = new UiKit.SearchPicker(Tribes, "parent A tribe");
            ui.TribeA.style.width = 200;
            ui.TribeA.SetValueSilently(tribeA ?? "");
            ui.GenderA = new DropdownField(new List<string> { "None", "Male", "Female" }, 0);
            ui.GenderA.style.width = 80;
            ui.TribeB = new UiKit.SearchPicker(Tribes, "parent B tribe");
            ui.TribeB.style.width = 200;
            ui.TribeB.SetValueSilently(tribeB ?? "");
            ui.GenderB = new DropdownField(new List<string> { "None", "Male", "Female" }, 0);
            ui.GenderB.style.width = 80;

            Label Sep(string text)
            {
                var l = new Label(text);
                l.style.color = UiKit.Muted;
                l.style.marginLeft = 4;
                l.style.marginRight = 4;
                l.style.marginTop = 3;
                return l;
            }

            var remove = new Button(() => { row.RemoveFromHierarchy(); _pairs.Remove(ui); }) { text = "x" };
            remove.style.width = 22;
            remove.style.marginLeft = 6;

            row.Add(ui.TribeA);
            row.Add(ui.GenderA);
            row.Add(Sep("+"));
            row.Add(ui.TribeB);
            row.Add(ui.GenderB);
            row.Add(Sep($"-> {_palName} (the child)"));
            row.Add(remove);
            ui.Row = row;
            _pairs.Add(ui);
            _pairList.Add(row);
        }

        private VisualElement BuildLootPage()
        {
            var page = UiKit.Column();
            var section = UiKit.Section(
                "Loot Drops - 10 slots (DT_PalDropItem_Common)", out var body);

            var header = new Label(
                "Ten slots is the table's own limit: the row has ItemId1..ItemId10 and no more. " +
                "Leave a slot's item blank to skip it.");
            header.style.color = UiKit.Muted;
            header.style.fontSize = 11;
            header.style.whiteSpace = WhiteSpace.Normal;
            header.style.marginBottom = 6;
            body.Add(header);

            for (var i = 1; i <= 10; i++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var caption = new Label(i.ToString(CultureInfo.InvariantCulture));
                caption.style.width = 20;
                caption.style.color = UiKit.Muted;
                row.Add(caption);

                var picker = new UiKit.SearchPicker(
                    () => _data.Items.Keys.Select(_data.LabelForItem),
                    $"search {_data.Items.Count} items");
                picker.style.width = 218;
                row.Add(picker);

                var slotNumber = i;
                picker.AddTrailing(BrowseButton("Browse items by type, rank and price", () =>
                {
                    var (columns, rows) = BrowserDatasets.Items(_data);
                    ShowBrowser($"Items - loot slot {slotNumber}", columns, rows,
                        picked => picker.SetValueSilently(picked));
                }));

                var rate = new FloatField { value = 0f };
                rate.style.width = 60;
                row.Add(WithCaption("rate %", rate));

                var min = new IntegerField { value = 0 };
                min.style.width = 44;
                row.Add(WithCaption("min", min));

                var max = new IntegerField { value = 0 };
                max.style.width = 44;
                row.Add(WithCaption("max", max));

                _lootSlots.Add(new LootSlotUi { Item = picker, Rate = rate, Min = min, Max = max });
                body.Add(row);
            }

            page.Add(section);
            return page;
        }

        private static VisualElement WithCaption(string caption, VisualElement field)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems = Align.Center;
            group.style.marginLeft = 8;

            var label = new Label(caption);
            label.style.color = UiKit.Muted;
            label.style.fontSize = 10;
            label.style.marginRight = 3;
            group.Add(label);
            group.Add(field);
            return group;
        }

        // ---- NPC page (fields only; nothing is exported yet) ------------

        private VisualElement BuildNpcPage()
        {
            var page = UiKit.Column();

            if (!_data.Npc.Available)
            {
                page.Add(NoticeBox(
                    "NPC data missing",
                    "npc.json is missing or empty in True Creation's data folder, so this tab stays empty. " +
                    "Reinstall True Creation to restore it.\n\n" +
                    "Developers: python Tools/PalCreationEngine/generate_lookups.py writes it " +
                    "(it reads DT_PalHumanParameter, DT_NPCTalkFlow and the ItemShop tables).",
                    UiKit.Warning));
                return page;
            }

            page.Add(NoticeBox(
                "Read-only for now",
                "These fields are wired to real game data so the values can be browsed " +
                "and planned, but nothing on this tab is written to a mod yet. The Pal " +
                "and Loot Drops tabs are the ones that export.",
                UiKit.Muted));

            page.Add(UiKit.Columns(BuildNpcIdentitySection(), BuildNpcTalkSection()));
            page.Add(UiKit.Columns(BuildNpcShopSection(), BuildNpcQuestSection()));
            return page;
        }

        private VisualElement BuildNpcIdentitySection()
        {
            var section = UiKit.Section("NPC Identity", out var body);

            var idField = new TextField { value = "WEO_EggCollector_01" };
            body.Add(UiKit.Row("CharacterID", idField, "row key in DT_PalHumanParameter",
                fieldWidth: 190));

            var modelPicker = new UiKit.SearchPicker(
                () => _data.Npc.Npcs.Keys.OrderBy(k => k),
                $"search {_data.Npc.Npcs.Count} NPCs");
            var modelRow = UiKit.Row("Borrow NPC model", modelPicker, "copies BPClass",
                fieldWidth: 168);

            void ApplyNpcModel(string id)
            {
                if (!_data.Npc.Npcs.TryGetValue(id, out var npc)) return;
                modelPicker.SetValueSilently(id);
                UiKit.SetRowHint(modelRow,
                    $"{npc.BpClass} - {(npc.CanTalk ? "can talk" : "CANNOT talk")}",
                    npc.CanTalk ? UiKit.Success : UiKit.Warning);
            }

            modelPicker.OnSelected += ApplyNpcModel;
            modelPicker.AddTrailing(BrowseButton("Browse NPCs, including who can talk", () =>
            {
                var (columns, rows) = BrowserDatasets.Npcs(_data);
                ShowBrowser("NPCs", columns, rows, ApplyNpcModel);
            }));
            body.Add(modelRow);

            body.Add(UiKit.Row("AI Response",
                EnumDropdownRaw("AIResponse", "VillageNPC"), "", fieldWidth: 190));
            body.Add(UiKit.Row("Sight Response",
                EnumDropdownRaw("AISightResponse", "Citizen"), "", fieldWidth: 190));
            body.Add(UiKit.Row("Organization",
                EnumDropdownRaw("EPalOrganizationType", "City"), "", fieldWidth: 190));

            var note = new Label(
                "None of these three control whether the NPC can be talked to. 205 NPCs " +
                "have exactly this combination and still have no [F] prompt - MobuVillager " +
                "and MobuCitizen are the townsfolk you walk past. See the Interaction panel.");
            note.style.color = UiKit.Muted;
            note.style.fontSize = 10;
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginTop = 6;
            body.Add(note);

            return section;
        }

        private VisualElement BuildNpcTalkSection()
        {
            var section = UiKit.Section("Interaction - the [F] Talk prompt", out var body);

            var headline = new Label(
                $"{_data.Npc.TalkableCount} of {_data.Npc.Npcs.Count} NPCs can be talked to. " +
                "The only difference is a DT_NPCTalkFlow row keyed by CharacterID.");
            headline.style.color = UiKit.Text;
            headline.style.fontSize = 11;
            headline.style.whiteSpace = WhiteSpace.Normal;
            headline.style.marginBottom = 6;
            body.Add(headline);

            var graphPicker = new UiKit.SearchPicker(
                () => _data.Npc.AvailableGraphs,
                $"{_data.Npc.AvailableGraphs.Count} existing conversations");
            var graphRow = UiKit.Row("Conversation", graphPicker,
                "FABP_CommonCaravan = standard merchant", fieldWidth: 168);

            void ApplyGraph(string graph)
            {
                graphPicker.SetValueSilently(graph);
                UiKit.SetRowHint(graphRow, _data.Npc.AssetPathForGraph(graph) ?? "", UiKit.Muted);
            }

            graphPicker.OnSelected += ApplyGraph;
            graphPicker.AddTrailing(BrowseButton("Browse conversations and who uses them", () =>
            {
                var (columns, rows) = BrowserDatasets.TalkFlows(_data);
                ShowBrowser("Conversations (DT_NPCTalkFlow)", columns, rows, ApplyGraph);
            }));
            body.Add(graphRow);

            var copyPicker = new UiKit.SearchPicker(
                () => _data.Npc.TalkFlows.Keys.OrderBy(k => k),
                "copy the conversation another NPC uses");
            var copyRow = UiKit.Row("Copy from NPC", copyPicker, "", fieldWidth: 190);
            copyPicker.OnSelected += npcId =>
            {
                var graph = _data.Npc.GraphFor(npcId);
                if (graph == null) return;
                graphPicker.SetValueSilently(graph);
                UiKit.SetRowHint(copyRow, $"uses {graph}", UiKit.Success);
                UiKit.SetRowHint(graphRow, _data.Npc.AssetPathForGraph(graph) ?? "", UiKit.Muted);
            };
            body.Add(copyRow);

            var warn = new Label(
                "A brand-new conversation cannot be made from JSON - conversations are " +
                "assets, and PalSchema edits tables and asset properties but cannot create " +
                "new assets. Reusing one of the 175 above works today; custom dialogue " +
                "needs a .pak shipped alongside the mod.");
            warn.style.color = UiKit.Warning;
            warn.style.fontSize = 10;
            warn.style.whiteSpace = WhiteSpace.Normal;
            warn.style.marginTop = 6;
            body.Add(warn);

            return section;
        }

        private VisualElement BuildNpcShopSection()
        {
            var section = UiKit.Section("Shop", out var body);

            var intro = new Label(
                "A new shop needs no new assets - three linked table rows:\n" +
                "  vendor component -> DT_ItemShopLotteryData -> DT_ItemShopCreateData");
            intro.style.color = UiKit.Muted;
            intro.style.fontSize = 10;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            body.Add(intro);

            // Both are free text -- a new shop invents new names -- but browsable,
            // so an existing lottery can be inspected or copied rather than guessed.
            var lotteryPicker = new UiKit.SearchPicker(
                () => _data.Npc.ShopLotteries.Keys.OrderBy(k => k),
                $"{_data.Npc.ShopLotteries.Count} existing, or type a new name");
            var lotteryRow = UiKit.Row("Lottery name", lotteryPicker,
                "goes on the vendor component", fieldWidth: 150);
            lotteryPicker.OnSelected += name =>
                UiKit.SetRowHint(lotteryRow,
                    _data.Npc.ShopLotteries.TryGetValue(name, out var groups)
                        ? "rolls: " + string.Join(", ", groups.Select(g => g.ShopGroupName))
                        : "new lottery name",
                    UiKit.Muted);
            lotteryPicker.AddTrailing(BrowseButton("Browse lotteries, their stock lists and weights", () =>
            {
                var (columns, rows) = BrowserDatasets.ShopLotteries(_data);
                ShowBrowser("Shop lotteries (DT_ItemShopLotteryData)", columns, rows,
                    picked => lotteryPicker.SetValueSilently(picked));
            }));
            body.Add(lotteryRow);

            var stockPicker = new UiKit.SearchPicker(
                () => _data.Npc.ShopStock.Keys.OrderBy(k => k),
                $"{_data.Npc.ShopStock.Count} existing, or type a new name");
            var stockRow = UiKit.Row("Stock list name", stockPicker,
                "DT_ItemShopCreateData row", fieldWidth: 150);
            stockPicker.OnSelected += name =>
                UiKit.SetRowHint(stockRow,
                    _data.Npc.ShopStock.TryGetValue(name, out var stock)
                        ? $"{stock.Count} items"
                        : "new stock list",
                    UiKit.Muted);
            stockPicker.AddTrailing(BrowseButton("Browse stock lists and their contents", () =>
            {
                var (columns, rows) = BrowserDatasets.ShopStockLists(_data);
                ShowBrowser("Shop stock lists (DT_ItemShopCreateData)", columns, rows,
                    picked => stockPicker.SetValueSilently(picked));
            }));
            body.Add(stockRow);

            var restock = new IntegerField { value = 120 };
            body.Add(UiKit.Row("Restock (minutes)", restock, "real minutes", fieldWidth: 170));

            // Alternate currency is how the Medal / Bounty / Arena shops work, and
            // is the obvious fit for an egg-trading merchant.
            var currency = new UiKit.SearchPicker(
                () => _data.Items.Keys.OrderBy(k => k),
                "blank = gold. Medal/Bounty shops use this");
            var currencyRow = UiKit.Row("Currency item", currency,
                $"{_data.Npc.ShopCurrencies.Count} shops use a custom one", fieldWidth: 150);
            currency.AddTrailing(BrowseButton("Browse items to use as currency", () =>
            {
                var (columns, rows) = BrowserDatasets.Items(_data);
                ShowBrowser("Items - shop currency", columns, rows,
                    picked => currency.SetValueSilently(picked));
            }));
            body.Add(currencyRow);

            var browse = new UiKit.SearchPicker(
                () => _data.Npc.ShopStock.Keys.OrderBy(k => k),
                $"inspect {_data.Npc.ShopStock.Count} existing stock lists");
            var browseRow = UiKit.Row("Copy stock from", browse, "", fieldWidth: 150);
            browse.AddTrailing(BrowseButton("Browse stock lists and their contents", () =>
            {
                var (columns, rows) = BrowserDatasets.ShopStockLists(_data);
                ShowBrowser("Shop stock lists (DT_ItemShopCreateData)", columns, rows,
                    picked => browse.SetValueSilently(picked));
            }));
            browse.OnSelected += shop =>
            {
                if (!_data.Npc.ShopStock.TryGetValue(shop, out var stock)) return;
                UiKit.SetRowHint(browseRow, $"{stock.Count} items", UiKit.Success);
                _shopStockList.Clear();
                foreach (var entry in stock.Take(14))
                {
                    var line = new Label(
                        $"  {entry.ItemId}  x{entry.ProductNum}" +
                        (entry.OverridePrice > 0 ? $"  @{entry.OverridePrice}" : "  @default"));
                    line.style.color = UiKit.Muted;
                    line.style.fontSize = 10;
                    _shopStockList.Add(line);
                }
                if (stock.Count > 14)
                {
                    var more = new Label($"  ... and {stock.Count - 14} more");
                    more.style.color = UiKit.Muted;
                    more.style.fontSize = 10;
                    _shopStockList.Add(more);
                }
            };
            body.Add(browseRow);

            _shopStockList = new VisualElement { style = { marginTop = 4 } };
            body.Add(_shopStockList);

            return section;
        }

        private VisualElement BuildNpcQuestSection()
        {
            var section = UiKit.Section("Quests", out var body);

            var intro = new Label(
                $"{_data.Npc.Quests.Count} quests in the game. A quest is a settings " +
                "container: title, description, a list of objective blocks, a reward, and " +
                "AutoOrderQuests - which is how quests chain.");
            intro.style.color = UiKit.Muted;
            intro.style.fontSize = 10;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            body.Add(intro);

            var questPicker = new UiKit.SearchPicker(
                () => _data.Npc.Quests.Keys.OrderBy(k => k),
                $"browse {_data.Npc.Quests.Count} existing quests");
            var questRow = UiKit.Row("Inspect quest", questPicker, "", fieldWidth: 190);
            questPicker.OnSelected += id =>
            {
                if (!_data.Npc.Quests.TryGetValue(id, out var quest)) return;
                UiKit.SetRowHint(questRow, quest.QuestType ?? "", UiKit.Muted);
            };
            body.Add(questRow);

            var chain = new Label(
                "Example chain, all of which the game already supports:\n" +
                "  talk to elder    -> QuestBranch, then OrderQuest\n" +
                "  craft the item   -> objective block with DetectItemName\n" +
                "  talk again       -> CompleteQuest, GetItem reward\n" +
                "                   -> AutoOrderQuests starts the next one");
            chain.style.color = UiKit.Text;
            chain.style.fontSize = 10;
            chain.style.whiteSpace = WhiteSpace.Normal;
            chain.style.marginTop = 6;
            body.Add(chain);

            var todo = new Label(
                "Not buildable from JSON alone: quest Blueprints and objective blocks are " +
                "assets. Editing an existing quest's item or count is a property edit and " +
                "is possible; a brand-new quest needs a .pak.");
            todo.style.color = UiKit.Warning;
            todo.style.fontSize = 10;
            todo.style.whiteSpace = WhiteSpace.Normal;
            todo.style.marginTop = 6;
            body.Add(todo);

            return section;
        }

        private VisualElement _shopStockList;

        private VisualElement NoticeBox(string title, string body, Color color)
        {
            var box = new VisualElement();
            box.style.marginBottom = 10;
            box.style.paddingLeft = 10;
            box.style.paddingRight = 10;
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;
            box.style.backgroundColor = UiKit.Panel;
            UiKit.SetBorder(box, color, 1);

            var heading = new Label(title);
            heading.style.color = color;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 12;
            box.Add(heading);

            var text = new Label(body);
            text.style.color = UiKit.Muted;
            text.style.fontSize = 10;
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.marginTop = 3;
            box.Add(text);

            return box;
        }

        /// <summary>
        /// Dropdown over an enum harvested from the real tables. AIResponse and
        /// AISightResponse are stored without an "EPal...::" prefix, so they are
        /// looked up under their bare field names.
        /// </summary>
        private DropdownField EnumDropdownRaw(string enumName, string initial)
        {
            var values = _data.Npc.Enums.TryGetValue(enumName, out var npcValues)
                ? npcValues
                : _data.EnumValues(enumName).ToList();

            if (values == null || values.Count == 0) values = new List<string> { initial };
            return new DropdownField(values, Math.Max(0, values.IndexOf(initial)));
        }

        private VisualElement BuildExportPage()
        {
            var page = UiKit.Column();
            var section = UiKit.Section("Validate and Export", out var body);

            _outputPath = new TextField
            {
                // the editor keeps its Desktop folder; the exe uses Documents\True Creation, the user's own place
                value = Application.isEditor
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PalCreationEngine Output")
                    : AppPaths.UserFolder("Creation Engine Output"),
            };
            body.Add(UiKit.Row("Output folder", _outputPath, string.Empty, fieldWidth: 420));

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 8;
            buttons.Add(UiKit.PrimaryButton("Validate", () => Validate()));
            buttons.Add(UiKit.SecondaryButton("Export mod package", Export));
            body.Add(buttons);

            _statusLabel = new Label(string.Empty);
            _statusLabel.style.marginTop = 8;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            body.Add(_statusLabel);

            _problemList = new VisualElement { style = { marginTop = 4 } };
            body.Add(_problemList);

            page.Add(section);
            return page;
        }

        private VisualElement BuildDataMissingNotice()
        {
            var box = new VisualElement { style = { paddingLeft = 20, paddingTop = 20 } };

            var title = new Label("Lookup data not found");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiKit.Danger;
            box.Add(title);

            var body = new Label(
                $"Looked in: {_data.SourceDirectory}\n\n" +
                "This data ships with True Creation. Reinstall True Creation to restore it.\n\n" +
                "Developers: the generators are in Tools/PalCreationEngine:\n" +
                "    python Tools/PalCreationEngine/generate_lookups.py   (and --base-rows)\n" +
                "    python Tools/PalCreationEngine/generate_index_lookups.py\n" +
                "    python Tools/PalCreationEngine/generate_model.py");
            body.style.color = UiKit.Text;
            body.style.marginTop = 10;
            body.style.whiteSpace = WhiteSpace.Normal;
            box.Add(body);

            foreach (var error in _data.LoadErrors)
            {
                var line = new Label("- " + error) { style = { marginTop = 2 } };
                line.style.color = UiKit.Muted;
                box.Add(line);
            }

            return box;
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement { style = { marginBottom = 10 } };

            var title = new Label("Pal Creation Engine");
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiKit.Text;
            header.Add(title);

            var subtitle = new Label(
                $"{_data.Pals.Count} Pals - {_data.Items.Count} items - " +
                $"{_data.PassiveSkills.Count} passives" +
                (_data.Names.Available ? "" : "  |  in-game names not loaded from display_names.json: reinstall True Creation "
                                              + "(developers: python Tools/PalCreationEngine/generate_lookups.py)"));
            subtitle.style.color = _data.Names.Available ? UiKit.Muted : UiKit.Warning;
            subtitle.style.fontSize = 11;
            header.Add(subtitle);

            return header;
        }

        // ---- behaviour -------------------------------------------------

        private void RebuildRow()
        {
            var bpClass = string.IsNullOrWhiteSpace(_bpClass) ? "PinkCat" : _bpClass;
            try
            {
                // The verified working mod builds every new Pal as a copy of a base Pal's real
                // row with edits, so when the borrowed Pal's row is in base_rows.json the editor
                // starts from THAT: what the sections show is what ships. The size medians stay
                // the fallback for an unknown model or a missing base_rows.json.
                var basePal = BorrowedPal();
                var baseRow = basePal != null ? _data.BaseRow(RawTables.PalMonsterParameterTable, basePal.RowKey) : null;
                if (baseRow != null)
                {
                    _row = PalFactory.CreateFromBase(baseRow, _palName, _palName, _palName);
                    _row.Size = PalFactory.Prefixed("EPalSizeType", _size);
                    // Legacy raw/ route semantics: the row borrows the vanilla class directly.
                    // The new-Pal route replaces this with the Pal's own DT_PalBPClass row key.
                    _row.BPClass = bpClass;
                    _seededFrom = $"{basePal.RowKey}'s own row";
                }
                else
                {
                    _row = PalFactory.CreateNew(_data, _palName, _size, bpClass);
                    _seededFrom = $"the {_size}-size medians";
                }
            }
            catch (ArgumentException e)
            {
                // An empty size_defaults.json used to leave _row null and every field read then
                // threw (audit pce-09). Keep an empty row so the editor stays usable and say why.
                _row = new PalMonsterParameterRow();
                _log($"[PalCreationEngine] Could not seed a row: {e.Message}");
                SetStatus($"Could not seed the row: {e.Message}", UiKit.Danger);
            }

            // The seed carries its own number; put the slot chosen in the editor back.
            ApplyZukanToRow();
            RowRebuilt?.Invoke();
        }

        private void SetRowField(string fieldName, object value)
        {
            var field = typeof(PalMonsterParameterRow).GetField(fieldName);
            if (field == null || _row == null) return;

            if (field.FieldType == typeof(int?)) field.SetValue(_row, System.Convert.ToInt32(value));
            else if (field.FieldType == typeof(float?)) field.SetValue(_row, System.Convert.ToSingle(value));
            else if (field.FieldType == typeof(string)) field.SetValue(_row, (string)value ?? "None");
        }

        private object GetRowField(string fieldName) =>
            _row == null ? null : typeof(PalMonsterParameterRow).GetField(fieldName)?.GetValue(_row);

        private void SyncAllFields()
        {
            foreach (var kv in _workSliders)
                if (GetRowField("WorkSuitability_" + kv.Key) is int current)
                    kv.Value.SetValueWithoutNotify(current);

            foreach (var kv in _statFields)
            {
                var value = GetRowField(kv.Key);
                kv.Value.SetValueWithoutNotify(value switch
                {
                    int i => i,
                    float f => (int)f,
                    _ => 0,
                });
            }

            // Dropdowns and pickers mirror the row too, or a re-seed from a base Pal's own row
            // (elements, genus, best job, fixed passives) leaves stale defaults over live values.
            SetDropdown(_element1Drop, Bare(_row?.ElementType1, "Normal"));
            SetDropdown(_element2Drop, Bare(_row?.ElementType2, "None"));
            SetDropdown(_genusDrop, Bare(_row?.GenusCategory, "FourLegged"));
            if (_bestJobDrop != null && _row != null)
            {
                var bestField = Bare(_row.BestWorkSuitability, "");
                var job = WorkJobs.FirstOrDefault(j => j.Field == bestField);
                if (job.Display != null) SetDropdown(_bestJobDrop, job.Display);
            }
            foreach (var (field, name) in _boundInts)
                if (GetRowField(name) is int bound) field.SetValueWithoutNotify(bound);
            _ignoreCombi?.SetValueWithoutNotify(_row?.IgnoreCombi == true);
            for (var i = 0; i < _passivePickers.Count; i++)
            {
                var (picker, row) = _passivePickers[i];
                var value = GetRowField("PassiveSkill" + (i + 1)) as string;
                var shown = string.IsNullOrEmpty(value) || value == "None" ? "" : value;
                picker.SetValueSilently(shown);
                var known = shown == "" || _data.PassiveSkills.ContainsKey(shown);
                UiKit.SetRowHint(row,
                    shown == "" ? string.Empty : known ? _data.PassiveSkills[shown].Describe() : "not a known skill ID",
                    known ? UiKit.Muted : UiKit.Warning);
            }

            RefreshStatSummary();
        }

        /// <summary>"EPalElementType::Dark" -> "Dark"; null or empty -> the fallback.</summary>
        private static string Bare(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            var i = value.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? value.Substring(i + 2) : value;
        }

        private static void SetDropdown(DropdownField dropdown, string value)
        {
            if (dropdown == null || value == null || !dropdown.choices.Contains(value)) return;
            dropdown.SetValueWithoutNotify(value);
        }

        private VisualElement StatRow(string label, string fieldName)
        {
            var schema = _data.Field(fieldName);
            var initial = GetRowField(fieldName) switch
            {
                int i => i,
                float f => (int)f,
                _ => 0,
            };

            var field = new IntegerField { value = initial };
            _statFields[fieldName] = field;
            SetRowField(fieldName, initial);

            var max = schema?.RealMax;
            var row = UiKit.Row(label, field,
                max != null ? $"max {Fmt(max.Value)}" : string.Empty);
            _statRows.Add(row);

            field.RegisterValueChangedCallback(evt =>
            {
                SetRowField(fieldName, evt.newValue);

                var placing = _data.Benchmarks.Describe(fieldName, evt.newValue);
                var overRange = PalFactory.TryNoteOutOfRange(_data, fieldName, evt.newValue, out _);

                UiKit.SetRowHint(row,
                    max != null
                        ? $"max {Fmt(max.Value)}" + (placing != null ? $" - {placing}" : "")
                        : placing ?? string.Empty,
                    overRange ? UiKit.Warning : UiKit.Muted);

                RefreshStatSummary();
            });

            return row;
        }

        private void RefreshStatSummary()
        {
            if (_statSummary == null || _data.Benchmarks.Tiers.Count == 0) return;

            var total = new[] { "Hp", "MeleeAttack", "ShotAttack", "Defense" }
                .Sum(f => GetRowField(f) is int i ? i : 0);

            var nearest = _data.Benchmarks.Tiers
                .OrderBy(t => Math.Abs(t.StatTotal - total))
                .First();

            _statSummary.text =
                $"Stat total {total} - closest to the {nearest.Label} tier " +
                $"({_data.LabelForPal(nearest.ExampleRowKey)}, total {nearest.StatTotal}).";
        }

        private DropdownField EnumDropdown(string enumName, string initial, Action<string> onChange)
        {
            var values = _data.EnumValues(enumName).ToList();
            if (values.Count == 0) values = new List<string> { "None" };

            var index = Math.Max(0, values.IndexOf(initial));
            var dropdown = new DropdownField(values, index);
            dropdown.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            onChange(values[index]);
            return dropdown;
        }

        private static string Fmt(double value) =>
            Math.Abs(value % 1) < 0.001
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture);

        private void SetStatus(string message, Color color)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message;
            _statusLabel.style.color = color;
        }

        /// <summary>
        /// Blueprint-side edits for the current Pal. Empty unless something
        /// actually needs one -- today only a non-unit mesh scale does.
        /// </summary>
        private PalSchemaBlueprints BuildBlueprints(PalSchemaMod mod)
        {
            var blueprints = new PalSchemaBlueprints();

            _scale.PalId = _palName;
            if (PalScaleBuilder.Validate(_scale).Count == 0)
                PalScaleBuilder.Apply(_scale, mod, blueprints);

            if (_companionEnabled != null && _companionEnabled.value)
            {
                _companion.PalId = _palName;
                // The companion is configured on the BORROWED model's Blueprint,
                // the same asset the mesh scale targets.
                _companion.BlueprintObjectPath = _data.BlueprintPathFor(_bpClass);
                if (CompanionBuilder.Validate(_companion).Count == 0)
                    CompanionBuilder.Apply(_companion, blueprints);
            }

            // pce-01 (2026-09-16): every edit above is keyed by the BORROWED vanilla class, so it
            // would resize or retarget the vanilla Pal as well. Such edits are dropped here and
            // reported; the same rule RouteGuard applies to the new-Pal route. A custom mesh
            // needs its own class in a pak (Route B, Tools > Pal Creation Engine > New Pal by Area).
            var vanillaKeys = blueprints.Edits.Keys
                .Where(k => _data.Scan == null || _data.Scan.IsEmpty || _data.Scan.ObjectPathExists(k))
                .ToList();
            if (vanillaKeys.Count > 0)
            {
                foreach (var k in vanillaKeys) blueprints.Edits.Remove(k);
                var msg = $"Dropped {vanillaKeys.Count} blueprint edit(s) keyed by the borrowed vanilla class ({string.Join(", ", vanillaKeys.Select(System.IO.Path.GetFileName))}): " +
                          "they would change the vanilla Pal too (pce-01). Mesh scale and companion edits need a custom class in a pak.";
                _log("[PalCreationEngine] " + msg);
                SetStatus(msg, UiKit.Warning);
            }

            return blueprints;
        }

        private PalSchemaMod BuildMod()
        {
            var mod = new PalSchemaMod();
            mod.Pals[_palName] = _row;

            var drops = BuildDropRow();
            if (drops != null) mod.AddDropRow(_palName, 0, drops);
            return mod;
        }

        private bool Validate()
        {
            _problemList.Clear();

            if (string.IsNullOrWhiteSpace(_bpClass))
            {
                SetStatus("Pick a Pal to borrow a model from first - without a BPClass it has nothing to render.",
                    UiKit.Danger);
                return false;
            }

            var known = new HashSet<string>(_data.Pals.Keys);
            var problems = BuildMod().Validate(known).ToList();

            foreach (var slot in _lootSlots)
            {
                var itemId = GameData.IdFromLabel(slot.Item.Value);
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                if (!_data.Items.ContainsKey(itemId))
                    problems.Add($"Loot: '{itemId}' is not a real item ID (IDs are case-sensitive).");
                else if (slot.Min.value > slot.Max.value)
                    problems.Add($"Loot: {itemId} has min {slot.Min.value} above max {slot.Max.value}.");
            }

            if (known.Contains(_palName))
            {
                SetStatus($"'{_palName}' is the row key of a Pal that ships with the game. " +
                          "Exporting OVERWRITES it rather than adding a new Pal.", UiKit.Warning);
            }

            if (problems.Count == 0)
            {
                SetStatus($"'{_palName}' looks good - all 90 fields populated.", UiKit.Success);
                return true;
            }

            SetStatus($"{problems.Count} problem(s):", UiKit.Danger);
            foreach (var problem in problems.Take(12))
            {
                var line = new Label("- " + problem);
                line.style.color = UiKit.Danger;
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.marginTop = 2;
                _problemList.Add(line);
            }

            return false;
        }

        private void Export()
        {
            if (!Validate()) return;

            try
            {
                var root = Path.Combine(_outputPath.value, _palName);
                // the exe writes no author, so this PC's account name stays out of the package (the editor keeps it)
                var info = ModPackager.BuildInfo(_palName, _palName, Application.isEditor ? Environment.UserName : "");
                var mod = BuildMod();
                var blueprints = BuildBlueprints(mod);
                ModPackager.Write(root, info, mod, blueprints);

                SetStatus(
                    $"Exported to {root}"
                    + (blueprints.IsEmpty ? "" : "\nIncludes a blueprints/ edit for the mesh scale.")
                    + "\nAdd a thumbnail.png, then upload with Palworld Mod Uploader.",
                    UiKit.Success);
                _log($"[PalCreationEngine] Exported {_palName} to {root}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SetStatus("Could not write the package: " + e.Message, UiKit.Danger);
            }
        }
    }
}
