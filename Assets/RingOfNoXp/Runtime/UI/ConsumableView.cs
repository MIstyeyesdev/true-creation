using System;
using System.Collections.Generic;
using System.Linq;
using RingOfNoXp.Lookup;
using RingOfNoXp.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace RingOfNoXp.UI
{
    /// <summary>
    /// Tab 1 - the drinkable. An item row plus its DT_StatusEffectFood row.
    /// Left: the item and its recipe. Right: the buff, and the shipped rows that
    /// justify the numbers, because -100 is a claim about the engine and the
    /// reader should be able to check it without leaving the window.
    /// </summary>
    public sealed class ConsumableView : VisualElement
    {
        private const float LabelWidth = 150f;

        private readonly GameData _data;
        private readonly Func<RingProject> _project;
        private readonly Action _changed;

        public ConsumableView(GameData data, Func<RingProject> project, Action changed)
        {
            _data = data;
            _project = project;
            _changed = changed;
            style.flexGrow = 1;
            Refresh();
        }

        private ConsumableSpec Spec => _project().Consumable;

        public void Refresh()
        {
            Clear();
            var spec = Spec;

            var enable = new Toggle { value = spec.Enabled };
            enable.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                spec.Enabled = evt.newValue;
                _changed?.Invoke();
                Refresh();
            });
            Add(UiKit.Row("Include in package", enable, "untick to generate the ring alone", LabelWidth, 30));

            if (!spec.Enabled)
            {
                Add(UiKit.Note("The consumable is switched off; nothing on this tab is written.", UiKit.Muted));
                Add(UiKit.Note("Save hazard: if a world already holds this item (inventory, chest, on a Pal) and the next package is installed without it, " +
                               "the client PalSchema cannot clean the invalid item up and that world can crash on load. Use up or drop every copy in game first, " +
                               "or keep the item enabled.", UiKit.Warning));
                return;
            }

            Add(UiKit.Columns(LeftColumn(spec), RightColumn(spec)));
        }

        private VisualElement LeftColumn(ConsumableSpec spec)
        {
            var column = UiKit.Column();

            var item = UiKit.Section("Item  (PalSchema items/ loader)", out var body,
                "PalSchema writes this as Type \"Consumable\", which builds the consumable item class. TypeA must stay Consume or Food, because the buff only lands when the item is eaten or drunk.");
            body.Add(TextRow("Item ID", spec.Item.Id, v => spec.Item.Id = v, "row key; letters, digits and underscore"));
            body.Add(TextRow("Display name", spec.Item.Name, v => spec.Item.Name = v, null));
            body.Add(TextRow("Description", spec.Item.Description, v => spec.Item.Description = v, null));
            body.Add(PickerRow("Icon from item", spec.Item.IconPath, _data.IconChoices, v => spec.Item.IconPath = v,
                "reuse a shipped item's icon texture path"));
            body.Add(TextRow("TypeA", spec.Item.TypeA, v => spec.Item.TypeA = v, "must be Food - the buff is applied by the eating path, and Consume items never reach it"));
            body.Add(TextRow("TypeB", spec.Item.TypeB, v => spec.Item.TypeB = v, "FoodDishVegetable, FoodDishMeat, FoodDishFish or FoodVegetable - the four craftable Food pairs"));
            body.Add(IntRow("Rank", spec.Item.Rank, v => spec.Item.Rank = v, null));
            body.Add(IntRow("Rarity", spec.Item.Rarity, v => spec.Item.Rarity = v, null));
            body.Add(IntRow("Max stack", spec.Item.MaxStack, v => spec.Item.MaxStack = v, null));
            body.Add(FloatRow("Weight", spec.Item.Weight, v => spec.Item.Weight = v, null));
            body.Add(IntRow("Price", spec.Item.Price, v => spec.Item.Price = v, null));
            body.Add(IntRow("Sort id", spec.Item.SortId, v => spec.Item.SortId = v, "where it sits in the inventory order"));
            column.Add(item);

            column.Add(RecipeSection(spec.Recipe, "Recipe  (DT_ItemRecipeDataTable)"));
            column.Add(TechSection(spec));
            column.Add(StationSection(spec));
            return column;
        }

        /// <summary>
        /// Optional technology gate. Off by default and that is the point: 1,035 of
        /// the 1,414 shipped recipes are referenced by no technology row at all, so
        /// a recipe works without one - and with no row there is no tech-tree entry
        /// taking up UI space.
        /// </summary>
        private VisualElement TechSection(ConsumableSpec spec)
        {
            var section = UiKit.Section("Technology unlock", out var body,
                "Off = the item never appears in the tech tree. Turn it on only if you want it earned.");

            var on = new Toggle { value = spec.TechUnlock };
            on.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                spec.TechUnlock = evt.newValue;
                _changed?.Invoke();
                Refresh();
            });
            body.Add(UiKit.Row("Add to tech tree", on,
                spec.TechUnlock ? "a DT_TechnologyRecipeUnlock row is written"
                                : "no tech row is written - nothing shows in the tech tree",
                LabelWidth, 30));

            if (spec.TechUnlock)
            {
                body.Add(IntRow("Unlock at level", spec.TechLevel, v => spec.TechLevel = v,
                    "player level the tech becomes available at; 1 = from the start"));
                body.Add(IntRow("Technology cost", spec.TechCost, v => spec.TechCost = v,
                    "technology points to spend; 0 = free"));
                body.Add(UiKit.Note(
                    spec.TechLevel <= 1 && spec.TechCost <= 0
                        ? "Free from the start - but it still occupies a slot in the tech tree UI. Switch the toggle off to keep it out entirely."
                        : $"Earned: reach level {spec.TechLevel} and spend {spec.TechCost} point(s).",
                    spec.TechLevel <= 1 && spec.TechCost <= 0 ? UiKit.Warning : UiKit.Muted));
            }
            return section;
        }

        /// <summary>
        /// Which crafting stations accept the item. A station takes an item only
        /// when its TypeA is in TargetTypesA AND its TypeB is in TargetTypesB, and
        /// the two lists are checked independently - so a station may need one of
        /// them added, or both. Ticking one that does not already accept the item
        /// makes the generator patch it through PalSchema's blueprints/ loader.
        /// </summary>
        private VisualElement StationSection(ConsumableSpec spec)
        {
            var section = UiKit.Section("Crafting stations", out var body,
                "Tick where this can be made. A station that does not already accept the item's types gets patched.");

            if (_data.Stations.Count == 0)
            {
                body.Add(UiKit.Note("stations.json is not loaded, so the station list is unavailable.", UiKit.Warning));
                return section;
            }

            var scroll = new ScrollView();
            scroll.style.maxHeight = 260;

            foreach (var st in _data.Stations.OrderBy(s => s.name))
            {
                var station = st;
                var ticked = spec.Stations.Contains(station.bp);

                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;
                line.style.minHeight = 20;

                var box = new Toggle { value = ticked };
                box.style.width = 22;
                box.style.flexShrink = 0;
                box.RegisterValueChangedCallback(evt =>
                {
                    evt.StopPropagation();
                    if (evt.newValue) { if (!spec.Stations.Contains(station.bp)) spec.Stations.Add(station.bp); }
                    else spec.Stations.Remove(station.bp);
                    _changed?.Invoke();
                    Refresh();
                });
                line.Add(box);

                var label = new Label(station.name);
                label.style.width = 200;
                label.style.fontSize = 11;
                label.style.color = UiKit.Text;
                label.style.flexShrink = 0;
                line.Add(label);

                // Say exactly what ticking this costs: nothing, or a patch, and which half.
                string note;
                Color colour;
                if (station.Accepts(spec.Item.TypeA, spec.Item.TypeB))
                {
                    note = "already accepts it";
                    colour = UiKit.Success;
                }
                else
                {
                    var missing = new List<string>();
                    if (!station.AcceptsA(spec.Item.TypeA)) missing.Add(spec.Item.TypeA);
                    if (!station.AcceptsB(spec.Item.TypeB)) missing.Add(spec.Item.TypeB);
                    note = ticked ? "will be patched: + " + string.Join(", ", missing)
                                  : "would need: + " + string.Join(", ", missing);
                    colour = ticked ? UiKit.Warning : UiKit.Muted;
                }
                var hint = new Label(note);
                hint.style.fontSize = 10;
                hint.style.color = colour;
                hint.style.flexShrink = 1;
                hint.style.whiteSpace = WhiteSpace.Normal;
                line.Add(hint);

                scroll.Add(line);
            }

            body.Add(scroll);

            var patched = _data.Stations
                .Where(s => spec.Stations.Contains(s.bp) && !s.Accepts(spec.Item.TypeA, spec.Item.TypeB))
                .ToList();
            body.Add(UiKit.Note(
                patched.Count == 0
                    ? "No station needs patching - blueprints/ will not be written."
                    : $"{patched.Count} station(s) will be patched. Note this also makes other items of type " +
                      $"{spec.Item.TypeB} craftable there.",
                patched.Count == 0 ? UiKit.Muted : UiKit.Warning));

            return section;
        }

        private VisualElement RightColumn(ConsumableSpec spec)
        {
            var column = UiKit.Column();

            var buff = UiKit.Section("Buff  (DT_StatusEffectFood)", out var body,
                "Keyed by the item's own id. This table is the real source of food buffs - DT_ItemDataTable.GrantEffect* is dead and does nothing.");

            var effects = _data.FoodEffectTypes.Count > 0
                ? _data.FoodEffectTypes
                : new List<string> { "None", "Exp_Increase" };

            body.Add(Dropdown("Effect 1", effects, effects.IndexOf(spec.Effect1Type), i => spec.Effect1Type = effects[i],
                "Exp_Increase is the only place player EXP is exposed to data"));
            var v1 = IntRow("Effect 1 value", spec.Effect1Value, v => { spec.Effect1Value = v; UpdateVerdict(); }, null);
            _valueRow = v1;
            body.Add(v1);
            body.Add(IntRow("Effect 1 interval", spec.Effect1Interval, v => spec.Effect1Interval = v, "0 for a flat modifier; only tick effects use this"));

            body.Add(Dropdown("Effect 2", effects, effects.IndexOf(spec.Effect2Type), i => spec.Effect2Type = effects[i], null));
            body.Add(IntRow("Effect 2 value", spec.Effect2Value, v => spec.Effect2Value = v, null));
            body.Add(IntRow("Effect 2 interval", spec.Effect2Interval, v => spec.Effect2Interval = v, null));

            body.Add(IntRow("Duration (seconds)", spec.EffectTime, v => { spec.EffectTime = v; UpdateVerdict(); }, null));
            _durationNote = UiKit.Note("", UiKit.Muted);
            body.Add(_durationNote);
            column.Add(buff);

            var restore = UiKit.Section("On consumption", out var rbody, "Satiety must be at least 1 or the game has no reason to let you eat it.");
            rbody.Add(IntRow("Restore satiety", spec.RestoreSatiety, v => spec.RestoreSatiety = v, "PoisonMushroom restores exactly 1"));
            rbody.Add(IntRow("Restore sanity", spec.RestoreSanity, v => spec.RestoreSanity = v, null));
            rbody.Add(IntRow("Restore health", spec.RestoreHealth, v => spec.RestoreHealth = v, null));
            column.Add(restore);

            var evidence = UiKit.Section("Why these numbers", out var ebody,
                "From the shipped tables, so the claim can be checked rather than trusted.");
            ebody.Add(UiKit.KV("Negatives work", "PoisonMushroom = HungerResist -25", UiKit.Text, 150,
                "a shipped negative food effect"));
            ebody.Add(UiKit.KV("-100 means off", "InvalidSlipDamage_Poison / _Burn", UiKit.Text, 150,
                "the only two passives at exactly -100.0, and the name says nullified"));
            ebody.Add(UiKit.KV("Longest shipped", "1800 s", UiKit.Text, 150,
                "no dish runs longer; nothing says the field is capped there"));
            ebody.Add(UiKit.KV("Player XP path", "EPalFoodStatusEffectType::Exp_Increase", UiKit.Text, 150,
                "the passive-skill enum has no player-EXP member at all"));
            column.Add(evidence);

            UpdateVerdict();
            return column;
        }

        private VisualElement _valueRow;
        private Label _durationNote;

        /// <summary>Says plainly what the current numbers will do, including when they will not do it.</summary>
        private void UpdateVerdict()
        {
            var spec = Spec;
            if (_valueRow != null)
            {
                if (spec.Effect1Type == "Exp_Increase" && spec.Effect1Value == -100)
                    UiKit.SetRowHint(_valueRow, "-100 is applied, but measured in game (2026-09-07) the food rate floors at 0.1 and player XP per event did not change. Not a cancel until a run shows otherwise.", UiKit.Warning);
                else if (spec.Effect1Type == "Exp_Increase" && spec.Effect1Value < -100)
                    UiKit.SetRowHint(_valueRow, "past -100; untested territory, the rate may go negative", UiKit.Danger);
                else if (spec.Effect1Type == "Exp_Increase")
                    UiKit.SetRowHint(_valueRow, $"XP gain changed by {spec.Effect1Value}%, not cancelled", UiKit.Warning);
                else
                    UiKit.SetRowHint(_valueRow, "percent modifier for the chosen effect", UiKit.Muted);
            }

            if (_durationNote != null)
            {
                if (spec.EffectTime > 1800)
                {
                    _durationNote.text = $"{spec.EffectTime}s is longer than any shipped dish (max 1800). Plausible, but this is the value to suspect first if the buff behaves oddly.";
                    _durationNote.style.color = UiKit.Warning;
                }
                else
                {
                    _durationNote.text = $"{spec.EffectTime}s. Re-drink to keep it up; a consumable cannot stay on forever.";
                    _durationNote.style.color = UiKit.Muted;
                }
            }
        }

        // ------------------------------------------------------------- recipe

        private VisualElement RecipeSection(RecipeSpec recipe, string title)
        {
            recipe.Normalise();
            var section = UiKit.Section(title, out var body, "Five material slots, the same shape the game uses.");

            var on = new Toggle { value = recipe.Enabled };
            on.RegisterValueChangedCallback(evt => { evt.StopPropagation(); recipe.Enabled = evt.newValue; _changed?.Invoke(); });
            body.Add(UiKit.Row("Craftable", on, "off = no recipe row is written", LabelWidth, 30));

            body.Add(IntRow("Product count", recipe.ProductCount, v => recipe.ProductCount = v, null));
            body.Add(FloatRow("Work amount", recipe.WorkAmount, v => recipe.WorkAmount = v, "Ring of Mercy is 30000"));
            body.Add(PickerRow("Unlock item", recipe.UnlockItemId, _data.ItemChoices, v => recipe.UnlockItemId = v,
                "the Blueprint_ item that unlocks it, or blank for always available"));

            for (var slot = 0; slot < 5; slot++)
            {
                var index = slot;
                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row;
                line.style.alignItems = Align.Center;

                var picker = new UiKit.SearchPicker(_data.ItemChoices, null, 150) { Value = recipe.MaterialIds[index] };
                // Both events: OnSelected for picking from the list, OnTextChanged so
                // typing - and especially clearing - actually reaches the model.
                picker.OnSelected += v => { recipe.MaterialIds[index] = v; _changed?.Invoke(); };
                picker.OnTextChanged += v => { recipe.MaterialIds[index] = (v ?? "").Trim(); _changed?.Invoke(); };
                picker.style.flexGrow = 1;
                line.Add(picker);

                var count = new IntegerField { value = recipe.MaterialCounts[index] };
                count.style.width = 60;
                count.style.marginLeft = 4;
                count.RegisterValueChangedCallback(evt => { recipe.MaterialCounts[index] = evt.newValue; _changed?.Invoke(); });
                line.Add(count);

                body.Add(UiKit.Row($"Material {index + 1}", line, null, LabelWidth));
            }
            return section;
        }

        // ------------------------------------------------------------- row helpers

        private VisualElement TextRow(string label, string value, Action<string> set, string hint, float width = 0)
        {
            var f = new TextField { value = value ?? "" };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, LabelWidth, width);
        }

        private VisualElement IntRow(string label, int value, Action<int> set, string hint, float width = 90)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, LabelWidth, width);
        }

        private VisualElement FloatRow(string label, float value, Action<float> set, string hint, float width = 90)
        {
            var f = new FloatField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, LabelWidth, width);
        }

        private VisualElement Dropdown(string label, List<string> choices, int index, Action<int> set, string hint)
        {
            var list = choices.Count > 0 ? choices : new List<string> { "(none)" };
            var f = new DropdownField(list, Math.Min(Math.Max(0, index), list.Count - 1));
            f.RegisterValueChangedCallback(evt => { set(Math.Max(0, list.IndexOf(evt.newValue))); _changed?.Invoke(); UpdateVerdict(); });
            return UiKit.Row(label, f, hint, LabelWidth, 0);
        }

        /// <param name="liveText">
        /// True (default) writes every keystroke to the model, so clearing the field
        /// sticks. Pass false when the setter does something heavy, like rebuilding
        /// the view - those must only run on an actual pick.
        /// </param>
        private VisualElement PickerRow(string label, string value, Func<IEnumerable<(string, string)>> source, Action<string> set, string hint, bool liveText = true)
        {
            var picker = new UiKit.SearchPicker(source, null, 150) { Value = value };
            picker.OnSelected += v => { set(v); _changed?.Invoke(); };
            if (liveText) picker.OnTextChanged += v => { set((v ?? "").Trim()); _changed?.Invoke(); };
            return UiKit.Row(label, picker, hint, LabelWidth);
        }
    }
}
