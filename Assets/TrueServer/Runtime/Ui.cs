using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueServer
{
    /// <summary>
    /// Small helpers so every panel looks the same: dark chrome, flat cards, one accent.
    /// </summary>
    public static class Ui
    {
        public static readonly Color Page = new Color(0.086f, 0.086f, 0.102f);
        public static readonly Color Rail = new Color(0.063f, 0.063f, 0.075f);
        public static readonly Color Card = new Color(0.114f, 0.114f, 0.141f);
        public static readonly Color Sunken = new Color(0.075f, 0.075f, 0.090f);

        public static readonly Color TextHi = new Color(0.96f, 0.96f, 0.98f);
        public static readonly Color TextMid = new Color(0.72f, 0.72f, 0.76f);
        public static readonly Color TextDim = new Color(0.42f, 0.42f, 0.47f);

        public static readonly Color Accent = new Color(0.357f, 0.498f, 0.878f);
        public static readonly Color Good = new Color(0.247f, 0.698f, 0.498f);
        public static readonly Color Warn = new Color(0.878f, 0.725f, 0.373f);
        public static readonly Color Bad = new Color(0.902f, 0.416f, 0.435f);

        public static VisualElement Row(float gap = 8f)
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Row;
            v.style.alignItems = Align.Center;
            return v;
        }

        public static VisualElement Col(float gap = 0f)
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Column;
            return v;
        }

        public static VisualElement CardBox(float pad = 12f)
        {
            var v = new VisualElement();
            v.style.backgroundColor = Card;
            SetRadius(v, 8);
            v.style.paddingLeft = pad; v.style.paddingRight = pad;
            v.style.paddingTop = pad; v.style.paddingBottom = pad;
            v.style.marginBottom = 8;
            return v;
        }

        public static void SetRadius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r;
            v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r;
        }

        public static void SetMargin(VisualElement v, float t, float r, float b, float l)
        {
            v.style.marginTop = t; v.style.marginRight = r;
            v.style.marginBottom = b; v.style.marginLeft = l;
        }

        public static Label Text(string s, int size, Color c, bool bold = false)
        {
            var l = new Label(s ?? string.Empty);
            l.style.fontSize = size;
            l.style.color = c;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static Label Caption(string s) => Text(s, 10, TextDim);
        public static Label Body(string s) => Text(s, 12, TextMid);
        public static Label Head(string s) => Text(s, 18, TextHi, true);

        /// <summary>Small uppercase-ish section marker, as in the reference GUI.</summary>
        public static Label SectionLabel(string s)
        {
            var l = Text(s, 10, TextDim, true);
            l.style.marginBottom = 6;
            l.style.letterSpacing = 1f;
            return l;
        }

        public static Button Btn(string label, Action onClick, Color? bg = null, Color? fg = null)
        {
            var b = new Button(onClick) { text = label };
            b.style.backgroundColor = bg ?? new Color(0.15f, 0.15f, 0.18f);
            b.style.color = fg ?? TextHi;
            b.style.fontSize = 11;
            b.style.paddingLeft = 10; b.style.paddingRight = 10;
            b.style.paddingTop = 5; b.style.paddingBottom = 5;
            b.style.marginLeft = 0; b.style.marginRight = 6;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            SetRadius(b, 5);
            return b;
        }

        /// <summary>Coloured status dot + text, used for every live/blocked indicator.</summary>
        public static VisualElement Dot(string label, Color c, int size = 11)
        {
            var row = Row();
            var d = new VisualElement();
            d.style.width = 7; d.style.height = 7;
            SetRadius(d, 4);
            d.style.backgroundColor = c;
            d.style.marginRight = 6;
            d.style.flexShrink = 0;
            row.Add(d);
            row.Add(Text(label, size, c));
            return row;
        }

        /// <summary>One metric tile: small caption, big value, small hint.</summary>
        public static VisualElement Stat(string caption, string value, string hint)
        {
            var v = CardBox(10);
            v.style.flexGrow = 1;
            v.style.flexBasis = 0;
            v.style.marginRight = 8;
            v.style.marginBottom = 0;
            v.Add(Caption(caption));
            var val = Text(value, 18, TextHi, true);
            val.style.marginTop = 2;
            v.Add(val);
            if (!string.IsNullOrEmpty(hint)) v.Add(Text(hint, 9, TextDim));
            return v;
        }

        /// <summary>A pill, used for mod entries and tier markers.</summary>
        public static VisualElement Pill(string text, Color fg, Color bg)
        {
            var v = new VisualElement();
            v.style.backgroundColor = bg;
            SetRadius(v, 4);
            v.style.paddingLeft = 7; v.style.paddingRight = 7;
            v.style.paddingTop = 3; v.style.paddingBottom = 3;
            v.style.marginRight = 5; v.style.marginBottom = 5;
            v.Add(Text(text, 10, fg));
            return v;
        }

        /// <summary>A warning strip. Used wherever the tool must not pretend something works.</summary>
        public static VisualElement Notice(string title, string body, Color accent)
        {
            var v = CardBox(10);
            v.style.borderLeftWidth = 3;
            v.style.borderLeftColor = accent;
            v.style.borderTopLeftRadius = 0; v.style.borderBottomLeftRadius = 0;
            v.Add(Text(title, 11, accent, true));
            if (!string.IsNullOrEmpty(body))
            {
                var b = Text(body, 11, TextMid);
                b.style.marginTop = 3;
                v.Add(b);
            }
            return v;
        }

        public static VisualElement Spacer(float h)
        {
            var v = new VisualElement();
            v.style.height = h;
            v.style.flexShrink = 0;
            return v;
        }


        // ---------------------------------------------------------------- form controls

        /// <summary>A titled card section, the basic block of the create form.</summary>
        public static VisualElement Section(string title, string subtitle = null)
        {
            var v = CardBox(14);
            v.style.marginBottom = 10;
            v.Add(Text(title, 14, TextHi, true));
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = Text(subtitle, 10, TextDim);
                s.style.marginBottom = 8;
                v.Add(s);
            }
            else v.Add(Spacer(8));
            return v;
        }

        /// <summary>
        /// Label on the left, control on the right. The control is width-capped rather than
        /// stretched: a text box spanning a 2000 px window is unreadable and hides the fact that
        /// most of these fields hold a short value.
        /// </summary>
        public static VisualElement FormRow(string label, VisualElement control, string hint = null,
                                            bool required = false, float labelWidth = 230f,
                                            float controlWidth = 420f)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 7;

            var lcol = Col();
            lcol.style.width = labelWidth;
            lcol.style.flexShrink = 0;
            lcol.style.paddingTop = 2;
            lcol.style.paddingRight = 10;

            var lrow = Row();
            lrow.Add(Text(label, 12, required ? TextHi : TextMid, required));
            if (required) lrow.Add(Text(" *", 12, Bad, true));
            lcol.Add(lrow);
            if (!string.IsNullOrEmpty(hint)) lcol.Add(Text(hint, 9, TextDim));
            row.Add(lcol);

            control.style.flexGrow = 1;
            control.style.maxWidth = controlWidth;
            row.Add(control);
            return row;
        }

        /// <summary>Two form rows side by side. Used for short paired values like the two ports.</summary>
        public static VisualElement TwoUp(VisualElement a, VisualElement b)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            a.style.flexGrow = 1; a.style.flexBasis = 0; a.style.marginRight = 16;
            b.style.flexGrow = 1; b.style.flexBasis = 0;
            row.Add(a); row.Add(b);
            return row;
        }

        /// <summary>A half-width form row, sized for TwoUp.</summary>
        public static VisualElement HalfRow(string label, VisualElement control, string hint = null,
                                            bool required = false) =>
            FormRow(label, control, hint, required, labelWidth: 150f, controlWidth: 220f);

        public static TextField Field(string value, bool password = false)
        {
            var t = new TextField { value = value ?? string.Empty };
            t.isPasswordField = password;
            t.style.fontSize = 12;
            t.style.height = 22;
            t.style.marginLeft = 0; t.style.marginRight = 0;
            return t;
        }

        /// <summary>Checkbox with its own caption to the right, as in the reference GUI.</summary>
        public static Toggle Check(bool value, string caption = null)
        {
            var t = new Toggle(caption ?? string.Empty) { value = value };
            t.style.fontSize = 12;
            t.style.marginLeft = 0;
            var lbl = t.Q<Label>();
            if (lbl != null)
            {
                lbl.style.color = TextMid; lbl.style.minWidth = 0;
                // a long caption wraps instead of being cut off ("Enable — required for Broadcast, Sh…", the exe, 2026-09-18)
                lbl.style.whiteSpace = WhiteSpace.Normal;
                lbl.style.flexShrink = 1;
            }
            var box = t.Q(className: Toggle.inputUssClassName);
            if (box != null) box.style.flexShrink = 0;
            return t;
        }

        public static DropdownField Drop(System.Collections.Generic.List<string> choices, string value)
        {
            var idx = choices.IndexOf(value);
            var d = new DropdownField(choices, idx < 0 ? 0 : idx);
            d.style.fontSize = 12;
            d.style.height = 22;
            d.style.marginLeft = 0; d.style.marginRight = 0;
            return d;
        }

        /// <summary>Slider plus a live numeric readout, used for every option with a published range.</summary>
        public static VisualElement SliderRow(float value, float min, float max, bool asInt, System.Action<float> onChange, out Label readout)
        {
            var row = Row();
            var s = new Slider(min, max) { value = Mathf.Clamp(value, min, max) };
            s.style.flexGrow = 1;
            s.style.marginLeft = 0;
            row.Add(s);

            var lbl = Text(Fmt(s.value, asInt), 11, TextHi);
            lbl.style.width = 58;
            lbl.style.flexShrink = 0;
            lbl.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(lbl);
            readout = lbl;

            var capture = lbl;
            s.RegisterValueChangedCallback(e =>
            {
                var v = asInt ? Mathf.Round(e.newValue) : e.newValue;
                capture.text = Fmt(v, asInt);
                onChange?.Invoke(v);
            });

            var rng = Text(Fmt(min, asInt) + " - " + Fmt(max, asInt), 9, TextDim);
            rng.style.width = 86;
            rng.style.flexShrink = 0;
            rng.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(rng);
            return row;
        }

        public static string Fmt(float v, bool asInt) =>
            asInt ? Mathf.RoundToInt(v).ToString() : v.ToString("0.###");

        /// <summary>Segmented selector -- difficulty and world mode use it.</summary>
        public static VisualElement Segmented(string[] options, string selected, System.Action<string> onPick)
        {
            var row = Row();
            row.style.flexWrap = Wrap.Wrap;
            var buttons = new System.Collections.Generic.List<Button>();

            foreach (var opt in options)
            {
                var o = opt;
                Button b = null;
                b = new Button(() =>
                {
                    onPick?.Invoke(o);
                    foreach (var x in buttons) Paint(x, (string)x.userData == o);
                }) { text = o };
                b.userData = o;
                b.style.fontSize = 11;
                b.style.height = 26;
                b.style.paddingLeft = 12; b.style.paddingRight = 12;
                b.style.marginLeft = 0; b.style.marginRight = 5; b.style.marginBottom = 4;
                b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0;
                b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
                SetRadius(b, 5);
                Paint(b, o == selected);
                buttons.Add(b);
                row.Add(b);
            }
            return row;
        }

        private static void Paint(Button b, bool on)
        {
            b.style.backgroundColor = on ? new Color(0.184f, 0.243f, 0.396f) : new Color(0.145f, 0.145f, 0.180f);
            b.style.color = on ? Color.white : TextMid;
            b.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
        }

        public static ScrollView Scroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.style.flexGrow = 1;
            return s;
        }
    }
}
