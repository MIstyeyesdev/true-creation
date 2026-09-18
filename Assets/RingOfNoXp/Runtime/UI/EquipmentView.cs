using System;
using System.Collections.Generic;
using System.Linq;
using RingOfNoXp.Lookup;
using RingOfNoXp.Model;
using UnityEngine.UIElements;

namespace RingOfNoXp.UI
{
    /// <summary>
    /// Tab 2 - the worn ring, built the way Ring of Mercy is built. Left: the
    /// item and its recipe, with the original beside it. Right: the passive row
    /// that carries the whole effect, and an honest statement of what each
    /// player-XP method can and cannot reach.
    /// </summary>
    public sealed class EquipmentView : VisualElement
    {
        private const float LabelWidth = 150f;

        private static readonly List<string> MethodLabels = new List<string>
        {
            "Pal XP only - data only, no Lua",
            "Per-player buff - Lua, needs a UE4SS dump",
            "World ExpRate toggle - Lua, global"
        };

        private readonly GameData _data;
        private readonly Func<RingProject> _project;
        private readonly Action _changed;

        private VisualElement _methodNotes;
        private VisualElement _effectRow;

        public EquipmentView(GameData data, Func<RingProject> project, Action changed)
        {
            _data = data;
            _project = project;
            _changed = changed;
            style.flexGrow = 1;
            Refresh();
        }

        private EquipSpec Spec => _project().Equip;

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
            Add(UiKit.Row("Include in package", enable, "untick to generate the consumable alone", LabelWidth, 30));

            if (!spec.Enabled)
            {
                Add(UiKit.Note("The ring is switched off; nothing on this tab is written.", UiKit.Muted));
                Add(UiKit.Note("Save hazard: if a world already holds the ring and the next package is installed without it, the client PalSchema " +
                               "cannot clean the invalid item up and that world can crash on load. Remove every copy in game first, or keep the ring enabled.", UiKit.Warning));
                return;
            }

            Add(UiKit.Columns(LeftColumn(spec), RightColumn(spec)));
        }

        private VisualElement LeftColumn(EquipSpec spec)
        {
            var column = UiKit.Column();

            var item = UiKit.Section("Item  (PalSchema items/ loader)", out var body,
                "An accessory is nearly empty: every stat stays zero and the power comes from the passive below. PalSchema writes it as Type \"Armor\", which builds the accessory item class for you - there is no static-class field to set.");
            body.Add(TextRow("Item ID", spec.Item.Id, v => spec.Item.Id = v, "row key"));
            body.Add(TextRow("Display name", spec.Item.Name, v => spec.Item.Name = v, null));
            body.Add(TextRow("Description", spec.Item.Description, v => spec.Item.Description = v, null));
            body.Add(PickerRow("Copy from", spec.Item.CopyFromItemId, () => _data.ItemChoices("Accessory"), v =>
            {
                spec.Item.CopyFromItemId = v;
                CopyDefaultsFrom(v, spec);
                _changed?.Invoke();
                Refresh();
                // liveText off: this setter rebuilds the whole tab, which must not
                // happen on every keystroke.
            }, "a shipped accessory whose numbers to start from", liveText: false));
            body.Add(PickerRow("Icon from item", spec.Item.IconPath, _data.IconChoices, v => spec.Item.IconPath = v,
                "reuse a shipped item's icon texture path"));
            body.Add(TextRow("TypeA", spec.Item.TypeA, v => spec.Item.TypeA = v, "bare enum name - Accessory, no EPal prefix"));
            body.Add(TextRow("TypeB", spec.Item.TypeB, v => spec.Item.TypeB = v, "bare enum name - Accessory"));
            body.Add(IntRow("Rank", spec.Item.Rank, v => spec.Item.Rank = v, null));
            body.Add(IntRow("Rarity", spec.Item.Rarity, v => spec.Item.Rarity = v, null));
            body.Add(FloatRow("Weight", spec.Item.Weight, v => spec.Item.Weight = v, null));
            body.Add(IntRow("Price", spec.Item.Price, v => spec.Item.Price = v, null));
            body.Add(IntRow("Sort id", spec.Item.SortId, v => spec.Item.SortId = v, null));
            column.Add(item);

            column.Add(RecipeSection(spec.Recipe, "Recipe  (DT_ItemRecipeDataTable)"));
            column.Add(OriginalSection(spec));
            return column;
        }

