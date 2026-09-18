using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;
using PalCreationEngine.UI;

namespace TrueCreation.App
{
    /// <summary>
    /// Item Gen, first cut: MIRROR a vanilla item. Pick any of the 2,466 vanilla items, clone every field of its
    /// DT_ItemDataTable row under a new id, edit what you want, and export a PalSchema package in the exact shape the
    /// verified working mod (PalVariantPandemonium, "Added Item" in the loader log) uses:
    ///   PalSchema/items/&lt;Mod&gt;.json          the row with prefixed enums + Type + IconName/IconTexture + Recipe
    ///   PalSchema/translations/en/loc.json    DT_ItemNameText / DT_ItemDescriptionText (+ technology text)
    ///   PalSchema/raw/DT_ItemIconDataTable.json         only when the icon name is new
    ///   PalSchema/raw/DT_TechnologyRecipeUnlock.json    a technology row copied from the source's, so it is unlockable
    /// Data comes from Assets/StreamingAssets/ItemGen (built by Tools/ItemGen/generate_itemgen_lookups.py from the index).
    /// Nothing is installed; the package is written to the output folder only. Runs in the editor and in the exe
    /// (settings, dialogs and Explorer via AppHost); the exe's default output is Documents\True Creation\Item Gen.
    /// </summary>
    public sealed class ItemGenPanel : VisualElement
    {
        private const string PrefOut = "TrueEngine.ItemGen.Out";
        private const string PrefMod = "TrueEngine.ItemGen.Mod";
        // row fields that exist in the DataTable but not on the item object the items/ loader fills (loader warns "not found in Item")
        private static readonly HashSet<string> OmitInItems = new HashSet<string> { "TechnologyTreeLock", "Editor_RowNameHash" };
        private static readonly string[] EnumFields = { "TypeA", "TypeB", "ElementType", "WazaID", "DropItemType" };
        private static readonly Dictionary<string, string> Group = new Dictionary<string, string>
        {
            ["OverrideName"] = "Text", ["OverrideDescription"] = "Text", ["IconName"] = "Icon",
            ["TypeA"] = "Classification", ["TypeB"] = "Classification", ["Rank"] = "Classification", ["Rarity"] = "Classification",
            ["MaxStackCount"] = "Classification", ["Weight"] = "Classification", ["Price"] = "Classification", ["SortId"] = "Classification",
            ["bInTreasureBox"] = "Flags", ["bNotConsumed"] = "Flags", ["bNotAvailableInPVP"] = "Flags", ["bEnableHandcraft"] = "Flags", ["bLegalInGame"] = "Flags", ["TechnologyTreeLock"] = "Flags",
            ["ItemStaticClass"] = "Classes (what the item is in code)", ["ItemDynamicClass"] = "Classes (what the item is in code)", ["ItemActorClass"] = "Classes (what the item is in code)",
            ["ItemStaticMeshName"] = "Classes (what the item is in code)", ["VisualBlueprintClassName"] = "Classes (what the item is in code)", ["VisualBlueprintClassSoft"] = "Classes (what the item is in code)", ["DropItemType"] = "Classes (what the item is in code)",
            ["RestoreSatiety"] = "Consumable", ["RestoreConcentration"] = "Consumable", ["RestoreSanity"] = "Consumable", ["RestoreHealth"] = "Consumable",
            ["GrantEffect1Id"] = "Consumable", ["GrantEffect1Time"] = "Consumable", ["GrantEffect2Id"] = "Consumable", ["GrantEffect2Time"] = "Consumable", ["GrantEffect3Id"] = "Consumable", ["GrantEffect3Time"] = "Consumable",
            ["Durability"] = "Weapon", ["ElementType"] = "Weapon", ["bSleepWeapon"] = "Weapon", ["MagazineSize"] = "Weapon", ["SneakAttackRate"] = "Weapon", ["PhysicalAttackValue"] = "Weapon", ["WazaID"] = "Weapon",
            ["HPValue"] = "Armor", ["PhysicalDefenseValue"] = "Armor", ["ShieldValue"] = "Armor", ["MagicAttackValue"] = "Armor", ["MagicDefenseValue"] = "Armor",
            ["PassiveSkillName"] = "Passives", ["PassiveSkillName2"] = "Passives", ["PassiveSkillName3"] = "Passives", ["PassiveSkillName4"] = "Passives",
            ["CorruptionFactor"] = "Other", ["FloatValue1"] = "Other", ["Editor_RowNameHash"] = "Other",
        };
        private static readonly string[] GroupOrder = { "Text", "Icon", "Classification", "Flags", "Classes (what the item is in code)", "Consumable", "Weapon", "Armor", "Passives", "Other" };
        private static readonly Dictionary<string, string> GroupHint = new Dictionary<string, string>
        {
            ["Text"] = "OverrideName/OverrideDescription stay None: the name and description come from the translations file written with the package.",
            ["Icon"] = "IconName is the DT_ItemIconDataTable row; the mirror reuses the source icon. A new IconName needs a texture path below and gets a raw icon row.",
            ["Classification"] = "TypeA/TypeB decide which stations list it (with Rank). Enum values keep the EPalItemType prefix, as in the working mod.",
            ["Flags"] = "Row-only flags. bLegalInGame: true for 1,892 vanilla items; false marks unobtainable leftovers (574 rows, e.g. the Rank 999 Fire/Poison crossbows), so keep it true for an item you want to use. The loader logs 'Property not found in Item' for keys the item object lacks (the working mod ships them all and loads with 0 errors); TechnologyTreeLock and Editor_RowNameHash are left out of the export.",
            ["Classes (what the item is in code)"] = "ItemStaticClass/ItemDynamicClass (CommonWeapon, CommonArmor, CommonConsume) pick the item object class; ItemActorClass names the weapon actor (NormalRifle = BP_NormalRifle) and is also exported as the object's actorClass path; VisualBlueprintClassName is the dropped/held model, exported as VisualBlueprintClassSoft. A mirror keeps the source's, so the new weapon looks and shoots like the source. actorClass on a new item is UNTESTED in game.",
            ["Consumable"] = "Restore and GrantEffect values apply to Consumable items.",
            ["Weapon"] = "Durability, MagazineSize, PhysicalAttackValue, SneakAttackRate: the values the Weapon Stats Customizer mod edits at runtime (as AttackValue/MagazineSize/Durability on PalStaticWeaponItemData); here they are baked into the item, exported under both spellings. ElementType is None on all 2,466 vanilla items and the item object has no such property: it does nothing. A bow or crossbow gets its element from the AMMO: the weapon class carries SupportedBulletMap (Arrow -> BP_Arrow, Arrow_Fire -> BP_Arrow_Fire, Arrow_Poison -> BP_Arrow_Poision) and the arrow bullet carries the effect; the Fire/Poison crossbow items use their own actor classes (BP_Bowgun_FIre2 / BP_Bowgun_Poison2). An electric arrow needs a new ammo item, a bullet class with the electric effect (vanilla ships an unused BP_Arrow_Paralyze), and a SupportedBulletMap entry on the bow class.",
            ["Armor"] = "Defense/HP/Shield values for Armor and accessories (object: DefenseValue, HPValue, ShieldValue). MagicAttack/MagicDefense exist only in the table.",
            ["Passives"] = "Passive skill ids from DT_PassiveSkill_Main; None for none. Exported as PassiveSkillName (row) and PassiveSkill (object).",
            ["Other"] = "Editor_RowNameHash is omitted from the export (editor-only hash).",
        };

