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
    /// The third page of the Management Station: what the item does as a
    /// production-chain station. Every field here lands in pm_config.lua
    /// (the Chain block, the Automation scalars, Facilities and Presets).
    /// Left: the station, interaction, producers tracked, item links per
    /// bench type. Right: maintain targets, automation, menu keys. Labels and
    /// values with a one-line hint at most; no prose.
    /// </summary>
    public sealed class ChainView : VisualElement
    {
        private const float LabelWidth = 150f;

        private readonly GameData _data;
        private readonly Func<ModProject> _project;
        private readonly Action _changed;

        /// <summary>Host-supplied table browser.</summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> ShowBrowser;

        private StationSpec _spec;
        private VisualElement _targetList;

        public ChainView(GameData data, Func<ModProject> project, Action changed)
        {
            _data = data;
            _project = project;
            _changed = changed;
            style.flexGrow = 1;
            Refresh();
        }

        private ModProject Project => _project();
        private ChainSettings Chain
        {
            get
            {
                var p = _project();
                if (p.Chain == null) p.Chain = new ChainSettings();
                return p.Chain;
            }
        }
        private AutomationSettings Auto => _project().Automation;

        public void Refresh()
        {
            Clear();
            _spec = _project().Stations.FirstOrDefault(s => s.IsManagerRole);
            if (_spec == null)
            {
                var missing = UiKit.Section("Chain station", out var mb);
                mb.Add(UiKit.Note("No item acts as the production manager: add the station on the Exact copy page, or switch 'Production manager' on for a Generator copy.", UiKit.Warning));
                Add(missing);
                return;
            }
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            Add(scroll);
            var left = UiKit.Column();
            var right = UiKit.Column();
            scroll.Add(UiKit.Columns(left, right));
            left.Add(BuildStation());
            left.Add(BuildInteraction());
            left.Add(BuildProducers());
            left.Add(BuildLinks());
            right.Add(BuildTargets());
            right.Add(BuildAutomation());
            right.Add(BuildMenuKeys());
        }

        // ------------------------------------------------------------- 2.4.1 the station

        private VisualElement BuildStation()
        {
            // every item pm_config.lua lists as the production manager: the Exact copy
            // item, plus any Generator copy with 'Production manager' switched on
            var managers = _project().Stations.Where(s => s.IsManagerRole).ToList();
            var section = UiKit.Section("Chain station", out var body, managers.Count == 1 ? "the item pm_config.lua lists as the production manager" : $"{managers.Count} items act as the production manager");
            foreach (var spec in managers)
            {
                var s = spec;
                var host = _data.Building(s.BlueprintSource(_data));
                var look = _data.Building(s.ReuseMapObjectId);
                var mirror = _data.Building(s.MirrorId);
                var cls = host?.Model ?? "";
                var isStand = cls == "PalMapObjectBaseCampPassiveWorkHardModel";
                body.Add(KV("Item", $"{s.Name}  [{s.Id}]" + (s.Kind == StationKind.Copy ? "   (Generator copy acting as the manager)" : "")));
                body.Add(KV("Reuses", host != null ? $"{host.Name ?? host.MapObjectId} ({host.Bp})" : "unknown blueprint", host == null ? UiKit.Warning : (Color?)null, LabelWidth, "the blueprint written into the row: it brings the functions"));
                if (look != null && host != null && look.MapObjectId != host.MapObjectId)
                    body.Add(KV("Graphic", $"{look.GraphicLabel} ({look.Name ?? look.MapObjectId}), applied at run time", UiKit.Accent, LabelWidth, "EXPERIMENTAL: mesh and boxes swapped per placed item"));
                body.Add(KV("Model", cls.Length == 0 ? "?" : cls, isStand ? (Color?)null : UiKit.Warning, LabelWidth,
                    isStand ? "the stand's screens (F / V / C) and per-base reach" : "not a Monitoring Stand: F cannot open the Assignment Board on it"));
                var rows = s.AssignmentRows?.Count ?? (mirror?.RawAssignments?.Count ?? 0);
                body.Add(rows == 0
                    ? KV("Pal work rows", "none", null, LabelWidth, "no \"[Hold] Work\" prompt")
                    : KV("Pal work rows", $"{rows} rows", UiKit.Warning, LabelWidth, "they add a \"[Hold] Work\" prompt"));
                body.Add(ToggleRow("Enabled", s.Enabled, v => s.Enabled = v, "off = nothing is generated for it"));
            }
            return section;
        }

        // ------------------------------------------------------------- 2.4.2 interaction

        private static readonly string[] TabIds = { "chain", "monitor", "prod", "board", "targets" };
        private static readonly List<string> TabLabels = new List<string> { "Chain screen", "Monitoring tab", "Production tab", "Assignment Board (producers only)", "Targets list (keep items stocked)" };
        private static readonly string[] CIds = { "vanilla", "targets", "chain", "monitor", "prod" };
        private static readonly List<string> CLabels = new List<string> { "Vanilla Assignment Board (untouched)", "Targets list (add an item + amount to keep)", "Chain screen", "Monitoring tab", "Production tab" };

        private VisualElement BuildInteraction()
        {
            var a = Auto;
            var c = Chain;
            var section = UiKit.Section("Interaction", out var body);
            var fChoices = new List<string>(TabLabels) { "Stand's own screen" };
            var fIndex = !a.OpenMenuOnInteract ? TabIds.Length : Math.Max(0, Array.IndexOf(TabIds, c.InteractTab ?? ""));
            body.Add(Dropdown("F opens", fChoices, fIndex, i =>
            {
                a.OpenMenuOnInteract = i != TabIds.Length;
                if (i >= 0 && i < TabIds.Length) c.InteractTab = TabIds[i];
            }, "the stand's work-mode screen is replaced; the board = the game's own C screen with producers only, grouped by family"));
            body.Add(ToggleRow("Close stand screen", a.CloseVanillaMenuOnInteract, v => a.CloseVanillaMenuOnInteract = v, "RemoveHUD on the work-mode screen when ours opens"));
            body.Add(IntRow("Close delay (ms)", a.InteractCloseDelayMs, v => a.InteractCloseDelayMs = Math.Max(0, v), "the stand's screen is closed this long after F (0 = next frame)"));
            body.Add(IntRow("Board delay (ms)", a.BoardOpenDelayMs, v => a.BoardOpenDelayMs = Math.Max(0, v), "the board opens this long after the stand's screen closed"));
            body.Add(Dropdown("C opens", CLabels, Math.Max(0, Array.IndexOf(CIds, c.CTab ?? "")), i =>
            {
                c.CTab = CIds[Math.Max(0, i)];
                c.KeepAssignBoard = c.CTab == "vanilla";
            }, "targets = keep X in the base, start below = the restock % unless typed; manual Pal assignment stays on F"));
            body.Add(ToggleRow("Cancel from list", c.AllowCancelFromList, v => c.AllowCancelFromList = v, "stop control on a producing row"));
            body.Add(ToggleRow("Make now", c.ShowMakeNow, v => c.ShowMakeNow = v, "select a row, then order on the Production tab"));
            var slot = ToggleRow("Product slot opens bench", c.ProductSlotOpensBench, v => c.ProductSlotOpensBench = v, "a 'make' control on every producer row opens that bench's own Production Menu (untested in game)");
            body.Add(slot);
            body.Add(IntRow("Slot interaction", c.ProductSlotIndicator, v => c.ProductSlotIndicator = Math.Max(0, v), "which bench screen 'make' fires: 8 = Production Menu, 7 = craft menu, 23 = recipe select"));
            body.Add(ToggleRow("Board: family headings", c.BoardHeaders, v => c.BoardHeaders = v, "producers-only board: a heading per production family"));
            body.Add(ToggleRow("Board: untracked producers", c.BoardShowUntracked, v => c.BoardShowUntracked = v, "list benches switched off above too; off = hidden like non-producers"));
            body.Add(ToggleRow("Board: HUD fallback", c.BoardHudFallback, v => c.BoardHudFallback = v, "when the stand's own C action cannot be fired, open the board through the HUD service (untested)"));
            body.Add(ToggleRow("Rename prompts", c.PromptText, v => c.PromptText = v, "the F and C prompt texts on our item; off = the stand's own texts"));
            body.Add(TextRow("F prompt", c.PromptF, v => c.PromptF = v, "shown on our item instead of 'Set Work Mode'", 220));
            body.Add(TextRow("C prompt", c.PromptC, v => c.PromptC = v, "shown on our item instead of 'Fixed assignment management'", 220));
            body.Add(IntRow("Prompt range (cm)", c.PromptRangeCm, v => c.PromptRangeCm = Math.Max(50, v), "our station within this distance of the player owns the prompt"));
            return section;
        }

        // ------------------------------------------------------------- 2.4.3 producers tracked

        private VisualElement BuildProducers()
        {
            var c = Chain;
            var all = ChainCatalog.Producers(_data).ToList();
            var section = UiKit.Section("Producers tracked", out var body, $"{all.Count} true producers in {ChainCatalog.Families.Length} families, strongest first");
            var tools = new VisualElement();
            tools.style.flexDirection = FlexDirection.Row;
            tools.style.alignItems = Align.Center;
            tools.style.marginBottom = 4;
            tools.Add(UiKit.SmallButton("all on", () =>
            {
                c.DisabledFamilies.Clear();
                c.ExcludedBenches.Clear();
                _changed?.Invoke();
                Refresh();
            }));
            tools.Add(UiKit.SmallButton("all off", () =>
            {
                c.DisabledFamilies = ChainCatalog.Families.Select(f => f.Id).ToList();
                c.ExcludedBenches = all.Select(p => p.MapObjectId).OrderBy(s => s, StringComparer.Ordinal).ToList();
                _changed?.Invoke();
                Refresh();
            }));
            tools.Add(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.ChainBenches(_data);
                ShowBrowser?.Invoke("Production benches - pick one to include / exclude", cols, rows, id =>
                {
                    SetExcluded(id, !c.ExcludedBenches.Contains(id));
                    _changed?.Invoke();
                    Refresh();
                });
            }));
            body.Add(tools);

            foreach (var fam in ChainCatalog.Families)
            {
                var famId = fam.Id;
                var benches = all.Where(p => ChainCatalog.FamilyOf(p) == famId).ToList();
                var fold = new Foldout { text = $"{benches.Count} benches", value = false };
                fold.style.marginLeft = 8;
                fold.style.marginBottom = 2;
                var head = new VisualElement();
                head.style.flexDirection = FlexDirection.Row;
                head.style.alignItems = Align.Center;
                head.style.minHeight = 20;
                var on = new Toggle { value = c.FamilyEnabled(famId) };
                on.style.width = 24;
                on.style.flexShrink = 0;
                on.RegisterValueChangedCallback(evt =>
                {
                    evt.StopPropagation();
                    c.SetFamily(famId, evt.newValue);
                    fold.SetEnabled(evt.newValue);
                    _changed?.Invoke();
                });
                head.Add(on);
                var name = new Label(fam.Label);
                name.style.width = LabelWidth + 40;
                name.style.flexShrink = 0;
                name.style.fontSize = 12;
                name.style.color = UiKit.Text;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                head.Add(name);
                var value = new Label(famId);
                value.style.fontSize = 10;
                value.style.color = UiKit.Muted;
                head.Add(value);
                body.Add(head);

                var built = false;
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != fold) return;
                    if (!evt.newValue || built) return;
                    built = true;
                    foreach (var bench in benches) fold.Add(BenchRow(bench));
                });
                fold.SetEnabled(c.FamilyEnabled(famId));
                body.Add(fold);
            }
            return section;
        }

        private VisualElement BenchRow(ProducerInfo bench)
        {
            var c = Chain;
            var id = bench.MapObjectId;
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;
            row.style.marginBottom = 1;
            var on = new Toggle { value = !c.ExcludedBenches.Contains(id) };
            on.style.width = 24;
            on.style.flexShrink = 0;
            on.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                SetExcluded(id, !evt.newValue);
                _changed?.Invoke();
            });
            row.Add(on);
            var name = new Label($"{bench.Name}  [{id}]");
            name.style.fontSize = 12;
            name.style.color = UiKit.Text;
            name.style.width = 260;
            name.style.flexShrink = 0;
            name.style.whiteSpace = WhiteSpace.Normal;
            row.Add(name);
            var stats = new Label(Stats(bench));
            stats.style.fontSize = 10;
            stats.style.color = UiKit.Muted;
            stats.style.whiteSpace = WhiteSpace.Normal;
            stats.style.flexShrink = 1;
            row.Add(stats);
            return row;
        }

        private string Stats(ProducerInfo bench)
        {
            var suit = ChainCatalog.Suitability(bench);
            var a = bench.Assignments?.FirstOrDefault(x => x.WorkSuitability != "Anyone") ?? bench.Assignments?.FirstOrDefault();
            var who = suit == "None" ? "any Pal" : $"{suit} {a?.WorkSuitabilityRank ?? 1}";
            var speed = bench.WorkSpeed.HasValue ? N(bench.WorkSpeed.Value) : "-";
            var tier = bench.Tech != null ? N(ChainCatalog.Tier(bench)) : "-";
            return $"rank {ChainCatalog.RankMax(bench)}  ·  speed {speed}  ·  tier {tier}  ·  spots {ChainCatalog.WorkSpots(_data, bench.MapObjectId)}  ·  {who}";
        }

        /// <summary>Adds or removes a bench id from the excluded list, kept ordinal-sorted and distinct.</summary>
        private void SetExcluded(string id, bool excluded)
        {
            var list = Chain.ExcludedBenches;
            if (excluded)
            {
                if (!list.Contains(id)) list.Add(id);
                list.Sort(StringComparer.Ordinal);
            }
            else list.RemoveAll(x => x == id);
        }

        // ------------------------------------------------------------- 2.4.4 item links per bench type

        private VisualElement BuildLinks()
        {
            var section = UiKit.Section("Item links per bench type", out var body, "empty = anything the bench can make");
            var all = ChainCatalog.Producers(_data).ToList();
            foreach (var fam in ChainCatalog.Families)
            {
                var famId = fam.Id;
                var benches = all.Where(p => ChainCatalog.FamilyOf(p) == famId).ToList();
                var linked = benches.Count(b => Project.FacilityLinks.TryGetValue(b.MapObjectId, out var l) && l != null && l.Count > 0);
                var fold = new Foldout { text = linked > 0 ? $"{fam.Label}  ({linked} with links)" : fam.Label, value = false };
                fold.style.marginBottom = 2;
                var built = false;
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != fold) return;
                    if (!evt.newValue || built) return;
                    built = true;
                    foreach (var bench in benches)
                    {
                        var holder = new VisualElement();
                        fold.Add(holder);
                        FillLinkCard(holder, bench);
                    }
                });
                body.Add(fold);
            }
            return section;
        }

        /// <summary>One bench type's links: count, the picker, one chip per linked item. Rebuilt in place on every change.</summary>
        private void FillLinkCard(VisualElement holder, ProducerInfo bench)
        {
            holder.Clear();
            var id = bench.MapObjectId;
            var project = Project;
            var makeable = _data.ItemsFor(bench).ToList();
            project.FacilityLinks.TryGetValue(id, out var links);
            var count = links?.Count ?? 0;

            var card = new VisualElement();
            card.style.marginBottom = 4;
            card.style.paddingLeft = 4;
            card.style.paddingRight = 4;
            card.style.paddingBottom = 4;
            UiKit.SetBorder(card, UiKit.Border, 1);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            head.style.minHeight = 20;
            var name = new Label(bench.Name ?? id);
            name.style.width = LabelWidth;
            name.style.flexShrink = 0;
            name.style.fontSize = 12;
            name.style.color = UiKit.Text;
            head.Add(name);
            var value = new Label(count > 0 ? $"{count} linked" : "any");
            value.style.fontSize = 12;
            value.style.color = count > 0 ? UiKit.Accent : UiKit.Text;
            value.style.marginRight = 8;
            head.Add(value);
            var note = new Label($"{makeable.Count} makeable");
            note.style.fontSize = 10;
            note.style.color = UiKit.Muted;
            head.Add(note);
            card.Add(head);

            var pick = new UiKit.SearchPicker(() => makeable.Select(it => (it.Id, it.Display)), "add item", 150);
            void AddLink(string itemId)
            {
                if (string.IsNullOrEmpty(itemId) || makeable.All(it => it.Id != itemId)) return;
                var list = project.LinksFor(id);
                if (!list.Contains(itemId)) list.Add(itemId);
                _changed?.Invoke();
                FillLinkCard(holder, bench);
            }
            pick.OnSelected += AddLink;
            pick.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Items(_data, true);
                var can = new HashSet<string>(makeable.Select(it => it.Id));
                rows = rows.Where(r => can.Contains(r[0])).ToList();
                ShowBrowser?.Invoke($"Items {bench.Name} can make - link one", cols, rows, AddLink);
            }));
            card.Add(UiKit.Row("Link item", pick, null, 90, 0));

            if (count > 0)
            {
                var chips = new VisualElement();
                chips.style.flexDirection = FlexDirection.Row;
                chips.style.flexWrap = Wrap.Wrap;
                foreach (var itemId in links.ToList())
                {
                    var chipId = itemId;
                    var chip = UiKit.SmallButton($"{_data.Item(chipId)?.Name ?? chipId} ×", () =>
                    {
                        var list = project.LinksFor(id);
                        list.RemoveAll(x => x == chipId);
                        if (list.Count == 0) project.FacilityLinks.Remove(id);
                        _changed?.Invoke();
                        FillLinkCard(holder, bench);
                    });
                    chip.style.marginBottom = 2;
                    chips.Add(chip);
                }
                card.Add(chips);
            }
            holder.Add(card);
        }

        // ------------------------------------------------------------- 2.4.5 maintain targets

        private static readonly string[] OrderIds = { "lottery", "weight", "priority" };
        private static readonly List<string> OrderLabels = new List<string> { "Weighted lottery", "Weight, highest first", "Priority number" };

        private VisualElement BuildTargets()
        {
            var a = Auto;
            var c = Chain;
            var section = UiKit.Section("Maintain targets", out var body, "keep X · start below Y · weight W");
            body.Add(Dropdown("Serve order", OrderLabels, Math.Max(0, Array.IndexOf(OrderIds, c.TargetOrder ?? "")), i => c.TargetOrder = OrderIds[Math.Max(0, i)], null));
            body.Add(ToggleRow("One producer per target", c.OneProducerPerTarget, v => c.OneProducerPerTarget = v, "whole keep - stock order at the best free bench"));
            body.Add(IntRow("Restock below (%)", a.DefaultTriggerPercent, v => a.DefaultTriggerPercent = v, "used when start-below is 0"));
            var add = UiKit.SecondaryButton("+ Target", () =>
            {
                Project.Presets.Add(new PresetTarget { ItemId = "IronIngot", Keep = 500 });
                _changed?.Invoke();
                RebuildTargets();
            });
            add.style.alignSelf = Align.FlexStart;
            add.style.marginBottom = 6;
            body.Add(add);
            _targetList = new VisualElement();
            body.Add(_targetList);
            RebuildTargets();
            return section;
        }

        private void RebuildTargets()
        {
            if (_targetList == null) return;
            _targetList.Clear();
            foreach (var t in Project.Presets) _targetList.Add(TargetCard(t));
        }

        private VisualElement TargetCard(PresetTarget p)
        {
            var c = Chain;
            var card = new VisualElement();
            card.style.marginBottom = 4;
            card.style.paddingLeft = 4;
            card.style.paddingRight = 4;
            card.style.paddingBottom = 4;
            UiKit.SetBorder(card, UiKit.Border, 1);

            var pick = new UiKit.SearchPicker(() => _data.CraftableItems().Select(it => (it.Id, it.Display)), null, 150) { Value = p.ItemId };
            var chosen = _data.Item(p.ItemId);
            if (chosen != null) pick.SetDisplay(chosen.Display);
            void PickItem(string v)
            {
                p.ItemId = v;
                _changed?.Invoke();
                RebuildTargets();   // producer choices and the best producer follow the item
            }
            pick.OnSelected += PickItem;
            pick.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Items(_data, true);
                ShowBrowser?.Invoke("Craftable items - maintain target", cols, rows, PickItem);
            }));
            card.Add(UiKit.Row("Item", pick, null, 70, 0));

            var nums = UiKit.Columns(UiKit.Column(), UiKit.Column());
            ((VisualElement)nums[0]).Add(IntRow("Keep", p.Keep, v => p.Keep = v, null, 70, 60));
            ((VisualElement)nums[0]).Add(IntRow("Start below", p.Trigger, v => p.Trigger = v, "0 = default percent", 70, 60));
            ((VisualElement)nums[1]).Add(IntRow("Weight", p.Weight, v => p.Weight = v, "lottery share; 1 = normal", 70, 60));
            ((VisualElement)nums[1]).Add(IntRow("Priority", p.Priority, v => p.Priority = v, "serve order = priority only; 1 = first", 70, 60));
            card.Add(nums);

            // Producer pin: "any" or one bench type that can make the item. Stored in Only,
            // apart from Line, so a pinned target stays in its serve group (lottery / weight).
            // Older projects kept the pin in Line; that value is shown and moved over on change.
            var makers = ChainCatalog.MakersOf(_data, p.ItemId).ToList();
            var lineNames = new HashSet<string>(Project.Stations.Where(s => s.Kind == StationKind.Line).Select(s => s.Line ?? ""));
            var legacyPin = !string.IsNullOrEmpty(p.Line) && p.Line != "default" && !lineNames.Contains(p.Line) && _data.Producer(p.Line) != null ? p.Line : null;
            var ids = new List<string> { "" };
            var labels = new List<string> { "any" };
            foreach (var m in makers)
            {
                ids.Add(m.MapObjectId);
                labels.Add($"{m.Name}  [{m.MapObjectId}]");
            }
            var current = !string.IsNullOrEmpty(p.Only) ? p.Only : (legacyPin ?? "");
            if (current != "" && !ids.Contains(current))
            {
                ids.Add(current);
                labels.Add($"{current}  (not a maker)");
            }
            card.Add(Dropdown("Producer", labels, Math.Max(0, ids.IndexOf(current)), i =>
            {
                p.Only = ids[Math.Max(0, i)];
                if (legacyPin != null && p.Line == legacyPin) p.Line = "";   // the pin now lives in Only
            }, "any = the strongest free bench that can make it", 70));

            var best = makers.FirstOrDefault(m => !c.ExcludedBenches.Contains(m.MapObjectId) && c.FamilyEnabled(ChainCatalog.FamilyOf(m)));
            if (best != null) card.Add(KV("Best producer", $"{best.Name}  [{best.MapObjectId}]", null, 70));
            else if (makers.Count > 0) card.Add(KV("Best producer", "all makers excluded", UiKit.Warning, 70));
            else card.Add(KV("Best producer", "none can make it", UiKit.Warning, 70));

            var rm = UiKit.SmallButton("remove", () =>
            {
                Project.Presets.Remove(p);
                _changed?.Invoke();
                RebuildTargets();
            });
            rm.style.alignSelf = Align.FlexStart;
            card.Add(rm);
            return card;
        }

        // ------------------------------------------------------------- 2.4.6 automation

        private VisualElement BuildAutomation()
        {
            var a = Auto;
            var c = Chain;
            var section = UiKit.Section("Automation", out var body);
            body.Add(IntRow("Scan every (s)", a.ScanIntervalSeconds, v => a.ScanIntervalSeconds = v, null));
            body.Add(IntRow("Frame budget (ms)", a.TickBudgetMs, v => a.TickBudgetMs = Math.Max(1, v), "a scan runs in stages; each game-thread step runs stages until this is spent"));
            body.Add(IntRow("Step every (ms)", a.TickStepMs, v => a.TickStepMs = Math.Max(5, v), "gap between the steps of one scan"));
            body.Add(IntRow("View refresh (scans)", a.ViewRefreshTicks, v => a.ViewRefreshTicks = Math.Max(1, v), "menu data rebuilt every N scans while the menu is closed"));
            body.Add(IntRow("Full refresh (scans)", a.FullRefreshTicks, v => a.FullRefreshTicks = Math.Max(1, v), "fixed data (ids, recipes) re-read from the game every N scans"));
            body.Add(ToggleRow("Require this station", a.RequireManagerStation, v => a.RequireManagerStation = v, "nothing runs in a base without it"));
            body.Add(IntRow("Default keep", a.DefaultKeep, v => a.DefaultKeep = v, null));
            body.Add(IntRow("Idle grace (s)", a.IdleGraceSeconds, v => a.IdleGraceSeconds = v, "a bench must be idle this long before it is used"));
            body.Add(Dropdown("Producer order", new List<string> { "Craft rank, speed, tier", "Live speed" }, c.RankBy == "speed" ? 1 : 0, i => c.RankBy = i == 1 ? "speed" : "rank", "strongest first"));
            body.Add(IntRow("Max order batch", a.MaxOrderBatch, v => a.MaxOrderBatch = v, "only when one producer per target is off"));
            body.Add(ToggleRow("Follow priority order", a.FollowPriorityOrder, v => a.FollowPriorityOrder = v, "ignored when one producer per target is on"));
            body.Add(ToggleRow("Material check", a.MaterialCheck, v => a.MaterialCheck = v, "no order when materials are short"));
            body.Add(ToggleRow("Auto-chain materials", a.AutoChainMaterials, v => a.AutoChainMaterials = v, "order missing craftable materials too"));
            body.Add(ToggleRow("Best Pal to producer", a.AssignBestWorker, v => a.AssignBestWorker = v, "fixed-assign the best-suited base Pal after each order"));
            body.Add(ToggleRow("Release when done", a.ReleaseWorkerWhenDone, v => a.ReleaseWorkerWhenDone = v, null));
            body.Add(IntRow("Pals per order", a.WorkersPerOrder, v => a.WorkersPerOrder = v, null));
            body.Add(IntRow("Hold after stop (s)", a.InterruptHoldSeconds, v => a.InterruptHoldSeconds = v, "a target stopped by hand waits this long; 0 = until resumed"));
            body.Add(IntRow("Stop = crafts left", a.InterruptMinRemaining, v => a.InterruptMinRemaining = v, "an order gone with this many crafts left counts as stopped by hand"));
            body.Add(IntRow("Announce level", a.AnnounceLevel, v => a.AnnounceLevel = v, "0 silent, 1 problems, 2 every order"));
            body.Add(IntRow("Repeat issues (s)", a.IssueRepeatSeconds, v => a.IssueRepeatSeconds = v, null));
            body.Add(ToggleRow("Diagnostics dump", a.Diagnostics, v => a.Diagnostics = v, null));
            return section;
        }

        // ------------------------------------------------------------- 2.4.7 menu keys

        private VisualElement BuildMenuKeys()
        {
            var a = Auto;
            var c = Chain;
            var section = UiKit.Section("Menu keys", out var body);
            body.Add(ToggleRow("In-game menu", a.ClientMenu, v => a.ClientMenu = v, "host only"));
            body.Add(TextRow("Menu key", a.MenuKey, v => a.MenuKey = v, "UE4SS key name; fallback key still opens it", 70));
            body.Add(TextRow("Fallback key", a.MenuFallbackKey, v => a.MenuFallbackKey = v, null, 70));
            body.Add(TextRow("Status hotkey", a.StatusKey, v => a.StatusKey = v, "empty = off", 70));
            body.Add(Dropdown("Opens on", TabLabels, Math.Max(0, Array.IndexOf(TabIds, c.DefaultTab ?? "")), i => c.DefaultTab = TabIds[Math.Max(0, i)], "the tab the menu key lands on; the board needs the station in your base"));
            body.Add(IntRow("Menu width", a.MenuWidth, v => a.MenuWidth = v, null));
            body.Add(IntRow("Menu height", a.MenuHeight, v => a.MenuHeight = v, null));
            body.Add(IntRow("Search rows", a.MenuSearchRows, v => a.MenuSearchRows = v, null));
            body.Add(IntRow("Poll (ms)", a.MenuPollMs, v => a.MenuPollMs = Math.Max(50, v), "min 50"));
            return section;
        }

        // ------------------------------------------------------------- row helpers

        private static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static string N(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private VisualElement TextRow(string label, string value, Action<string> set, string hint, float width = 0, float labelWidth = LabelWidth)
        {
            var f = new TextField { value = value ?? "" };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, labelWidth, width);
        }

        private VisualElement IntRow(string label, int value, Action<int> set, string hint, float labelWidth = LabelWidth, float width = 70)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, labelWidth, width);
        }

        private VisualElement ToggleRow(string label, bool value, Action<bool> set, string hint, float labelWidth = LabelWidth)
        {
            var f = new Toggle { value = value };
            f.RegisterValueChangedCallback(evt => { evt.StopPropagation(); set(evt.newValue); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, labelWidth, 30);
        }

        private VisualElement Dropdown(string label, List<string> choices, int index, Action<int> set, string hint, float labelWidth = LabelWidth)
        {
            var list = choices.Count > 0 ? choices : new List<string> { "(none)" };
            var f = new DropdownField(list, Math.Min(Math.Max(0, index), list.Count - 1));
            f.RegisterValueChangedCallback(evt => { set(list.IndexOf(evt.newValue)); _changed?.Invoke(); });
            return UiKit.Row(label, f, hint, labelWidth, 0);
        }

        private static VisualElement KV(string key, string value, Color? color = null, float labelWidth = LabelWidth, string hint = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;
            row.style.minHeight = 20;
            row.style.alignItems = Align.Center;
            var k = new Label(key);
            k.style.width = labelWidth;
            k.style.flexShrink = 0;
            k.style.fontSize = 12;
            k.style.color = UiKit.Muted;
            row.Add(k);
            var v = new Label(value);
            v.style.fontSize = 12;
            v.style.color = color ?? UiKit.Text;
            v.style.whiteSpace = WhiteSpace.Normal;
            v.style.flexShrink = 1;
            row.Add(v);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = new Label(hint);
                h.style.fontSize = 10;
                h.style.color = UiKit.Muted;
                h.style.marginLeft = 8;
                h.style.flexShrink = 1;
                h.style.whiteSpace = WhiteSpace.Normal;
                row.Add(h);
            }
            return row;
        }
    }
}
