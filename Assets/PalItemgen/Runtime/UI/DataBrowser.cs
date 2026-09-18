using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>One column of a browser table.</summary>
    public sealed class BrowserColumn
    {
        public string Title;
        public float Width;

        /// <summary>True for numeric columns so sorting compares values rather than text.</summary>
        public bool Numeric;

        public BrowserColumn(string title, float width, bool numeric = false)
        {
            Title = title;
            Width = width;
            Numeric = numeric;
        }
    }

    /// <summary>
    /// The spreadsheet-style browser from the Pal Creation Engine: every field
    /// as a column, a search box per column plus one across all columns,
    /// click-to-sort headers, a live row count, click a row to pick it.
    /// Column 0 is always the value handed back when a row is picked.
    ///
    /// Plain VisualElement with no UnityEditor dependency, so the editor window
    /// and the runtime app can both host it.
    /// </summary>
    public sealed class DataBrowser : VisualElement
    {
        private readonly List<BrowserColumn> _columns;
        private readonly List<string[]> _rows;
        private readonly List<TextField> _columnSearches = new();
        private readonly VisualElement _body;
        private readonly Label _countLabel;
        private readonly Action<string> _onPick;

        private TextField _globalSearch;
        private int _sortColumn = -1;
        private bool _sortDescending;

        /// <summary>Rows drawn at once. Everything matching is still counted.</summary>
        private const int MaxRendered = 400;

        public DataBrowser(
            string title,
            List<BrowserColumn> columns,
            List<string[]> rows,
            Action<string> onPick,
            Action onClose = null)
        {
            _columns = columns;
            _rows = rows;
            _onPick = onPick;

            style.flexGrow = 1;
            style.backgroundColor = UiKit.Background;
            style.paddingLeft = 10;
            style.paddingRight = 10;
            style.paddingTop = 8;
            style.paddingBottom = 8;

            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.alignItems = Align.Center;
            titleBar.style.marginBottom = 6;

            var heading = new Label(title);
            heading.style.fontSize = 14;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = UiKit.Text;
            heading.style.flexGrow = 1;
            titleBar.Add(heading);

            _countLabel = new Label();
            _countLabel.style.color = UiKit.Muted;
            _countLabel.style.fontSize = 11;
            _countLabel.style.marginRight = 8;
            titleBar.Add(_countLabel);

            if (onClose != null)
            {
                var close = new Button(onClose) { text = "Close" };
                close.style.height = 22;
                close.style.minWidth = 60;
                close.style.backgroundColor = UiKit.Panel;
                close.style.color = UiKit.Text;
                UiKit.SetBorder(close, UiKit.Border, 1);
                titleBar.Add(close);
            }
            Add(titleBar);

            _globalSearch = new TextField();
            _globalSearch.style.marginBottom = 4;
            _globalSearch.RegisterValueChangedCallback(_ => Refresh());
            Add(UiKit.Row("Search all columns", _globalSearch, "matches any field", 130, 260));

            Add(BuildHeaderRow());
            Add(BuildSearchRow());

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            _body = scroll.contentContainer;
            Add(scroll);

            Refresh();
        }

        private VisualElement BuildHeaderRow()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.backgroundColor = UiKit.Panel;
            header.style.marginTop = 2;
            for (var i = 0; i < _columns.Count; i++)
            {
                var index = i;
                var column = _columns[i];
                var button = new Button(() => SortBy(index)) { text = column.Title };
                button.style.width = column.Width;
                button.style.flexShrink = 0;
                button.style.height = 20;
                button.style.marginLeft = 0;
                button.style.marginRight = 1;
                button.style.marginTop = 0;
                button.style.marginBottom = 0;
                button.style.fontSize = 11;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.backgroundColor = UiKit.Panel;
                button.style.color = UiKit.Text;
                UiKit.SetBorder(button, UiKit.Border, 1);
                header.Add(button);
            }
            return header;
        }

        private VisualElement BuildSearchRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;
            foreach (var column in _columns)
            {
                var field = new TextField();
                field.style.width = column.Width;
                field.style.flexShrink = 0;
                field.style.marginLeft = 0;
                field.style.marginRight = 1;
                field.style.fontSize = 10;
                field.RegisterValueChangedCallback(_ => Refresh());
                _columnSearches.Add(field);
                row.Add(field);
            }
            return row;
        }

        private void SortBy(int column)
        {
            if (_sortColumn == column) _sortDescending = !_sortDescending;
            else { _sortColumn = column; _sortDescending = false; }
            Refresh();
        }

        private void Refresh()
        {
            _body.Clear();
            var matches = _rows.Where(Matches);
            if (_sortColumn >= 0)
            {
                var column = _columns[_sortColumn];
                matches = column.Numeric
                    ? (_sortDescending ? matches.OrderByDescending(r => AsNumber(r, _sortColumn)) : matches.OrderBy(r => AsNumber(r, _sortColumn)))
                    : (_sortDescending
                        ? matches.OrderByDescending(r => Cell(r, _sortColumn), StringComparer.OrdinalIgnoreCase)
                        : matches.OrderBy(r => Cell(r, _sortColumn), StringComparer.OrdinalIgnoreCase));
            }
            var list = matches.ToList();
            _countLabel.text = list.Count == _rows.Count ? $"{_rows.Count} rows" : $"{list.Count} of {_rows.Count} rows";
            var shown = 0;
            foreach (var row in list)
            {
                if (shown >= MaxRendered) break;
                _body.Add(BuildRow(row, shown++));
            }
            if (list.Count > shown)
            {
                var more = new Label($"  {shown} of {list.Count} shown - narrow the search to see the rest");
                more.style.color = UiKit.Muted;
                more.style.fontSize = 10;
                more.style.marginTop = 4;
                _body.Add(more);
            }
        }

        private static double AsNumber(string[] row, int index) =>
            double.TryParse(Cell(row, index), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.MinValue;   // written invariant, read invariant

        private static string Cell(string[] row, int index) =>
            index >= 0 && index < row.Length ? row[index] ?? "" : "";

        private bool Matches(string[] row)
        {
            var global = _globalSearch?.value;
            if (!string.IsNullOrWhiteSpace(global) &&
                !row.Any(c => c != null && c.IndexOf(global, StringComparison.OrdinalIgnoreCase) >= 0))
                return false;
            for (var i = 0; i < _columnSearches.Count; i++)
            {
                var query = _columnSearches[i].value;
                if (string.IsNullOrWhiteSpace(query)) continue;
                if (Cell(row, i).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        private VisualElement BuildRow(string[] row, int position)
        {
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.flexShrink = 0;
            line.style.height = 19;
            if (position % 2 == 1) line.style.backgroundColor = UiKit.Panel;
            for (var i = 0; i < _columns.Count; i++)
            {
                var cell = new Label(Cell(row, i));
                cell.style.width = _columns[i].Width;
                cell.style.flexShrink = 0;
                cell.style.fontSize = 11;
                cell.style.color = i == 0 ? UiKit.Text : UiKit.Muted;
                cell.style.overflow = Overflow.Hidden;
                cell.style.marginRight = 1;
                cell.style.paddingLeft = 3;
                line.Add(cell);
            }
            line.RegisterCallback<ClickEvent>(_ => _onPick?.Invoke(Cell(row, 0)));
            line.RegisterCallback<MouseEnterEvent>(_ => line.style.backgroundColor = new Color(0.25f, 0.32f, 0.42f));
            line.RegisterCallback<MouseLeaveEvent>(_ => line.style.backgroundColor = position % 2 == 1 ? UiKit.Panel : Color.clear);
            return line;
        }
    }
}
