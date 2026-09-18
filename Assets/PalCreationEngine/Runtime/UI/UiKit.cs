using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalCreationEngine.UI
{
    /// <summary>
    /// Small widget helpers and a shared palette.
    ///
    /// Styling is applied in C# rather than through a .uss asset on purpose:
    /// a stylesheet has to be found and loaded at runtime, and a missing or
    /// mis-referenced asset fails as an unstyled window rather than a clear
    /// error. Inline styles cannot be misplaced.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Background = new Color(0.11f, 0.11f, 0.12f);
        public static readonly Color Panel = new Color(0.16f, 0.16f, 0.18f);
        public static readonly Color Border = new Color(0.30f, 0.30f, 0.34f);
        public static readonly Color Text = new Color(0.88f, 0.88f, 0.90f);
        public static readonly Color Muted = new Color(0.55f, 0.55f, 0.60f);
        public static readonly Color Accent = new Color(0.38f, 0.65f, 0.94f);
        public static readonly Color Warning = new Color(0.95f, 0.72f, 0.35f);
        public static readonly Color Danger = new Color(0.92f, 0.45f, 0.45f);
        public static readonly Color Success = new Color(0.48f, 0.80f, 0.52f);

        public static VisualElement Section(string title, out VisualElement body)
        {
            var wrapper = new VisualElement();
            wrapper.style.marginBottom = 14;
            wrapper.style.backgroundColor = Panel;
            wrapper.style.borderTopLeftRadius = 6;
            wrapper.style.borderTopRightRadius = 6;
            wrapper.style.borderBottomLeftRadius = 6;
            wrapper.style.borderBottomRightRadius = 6;
            SetBorder(wrapper, Border, 1);

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 13;
            header.style.color = Text;
            header.style.paddingLeft = 10;
            header.style.paddingRight = 10;
            header.style.paddingTop = 8;
            header.style.paddingBottom = 8;
            wrapper.Add(header);

            body = new VisualElement();
            body.style.paddingLeft = 10;
            body.style.paddingRight = 10;
            body.style.paddingBottom = 10;
            wrapper.Add(body);

            return wrapper;
        }

        /// <summary>
        /// A compact label/field pair. Fields are kept narrow and the hint sits
        /// immediately after rather than stretching across the window: wide
        /// full-width rows put a label and its value so far apart they read as
        /// unrelated.
        /// </summary>
        public static VisualElement Row(
            string label, VisualElement field, string hint = null,
            float labelWidth = 132, float fieldWidth = 104)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.minHeight = 20;

            var caption = new Label(label);
            caption.style.width = labelWidth;
            caption.style.color = Text;
            caption.style.fontSize = 12;
            caption.style.flexShrink = 0;
            caption.style.overflow = Overflow.Hidden;
            row.Add(caption);

            field.style.width = fieldWidth;
            field.style.flexShrink = 0;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            row.Add(field);

            var note = new Label(hint ?? string.Empty);
            note.style.color = Muted;
            note.style.fontSize = 10;
            note.style.marginLeft = 6;
            note.style.flexGrow = 1;
            note.style.overflow = Overflow.Hidden;
            row.Add(note);
            row.userData = note;   // so callers can update the hint live

            return row;
        }

        /// <summary>Updates the hint text on a row built by <see cref="Row"/>.</summary>
        public static void SetRowHint(VisualElement row, string hint, Color? color = null)
        {
            if (row?.userData is not Label note) return;
            note.text = hint ?? string.Empty;
            note.style.color = color ?? Muted;
        }

        /// <summary>Side-by-side columns, for packing related groups together.</summary>
        public static VisualElement Columns(params VisualElement[] columns)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            foreach (var column in columns)
            {
                column.style.flexGrow = 1;
                column.style.flexBasis = 0;
                column.style.marginRight = 10;
                row.Add(column);
            }

            if (columns.Length > 0) columns[^1].style.marginRight = 0;
            return row;
        }

        public static VisualElement Column()
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        /// <summary>A tab strip that shows one page at a time.</summary>
        public sealed class Tabs : VisualElement
        {
            private readonly VisualElement _strip = new VisualElement();
            private readonly VisualElement _content = new VisualElement();
            private readonly List<(Button Button, VisualElement Page)> _pages =
                new List<(Button, VisualElement)>();

            public Tabs()
            {
                style.flexGrow = 1;

                _strip.style.flexDirection = FlexDirection.Row;
                _strip.style.marginBottom = 10;
                Add(_strip);

                _content.style.flexGrow = 1;
                Add(_content);
            }

            public void AddPage(string title, VisualElement page)
            {
                var index = _pages.Count;
                var button = new Button(() => Select(index)) { text = title };
                button.style.height = 26;
                button.style.minWidth = 96;
                button.style.marginRight = 4;
                SetBorder(button, Border, 1);
                _strip.Add(button);

                page.style.display = DisplayStyle.None;
                _content.Add(page);
                _pages.Add((button, page));

                if (index == 0) Select(0);
            }

            public void Select(int index)
            {
                for (var i = 0; i < _pages.Count; i++)
                {
                    var (button, page) = _pages[i];
                    var active = i == index;
                    page.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                    button.style.backgroundColor = active ? Accent : Panel;
                    button.style.color = active ? Color.white : Text;
                }
            }
        }

        public static Button PrimaryButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 30;
            button.style.minWidth = 130;
            button.style.marginRight = 8;
            button.style.backgroundColor = Accent;
            button.style.color = Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(button, Accent, 0);
            return button;
        }

        public static Button SecondaryButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 30;
            button.style.minWidth = 130;
            button.style.marginRight = 8;
            button.style.backgroundColor = Panel;
            button.style.color = Text;
            SetBorder(button, Border, 1);
            return button;
        }

        public static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        /// <summary>
        /// Type-to-filter picker. A plain dropdown is unusable at this scale --
        /// 731 Pals, 2,466 items, 1,905 passive skills -- so this filters as you
        /// type and shows the top matches, preferring prefix hits over
        /// substring ones so "Pal" surfaces "PalSphere" before "CrystalPalace".
        ///
        /// The text field stays freely editable: a valid ID that this build's
        /// data does not know about should still be typeable rather than
        /// blocked.
        /// </summary>
        public sealed class SearchPicker : VisualElement
        {
            private readonly TextField _input;
            private readonly VisualElement _results;
            private VisualElement _inputRow;

            /// <summary>
            /// Only one list is open at a time. Without this, every picker you
            /// touched stayed parented to the root until it individually lost
            /// focus, so several lists could sit open at once and each needed
            /// its own click to dismiss.
            /// </summary>
            private static SearchPicker _openPicker;
            private readonly Func<IEnumerable<string>> _source;
            private readonly int _maxResults;

            public event Action<string> OnSelected;

            /// <summary>
            /// Raised with the field text when Enter is pressed, for pickers that
            /// accept a value the list does not offer (a new Paldex number).
            /// </summary>
            public event Action<string> OnCommitted;

            public string Value
            {
                get => _input.value;
                set => _input.SetValueWithoutNotify(value ?? string.Empty);
            }

            public SearchPicker(Func<IEnumerable<string>> source, string placeholder, int maxResults = 320)
            {
                _source = source;
                _maxResults = maxResults;

                // Text field and a caret side by side: the caret opens the full
                // list so the options can be browsed, and typing narrows it.
                // A field that only reveals anything after you already guessed
                // some of the answer is not usable for lists this size.
                var inputRow = new VisualElement();
                inputRow.style.flexDirection = FlexDirection.Row;
                inputRow.style.alignItems = Align.Center;
                Add(inputRow);
                _inputRow = inputRow;

                _input = new TextField { value = string.Empty };
                _input.style.flexGrow = 1;
                _input.style.marginBottom = 0;
                _input.style.marginRight = 0;
                inputRow.Add(_input);

                var caret = new Button(Toggle) { text = "▾" };
                caret.style.width = 20;
                caret.style.height = 18;
                caret.style.marginLeft = 1;
                caret.style.marginRight = 0;
                caret.style.paddingLeft = 0;
                caret.style.paddingRight = 0;
                caret.style.backgroundColor = Panel;
                caret.style.color = Text;
                SetBorder(caret, Border, 1);
                inputRow.Add(caret);

                var hint = new Label(placeholder);
                hint.style.color = Muted;
                hint.style.fontSize = 10;
                hint.style.marginLeft = 2;
                Add(hint);

                // Absolutely positioned so the open list floats over the rows
                // below instead of shoving them down the panel -- an inline list
                // makes every field jump every time you type.
                // The open list is parented to the PANEL ROOT while visible,
                // not to this picker. UI Toolkit paints strictly in hierarchy
                // order, so a list nested in this row is painted over by every
                // row that comes after it however it is positioned. Reordering
                // siblings to compensate only moves the problem, and scrambles
                // the row's own label/field/hint order. Hoisting it to the root
                // at this picker's world coordinates is what actually works.
                _results = new VisualElement();
                _results.style.backgroundColor = Background;
                _results.style.display = DisplayStyle.None;
                _results.style.maxHeight = 240;
                // An absolutely positioned box does not clip its children by
                // default, so without this the list draws its full length down
                // the panel instead of scrolling inside 240px.
                _results.style.overflow = Overflow.Hidden;
                _results.style.position = Position.Absolute;
                SetBorder(_results, Border, 1);

                // A hoisted list would otherwise outlive the window that owns it.
                RegisterCallback<DetachFromPanelEvent>(_ => _results.RemoveFromHierarchy());

                _input.RegisterValueChangedCallback(evt => Refresh(evt.newValue));
                _input.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                    Hide();
                    OnCommitted?.Invoke(_input.value);
                });
                _input.RegisterCallback<FocusInEvent>(_ => Refresh(null));
                _input.RegisterCallback<FocusOutEvent>(_ =>
                {
                    // Delayed so a click on a result is not cancelled by the
                    // list disappearing on blur first.
                    schedule.Execute(Hide).ExecuteLater(150);
                });
            }

            /// <summary>
            /// Appends a button to the picker's own input row.
            ///
            /// Placing it in the surrounding row instead makes it sit lower than
            /// the caret: the picker is taller than its input row (it carries a
            /// hint label underneath), so anything centred against the picker
            /// drops below the caret centred against the input row.
            /// </summary>
            public void AddTrailing(VisualElement element)
            {
                _inputRow?.Add(element);
            }

            /// <summary>
            /// Sets the displayed text without raising OnSelected, for when the
            /// value is being driven by something other than the user picking
            /// from the list.
            /// </summary>
            public void SetValueSilently(string value) =>
                _input.SetValueWithoutNotify(value ?? string.Empty);

            /// <summary>
            /// Opens the list showing EVERYTHING, ignoring whatever is currently
            /// in the field. Filtering by the existing value meant a field that
            /// already held a pick could only ever show that one row -- which
            /// looked like the other 203 Paldex entries were missing.
            /// </summary>
            public void Toggle()
            {
                if (_results.style.display == DisplayStyle.Flex) Hide();
                else Refresh(null);
            }

            private void Hide()
            {
                if (_openPicker == this) _openPicker = null;
                _results.style.display = DisplayStyle.None;
                _results.RemoveFromHierarchy();
            }

            /// <summary>Places the list under this picker, in panel coordinates.</summary>
            private void ShowAt()
            {
                var root = panel?.visualTree;
                if (root == null) return;

                if (_openPicker != null && _openPicker != this) _openPicker.Hide();
                _openPicker = this;

                if (_results.parent != root) root.Add(_results);

                // ChangeCoordinatesTo walks every transform between this element
                // and the root -- scroll offsets, nested panels, editor DPI
                // scaling. Deriving the position from worldBound by hand ignored
                // all of that, which is what kept putting the list in the wrong
                // place.
                var rowHeight = _inputRow is { layout: { height: > 0 } }
                    ? _inputRow.layout.height
                    : 20f;
                var rowWidth = _inputRow is { layout: { width: > 0 } }
                    ? _inputRow.layout.width
                    : layout.width;

                var below = this.ChangeCoordinatesTo(root, new Vector2(0f, rowHeight));
                _results.style.left = below.x;
                _results.style.top = below.y;
                _results.style.width = rowWidth > 0 ? rowWidth : 200f;
                _results.style.display = DisplayStyle.Flex;
                _results.BringToFront();
            }

            private void Refresh(string query)
            {
                _results.Clear();

                // An empty query browses the whole list rather than showing
                // nothing, so the options are discoverable without guessing.
                var all = _source() ?? Enumerable.Empty<string>();
                var ranked = string.IsNullOrWhiteSpace(query) ? all : Rank(all, query);

                var matches = ranked.Take(_maxResults).ToList();
                if (matches.Count == 0)
                {
                    var empty = new Label("  no match");
                    empty.style.color = Muted;
                    empty.style.fontSize = 11;
                    _results.Add(empty);
                    ShowAt();
                    return;
                }

                var scroll = new ScrollView();
                scroll.style.maxHeight = 216;
                scroll.style.flexShrink = 0;
                foreach (var match in matches)
                {
                    var captured = match;
                    // A Label driven by PointerDownEvent, not a Button. A Button
                    // fires only when press AND release land on it, and the
                    // focus-out timer could remove the list in between -- so a
                    // deliberate click selected nothing.
                    var option = new Label(captured);
                    option.style.unityTextAlign = TextAnchor.MiddleLeft;
                    option.style.color = Text;
                    option.style.height = 18;
                    option.style.paddingLeft = 4;
                    // Without this the options shrink to nothing and draw on top
                    // of each other: they are flex children of a height-capped
                    // container, and flexShrink defaults to 1.
                    option.style.flexShrink = 0;
                    // Long IDs like AmaterasuWolf_Dark_Quest_Enemy wrap to two
                    // lines inside an 18px row and spill over the option below.
                    option.style.whiteSpace = WhiteSpace.NoWrap;
                    option.style.overflow = Overflow.Hidden;
                    option.style.fontSize = 11;

                    option.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        evt.StopPropagation();
                        _input.SetValueWithoutNotify(captured);
                        Hide();
                        OnSelected?.Invoke(captured);
                    });
                    option.RegisterCallback<PointerEnterEvent>(_ =>
                        option.style.backgroundColor = new Color(0.25f, 0.32f, 0.42f));
                    option.RegisterCallback<PointerLeaveEvent>(_ =>
                        option.style.backgroundColor = Color.clear);

                    scroll.Add(option);
                }

                _results.Add(scroll);

                // Say so when the list is cut off, rather than letting it look
                // like these are all the options there are.
                var total = string.IsNullOrWhiteSpace(query)
                    ? _source().Count()
                    : Rank(_source(), query).Count();
                if (total > matches.Count)
                {
                    var more = new Label($"  {matches.Count} of {total} shown - keep typing to narrow");
                    more.style.color = Muted;
                    more.style.fontSize = 10;
                    more.style.paddingBottom = 2;
                    _results.Add(more);
                }

                ShowAt();
            }

            private static IEnumerable<string> Rank(IEnumerable<string> candidates, string query)
            {
                var prefix = new List<string>();
                var contains = new List<string>();

                foreach (var candidate in candidates)
                {
                    if (candidate == null) continue;
                    if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                        prefix.Add(candidate);
                    else if (candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        contains.Add(candidate);
                }

                return prefix.Concat(contains);
            }
        }
    }
}
