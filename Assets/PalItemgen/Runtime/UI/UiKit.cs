using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// Widget helpers and the shared palette, styled in C# rather than a .uss
    /// asset so a missing stylesheet can never leave the window unstyled.
    /// Adapted from the PalCreationEngine UiKit so both tools look alike.
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

        public static VisualElement Section(string title, out VisualElement body, string subtitle = null)
        {
            var wrapper = new VisualElement();
            wrapper.style.marginBottom = 10;
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
            header.style.paddingBottom = subtitle == null ? 6 : 2;
            wrapper.Add(header);
            if (subtitle != null)
            {
                var sub = new Label(subtitle);
                sub.style.color = Muted;
                sub.style.fontSize = 10;
                sub.style.paddingLeft = 10;
                sub.style.paddingRight = 10;
                sub.style.paddingBottom = 6;
                sub.style.whiteSpace = WhiteSpace.Normal;
                wrapper.Add(sub);
            }

            body = new VisualElement();
            body.style.paddingLeft = 10;
            body.style.paddingRight = 10;
            body.style.paddingBottom = 10;
            wrapper.Add(body);
            return wrapper;
        }

        /// <summary>
        /// Label + field on one line, hint on its own line underneath. The hint
        /// used to sit beside the field, which in a three-column window left it
        /// a few dozen pixels and wrapped it one word per line.
        /// </summary>
        /// <param name="fieldWidth">Fixed width, or 0 to let the field fill the line.</param>
        public static VisualElement Row(string label, VisualElement field, string hint = null, float labelWidth = 150, float fieldWidth = 0)
        {
            var wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.marginBottom = 3;

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            line.style.minHeight = 20;
            wrapper.Add(line);

            var caption = new Label(label);
            caption.style.width = labelWidth;
            caption.style.color = Text;
            caption.style.fontSize = 12;
            caption.style.flexShrink = 0;
            caption.style.whiteSpace = WhiteSpace.Normal;
            line.Add(caption);

            if (fieldWidth > 0)
            {
                field.style.width = fieldWidth;
                field.style.flexGrow = 0;
            }
            else
            {
                field.style.flexGrow = 1;
            }
            field.style.flexShrink = 1;
            field.style.minWidth = 40;
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            line.Add(field);

            var note = new Label(hint ?? string.Empty);
            note.style.color = Muted;
            note.style.fontSize = 10;
            note.style.marginLeft = labelWidth;
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            wrapper.Add(note);
            wrapper.userData = note;
            return wrapper;
        }

        public static void SetRowHint(VisualElement row, string hint, Color? color = null)
        {
            if (row?.userData is not Label note) return;
            note.text = hint ?? string.Empty;
            note.style.color = color ?? Muted;
            note.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
        }

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

        /// <summary>A tab strip that shows one page at a time (same look as PalCreationEngine).</summary>
        public sealed class Tabs : VisualElement
        {
            private readonly VisualElement _strip = new VisualElement();
            private readonly VisualElement _content = new VisualElement();
            private readonly List<(Button Button, VisualElement Page)> _pages = new List<(Button, VisualElement)>();

            public event Action<int> OnSelected;

            public Tabs()
            {
                style.flexGrow = 1;
                _strip.style.flexDirection = FlexDirection.Row;
                _strip.style.marginBottom = 8;
                Add(_strip);
                _content.style.flexGrow = 1;
                Add(_content);
            }

            public void AddPage(string title, VisualElement page)
            {
                var index = _pages.Count;
                var button = new Button(() => Select(index)) { text = title };
                button.style.height = 26;
                button.style.minWidth = 150;
                button.style.marginRight = 4;
                SetBorder(button, Border, 1);
                _strip.Add(button);
                page.style.display = DisplayStyle.None;
                page.style.flexGrow = 1;
                _content.Add(page);
                _pages.Add((button, page));
                if (index == 0) Select(0);
            }

            public void Select(int index)
            {
                SearchPicker.CloseOpen();
                for (var i = 0; i < _pages.Count; i++)
                {
                    var (button, page) = _pages[i];
                    var active = i == index;
                    page.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                    button.style.backgroundColor = active ? Accent : Panel;
                    button.style.color = active ? Color.white : Text;
                }
                OnSelected?.Invoke(index);
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
            button.style.minWidth = 110;
            button.style.marginRight = 8;
            button.style.backgroundColor = Panel;
            button.style.color = Text;
            SetBorder(button, Border, 1);
            return button;
        }

        public static Button SmallButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 20;
            button.style.minWidth = 24;
            button.style.marginLeft = 4;
            button.style.marginRight = 0;
            button.style.paddingLeft = 6;
            button.style.paddingRight = 6;
            button.style.backgroundColor = Panel;
            button.style.color = Text;
            SetBorder(button, Border, 1);
            return button;
        }

        /// <summary>A small "browse" button that opens the table-style browser for a picker.</summary>
        public static Button BrowseButton(Action onClick)
        {
            var b = SmallButton("▤ browse", onClick);
            b.tooltip = "open the searchable table (every column, sortable)";
            return b;
        }

        public static Label Note(string text, Color? color = null)
        {
            var l = new Label(text);
            l.style.color = color ?? Muted;
            l.style.fontSize = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 4;
            return l;
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
        /// Type-to-filter picker for lists far too long for a dropdown (2,466 items).
        /// Shows the best matches under the field; the text stays freely editable.
        /// </summary>
        public sealed class SearchPicker : VisualElement
        {
            private readonly TextField _input;
            private readonly VisualElement _results;
            private readonly VisualElement _inputRow;
            private readonly Func<IEnumerable<(string Value, string Display)>> _source;
            private readonly int _maxResults;
            private static SearchPicker _openPicker;

            /// <summary>Closes whichever picker list is open (tab switches, dialogs).</summary>
            public static void CloseOpen() => _openPicker?.Hide();

            public event Action<string> OnSelected;

            public string Value
            {
                get => _input.value;
                set => _input.SetValueWithoutNotify(value ?? string.Empty);
            }

            private string _lastDisplay;
            private bool _hoverResults;

            /// <summary>Sets the text without raising OnSelected (used when the table browser picks for us).</summary>
            public void SetValueSilently(string value) => _input.SetValueWithoutNotify(value ?? string.Empty);

            /// <summary>
            /// Shows the chosen entry's display text ("Name  [id]") instead of the raw id.
            /// Focusing or opening the caret then lists everything, not just that entry.
            /// </summary>
            public void SetDisplay(string display)
            {
                _lastDisplay = display ?? string.Empty;
                _input.SetValueWithoutNotify(_lastDisplay);
            }

            /// <summary>Adds a control after the caret, on the same line (the ▤ browse button).</summary>
            public void AddTrailing(VisualElement element)
            {
                element.style.marginLeft = 2;
                element.style.marginRight = 0;
                _inputRow.Add(element);
            }

            public SearchPicker(Func<IEnumerable<(string Value, string Display)>> source, string placeholder, int maxResults = 200)
            {
                _source = source;
                _maxResults = maxResults;

                _inputRow = new VisualElement();
                _inputRow.style.flexDirection = FlexDirection.Row;
                _inputRow.style.alignItems = Align.Center;
                Add(_inputRow);

                _input = new TextField { value = string.Empty };
                _input.style.flexGrow = 1;
                _input.style.marginBottom = 0;
                _input.style.marginRight = 0;
                _inputRow.Add(_input);

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
                _inputRow.Add(caret);

                if (!string.IsNullOrEmpty(placeholder))
                {
                    var hint = new Label(placeholder);
                    hint.style.color = Muted;
                    hint.style.fontSize = 10;
                    hint.style.marginLeft = 2;
                    Add(hint);
                }

                _results = new VisualElement();
                _results.style.backgroundColor = Background;
                _results.style.display = DisplayStyle.None;
                _results.style.overflow = Overflow.Hidden;
                _results.style.position = Position.Absolute;
                SetBorder(_results, Border, 1);

                RegisterCallback<DetachFromPanelEvent>(_ => _results.RemoveFromHierarchy());
                _input.RegisterValueChangedCallback(evt => Refresh(evt.newValue));
                _input.RegisterCallback<FocusInEvent>(_ => Refresh(_lastDisplay != null && _input.value == _lastDisplay ? null : _input.value));
                // Dragging the list's scrollbar takes focus off the input: keep the list open while the pointer is over it.
                _results.RegisterCallback<MouseEnterEvent>(_ => _hoverResults = true);
                _results.RegisterCallback<MouseLeaveEvent>(_ => _hoverResults = false);
                _input.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() => { if (!_hoverResults) Hide(); }).ExecuteLater(150));
            }

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

            private void ShowAt()
            {
                var root = panel?.visualTree;
                if (root == null) return;
                if (_openPicker != null && _openPicker != this) _openPicker.Hide();
                _openPicker = this;
                if (_results.parent != root) root.Add(_results);
                var rowHeight = _inputRow.layout.height > 0 ? _inputRow.layout.height : 20f;
                var rowWidth = _inputRow.layout.width > 0 ? _inputRow.layout.width : layout.width;
                var below = this.ChangeCoordinatesTo(root, new Vector2(0f, rowHeight));
                _results.style.left = below.x;
                _results.style.top = below.y;
                _results.style.width = rowWidth > 0 ? Mathf.Max(rowWidth, 420f) : 420f;
                _results.style.display = DisplayStyle.Flex;
                _results.BringToFront();
            }

            private void Refresh(string query)
            {
                _results.Clear();
                var q = (query ?? string.Empty).Trim();
                var lower = q.ToLowerInvariant();
                IEnumerable<(string Value, string Display)> hits;
                var all = _source().ToList();
                if (lower.Length == 0)
                {
                    hits = all.Take(_maxResults);
                }
                else
                {
                    var prefix = all.Where(e => e.Display.ToLowerInvariant().StartsWith(lower) || e.Value.ToLowerInvariant().StartsWith(lower));
                    var contains = all.Where(e => !e.Display.ToLowerInvariant().StartsWith(lower) && !e.Value.ToLowerInvariant().StartsWith(lower)
                                                  && (e.Display.ToLowerInvariant().Contains(lower) || e.Value.ToLowerInvariant().Contains(lower)));
                    hits = prefix.Concat(contains).Take(_maxResults);
                }
                // A real scrolling list (mouse wheel, drag), not the editor popup menu.
                // Every row has the same fixed height and one line of text, so rows
                // never wrap or pile onto each other; the list shows up to 14 rows.
                const float rowHeight = 26f;
                var scroll = new ScrollView();
                scroll.style.maxHeight = rowHeight * 14 + 4;
                var count = 0;
                foreach (var hit in hits)
                {
                    count++;
                    var value = hit.Value;
                    var item = new Label(hit.Display);
                    item.style.color = Text;
                    item.style.fontSize = 13;
                    item.style.height = rowHeight;
                    item.style.minHeight = rowHeight;
                    item.style.flexShrink = 0;
                    item.style.unityTextAlign = TextAnchor.MiddleLeft;
                    item.style.whiteSpace = WhiteSpace.NoWrap;
                    item.style.overflow = Overflow.Hidden;
                    item.style.textOverflow = TextOverflow.Ellipsis;
                    item.style.paddingLeft = 8;
                    item.style.paddingRight = 8;
                    item.RegisterCallback<MouseEnterEvent>(_ => item.style.backgroundColor = Panel);
                    item.RegisterCallback<MouseLeaveEvent>(_ => item.style.backgroundColor = StyleKeyword.Null);
                    item.RegisterCallback<MouseDownEvent>(_ =>
                    {
                        _input.SetValueWithoutNotify(value);
                        Hide();
                        OnSelected?.Invoke(value);
                    });
                    scroll.Add(item);
                }
                if (count == 0)
                {
                    var none = new Label("no matches");
                    none.style.color = Muted;
                    none.style.paddingLeft = 6;
                    scroll.Add(none);
                }
                _results.Add(scroll);
                ShowAt();
            }
        }
    }
}
