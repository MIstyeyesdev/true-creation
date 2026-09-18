using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RingOfNoXp.Lookup;
using RingOfNoXp.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace RingOfNoXp.UI
{
    /// <summary>
    /// Tab 3 - what the game actually pays out, and what this mod does to it.
    ///
    /// The numbers on the left are the game's own tables (DT_PalExpTable and
    /// DT_PalCaptureBonusExpTable, read straight out of the FModel export). The
    /// column on the right applies the tonic's current Exp_Increase value so the
    /// effect of a setting is visible before it is ever installed.
    ///
    /// The measured block underneath is real in-game data, not a prediction: it
    /// is what polling GetExp() recorded, and it is here because it disagrees
    /// with the arithmetic in a way worth keeping in front of the reader.
    /// </summary>
    public sealed class XpRatesView : VisualElement
    {
        private const float LabelWidth = 150f;

        private readonly GameData _data;
        private readonly Func<RingProject> _project;

        private int _from = 1;
        private int _to = 25;
        private VisualElement _tableBody;
        private Label _rateNote;

        public XpRatesView(GameData data, Func<RingProject> project)
        {
            _data = data;
            _project = project;
            style.flexGrow = 1;
            Refresh();
        }

        private ConsumableSpec Spec => _project().Consumable;

        /// <summary>
        /// The multiplier the tonic's Exp_Increase value implies, read the obvious
        /// way: -100 means "reduce by 100%". Whether the game clamps this is an
        /// open question the measured block speaks to.
        /// </summary>
        private float Multiplier()
        {
            var spec = Spec;
            if (!spec.Enabled || spec.Effect1Type != "Exp_Increase") return 1f;
            return Math.Max(0f, 1f + spec.Effect1Value / 100f);
        }

        public void Refresh()
        {
            Clear();

            if (_data.ExpLevels.Count == 0)
            {
                Add(UiKit.Note("exp.json is not loaded, so the game's EXP tables cannot be shown. " +
                               "It belongs beside items.json in StreamingAssets/RingOfNoXp.", UiKit.Warning));
                return;
            }

            Add(UiKit.Columns(LeftColumn(), RightColumn()));
        }

        private VisualElement LeftColumn()
        {
            var column = UiKit.Column();

            var section = UiKit.Section("Game EXP tables", out var body,
                "Straight from DT_PalExpTable and DT_PalCaptureBonusExpTable. \"Defeat\" is what a character " +
                "of that level is worth; \"capture bonus\" is added on top when you catch it.");

            var range = new VisualElement();
            range.style.flexDirection = FlexDirection.Row;
            range.style.alignItems = Align.Center;

            var from = new IntegerField { value = _from };
            from.style.width = 60;
            from.RegisterValueChangedCallback(evt => { _from = Math.Max(1, evt.newValue); RebuildTable(); });
            range.Add(from);

            var dash = new Label("  to  ");
            dash.style.color = UiKit.Muted;
            range.Add(dash);

            var to = new IntegerField { value = _to };
            to.style.width = 60;
            to.RegisterValueChangedCallback(evt => { _to = Math.Min(100, evt.newValue); RebuildTable(); });
            range.Add(to);

            body.Add(UiKit.Row("Levels", range, "1 to 100", LabelWidth));

            body.Add(HeaderRow());

            var scroll = new ScrollView();
            scroll.style.maxHeight = 420;
            _tableBody = scroll.contentContainer;
            body.Add(scroll);

            column.Add(section);
            RebuildTable();
            return column;
        }

        private static VisualElement HeaderRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = UiKit.Border;
            row.style.paddingBottom = 3;
            row.style.marginTop = 4;
            foreach (var (text, width) in new[]
                     {
                         ("Lv", 34f), ("Defeat", 62f), ("Capture", 66f),
                         ("Build", 52f), ("Craft", 52f), ("To next", 78f), ("With tonic", 78f),
                     })
            {
                var l = new Label(text);
                l.style.width = width;
                l.style.fontSize = 10;
                l.style.color = UiKit.Muted;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(l);
            }
            return row;
        }

        private void RebuildTable()
        {
            if (_tableBody == null) return;
            _tableBody.Clear();

            var mult = Multiplier();
            var rows = _data.ExpLevels.Where(l => l.level >= _from && l.level <= _to).OrderBy(l => l.level);

            foreach (var lv in rows)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.minHeight = 18;

                // A capture pays the defeat value plus the level's capture bonus.
                var capture = lv.dropExp + lv.captureBonus;
                var reduced = capture * mult;

                void Cell(string text, float width, Color? color = null)
                {
                    var l = new Label(text);
                    l.style.width = width;
                    l.style.fontSize = 11;
                    l.style.color = color ?? UiKit.Text;
                    row.Add(l);
                }

                Cell(lv.level.ToString(), 34, UiKit.Muted);
                Cell(N(lv.dropExp), 62);
                Cell(N(capture), 66);
                Cell(N(lv.buildExp), 52, UiKit.Muted);
                Cell(N(lv.craftExp), 52, UiKit.Muted);
                Cell(N(lv.nextExp), 78, UiKit.Muted);
                Cell(reduced < 1f && mult < 1f ? "<1" : N((int)Math.Round(reduced)),
                    78, mult < 1f ? UiKit.Success : UiKit.Muted);

                _tableBody.Add(row);
            }
        }

        private VisualElement RightColumn()
        {
            var column = UiKit.Column();

            var spec = Spec;
            var mult = Multiplier();

            var current = UiKit.Section("Your current setting", out var cbody,
                "What the consumable tab is set to right now.");
            cbody.Add(UiKit.KV("Effect", spec.Effect1Type, UiKit.Text, 130));
            cbody.Add(UiKit.KV("Value", spec.Effect1Value.ToString(CultureInfo.InvariantCulture), UiKit.Text, 130));
            cbody.Add(UiKit.KV("Multiplier", mult.ToString("0.##") + " x", mult < 1f ? UiKit.Success : UiKit.Muted, 130,
                mult <= 0f ? "arithmetically zero" : null));
            cbody.Add(UiKit.KV("Duration", spec.EffectTime + " s", UiKit.Text, 130));
            _rateNote = UiKit.Note("", UiKit.Muted);
            cbody.Add(_rateNote);
            column.Add(current);

            var measured = UiKit.Section("Measured in game", out var mbody,
                "Recorded by polling GetExp() from Lua - not read off the HUD, which proved unreliable.");
            mbody.Add(UiKit.KV("No tonic, capture", "+36 +38 +39 +41 +44 +48", UiKit.Text, 130,
                "climbing because of the same-species capture streak bonus"));
            mbody.Add(UiKit.KV("Tonic, capture", "+1  +1  +1", UiKit.Success, 130,
                "three consecutive; party pals took +3/+4 the same moment"));
            mbody.Add(UiKit.KV("Tonic, boss", "+68", UiKit.Warning, 130,
                "in line with pals' +32..+67, i.e. NOT reduced"));
            mbody.Add(UiKit.Note(
                "So the tonic cuts ordinary capture EXP by roughly 98%, but a boss capture came through at " +
                "full value. Either the multiplier floors near 1%, or boss EXP travels a path that never " +
                "consults the food rate. Not yet settled.", UiKit.Muted));
            column.Add(measured);

            var scope = UiKit.Section("What each item can reach", out var sbody,
                "The two halves of this mod act on different things, and only one of them can touch you.");
            sbody.Add(UiKit.KV("Tonic", "player EXP", UiKit.Success, 130,
                "EPalFoodStatusEffectType::Exp_Increase, applied when eaten"));
            sbody.Add(UiKit.KV("Ring passive", "PAL EXP only", UiKit.Warning, 130,
                "EPalPassiveSkillEffectType::PalExp_Increase - there is no player-EXP member in that enum"));
            sbody.Add(UiKit.Note(
                "If the ring is meant to leave pal EXP alone, its PalExp_Increase effect is the wrong lever " +
                "and should be removed until a player-side one is found.", UiKit.Danger));
            column.Add(scope);

            UpdateNote();
            return column;
        }

        private void UpdateNote()
        {
            if (_rateNote == null) return;
            var mult = Multiplier();
            if (mult >= 1f)
            {
                _rateNote.text = "No reduction at this setting.";
                _rateNote.style.color = UiKit.Muted;
            }
            else if (mult <= 0f)
            {
                _rateNote.text = "Arithmetically this is total cancellation. In game it measured ~98% on ordinary " +
                                 "captures and 0% on a boss, so treat the last column as an upper bound on the effect.";
                _rateNote.style.color = UiKit.Warning;
            }
            else
            {
                _rateNote.text = $"Ordinary EXP should land at about {mult * 100f:0}% of the table value.";
                _rateNote.style.color = UiKit.Muted;
            }
        }

        private static string N(int v) => v.ToString("#,0", CultureInfo.InvariantCulture);
    }
}
