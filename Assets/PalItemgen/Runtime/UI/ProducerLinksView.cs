using System;
using System.Collections.Generic;
using System.Linq;
using PalItemgen.Lookup;
using PalItemgen.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// The "Producers &amp; item links" tab: every production line on the left
    /// (this project's line stations first, then every vanilla bench), and on
    /// the right the items the selected line can make - the item links.
    ///
    /// The manager controls every line in the base. Ticking items limits what
    /// that line may make under the manager ("Assembly Line may only make
    /// spheres"); none ticked = everything its blueprint allows. Project
    /// stations keep their links in the station (StationSpec.AllowedItems),
    /// vanilla lines in ModProject.FacilityLinks; both go to pm_config.lua and
    /// the planner and the F7 menu honour them. Which of those the player has
    /// actually unlocked is decided in game from the player's technology data,
    /// the way the game's own crafting menu does it.
    /// </summary>
    public sealed class ProducerLinksView : VisualElement
    {
        private readonly GameData _data;
        private readonly Func<ModProject> _project;
        private readonly Action _changed;

        private VisualElement _producerList;
        private VisualElement _itemList;
        private Label _itemHeader;
        private TextField _filter;
        private Toggle _craftableOnly;

        private string _selectedKey;      // station id or vanilla map object id
        private StationSpec _selectedStation;
        private ProducerInfo _selectedBench;

        public ProducerLinksView(GameData data, Func<ModProject> project, Action changed)
        {
            _data = data;
            _project = project;
            _changed = changed;
            style.flexGrow = 1;
            Build();
        }

        public void Refresh()
        {
            // Stations may have been replaced (reset, remove, kind change): find the
            // selected one again by key so ticks never land on a stale object.
            if (_selectedKey != null)
            {
                var station = _project().Stations.FirstOrDefault(s => s.Kind == StationKind.Line && s.Id == _selectedKey);
                if (station != null)
                {
                    _selectedStation = station;
                    _selectedBench = _data.Producer(station.ReuseMapObjectId);
                }
                else
                {
                    _selectedStation = null;
                    _selectedBench = _data.Producers.FirstOrDefault(p => p.MapObjectId == _selectedKey);
                    if (_selectedBench == null) _selectedKey = null;
                }
            }
            RebuildProducers();
            RebuildItems();
        }

        private void Build()
        {
            Clear();
            var columns = UiKit.Columns(UiKit.Column(), UiKit.Column());
            columns.style.flexGrow = 1;
            Add(columns);
            var left = (VisualElement)columns[0];
            var right = (VisualElement)columns[1];
            left.style.flexGrow = 0.9f;
            right.style.flexGrow = 1.4f;

            var producers = UiKit.Section("Production lines", out var pbody,
                "Every line the manager can order at: this project's stations first, then every vanilla bench. Select one to see and limit what it may make.");
            producers.style.flexGrow = 1;
            left.Add(producers);
            _producerList = new ScrollView();
            _producerList.style.maxHeight = 720;
            pbody.Add(_producerList);

            var links = UiKit.Section("Item links", out var ibody,
                "What the selected line can make (item type A/B and rank from its blueprint, recipes from DT_ItemRecipeDataTable). " +
                "Tick items to limit the line to them under the manager; none ticked = everything it can make.");
            links.style.flexGrow = 1;
            right.Add(links);
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.marginBottom = 4;
            _filter = new TextField { value = "" };
            _filter.style.flexGrow = 1;
            _filter.RegisterValueChangedCallback(_ => RebuildItems());
            bar.Add(new Label("filter") { style = { color = UiKit.Muted, marginRight = 6 } });
            bar.Add(_filter);
            _craftableOnly = new Toggle { value = true, text = "craftable only" };
            _craftableOnly.style.marginLeft = 8;
            _craftableOnly.RegisterValueChangedCallback(_ => RebuildItems());
            bar.Add(_craftableOnly);
            bar.Add(UiKit.SmallButton("tick all shown", () => SetAllShown(true)));
            bar.Add(UiKit.SmallButton("clear ticks", () => SetAllShown(false)));
            ibody.Add(bar);
            _itemHeader = UiKit.Note("select a production line on the left");
            ibody.Add(_itemHeader);
            _itemList = new ScrollView();
            _itemList.style.maxHeight = 680;
            ibody.Add(_itemList);

            RebuildProducers();
        }

        /// <summary>The links list of the selected line: the station's own list, or the project's list for a vanilla bench.</summary>
        private List<string> Links(bool create)
        {
            if (_selectedStation != null) return _selectedStation.AllowedItems ??= new List<string>();
            if (_selectedBench?.MapObjectId == null) return null;
            var project = _project();
            if (project.FacilityLinks.TryGetValue(_selectedBench.MapObjectId, out var list) && list != null) return list;
            return create ? project.LinksFor(_selectedBench.MapObjectId) : new List<string>();
        }

        private int LinkCount(string mapObjectId) =>
            _project().FacilityLinks.TryGetValue(mapObjectId, out var list) && list != null ? list.Count : 0;

        private void RebuildProducers()
        {
            _producerList.Clear();
            var project = _project();
            var header1 = new Label("This project");
            header1.style.color = UiKit.Accent;
            header1.style.unityFontStyleAndWeight = FontStyle.Bold;
            _producerList.Add(header1);
            var any = false;
            foreach (var s in project.Stations.Where(s => s.Kind == StationKind.Line))
            {
                any = true;
                var bench = _data.Producer(s.ReuseMapObjectId);
                var count = bench != null ? _data.ItemsFor(bench).Count() : 0;
                var picked = s.AllowedItems?.Count ?? 0;
                var label = $"{s.Name}  [{s.Id}]  -> {bench?.Name ?? s.ReuseMapObjectId}: {count} items{(picked > 0 ? $", {picked} linked" : "")}";
                _producerList.Add(ProducerRow(label, s.Id, () => { _selectedStation = s; _selectedBench = bench; }));
            }
            if (!any) _producerList.Add(UiKit.Note("no production line stations in the project"));

            var header2 = new Label("Vanilla production lines");
            header2.style.color = UiKit.Accent;
            header2.style.unityFontStyleAndWeight = FontStyle.Bold;
            header2.style.marginTop = 8;
            _producerList.Add(header2);
            foreach (var p in _data.Producers.Where(p => p.MapObjectId != null).OrderBy(p => p.Name))
            {
                var bench = p;
                var count = _data.ItemsFor(bench).Count();
                var picked = LinkCount(p.MapObjectId);
                var label = $"{p.Name}  [{p.MapObjectId}]  rank {p.RankMax}: {count} items{(picked > 0 ? $", {picked} linked" : "")}{(p.Buildable ? "" : "  (not buildable)")}";
                _producerList.Add(ProducerRow(label, p.MapObjectId, () => { _selectedStation = null; _selectedBench = bench; }));
            }
        }

        private VisualElement ProducerRow(string label, string key, Action select)
        {
            var b = new Button(() =>
            {
                _selectedKey = key;
                select();
                RebuildProducers();
                RebuildItems();
            }) { text = label };
            b.style.height = 22;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.marginBottom = 1;
            b.style.backgroundColor = _selectedKey == key ? UiKit.Accent : UiKit.Panel;
            b.style.color = _selectedKey == key ? Color.white : UiKit.Text;
            UiKit.SetBorder(b, UiKit.Border, 1);
            return b;
        }

        private IEnumerable<ItemInfo> ShownItems()
        {
            if (_selectedBench == null) return Enumerable.Empty<ItemInfo>();
            var q = (_filter.value ?? "").Trim().ToLowerInvariant();
            var all = _data.ItemsFor(_selectedBench);
            if (!_craftableOnly.value)
            {
                var a = new HashSet<string>(_selectedBench.TypesA);
                var b = new HashSet<string>(_selectedBench.TypesB);
                all = _data.Items.Where(i => a.Contains(i.TypeA) && b.Contains(i.TypeB) && i.Rank <= (_selectedBench.RankMax ?? 0));
            }
            if (q.Length > 0)
                all = all.Where(i => (i.Name ?? "").ToLowerInvariant().Contains(q) || i.Id.ToLowerInvariant().Contains(q) || i.SortId.ToString() == q);
            return all.OrderBy(i => string.IsNullOrEmpty(i.Name) ? "~" + i.Id : i.Name).ThenBy(i => i.Id);
        }

        private void RebuildItems()
        {
            _itemList.Clear();
            if (_selectedBench == null)
            {
                _itemHeader.text = _selectedKey == null ? "select a production line on the left" : "the selected station reuses an unknown blueprint";
                return;
            }
            var shown = ShownItems().ToList();
            var links = Links(false) ?? new List<string>();
            var who = _selectedStation != null ? _selectedStation.Name : _selectedBench.Name;
            _itemHeader.text = links.Count == 0
                ? $"{who} may make everything its bench can (no ticks); {shown.Count} shown. Tick to limit it."
                : $"{who} may only make the {links.Count} linked items; the manager orders nothing else there and the F7 menu offers nothing else for it. {shown.Count} shown.";
            foreach (var item in shown.Take(600))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 1;
                var toggle = new Toggle { value = links.Contains(item.Id) };
                toggle.style.marginRight = 4;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    var list = Links(true);
                    if (evt.newValue) { if (!list.Contains(item.Id)) list.Add(item.Id); }
                    else list.Remove(item.Id);
                    TrimEmpty();
                    _changed?.Invoke();
                    RebuildProducers();
                    RebuildItems();
                });
                row.Add(toggle);
                var recipe = _data.RecipeFor(item.Id);
                var mats = recipe == null ? "no recipe" : string.Join(" + ", recipe.Materials.Select(m => $"{m.Count} {_data.Item(m.Id)?.Name ?? m.Id}"));
                var tech = recipe?.Tech != null ? $"  tech: {recipe.Tech}" : "  tech: none";
                var label = new Label($"{(string.IsNullOrEmpty(item.Name) ? item.Id : item.Name)}  [{item.Id}]  #{item.SortId}  rank {item.Rank}  {item.TypeB}");
                label.style.color = item.Craftable ? UiKit.Text : UiKit.Muted;
                label.style.fontSize = 12;
                label.style.flexGrow = 1;
                row.Add(label);
                var detail = new Label(mats + tech);
                detail.style.color = UiKit.Muted;
                detail.style.fontSize = 10;
                detail.style.flexGrow = 1.2f;
                detail.style.whiteSpace = WhiteSpace.Normal;
                row.Add(detail);
                _itemList.Add(row);
            }
            if (shown.Count > 600) _itemList.Add(UiKit.Note($"... {shown.Count - 600} more; narrow the filter", UiKit.Warning));
        }

        /// <summary>An emptied vanilla-line list is dropped so the project file and config stay clean.</summary>
        private void TrimEmpty()
        {
            if (_selectedStation != null || _selectedBench?.MapObjectId == null) return;
            var project = _project();
            if (project.FacilityLinks.TryGetValue(_selectedBench.MapObjectId, out var list) && (list == null || list.Count == 0))
                project.FacilityLinks.Remove(_selectedBench.MapObjectId);
        }

        private void SetAllShown(bool on)
        {
            if (_selectedBench == null) return;
            var list = Links(true);
            foreach (var item in ShownItems())
            {
                if (on) { if (!list.Contains(item.Id)) list.Add(item.Id); }
                else list.Remove(item.Id);
            }
            TrimEmpty();
            _changed?.Invoke();
            RebuildProducers();
            RebuildItems();
        }
    }
}