        // items/ keys reflect onto the PalStatic*ItemData object (usmap104): row field -> object property, written in addition to the row field
        private static readonly Dictionary<string, string> ObjectAlias = new Dictionary<string, string>
        {
            ["PassiveSkillName"] = "PassiveSkill", ["PassiveSkillName2"] = "PassiveSkill2", ["PassiveSkillName3"] = "PassiveSkill3", ["PassiveSkillName4"] = "PassiveSkill4",
            ["PhysicalAttackValue"] = "AttackValue", ["PhysicalDefenseValue"] = "DefenseValue",
            ["RestoreHealth"] = "RestoreHP", ["RestoreConcentration"] = "RestoreSP",
        };

        private readonly string _dataDir;
        private JObject _items, _recipes, _tech, _text, _enums, _vocab, _actorClasses, _visualClasses, _foodEffects, _cooked;
        private JObject _food; private Toggle _includeFood; private VisualElement _foodBox;
        private Dictionary<string, string> _icons = new Dictionary<string, string>();
        private HashSet<string> _passives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> _passiveList = new List<string>();
        private HashSet<string> _textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _itemIdsLower = new HashSet<string>();
        private List<string> _itemIds = new List<string>();

        private sealed class ItemInfo { public string Id, Name, TypeA, TypeB, Rank, Search; }
        private List<ItemInfo> _infos = new List<ItemInfo>();

        private string _sourceId;
        private JObject _row, _recipe;
        private string _sourceTechId;
        private TextField _newId, _name, _desc, _iconName, _iconTexture, _modName, _outDir;
        private Label _idCheck, _sourceLabel, _report, _diag, _count;
        private ListView _list; private List<ItemInfo> _hits = new List<ItemInfo>();
        private UiKit.SearchPicker _picker; private bool _suppressSelect; private string _autoId;
        private const string LabelSep = "  -  ";
        private VisualElement _fields, _recipeBox, _techBox;
        private Toggle _includeRecipe, _includeTech;
        private DropdownField _type;

        public ItemGenPanel()
        {
            style.flexGrow = 1;
            _dataDir = Path.Combine(Application.streamingAssetsPath, "ItemGen");
            LoadData();
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; Add(scroll);
            scroll.Add(Header());
            scroll.Add(SourceSection());
            scroll.Add(NewItemSection());
            _fields = Section("3. Every field of the row (mirrored from the source; edit what you want)", "Grouped like the game uses them. Values keep the vanilla spelling and enum prefixes.");
            scroll.Add(_fields);
            _recipeBox = Section("4. Recipe (DT_ItemRecipeDataTable)", "The source's recipe, cloned. Without a recipe the item exists but cannot be crafted.");
            scroll.Add(_recipeBox);
            _foodBox = Section("5. Status effect on use (DT_StatusEffectFood) - the XP gain section", "What a consumable does when eaten or drunk: two effects with a value, an interval and a shared duration. Exp_Increase is the XP gain effect (the Ring of No XP potion is this row). Vanilla has 54 rows, keyed by the item id; the mirror clones the source's if it has one. Written as raw/DT_StatusEffectFood.json keyed by the new id.");
            scroll.Add(_foodBox);
            _techBox = Section("6. Technology (DT_TechnologyRecipeUnlock)", "A technology row copied from the one that unlocks the source, keyed Tech_<new id>, so the recipe appears in the tree. The working mod adds its technologies the same way (raw/, 21 rows added, 0 errors).");
            scroll.Add(_techBox);
            scroll.Add(ExportSection());
        }

        // ------------------------------------------------------------------ data
        private void LoadData()
        {
            JObject Read(string n)
            {
                var p = Path.Combine(_dataDir, n);
                return File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : null;
            }
            _items = Read("item_rows.json"); _recipes = Read("recipe_rows.json"); _tech = Read("tech_rows.json"); _text = Read("item_text_en.json");
            _enums = Read("enums.json"); _vocab = Read("vocab.json"); _actorClasses = Read("actor_classes.json"); _visualClasses = Read("visual_classes.json");
            _foodEffects = Read("food_effect_rows.json"); _cooked = Read("cooked_item_classes.json");
            var ic = Read("icon_rows.json");
            if (ic != null) foreach (var kv in ic) _icons[kv.Key] = kv.Value?.ToString();
            var pp = Path.Combine(_dataDir, "passive_ids.json");
            if (File.Exists(pp)) foreach (var t in JArray.Parse(File.ReadAllText(pp))) { _passives.Add(t.ToString()); _passiveList.Add(t.ToString()); }
            _passiveList.Sort(StringComparer.OrdinalIgnoreCase);
            var tp = Path.Combine(_dataDir, "icon_textures.json");
            if (File.Exists(tp)) foreach (var t in JArray.Parse(File.ReadAllText(tp))) _textures.Add(t.ToString());
            if (_items != null)
            {
                _itemIds = _items.Properties().Select(p => p.Name).ToList();
                foreach (var i in _itemIds) _itemIdsLower.Add(i.ToLowerInvariant());
                // plain-string search index, built once: id, en name, TypeA, TypeB, rank
                foreach (var id in _itemIds)
                {
                    var r = _items[id] as JObject;
                    var info = new ItemInfo { Id = id, Name = NameOf(id), TypeA = Bare(r?["TypeA"]), TypeB = Bare(r?["TypeB"]), Rank = r?["Rank"]?.ToString() ?? "" };
                    info.Search = (info.Id + " | " + info.Name + " | " + info.TypeA + " | " + info.TypeB).ToLowerInvariant();
                    _infos.Add(info);
                }
            }
        }

        private string NameOf(string id) => (_text?[id]?["name"]?.ToString()) ?? "";
        private string DescOf(string id) => (_text?[id]?["desc"]?.ToString()) ?? "";

        // ------------------------------------------------------------------ ui pieces
        private static VisualElement Section(string title, string hint)
        {
            var box = new VisualElement();
            box.style.marginLeft = 10; box.style.marginRight = 10; box.style.marginTop = 10; box.style.paddingLeft = 10; box.style.paddingRight = 10; box.style.paddingTop = 6; box.style.paddingBottom = 8;
            box.style.backgroundColor = new Color(0.17f, 0.17f, 0.19f); box.style.borderLeftWidth = 3; box.style.borderLeftColor = new Color(0.36f, 0.64f, 1f);
            var t = new Label(title); t.style.fontSize = 14; t.style.unityFontStyleAndWeight = FontStyle.Bold; box.Add(t);
            var h = new Label(hint); h.style.fontSize = 11; h.style.color = new Color(0.72f, 0.72f, 0.76f); h.style.whiteSpace = WhiteSpace.Normal; h.style.marginBottom = 6; box.Add(h);
            return box;
        }