        private VisualElement RightColumn(EquipSpec spec)
        {
            var column = UiKit.Column();

            var passive = UiKit.Section("Passive  (DT_PassiveSkill_Main)", out var body,
                "The item's PassiveSkillName points here. An effect type must be a member of EPalPassiveSkillEffectType - PalSchema cannot add a new one.");

            body.Add(TextRow("Passive row id", spec.PassiveId, v => spec.PassiveId = v, "what PassiveSkillName on the item points at"));

            var effects = _data.PassiveEffectTypes.Count > 0
                ? _data.PassiveEffectTypes
                : new List<string> { "no", "PalExp_Increase" };

            body.Add(PickerRow("Effect 1", spec.Effect1Type, () => effects.Select(e => (Value: e, Display: e)), v =>
            {
                spec.Effect1Type = v;
                _changed?.Invoke();
                UpdateEffectHint();
            }, $"{effects.Count} members in the enum; PalExp_Increase is the only XP one"));

            _effectRow = FloatRow("Effect 1 value", spec.Effect1Value, v => { spec.Effect1Value = v; UpdateEffectHint(); }, null);
            body.Add(_effectRow);
            body.Add(TextRow("Target 1", spec.Target1, v => spec.Target1 = v, "ToSelf, ToTrainer, ToBaseCamp"));

            body.Add(PickerRow("Effect 2", spec.Effect2Type, () => effects.Select(e => (Value: e, Display: e)), v => { spec.Effect2Type = v; _changed?.Invoke(); },
                "'no' - not 'None' - is the empty marker in slots 2 to 4"));
            body.Add(FloatRow("Effect 2 value", spec.Effect2Value, v => spec.Effect2Value = v, null));

            body.Add(IntRow("Passive rank", spec.PassiveRank, v => spec.PassiveRank = v, "-1 on hidden, item-granted passives"));
            body.Add(IntRow("Lottery weight", spec.LotteryWeight, v => spec.LotteryWeight = v, null));
            body.Add(TextRow("Category", spec.Category, v => spec.Category = v, "SortNotDisplayable keeps it out of the passive lists"));

            body.Add(ToggleRow("Invoke always", spec.InvokeAlways, v => spec.InvokeAlways = v, "what Ring of Mercy uses"));
            body.Add(ToggleRow("Invoke active otomo", spec.InvokeActiveOtomo, v => spec.InvokeActiveOtomo = v, null));
            body.Add(ToggleRow("Invoke worker", spec.InvokeWorker, v => spec.InvokeWorker = v, null));
            body.Add(ToggleRow("Invoke riding", spec.InvokeRiding, v => spec.InvokeRiding = v, null));
            body.Add(ToggleRow("Invoke reserve", spec.InvokeReserve, v => spec.InvokeReserve = v, null));
            body.Add(ToggleRow("Invoke in otomo", spec.InvokeInOtomo, v => spec.InvokeInOtomo = v, null));
            body.Add(ToggleRow("Invoke in base camp", spec.InvokeInBaseCamp, v => spec.InvokeInBaseCamp = v, null));
            column.Add(passive);

            var method = UiKit.Section("How it stops XP", out var mbody,
                "The passive enum has PalExp_Increase but no player-EXP member, so a worn ring cannot reach player XP through data alone.");
            mbody.Add(Dropdown("Method", MethodLabels, (int)spec.Method, i =>
            {
                spec.Method = (PlayerXpMethod)i;
                _changed?.Invoke();
                // The fallback-rate field only belongs to the ExpRate method, so the
                // section is rebuilt rather than just re-noted.
                Refresh();
            }, null));

            if (spec.Method == PlayerXpMethod.WorldExpRateLua)
                mbody.Add(FloatRow("Fallback ExpRate", spec.FallbackExpRate, v => spec.FallbackExpRate = v,
                    "written back if the repair file is lost - set it to YOUR world's rate, not 1.0"));

            _methodNotes = new VisualElement();
            mbody.Add(_methodNotes);
            column.Add(method);

            UpdateEffectHint();
            UpdateMethodNotes();
            return column;
        }

        /// <summary>The shipped row this one is modelled on, so the two can be read side by side.</summary>
        private VisualElement OriginalSection(EquipSpec spec)
        {
            var section = UiKit.Section("Original  (reference)", out var body,
                "Ring of Mercy is the whole pattern: an empty accessory plus one passive.");
            var original = _data.Find(string.IsNullOrEmpty(spec.Item.CopyFromItemId) ? "Accessory_Nonkilling" : spec.Item.CopyFromItemId);
            if (original == null)
            {
                body.Add(UiKit.Note("Lookup data not loaded; the reference is unavailable.", UiKit.Warning));
                return section;
            }
            body.Add(UiKit.KV("Item id", original.id));
            body.Add(UiKit.KV("Name", original.name == "-" ? "(no name in export)" : original.name));
            body.Add(UiKit.KV("Type", $"{original.typeA} / {original.typeB}"));
            body.Add(UiKit.KV("Rank / rarity", $"{original.rank} / {original.rarity}"));
            body.Add(UiKit.KV("Weight", original.weight.ToString("0.##")));
            if (original.id == "Accessory_Nonkilling")
            {
                body.Add(UiKit.KV("PassiveSkillName", "NonKilling", UiKit.Accent));
                body.Add(UiKit.KV("Passive effect", "EPalPassiveSkillEffectType::NonKilling", UiKit.Accent, 150, "value 0, InvokeAlways true"));
            }
            return section;
        }