        private static Label Para(string s)
        {
            var l = new Label(s); l.style.whiteSpace = WhiteSpace.Normal; l.style.fontSize = 12; return l;
        }

        private static TextField Text(string label, string value, float width = 420, bool multiline = false)
        {
            var f = new TextField(label) { value = value ?? "", multiline = multiline };
            f.labelElement.style.minWidth = 190; f.style.width = width + 200; f.style.marginLeft = 0;
            return f;
        }

        private VisualElement Header()
        {
            var box = new VisualElement(); box.style.paddingLeft = 10; box.style.paddingTop = 8; box.style.paddingRight = 10;
            var t = new Label("Item Gen - mirror a vanilla item"); t.style.fontSize = 16; t.style.unityFontStyleAndWeight = FontStyle.Bold; box.Add(t);
            var ok = _items != null;
            var s = new Label(ok
                ? $"data: {_itemIds.Count} items, {_recipes?.Count ?? 0} recipes, {_icons.Count} icon rows, {_tech?.Count ?? 0} technologies, {_passives.Count} passives, {_textures.Count} icon textures  ({_dataDir})"
                : $"data missing from {_dataDir}: reinstall True Creation to restore it (developers: python Tools/ItemGen/generate_itemgen_lookups.py)");
            s.style.fontSize = 11; s.style.color = ok ? new Color(0.72f, 0.72f, 0.76f) : new Color(0.95f, 0.5f, 0.5f); s.style.whiteSpace = WhiteSpace.Normal; box.Add(s);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 6;
            _modName = Text("Mod (package) name", AppHost.Current.GetString(PrefMod, "MyItemMod"), 260);
            _modName.RegisterValueChangedCallback(e => AppHost.Current.SetString(PrefMod, e.newValue));
            row.Add(_modName);
            box.Add(row);
            var row2 = new VisualElement(); row2.style.flexDirection = FlexDirection.Row;
            var defaultOut = Application.isEditor ? Path.Combine(AppPaths.ProjectRoot, "Tools", "ItemGen", "Output") : AppPaths.UserFolder("Item Gen");
            _outDir = Text("Output folder", AppHost.Current.GetString(PrefOut, defaultOut), 520);
            _outDir.RegisterValueChangedCallback(e => AppHost.Current.SetString(PrefOut, e.newValue));
            row2.Add(_outDir);
            var browse = new Button(() =>
            {
                var p = AppHost.Current.OpenFolderPanel("Item Gen output folder", _outDir.value);
                if (!string.IsNullOrEmpty(p)) _outDir.value = p;
            }) { text = "Browse" }; browse.style.height = 22; row2.Add(browse);
            box.Add(row2);
            var rule = new Label("The package is written to the output folder only. It is never installed into the game.");
            rule.style.fontSize = 11; rule.style.color = new Color(0.62f, 0.62f, 0.66f); box.Add(rule);
            return box;
        }

        private VisualElement SourceSection()
        {
            var box = Section("1. Source item to mirror", "Type an id, name or type in the picker, or open its list with the caret; the table underneath is the browse view and narrows as you type. Click a list entry or a table row to load it.");
            var pickRow = new VisualElement(); pickRow.style.flexDirection = FlexDirection.Row; pickRow.style.alignItems = Align.FlexStart;
            var pickLabel = new Label("Source item"); pickLabel.style.minWidth = 190; pickLabel.style.paddingTop = 3; pickRow.Add(pickLabel);
            _picker = new UiKit.SearchPicker(() => _infos.Select(h => h.Id + LabelSep + h.Name + "  [" + h.TypeB + "]"),
                "type an id, name or TypeB, or open the list with the caret; the table below browses every item", 400);
            _picker.style.width = 620;
            _picker.OnSelected += label => { var id = IdOfLabel(label); if (id != null) LoadSource(id); };
            _picker.OnCommitted += text =>
            {
                var t = (text ?? "").Trim(); var id = IdOfLabel(t) ?? t;
                var exact = _infos.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase));
                if (exact != null) LoadSource(exact.Id);
            };
            // the picker's own text field bubbles its value changes up here; use them to narrow the browse table
            _picker.RegisterCallback<ChangeEvent<string>>(e => { if (e.target is TextField) RunSearch(e.newValue); });
            pickRow.Add(_picker);
            box.Add(pickRow);
            _count = new Label(""); _count.style.fontSize = 11; _count.style.color = new Color(0.72f, 0.72f, 0.76f); _count.style.marginTop = 2; box.Add(_count);
            _diag = new Label(""); _diag.style.fontSize = 10; _diag.style.color = new Color(0.5f, 0.5f, 0.55f); _diag.style.whiteSpace = WhiteSpace.Normal; box.Add(_diag);
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.marginTop = 4;
            foreach (var kv in new[] { ("id", 260), ("name (en)", 260), ("TypeA", 120), ("TypeB", 200), ("rank", 50) })
            {
                var l = new Label(kv.Item1); l.style.width = kv.Item2; l.style.flexShrink = 0; l.style.fontSize = 11; l.style.paddingLeft = 6; l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.color = new Color(0.72f, 0.72f, 0.76f); head.Add(l);
            }
            box.Add(head);
            _list = new ListView(new List<object>(), 22, () =>
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                foreach (var w in new[] { 260, 260, 120, 200, 50 })
                {
                    var l = new Label(); l.style.width = w; l.style.flexShrink = 0; l.style.fontSize = 11; l.style.paddingLeft = 6; l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis; l.style.whiteSpace = WhiteSpace.NoWrap; r.Add(l);
                }
                return r;
            }, (e, i) =>
            {
                if (i < 0 || i >= _hits.Count) return;
                var h = _hits[i];
                ((Label)e[0]).text = h.Id; ((Label)e[1]).text = h.Name; ((Label)e[2]).text = h.TypeA; ((Label)e[3]).text = h.TypeB; ((Label)e[4]).text = h.Rank;
            });
            _list.style.height = 220; _list.selectionType = SelectionType.Single;
            _list.selectionChanged += _ =>
            {
                if (_suppressSelect) return;
                var i = _list.selectedIndex;
                if (i >= 0 && i < _hits.Count) LoadSource(_hits[i].Id);
            };
            box.Add(_list);
            _sourceLabel = Para("No source selected."); _sourceLabel.style.marginTop = 4; box.Add(_sourceLabel);
            RunSearch("");
            return box;
        }

        private static string Bare(JToken t) { var s = t?.ToString() ?? ""; var i = s.IndexOf("::", StringComparison.Ordinal); return i >= 0 ? s.Substring(i + 2) : s; }

        private static string IdOfLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var i = label.IndexOf(LabelSep, StringComparison.Ordinal);
            return i > 0 ? label.Substring(0, i).Trim() : null;
        }

        /// <summary>Search over the plain-string index only; the table selection never feeds back into it.</summary>
        private void RunSearch(string q)
        {
            var words = (q ?? "").Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<ItemInfo>();
            string error = null;
            try
            {
                foreach (var info in _infos)
                {
                    var ok = true;
                    foreach (var w in words) if ((info.Search ?? "").IndexOf(w, StringComparison.Ordinal) < 0) { ok = false; break; }
                    if (ok) hits.Add(info);
                }
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; Debug.LogException(ex); }
            _hits = hits;
            // counts go to their own label: setting a field's label fires a text change that bubbles into the field (that was the 0-hit bug)
            _count.text = $"{_hits.Count} of {_infos.Count} items match" + (words.Length == 0 ? "" : $" '{string.Join(" ", words)}'");
            _diag.text = error != null ? "search error: " + error : "";
            _suppressSelect = true;
            try
            {
                _list.ClearSelection();
                _list.itemsSource = _hits;
                _list.Rebuild();
            }
            finally { _suppressSelect = false; }
        }

        private VisualElement NewItemSection()
        {
            var box = Section("2. The new item", "Id must be new (checked against all 2,466 vanilla ids, case-insensitive like the game). Name and description go to translations/en.");
            _newId = Text("New item id", "", 320); _newId.RegisterValueChangedCallback(_ => CheckId()); box.Add(_newId);
            _idCheck = Para(""); _idCheck.style.fontSize = 11; box.Add(_idCheck);
            _name = Text("Name (en)", "", 420); box.Add(_name);
            _desc = Text("Description (en)", "", 520, true); box.Add(_desc);
            _type = new DropdownField("Type (items/ loader)", new List<string> { "Generic", "Consumable", "Weapon", "Armor" }, 0);
            _type.labelElement.style.minWidth = 190; _type.style.width = 420; box.Add(_type);
            var th = Para("Type follows the source's ItemStaticClass: CommonWeapon = Weapon, CommonArmor = Armor (accessories too), CommonConsume = Consumable, else Generic. This is what the loader builds the item object from.");
            th.style.fontSize = 11; th.style.color = new Color(0.72f, 0.72f, 0.76f); box.Add(th);
            _iconName = Text("IconName (icon table row)", "", 320); _iconName.RegisterValueChangedCallback(e => { if (_icons.TryGetValue(e.newValue, out var p)) _iconTexture.value = p; }); box.Add(_iconName);
            _iconTexture = Text("IconTexture (object path)", "", 620); box.Add(_iconTexture);
            return box;
        }

        private void CheckId()
        {
            var id = (_newId.value ?? "").Trim();
            if (id.Length == 0) { _idCheck.text = "enter an id"; _idCheck.style.color = new Color(0.9f, 0.6f, 0.4f); return; }
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_]+$")) { _idCheck.text = "letters, digits and underscore only"; _idCheck.style.color = new Color(0.95f, 0.5f, 0.5f); return; }
            if (_itemIdsLower.Contains(id.ToLowerInvariant())) { _idCheck.text = "TAKEN: a vanilla item already uses this id"; _idCheck.style.color = new Color(0.95f, 0.5f, 0.5f); return; }
            _idCheck.text = "free"; _idCheck.style.color = new Color(0.55f, 0.9f, 0.55f);
        }

        // ------------------------------------------------------------------ loading a source
        private void LoadSource(string id)
        {
            _sourceId = id;
            _row = (JObject)_items[id].DeepClone();
            _recipe = _recipes?[id] is JObject r ? (JObject)r.DeepClone() : null;
            _sourceTechId = null;
            if (_tech != null)
                foreach (var kv in _tech)
                    if (kv.Value["UnlockItemRecipes"] is JArray arr && arr.Any(x => string.Equals(x.ToString(), id, StringComparison.OrdinalIgnoreCase))) { _sourceTechId = kv.Key; break; }
            _picker.Value = id + LabelSep + NameOf(id) + "  [" + Bare(_row["TypeB"]) + "]";
            var ck = _cooked?[id] as JObject;
            var actorInfo = ck?["actorClass"] != null ? $"  |  actor class {ck["actorClass"]} ({(ck["actor_exists"]?.Value<bool>() == true ? "ships in 1.0.5" : "MISSING from 1.0.5")})" : "  |  no actor class (not a weapon)";
            _sourceLabel.text = $"Source: {id}  |  {NameOf(id)}  |  {Bare(_row["TypeA"])} / {Bare(_row["TypeB"])}  |  rank {_row["Rank"]}  |  static {_row["ItemStaticClass"]}  actor {_row["ItemActorClass"]}  visual {_row["VisualBlueprintClassName"]}{actorInfo}"
                + (_recipe != null ? "  |  has recipe" : "  |  no recipe") + (_sourceTechId != null ? $"  |  unlocked by {_sourceTechId}" : "  |  no technology row");
            // the suggested id follows the pick until the user types their own
            if (string.IsNullOrEmpty(_newId.value) || _newId.value == _autoId) { _autoId = id + "_Mirror"; _newId.value = _autoId; }
            _name.value = NameOf(id) + " (mirror)"; _desc.value = DescOf(id);
            var sc = _row["ItemStaticClass"]?.ToString() ?? "None";
            _type.value = sc == "CommonWeapon" ? "Weapon" : sc == "CommonArmor" ? "Armor" : sc.StartsWith("CommonConsume") || sc.StartsWith("Consume_") ? "Consumable" : "Generic";
            var icon = _row["IconName"]?.ToString() ?? "";
            _iconName.value = icon; _iconTexture.value = _icons.TryGetValue(icon, out var tex) ? tex : "";
            _food = _foodEffects?[id] is JObject fo ? (JObject)fo.DeepClone() : null;
            CheckId();
            if (_fields != null) BuildFields();
            if (_recipeBox != null) BuildRecipe();
            if (_foodBox != null) BuildFood();
            if (_techBox != null) BuildTech();
        }

        /// <summary>DT_StatusEffectFood row: EffectTime, EffectType1/EffectValue1/Interaval1, EffectType2/EffectValue2/Interaval2 (the game's spelling).</summary>
        private void BuildFood()
        {
            while (_foodBox.childCount > 2) _foodBox.RemoveAt(2);
            var had = _food != null;
            _includeFood = new Toggle("Include a status effect row") { value = had }; _foodBox.Add(_includeFood);
            if (_food == null)
            {
                _food = new JObject
                {
                    ["EffectTime"] = 0, ["EffectType1"] = "EPalFoodStatusEffectType::None", ["EffectValue1"] = 0, ["Interaval1"] = 0,
                    ["EffectType2"] = "EPalFoodStatusEffectType::None", ["EffectValue2"] = 0, ["Interaval2"] = 0,
                };
                _foodBox.Add(Para("The source has no status effect row (54 vanilla items have one, all food). Turn the toggle on and set an effect to give the mirror one."));
            }
            else _foodBox.Add(Para($"Cloned from the source's row: {_food["EffectType1"]} {_food["EffectValue1"]} / {_food["EffectType2"]} {_food["EffectValue2"]} for {_food["EffectTime"]} s."));
            var members = (_enums?["EPalFoodStatusEffectType"] as JArray)?.Select(x => "EPalFoodStatusEffectType::" + x).ToList() ?? new List<string>();
            VisualElement IntRow(string key, string hint)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
                var f = new IntegerField(key) { value = (int)(_food[key]?.Value<long>() ?? 0) }; f.labelElement.style.minWidth = 190; f.style.width = 360; f.style.marginLeft = 0;
                f.RegisterValueChangedCallback(e => { if (e.target == f) _food[key] = e.newValue; });
                row.Add(f); row.Add(Hint(hint)); return row;
            }
            VisualElement EnumRow(string key, string hint)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
                var cur = _food[key]?.ToString() ?? "EPalFoodStatusEffectType::None";
                var list = new List<string>(members); if (!list.Contains(cur)) list.Insert(0, cur);
                var dd = new DropdownField(key, list, list.IndexOf(cur)); dd.labelElement.style.minWidth = 190; dd.style.width = 360; dd.style.marginLeft = 0;
                dd.RegisterValueChangedCallback(e => { if (e.target == dd) _food[key] = e.newValue; });
                row.Add(dd); row.Add(Hint(hint)); return row;
            }
            _foodBox.Add(IntRow("EffectTime", "seconds both effects last (vanilla food: 600)"));
            _foodBox.Add(SubHead("Effect 1"));
            _foodBox.Add(EnumRow("EffectType1", "Exp_Increase = XP gain; Attack, Defense, WorkSpeed, HungerResist, SANResist, Regene_Hp, MaxSP, ..."));
            _foodBox.Add(IntRow("EffectValue1", "the effect's number (vanilla: 10-30 for stat buffs)"));
            _foodBox.Add(IntRow("Interaval1", "tick interval, 0 in vanilla rows (game spelling kept)"));
            _foodBox.Add(SubHead("Effect 2"));
            _foodBox.Add(EnumRow("EffectType2", "second effect, or None"));
            _foodBox.Add(IntRow("EffectValue2", ""));
            _foodBox.Add(IntRow("Interaval2", ""));
            var note = Para("XP comes from many places (crafting CraftExpRate, capture and area bonuses, defeat ExpRatio, the level curve, the world ExpRate); this row is one lever, the one the potion used.");
            note.style.fontSize = 11; note.style.color = new Color(0.72f, 0.72f, 0.76f); _foodBox.Add(note);
        }

        private void BuildFields()
        {
            while (_fields.childCount > 2) _fields.RemoveAt(2);
            var groups = new Dictionary<string, Foldout>();
            foreach (var g in GroupOrder)
            {
                var f = new Foldout { text = g, value = g == "Weapon" || g == "Classification" || g == "Classes (what the item is in code)" };
                if (GroupHint.TryGetValue(g, out var hint)) { var h = Para(hint); h.style.fontSize = 11; h.style.color = new Color(0.72f, 0.72f, 0.76f); f.Add(h); }
                groups[g] = f;
            }
            foreach (var prop in _row.Properties().ToList())
            {
                var key = prop.Name;
                if (key == "IconName") continue; // edited in section 2
                var g = Group.TryGetValue(key, out var gg) ? gg : "Other";
                groups[g].Add(FieldFor(prop));
            }
            foreach (var g in GroupOrder) if (groups[g].childCount > 1) _fields.Add(groups[g]);
        }

        private VisualElement FieldFor(JProperty prop)
        {
            var key = prop.Name; var v = prop.Value;
            var omitted = OmitInItems.Contains(key) ? "  (not exported)" : "";
            if (v.Type == JTokenType.Boolean)
            {
                var t = new Toggle(key + omitted) { value = v.Value<bool>() }; t.labelElement.style.minWidth = 190;
                t.RegisterValueChangedCallback(e => _row[key] = e.newValue); return t;
            }
            if (v.Type == JTokenType.Integer)
            {
                var f = new IntegerField(key + omitted) { value = (int)v.Value<long>() }; f.labelElement.style.minWidth = 190; f.style.width = 360;
                f.RegisterValueChangedCallback(e => _row[key] = e.newValue); return f;
            }
            if (v.Type == JTokenType.Float)
            {
                var f = new FloatField(key + omitted) { value = v.Value<float>() }; f.labelElement.style.minWidth = 190; f.style.width = 360;
                f.RegisterValueChangedCallback(e => _row[key] = e.newValue); return f;
            }
            if (v.Type == JTokenType.Object)
            {
                var inner = (JObject)v; var path = inner["AssetPathName"]?.ToString() ?? inner.ToString(Formatting.None);
                var f = Text(key + ".AssetPathName", path, 620);
                f.RegisterValueChangedCallback(e => { if (inner["AssetPathName"] != null) inner["AssetPathName"] = e.newValue; }); return f;
            }
            var s = v.ToString();
            if (Array.IndexOf(EnumFields, key) >= 0 && _enums != null)
            {
                var enumName = key == "TypeA" ? "EPalItemTypeA" : key == "TypeB" ? "EPalItemTypeB" : key == "ElementType" ? "EPalElementType" : key == "WazaID" ? "EPalWazaID" : "EPalDropItemType";
                var members = (_enums[enumName] as JArray)?.Select(x => enumName + "::" + x).ToList() ?? new List<string>();
                if (!members.Contains(s)) members.Insert(0, s);
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tf = Text(key, s, 300); row.Add(tf);
                var dd = new DropdownField(members, members.IndexOf(s)); dd.style.width = 260;
                dd.RegisterValueChangedCallback(e => { tf.value = e.newValue; }); row.Add(dd);
                tf.RegisterValueChangedCallback(e => _row[key] = e.newValue);
                return row;
            }
            if (key.StartsWith("PassiveSkillName"))
            {
                // passive slot: caret list with None on top, then every DT_PassiveSkill_Main row id
                var prow = new VisualElement(); prow.style.flexDirection = FlexDirection.Row; prow.style.alignItems = Align.FlexStart; prow.style.marginBottom = 2;
                var pl = new Label(key); pl.style.minWidth = 190; pl.style.paddingTop = 3; prow.Add(pl);
                var pp = new UiKit.SearchPicker(() => new[] { "None" }.Concat(_passiveList), "None on top; " + _passiveList.Count + " passive ids from DT_PassiveSkill_Main; type to narrow or open the list", 400);
                pp.style.width = 520; pp.Value = string.IsNullOrEmpty(s) ? "None" : s;
                void ApplyPassive(string id)
                {
                    var exact = id == "None" ? "None" : _passiveList.FirstOrDefault(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) ?? "None";
                    _row[key] = exact; pp.Value = exact;
                }
                pp.OnSelected += ApplyPassive;
                pp.OnCommitted += t => ApplyPassive((t ?? "").Trim());
                pp.RegisterCallback<FocusOutEvent>(_ => pp.schedule.Execute(() => { var t = (pp.Value ?? "").Trim(); if (!string.Equals(t, _row[key]?.ToString() ?? "None", StringComparison.OrdinalIgnoreCase)) ApplyPassive(t); }).ExecuteLater(200));
                prow.Add(pp);
                return prow;
            }
            var field = Text(key + omitted, s, key.EndsWith("Class") || key.EndsWith("ClassName") ? 420 : 300);
            field.RegisterValueChangedCallback(e => _row[key] = e.newValue);
            if (_vocab?[key] is JArray vocab && vocab.Count > 0)
            {
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                row.Add(field);
                var opts = vocab.Select(x => x.ToString()).ToList(); opts.Insert(0, "None");
                var dd = new DropdownField(opts, Math.Max(0, opts.IndexOf(s))); dd.style.width = 260; dd.tooltip = "vanilla values of " + key;
                dd.RegisterValueChangedCallback(e => field.value = e.newValue); row.Add(dd);
                return row;
            }
            return field;
        }

        private void BuildRecipe()
        {
            while (_recipeBox.childCount > 2) _recipeBox.RemoveAt(2);
            _includeRecipe = new Toggle("Include a recipe") { value = _recipe != null }; _recipeBox.Add(_includeRecipe);
            if (_recipe == null)
            {
                _recipe = new JObject
                {
                    ["Product_Count"] = 1, ["WorkAmount"] = 1000.0, ["WorkableAttribute"] = 0, ["UnlockItemID"] = "None",
                    ["Material1_Id"] = "None", ["Material1_Count"] = 0, ["Material2_Id"] = "None", ["Material2_Count"] = 0, ["Material3_Id"] = "None", ["Material3_Count"] = 0,
                    ["Material4_Id"] = "None", ["Material4_Count"] = 0, ["Material5_Id"] = "None", ["Material5_Count"] = 0,
                    ["EnergyType"] = "EPalEnergyType::None", ["EnergyAmount"] = 0, ["CraftExpRate"] = 1.0,
                };
                _recipeBox.Add(Para("The source has no recipe; this is an empty template (fill materials to make it craftable)."));
            }
            _recipeBox.Add(SubHead("Output and work"));
            _recipeBox.Add(RecipeInt("Product_Count", "how many the craft yields"));
            _recipeBox.Add(RecipeFloat("WorkAmount", "work points to finish one craft"));
            _recipeBox.Add(RecipeInt("WorkableAttribute", "0 in every vanilla recipe"));
            _recipeBox.Add(SubHead("Unlock"));
            var unlockRow = new VisualElement(); unlockRow.style.flexDirection = FlexDirection.Row; unlockRow.style.alignItems = Align.FlexStart;
            var ul = new Label("UnlockItemID"); ul.style.minWidth = 190; ul.style.paddingTop = 3; unlockRow.Add(ul);
            unlockRow.Add(MakeItemPicker("UnlockItemID", _recipe["UnlockItemID"]?.ToString(), null));
            _recipeBox.Add(unlockRow);
            _recipeBox.Add(SubHead("Materials (five slots; None on top of each list; a None slot keeps its count at 0)"));
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row;
            foreach (var kv in new[] { ("slot", 190), ("item (id - name)", 440), ("count", 90) })
            {
                var l = new Label(kv.Item1); l.style.width = kv.Item2; l.style.flexShrink = 0; l.style.fontSize = 11; l.style.color = new Color(0.72f, 0.72f, 0.76f); l.style.unityFontStyleAndWeight = FontStyle.Bold; head.Add(l);
            }
            _recipeBox.Add(head);
            for (var i = 1; i <= 5; i++) _recipeBox.Add(MaterialRow(i));
            _recipeBox.Add(SubHead("Energy and experience"));
            _recipeBox.Add(RecipeEnum("EnergyType", "EPalEnergyType", "power the station needs for this craft"));
            _recipeBox.Add(RecipeInt("EnergyAmount", "0 unless EnergyType is Electric"));
            _recipeBox.Add(RecipeFloat("CraftExpRate", "crafting XP per craft: THE lever for a no-XP build (0 = no crafting XP from this recipe)"));
        }

        private static Label SubHead(string text)
        {
            var l = new Label(text); l.style.fontSize = 12; l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.marginTop = 8; l.style.marginBottom = 2; l.style.color = new Color(0.85f, 0.85f, 0.9f);
            return l;
        }

        private static Label Hint(string text)
        {
            var l = new Label(text); l.style.fontSize = 10; l.style.color = new Color(0.55f, 0.55f, 0.6f); l.style.paddingTop = 4; l.style.marginLeft = 8; l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        private VisualElement RecipeInt(string key, string hint)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
            var f = new IntegerField(key) { value = (int)(_recipe[key]?.Value<long>() ?? 0) }; f.labelElement.style.minWidth = 190; f.style.width = 360; f.style.marginLeft = 0;
            f.RegisterValueChangedCallback(e => { if (e.target == f) _recipe[key] = e.newValue; });
            row.Add(f); row.Add(Hint(hint)); return row;
        }

        private VisualElement RecipeFloat(string key, string hint)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
            var f = new FloatField(key) { value = _recipe[key]?.Value<float>() ?? 0f }; f.labelElement.style.minWidth = 190; f.style.width = 360; f.style.marginLeft = 0;
            f.RegisterValueChangedCallback(e => { if (e.target == f) _recipe[key] = e.newValue; });
            row.Add(f); row.Add(Hint(hint)); return row;
        }

        private VisualElement RecipeEnum(string key, string enumName, string hint)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
            var members = (_enums?[enumName] as JArray)?.Select(x => enumName + "::" + x).ToList() ?? new List<string>();
            var cur = _recipe[key]?.ToString() ?? enumName + "::None";
            if (!members.Contains(cur)) members.Insert(0, cur);
            var dd = new DropdownField(key, members, members.IndexOf(cur)); dd.labelElement.style.minWidth = 190; dd.style.width = 360; dd.style.marginLeft = 0;
            dd.RegisterValueChangedCallback(e => { if (e.target == dd) _recipe[key] = e.newValue; });
            row.Add(dd); row.Add(Hint(hint)); return row;
        }

        /// <summary>One material slot: label, item picker (None on top), count. A None item forces count 0.</summary>
        private VisualElement MaterialRow(int i)
        {
            var idKey = "Material" + i + "_Id"; var countKey = "Material" + i + "_Count";
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart; row.style.marginBottom = 2;
            var l = new Label("Material " + i); l.style.width = 190; l.style.flexShrink = 0; l.style.paddingTop = 3; row.Add(l);
            var count = new IntegerField { value = (int)(_recipe[countKey]?.Value<long>() ?? 0) }; count.style.width = 80; count.style.marginLeft = 10; count.style.marginTop = 0;
            var picker = MakeItemPicker(idKey, _recipe[idKey]?.ToString(), exact =>
            {
                if (exact == "None") { _recipe[countKey] = 0; count.SetValueWithoutNotify(0); }
            });
            picker.style.width = 440;
            count.RegisterValueChangedCallback(e =>
            {
                if (e.target != count) return;
                var none = (_recipe[idKey]?.ToString() ?? "None") == "None";
                if (none && e.newValue != 0) { count.SetValueWithoutNotify(0); _recipe[countKey] = 0; return; }
                _recipe[countKey] = Math.Max(0, e.newValue);
            });
            row.Add(picker); row.Add(count);
            return row;
        }

        /// <summary>
        /// An item id picker for a recipe slot: caret list with None on top, then every item as "id - name". A list pick or an
        /// exact id on Enter sets the slot; anything else (cleared, partial, unknown) becomes None on commit or blur.
        /// </summary>
        private UiKit.SearchPicker MakeItemPicker(string key, string current, Action<string> onApplied)
        {
            var picker = new UiKit.SearchPicker(() => new[] { "None" }.Concat(_infos.Select(h => h.Id + LabelSep + h.Name)), "None on top; pick from the list, or type an exact id and press Enter", 400);
            picker.style.width = 520;
            void Apply(string id)
            {
                var exact = id == "None" ? "None" : _infos.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase))?.Id;
                if (exact == null) exact = "None";
                _recipe[key] = exact;
                picker.Value = exact == "None" ? "None" : exact + LabelSep + NameOf(exact);
                onApplied?.Invoke(exact);
            }
            picker.Value = string.IsNullOrEmpty(current) || current == "None" ? "None" : current + LabelSep + NameOf(current);
            picker.OnSelected += lab => Apply(IdOfLabel(lab) ?? lab.Trim());
            picker.OnCommitted += t => Apply(IdOfLabel(t) ?? (t ?? "").Trim());
            picker.RegisterCallback<FocusOutEvent>(_ => picker.schedule.Execute(() =>
            {
                var t = picker.Value ?? ""; var id = IdOfLabel(t) ?? t.Trim();
                var stored = _recipe[key]?.ToString() ?? "None";
                if (string.Equals(id, stored, StringComparison.OrdinalIgnoreCase)) return;
                Apply(id);
            }).ExecuteLater(200));
            return picker;
        }

        private void BuildTech()
        {
            while (_techBox.childCount > 2) _techBox.RemoveAt(2);
            _includeTech = new Toggle("Add a technology row (copied from the source's)") { value = _sourceTechId != null }; _techBox.Add(_includeTech);
            if (_sourceTechId == null) { _includeTech.value = false; _includeTech.SetEnabled(false); _techBox.Add(Para("The source is not unlocked by any technology row (it comes from drops, shops or a station), so nothing is copied.")); return; }
            var t = _tech[_sourceTechId] as JObject;
            _techBox.Add(Para($"Source technology: {_sourceTechId}  |  LevelCap {t?["LevelCap"]}  Cost {t?["Cost"]}  Tier {t?["Tier"]}  RequireTechnology {t?["RequireTechnology"]}  RequireResearchId {t?["RequireResearchId"]}"));
            _techBox.Add(Para($"Written as raw/DT_TechnologyRecipeUnlock row 'Tech_<new id>' with UnlockItemRecipes = [new id], the same LevelCap/Cost/Tier, and NAME_RECIPE_/DESC_RECIPE_ texts in translations."));
        }

        // ------------------------------------------------------------------ export
        private VisualElement ExportSection()
        {
            var box = Section("7. Validate and export", "Validate checks the id, the materials, the passives, the icon texture, the status effect and the technology against the vanilla data. Export writes the package folder.");
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var v = new Button(() => { var r = Validate(); _report.text = r.Count == 0 ? "OK: no problems found." : string.Join("\n", r); }) { text = "Validate" }; v.style.height = 26; row.Add(v);
            var x = new Button(Export) { text = "Export package" }; x.style.height = 26; row.Add(x);
            box.Add(row);
            _report = Para(""); _report.style.marginTop = 6; box.Add(_report);
            return box;
        }

        private List<string> Validate()
        {
            var r = new List<string>();
            if (_row == null) { r.Add("no source item loaded"); return r; }
            var id = (_newId.value ?? "").Trim();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_]+$")) r.Add("id: letters, digits and underscore only");
            else if (_itemIdsLower.Contains(id.ToLowerInvariant())) r.Add("id: taken by a vanilla item");
            if (string.IsNullOrWhiteSpace(_modName.value) || !Regex.IsMatch(_modName.value.Trim(), "^[A-Za-z0-9_]+$")) r.Add("mod name: letters, digits and underscore only");
            if (string.IsNullOrWhiteSpace(_name.value)) r.Add("name is empty");
            var tex = (_iconTexture.value ?? "").Trim();
            if (tex.StartsWith("/Game/") && !_textures.Contains(tex)) r.Add("icon texture is not in the 1.0.5 export: " + tex);
            else if (tex.Length == 0) r.Add("icon texture is empty");
            for (var i = 1; i <= 4; i++)
            {
                var k = i == 1 ? "PassiveSkillName" : "PassiveSkillName" + i; var pv = _row[k]?.ToString() ?? "None";
                if (pv != "None" && !_passives.Contains(pv)) r.Add($"{k} '{pv}' is not a DT_PassiveSkill_Main row");
            }
            if (_includeRecipe != null && _includeRecipe.value)
                for (var i = 1; i <= 5; i++)
                {
                    var m = _recipe[$"Material{i}_Id"]?.ToString() ?? "None";
                    if (m != "None" && !_itemIdsLower.Contains(m.ToLowerInvariant()) && !string.Equals(m, id, StringComparison.OrdinalIgnoreCase)) r.Add($"recipe material {i} '{m}' is not an item");
                }
            foreach (var k in new[] { "TypeA", "TypeB" })
            {
                var s = _row[k]?.ToString() ?? "";
                var en = k == "TypeA" ? "EPalItemTypeA" : "EPalItemTypeB";
                if (_enums?[en] is JArray arr && !arr.Any(m => en + "::" + m == s)) r.Add($"{k} '{s}' is not a member of {en}");
            }
            if (_cooked?[_sourceId] is JObject ck && ck["actorClass"] != null && ck["actor_exists"]?.Value<bool>() == false)
                r.Add($"the source's weapon actor class {ck["actorClass"]} does not exist in the 1.0.5 export (a leftover item, e.g. the Fire/Poison crossbows); mirror a shipping item instead");
            if (_row["bLegalInGame"]?.Value<bool>() == false && (_items[_sourceId]?["bLegalInGame"]?.Value<bool>() ?? true))
                r.Add("bLegalInGame is false but the source ships with true; false marks unobtainable leftovers (574 vanilla rows, e.g. Rank 999 items)");
            if (_includeFood != null && _includeFood.value && _food != null)
            {
                var t1 = _food["EffectType1"]?.ToString() ?? ""; var t2 = _food["EffectType2"]?.ToString() ?? "";
                if (t1.EndsWith("::None") && t2.EndsWith("::None")) r.Add("status effect row included but both effect types are None");
                if (_type.value != "Consumable") r.Add("status effect row included but the item Type is " + _type.value + " (only a Consumable is used)");
            }
            return r;
        }

        private void Export()
        {
            var problems = Validate();
            if (problems.Count > 0) { _report.text = "Not exported:\n" + string.Join("\n", problems); return; }
            var id = _newId.value.Trim(); var mod = _modName.value.Trim();
            var root = Path.Combine(_outDir.value, mod); var ps = Path.Combine(root, "PalSchema");
            Directory.CreateDirectory(Path.Combine(ps, "items")); Directory.CreateDirectory(Path.Combine(ps, "translations", "en")); Directory.CreateDirectory(Path.Combine(ps, "raw"));
            // items/ entry: the row minus loader-rejected keys, plus Type, IconTexture, Recipe
            var entry = new JObject();
            foreach (var p in _row.Properties())
            {
                if (OmitInItems.Contains(p.Name)) continue;
                if (p.Name == "IconName") { entry["IconName"] = _iconName.value.Trim(); continue; }
                entry[p.Name] = p.Value.DeepClone();
            }
            entry["OverrideName"] = "None"; entry["OverrideDescription"] = "None";
            entry["IconTexture"] = _iconTexture.value.Trim();
            entry["Type"] = _type.value;
            // object-spelled aliases (PalStatic*ItemData property names) next to the row spellings, so both the row and the item object get the values
            foreach (var kv in ObjectAlias)
                if (_row[kv.Key] != null) entry[kv.Value] = _row[kv.Key].DeepClone();
            // the cooked item asset (DA_StaticItemDataAsset) says which classes the SOURCE item really uses; the name guess is the fallback
            var cooked = _cooked?[_sourceId] as JObject;
            var actorName = _row["ItemActorClass"]?.ToString();
            var actorPath = cooked?["actorClass"]?.ToString();
            if (string.IsNullOrEmpty(actorPath) && !string.IsNullOrEmpty(actorName) && actorName != "None") actorPath = _actorClasses?[actorName]?.ToString();
            if (!string.IsNullOrEmpty(actorPath)) entry["actorClass"] = actorPath;
            var visualName = _row["VisualBlueprintClassName"]?.ToString();
            var visualPath = (_row["VisualBlueprintClassSoft"] as JObject)?["AssetPathName"]?.ToString();
            if (string.IsNullOrEmpty(visualPath)) visualPath = cooked?["VisualBlueprintClassSoft"]?.ToString();
            if (string.IsNullOrEmpty(visualPath) && !string.IsNullOrEmpty(visualName) && visualName != "None") visualPath = _visualClasses?[visualName]?.ToString();
            if (!string.IsNullOrEmpty(visualPath)) entry["VisualBlueprintClassSoft"] = new JObject { ["AssetPathName"] = visualPath, ["SubPathString"] = "" };
            var meshPath = cooked?["StaticMeshPath"]?.ToString();
            if (!string.IsNullOrEmpty(meshPath)) entry["StaticMeshPath"] = meshPath;
            if (_includeRecipe.value)
            {
                var rec = new JObject();
                foreach (var p in _recipe.Properties()) if (p.Name != "Product_Id" && p.Name != "DenyRecipeChain" && p.Name != "Editor_RowNameHash") rec[p.Name] = p.Value.DeepClone();
                entry["Recipe"] = rec;
            }
            var items = new JObject { [id] = entry };
            File.WriteAllText(Path.Combine(ps, "items", mod + ".json"), items.ToString(Formatting.Indented));
            // translations
            var loc = new JObject
            {
                ["DT_ItemNameText"] = new JObject { ["ITEM_NAME_" + id] = _name.value.Trim() },
                ["DT_ItemDescriptionText"] = new JObject { ["ITEM_DESC_" + id] = _desc.value ?? "" },
            };
            var written = new List<string> { "PalSchema/items/" + mod + ".json", "PalSchema/translations/en/loc.json" };
            // icon row when the icon name is new
            var iconName = _iconName.value.Trim();
            if (!_icons.ContainsKey(iconName))
            {
                var icon = new JObject { ["DT_ItemIconDataTable"] = new JObject { [iconName] = new JObject { ["Icon"] = _iconTexture.value.Trim() } } };
                File.WriteAllText(Path.Combine(ps, "raw", "DT_ItemIconDataTable.json"), icon.ToString(Formatting.Indented));
                written.Add("PalSchema/raw/DT_ItemIconDataTable.json (new icon row)");
            }
            // status effect row (the consumable's effect on use)
            if (_includeFood != null && _includeFood.value && _food != null)
            {
                var food = new JObject { ["DT_StatusEffectFood"] = new JObject { [id] = _food.DeepClone() } };
                File.WriteAllText(Path.Combine(ps, "raw", "DT_StatusEffectFood.json"), food.ToString(Formatting.Indented));
                written.Add("PalSchema/raw/DT_StatusEffectFood.json (row " + id + ")");
            }
            // technology row copied from the source's
            if (_includeTech != null && _includeTech.value && _sourceTechId != null && _tech[_sourceTechId] is JObject src)
            {
                var techId = "Tech_" + id;
                var t = (JObject)src.DeepClone();
                t["UnlockBuildObjects"] = new JArray(); t["UnlockItemRecipes"] = new JArray(id);
                t["Name"] = "NAME_RECIPE_" + id; t["Description"] = "DESC_RECIPE_" + id; t["IconName"] = iconName;
                t.Remove("Editor_RowNameHash");
                var tech = new JObject { ["DT_TechnologyRecipeUnlock"] = new JObject { [techId] = t } };
                File.WriteAllText(Path.Combine(ps, "raw", "DT_TechnologyRecipeUnlock.json"), tech.ToString(Formatting.Indented));
                loc["DT_TechnologyNameText_Common"] = new JObject { ["NAME_RECIPE_" + id] = $"<itemName id=|{id}|/>" };
                loc["DT_TechnologyDescText_Common"] = new JObject { ["DESC_RECIPE_" + id] = $"The <itemName id=|{id}|/> becomes craftable." };
                written.Add("PalSchema/raw/DT_TechnologyRecipeUnlock.json (row " + techId + ")");
            }
            File.WriteAllText(Path.Combine(ps, "translations", "en", "loc.json"), loc.ToString(Formatting.Indented));
            var info = new JObject
            {
                ["ModName"] = mod, ["PackageName"] = mod, ["Version"] = "1.0.0", ["Author"] = "True Creation Item Gen",
                ["Description"] = $"Mirror of vanilla item {_sourceId} as {id}",
                ["Dependencies"] = new JArray("PalSchema", "UE4SSExperimentalPW"), ["Tags"] = new JArray("PalSchema"),
                ["InstallRule"] = new JArray(new JObject { ["Type"] = "PalSchema", ["Targets"] = new JArray("./PalSchema/") }),
            };
            File.WriteAllText(Path.Combine(root, "Info.json"), info.ToString(Formatting.Indented));
            written.Insert(0, "Info.json");
            _report.text = "Exported to " + root + "\n" + string.Join("\n", written) + "\nNot installed. In game: UNTESTED until you run it; the loader should log \"Added Item '" + id + "'\".";
            AppHost.Current.Reveal(root);
        }
    }
}