        private void CopyDefaultsFrom(string itemId, EquipSpec spec)
        {
            var src = _data.Find(itemId);
            if (src == null) return;
            spec.Item.TypeA = src.typeA;
            spec.Item.TypeB = src.typeB;
            spec.Item.Rank = src.rank;
            spec.Item.Rarity = src.rarity;
            spec.Item.MaxStack = src.maxStack;
            spec.Item.Weight = src.weight;
            if (!string.IsNullOrEmpty(src.icon)) spec.Item.IconPath = src.icon;
        }

        private void UpdateEffectHint()
        {
            if (_effectRow == null) return;
            var spec = Spec;
            if (spec.Effect1Type == "PalExp_Increase" && Math.Abs(spec.Effect1Value + 100f) < 0.001f)
                UiKit.SetRowHint(_effectRow, "Pal XP fully cancelled. Player XP is untouched by this route.", UiKit.Success);
            else if (spec.Effect1Type == "PalExp_Increase" && spec.Effect1Value < -100f)
                UiKit.SetRowHint(_effectRow, "past -100; the rate may go negative rather than clamp", UiKit.Danger);
            else if (spec.Effect1Type == "PalExp_Increase")
                UiKit.SetRowHint(_effectRow, $"Pal XP changed by {spec.Effect1Value}%, not cancelled", UiKit.Warning);
            else
                UiKit.SetRowHint(_effectRow, "percent modifier for the chosen effect", UiKit.Muted);
        }

        private void UpdateMethodNotes()
        {
            if (_methodNotes == null) return;
            _methodNotes.Clear();
            switch (Spec.Method)
            {
                case PlayerXpMethod.PalXpOnlyDataOnly:
                    _methodNotes.Add(UiKit.Note("Pure PalSchema. No Lua, nothing to dump, works on a dedicated server with no host cooperation.", UiKit.Success));
                    _methodNotes.Add(UiKit.Note("Cancels PAL xp only. It cannot touch player XP: there is no player-EXP member in EPalPassiveSkillEffectType.", UiKit.Warning));
                    _methodNotes.Add(UiKit.Note("Untested: no shipped accessory uses PalExp_Increase, so whether the equipped-accessory path feeds it needs a run in game.", UiKit.Warning));
                    break;
                case PlayerXpMethod.PerPlayerBuffLua:
                    _methodNotes.Add(UiKit.Note("Per-player and multiplayer-correct: the wearer alone loses XP.", UiKit.Success));
                    _methodNotes.Add(UiKit.Note("Blocked on one UE4SS dump. Content/Pal was grepped for AddExp / GainExp with zero hits - exp granting and status-effect application are native C++ and appear nowhere in the FModel export.", UiKit.Danger));
                    _methodNotes.Add(UiKit.Note("The generated Scripts/main.lua is a working skeleton with the function name left blank.", UiKit.Muted));
                    break;
                case PlayerXpMethod.WorldExpRateLua:
                    _methodNotes.Add(UiKit.Note("Uses PalGameWorldSettings.OptionSettings.ExpRate - a real named property from Config/DefaultPalWorldSettings.json, so no dump is needed.", UiKit.Success));
                    _methodNotes.Add(UiKit.Note("WORLD-WIDE, not per-player. In co-op, wearing the ring zeroes XP for everyone in the world.", UiKit.Warning));
                    _methodNotes.Add(UiKit.Note("It writes saved world config. If the game autosaves while the ring is on and then crashes, the world stays at ExpRate 0 with no ring equipped. The script stashes the previous rate to disk and repairs it on load.", UiKit.Danger));
                    _methodNotes.Add(UiKit.Note("Server-authoritative: on a dedicated server this only works from the host side.", UiKit.Muted));
                    break;
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

        private VisualElement ToggleRow(string label, bool value, Action<bool> set, string hint)
        {
            var f = new Toggle { value = value };
            f.RegisterValueChangedCallback(evt => { evt.StopPropagation(); set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, LabelWidth, 30);
        }

        private VisualElement Dropdown(string label, List<string> choices, int index, Action<int> set, string hint)
        {
            var list = choices.Count > 0 ? choices : new List<string> { "(none)" };
            var f = new DropdownField(list, Math.Min(Math.Max(0, index), list.Count - 1));
            f.RegisterValueChangedCallback(evt => { set(Math.Max(0, list.IndexOf(evt.newValue))); });
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
            picker.OnSelected += v => set(v);
            if (liveText) picker.OnTextChanged += v => set((v ?? "").Trim());
            return UiKit.Row(label, picker, hint, LabelWidth);
        }
    }
}
