using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PalItemgen.Export;
using PalItemgen.Lookup;
using PalItemgen.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// The one-screen creator. Three columns: package settings and output on
    /// the left, the stations the package adds in the middle (wider), automation
    /// defaults and preset targets on the right, validation and the Generate
    /// button along the bottom. Plain VisualElement tree so the editor window
    /// and the runtime app host the same thing.
    /// </summary>
    public sealed class PalItemgenView : VisualElement
    {
        private const float LabelWidth = 150f;

        private readonly GameData _data;
        private readonly Action<string> _log;
        private ModProject _project;

        private VisualElement _stationList;
        private VisualElement _presetList;
        private Label _status;
        private VisualElement _problems;
        private Label _layoutHint;

        /// <summary>
        /// Management Station build: the screen is the Exact copy tab alone
        /// (original stand left, this item right) plus the action bar. The other
        /// tabs are still built so every shared field keeps its defaults, but
        /// they are never shown.
        /// </summary>
        public bool ExactCopyOnly { get; }

        public PalItemgenView(GameData data, Action<string> log = null, bool exactCopyOnly = false)
        {
            ExactCopyOnly = exactCopyOnly;
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _log = log ?? UnityEngine.Debug.Log;
            // OpenDump is assigned by the host after construction; the item tab reads it lazily.
            _project = LoadProjectOrDefault();
            // No dialog on open (2026-09-16: "we haven't touched the tool or a file").
            // An unnamed project is only asked for a name when an action needs one: Save,
            // Generate and Generate + install all go through SaveProjectFile, which prompts then.
            if (string.IsNullOrEmpty(_project.OutputFolder)) _project.OutputFolder = DataPaths.DefaultOutputFolder;
            foreach (var s in _project.Stations.Where(s => s.Kind == StationKind.Manager && string.IsNullOrEmpty(s.MirrorMapObjectId)))
                s.MirrorMapObjectId = "BaseCampWorkHard";

            style.flexGrow = 1;
            style.backgroundColor = UiKit.Background;

            if (!_data.IsLoaded)
            {
                Add(BuildDataMissingNotice());
                return;
            }
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            scroll.contentContainer.style.paddingLeft = 12;
            scroll.contentContainer.style.paddingRight = 12;
            scroll.contentContainer.style.paddingTop = 10;
            scroll.contentContainer.style.paddingBottom = 10;
            Add(scroll);

            scroll.Add(BuildHeader());
            var left = BuildPackageColumn();
            var middle = BuildStationsColumn();
            var right = BuildAutomationColumn();
            _automationColumn = right;
            var columns = UiKit.Columns(left, middle, right);
            // The station cards carry the most fields, so the middle column gets
            // more room than the two settings columns.
            left.style.flexGrow = 1.0f;
            middle.style.flexGrow = 1.45f;
            right.style.flexGrow = 1.05f;

            var tabs = new UiKit.Tabs();
            tabs.AddPage("Package & stations", columns);
            _links = new ProducerLinksView(_data, () => _project, () => Revalidate());
            tabs.AddPage("Producers & item links", _links);
            _item = new ManagerItemView(_data, () => _project, () => { RefreshStationCards(); Revalidate(); }) { DumpHost = () => OpenDump, ShowBrowser = ShowBrowser };
            tabs.AddPage("Manager item", _item);
            _copy = new CopyCompareView(_data, () => _project, () => { RefreshStationCards(); Revalidate(); }) { ShowBrowser = ShowBrowser };
            tabs.AddPage("Exact copy", _copy);
            tabs.OnSelected += i =>
            {
                if (i == 1) _links.Refresh();
                if (i == 2) _item.Refresh();
                if (i == 3) _copy.Refresh();
                if (i == 0) { RefreshStationCards(); RefreshAutomationColumn(); }
            };
            if (ExactCopyOnly)
            {
                // Management Station: two pages of the same exact-copy screen over the
                // same project. "Generator" is a second instance of the view; switching
                // pages refreshes the one shown so both always agree.
                _copy.style.display = DisplayStyle.Flex;
                _generator = new CopyCompareView(_data, () => _project, () => { RefreshStationCards(); Revalidate(); }, generator: true) { ShowBrowser = ShowBrowser };
                _chain = new ChainView(_data, () => _project, () => { RefreshStationCards(); Revalidate(); }) { ShowBrowser = ShowBrowser };
                var pages = new UiKit.Tabs();
                pages.AddPage("Exact copy", _copy);
                pages.AddPage("Generator", _generator);
                pages.AddPage("Chain", _chain);
                pages.OnSelected += i =>
                {
                    if (i == 0) _copy.Refresh();
                    if (i == 1) _generator.Refresh();
                    if (i == 2) _chain.Refresh();
                };
                scroll.Add(pages);
            }
            else scroll.Add(tabs);
            Add(BuildFooter());
            Revalidate();
        }

        private ProducerLinksView _links;
        private ManagerItemView _item;
        private CopyCompareView _copy;
        private CopyCompareView _generator;   // Management Station: the second page, same screen
        private ChainView _chain;   // Management Station: the third page, what the item does
        private VisualElement _automationColumn;

        /// <summary>The Manager item tab edits the same worker flags; rebuild this column so both tabs agree.</summary>
        private void RefreshAutomationColumn()
        {
            if (_automationColumn?.parent == null) return;
            var parent = _automationColumn.parent;
            var index = parent.IndexOf(_automationColumn);
            var fresh = BuildAutomationColumn();
            fresh.style.flexGrow = _automationColumn.style.flexGrow;
            parent.Insert(index, fresh);
            _automationColumn.RemoveFromHierarchy();
            _automationColumn = fresh;
        }

        /// <summary>Supplied by the editor host to open the "all options" dump in a real window.</summary>
        public Action<string, string> OpenDump;

        /// <summary>Supplied by the editor host to open the table browser in a real window; null = modal overlay.</summary>
        public Action<string, List<BrowserColumn>, List<string[]>, Action<string>> OpenBrowser;

        /// <summary>Supplied by the editor host: (title, directory, default name, extension) -> chosen path or "".</summary>
        public Func<string, string, string, string, string> SaveFileDialog;

        /// <summary>Supplied by the editor host: (title, directory, extension) -> chosen path or "".</summary>
        public Func<string, string, string, string> OpenFileDialog;

        private VisualElement _browserOverlay;

        /// <summary>Opens the browser in the host's window, or as an overlay inside this view.</summary>
        public void ShowBrowser(string title, List<BrowserColumn> columns, List<string[]> rows, Action<string> onPick)
        {
            if (OpenBrowser != null)
            {
                OpenBrowser(title, columns, rows, onPick);
                return;
            }
            _browserOverlay?.RemoveFromHierarchy();
            _browserOverlay = new VisualElement();
            _browserOverlay.style.position = Position.Absolute;
            _browserOverlay.style.left = 0; _browserOverlay.style.right = 0; _browserOverlay.style.top = 0; _browserOverlay.style.bottom = 0;
            _browserOverlay.style.backgroundColor = new Color(0, 0, 0, 0.75f);
            var browser = new DataBrowser(title, columns, rows,
                picked => { onPick(picked); CloseBrowser(); }, CloseBrowser);
            browser.style.marginLeft = 30; browser.style.marginRight = 30; browser.style.marginTop = 24; browser.style.marginBottom = 24;
            _browserOverlay.Add(browser);
            Add(_browserOverlay);
        }

        private void CloseBrowser()
        {
            _browserOverlay?.RemoveFromHierarchy();
            _browserOverlay = null;
        }

        /// <summary>Rebuilds the station cards without touching the other tabs (values edited elsewhere).</summary>
        private void RefreshStationCards()
        {
            if (_stationList == null) return;
            _stationList.Clear();
            for (var i = 0; i < _project.Stations.Count; i++)
                _stationList.Add(BuildStationCard(_project.Stations[i], i));
        }

        // ------------------------------------------------------------------ header

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;

            var title = new Label(ExactCopyOnly ? "Management Station  -  exact copy of the Monitoring Stand" : "Pal Itemgen  -  Production Manager mod creator");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiKit.Text;
            title.style.flexGrow = 1;
            header.Add(title);

            var counts = new Label($"{_data.Items.Count:n0} items, {_data.Recipes.Count:n0} recipes, {_data.Producers.Count} benches, {_data.Buildings.Count} buildings");
            counts.style.color = UiKit.Muted;
            counts.style.fontSize = 10;
            header.Add(counts);
            return header;
        }

        private VisualElement BuildDataMissingNotice()
        {
            var box = UiKit.Section("Lookup data missing", out var body);
            body.Add(UiKit.Note("This data ships with True Creation. Reinstall True Creation to restore it.\n" +
                                "Developers: run  python Tools/PalItemgen/generate_lookups.py  in the project folder, then Tools > Pal Itemgen (Reload Data) or Tools > Management Station (Reload Data).", UiKit.Warning));
            foreach (var e in _data.LoadErrors) body.Add(UiKit.Note(e, UiKit.Danger));
            return box;
        }

        // ------------------------------------------------------------------ left column

        private VisualElement BuildPackageColumn()
        {
            var col = UiKit.Column();

            var pkg = UiKit.Section("Mod package", out var body, "What the Steam Mod Uploader and the in-game loader read from Info.json.");
            col.Add(pkg);
            _modNameField = new TextField { value = _project.ModName ?? "" };
            _modNameField.RegisterValueChangedCallback(evt => { _project.ModName = evt.newValue; Revalidate(); });
            body.Add(UiKit.Row("Mod name", _modNameField, "follows the item's name until you type your own", LabelWidth, 0));
            _packageNameField = new TextField { value = _project.PackageName ?? "" };
            _packageNameField.RegisterValueChangedCallback(evt => { _project.PackageName = evt.newValue; Revalidate(); });
            body.Add(UiKit.Row("Package name", _packageNameField, "letters and digits only; the project folder, the package folder and the install folder carry this name; follows the item's name until you type your own", LabelWidth, 0));
            body.Add(TextRow("Author", _project.Author, v => _project.Author = v, null));
            body.Add(TextRow("Version", _project.Version, v => _project.Version = v, "change it on every upload or the game keeps the old copy", 110));
            body.Add(IntRow("Min revision", _project.MinRevision, v => _project.MinRevision = v, "last 5 digits of the title-screen version (82182 works)", 90));
            body.Add(ToggleRow("Debug mode", _project.DebugMode, v => _project.DebugMode = v, "reinstall on every launch while developing"));
            body.Add(ToggleRow("Dedicated server rules", _project.IncludeServerRules, v => _project.IncludeServerRules = v, "adds IsServer install rules so servers get the files too"));
            body.Add(TextRow("Thumbnail PNG", _project.ThumbnailPng, v => _project.ThumbnailPng = v, "optional; a placeholder is used when empty"));

            var outSection = UiKit.Section("Output", out var outBody, "Toggle between a Steam-Workshop-ready package and files laid out for manual copying.");
            col.Add(outSection);
            var layout = new Toggle { value = _project.Layout == OutputLayout.SteamWorkshop };
            layout.RegisterValueChangedCallback(evt =>
            {
                _project.Layout = evt.newValue ? OutputLayout.SteamWorkshop : OutputLayout.ManualInstall;
                UpdateLayoutHint();
                Revalidate();
            });
            outBody.Add(UiKit.Row("Steam Workshop package", layout, null, LabelWidth, 30));
            _layoutHint = UiKit.Note("");
            outBody.Add(_layoutHint);
            outBody.Add(TextRow("Steam item id", _project.SteamItemId, v => _project.SteamItemId = v, "optional: names the folder by the Workshop item id so it drops straight into steamapps\\workshop\\content\\1623730", 140));
            outBody.Add(TextRow("Output folder", _project.OutputFolder, v => _project.OutputFolder = v, null));
            if (string.IsNullOrEmpty(_project.GameFolder)) _project.GameFolder = ModPackager.GuessGameFolder(_project.OutputFolder);
            outBody.Add(TextRow("Game folder", _project.GameFolder, v => { _project.GameFolder = v; RefreshInstalled(); }, "the Palworld install (holds Pal\\ and Mods\\); Generate copies the package into its mod folders so the game runs what was just written"));
            outBody.Add(ToggleRow("Install after Generate", _project.InstallAfterGenerate, v => _project.InstallAfterGenerate = v, "hard-copy PalSchema + Scripts into the game folder and add the mods.txt line"));
            // every package this tool has put into the game, each with its own Uninstall:
            // working on one mod never hides another mod's install
            var installedHead = new Label("Installed in the game (by this tool)");
            installedHead.style.fontSize = 11;
            installedHead.style.color = UiKit.Muted;
            installedHead.style.marginTop = 6;
            installedHead.style.unityFontStyleAndWeight = FontStyle.Bold;
            outBody.Add(installedHead);
            _installedList = new VisualElement();
            outBody.Add(_installedList);
            RefreshInstalled();
            UpdateLayoutHint();

            var extras = UiKit.Section("Extra files (optional)", out var xbody,
                "Paks and JSON never share a target: .pak -> Paks\\ (Workshop installs to Pal\\Content\\Paks\\~WorkshopMods\\<package>, manual to ~mods), " +
                "LogicMods .pak -> Pal\\Content\\Paks\\LogicMods, PalSchema raw/blueprint .json -> their own folders under PalSchema\\mods\\<package>.");
            col.Add(extras);
            _extraList = new VisualElement();
            xbody.Add(_extraList);
            var addExtra = UiKit.SecondaryButton("+ Extra file", () => { _project.ExtraFiles.Add(new ExtraFile()); RebuildExtras(); });
            addExtra.style.alignSelf = Align.FlexStart;
            xbody.Add(addExtra);
            RebuildExtras();
            return col;
        }

        private VisualElement _extraList;
        private VisualElement _installedList;

        /// <summary>
        /// Lists every package this tool installed into the game folder (its manifest,
        /// or pm_config.lua for older installs), marks the one this project writes,
        /// and offers Uninstall per package. Refreshed after Generate, Install and Uninstall.
        /// </summary>
        private void RefreshInstalled()
        {
            if (_installedList == null) return;
            _installedList.Clear();
            List<ModPackager.InstalledPackage> list;
            try { list = ModPackager.ListInstalled(_project.GameFolder); }
            catch (Exception e) { _installedList.Add(UiKit.Note("could not read the game folder: " + e.Message, UiKit.Warning)); return; }
            var mine = ModPackager.SafeName(_project.PackageName);
            if (list.Count == 0)
            {
                _installedList.Add(UiKit.Note("nothing from this tool is installed in the game folder", UiKit.Muted));
                return;
            }
            foreach (var ip in list)
            {
                var pkg = ip.Package;
                var isThis = string.Equals(pkg, mine, StringComparison.OrdinalIgnoreCase);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                var label = new Label($"{pkg}{(isThis ? "   (this project)" : "")}   -   {ip.State}");
                label.style.fontSize = 11;
                label.style.color = isThis ? UiKit.Accent : UiKit.Text;
                label.style.flexGrow = 1;
                label.style.flexShrink = 1;
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);
                row.Add(UiKit.SmallButton("Uninstall " + pkg, () =>
                {
                    try
                    {
                        var removed = ModPackager.UninstallFromGame(pkg, _project.GameFolder, exactName: true);   // the folder name as listed, never re-sanitised
                        _status.text = removed.Count == 0 ? $"Nothing of {pkg} was installed in {_project.GameFolder}"
                            : $"Removed {pkg} from the game: " + string.Join(", ", removed) + ". Other packages were not touched.";
                        _status.style.color = removed.Count == 0 ? UiKit.Muted : UiKit.Success;
                    }
                    catch (Exception e) { _status.text = $"Uninstall of {pkg} failed: " + e.Message; _status.style.color = UiKit.Danger; }
                    RefreshInstalled();
                }));
                _installedList.Add(row);
                var detail = new Label(ip.Detail);
                detail.style.fontSize = 10;
                detail.style.color = UiKit.Muted;
                detail.style.marginLeft = 8;
                detail.style.whiteSpace = WhiteSpace.Normal;
                _installedList.Add(detail);
            }
        }
        private static readonly List<string> ExtraKinds = new List<string> { "Paks (.pak)", "LogicMods (.pak)", "PalSchema raw (.json)", "PalSchema blueprints (.json)" };

        private void RebuildExtras()
        {
            _extraList.Clear();
            foreach (var f in _project.ExtraFiles)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;
                var kind = new DropdownField(ExtraKinds, (int)f.Kind);
                kind.style.width = 150;
                kind.RegisterValueChangedCallback(evt => { f.Kind = (ExtraFileKind)Math.Max(0, ExtraKinds.IndexOf(evt.newValue)); Revalidate(); });
                row.Add(kind);
                var path = new TextField { value = f.Path };
                path.style.flexGrow = 1;
                path.RegisterValueChangedCallback(evt => { f.Path = evt.newValue; Revalidate(); });
                row.Add(path);
                var file = f;
                row.Add(UiKit.SmallButton("x", () => { _project.ExtraFiles.Remove(file); RebuildExtras(); Revalidate(); }));
                _extraList.Add(row);
            }
            if (_project.ExtraFiles.Count == 0) _extraList.Add(UiKit.Note("none - this package is Lua + PalSchema buildings only"));
        }

        private void UpdateLayoutHint()
        {
            _layoutHint.text = _project.Layout == OutputLayout.SteamWorkshop
                ? "Writes <folder>/Info.json + Scripts/ + PalSchema/ + thumbnail.png. Open it in the Palworld Mod Uploader (Shift+Create New Mod for a local test) or copy it into the Workshop content folder."
                : "Writes <PackageName>_ManualInstall/Mods/... mirroring the game folder, plus INSTALL.txt with the mods.txt line to add.";
        }

        // ------------------------------------------------------------------ middle column

        private VisualElement BuildStationsColumn()
        {
            var col = UiKit.Column();
            var section = UiKit.Section("Stations added to the game", out var body,
                "Each station is its own building id that reuses a vanilla model, so nothing vanilla is replaced. " +
                "The Production Manager (Monitoring Stand model by default) switches automation on in its base; " +
                "a Console sign takes typed commands; line stations are benches whose orders become targets.");
            col.Add(section);

            _stationList = new VisualElement();
            body.Add(_stationList);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexWrap = Wrap.Wrap;
            buttons.style.marginTop = 6;
            buttons.Add(UiKit.SecondaryButton("+ Production line", () =>
            {
                var n = _project.Stations.Count(s => s.Kind == StationKind.Line) + 1;
                _project.Stations.Add(StationSpec.LineFrom("Line" + n, "AncientWorkBench", "AncientWorkBench", 28));
                RebuildStations();
            }));
            buttons.Add(UiKit.SecondaryButton("+ Console sign", () =>
            {
                var c = StationSpec.Console();
                if (_project.Stations.Any(s => s.Id == c.Id)) c.Id += "2";
                _project.Stations.Add(c);
                RebuildStations();
            }));
            buttons.Add(UiKit.SecondaryButton("+ Manager", () =>
            {
                var m = StationSpec.Manager();
                if (_project.Stations.Any(s => s.Id == m.Id)) m.Id += "2";
                _project.Stations.Add(m);
                RebuildStations();
            }));
            buttons.Add(UiKit.SecondaryButton("Reset to defaults", () =>
            {
                _project.Stations = ModProject.Default().Stations;
                RebuildStations();
            }));
            body.Add(buttons);
            RebuildStations();
            return col;
        }

        private void RebuildStations()
        {
            _stationList.Clear();
            if (_presetList != null) RebuildPresets();   // line names may have changed
            for (var i = 0; i < _project.Stations.Count; i++)
                _stationList.Add(BuildStationCard(_project.Stations[i], i));
            _links?.Refresh();
            _item?.Refresh();
            Revalidate();
        }

        private static readonly List<string> KindLabels = new List<string>
        {
            ModProject.KindLabel(StationKind.Manager),
            ModProject.KindLabel(StationKind.ConsoleSign),
            ModProject.KindLabel(StationKind.Line),
            ModProject.KindLabel(StationKind.Copy),
        };

        private IEnumerable<(string Value, string Display)> ReuseSource(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.ConsoleSign:
                    return _data.Signboards.Where(p => p.MapObjectId != null).Select(p => (p.MapObjectId, p.Display));
                case StationKind.Line:
                    return _data.Producers.Where(p => p.MapObjectId != null && p.Buildable).Select(p => (p.MapObjectId, p.Display + "  -  " + p.TypeSummary));
                default:
                    return _data.Buildings.Select(p => (p.MapObjectId, p.Display + (string.IsNullOrEmpty(p.Model) ? "" : "  -  " + ShortModel(p.Model))));
            }
        }

        private static string ShortModel(string model) => (model ?? "").Replace("PalMapObject", "").Replace("Model", "");

        private VisualElement BuildStationCard(StationSpec s, int index)
        {
            var fold = new Foldout { text = $"{s.Name}  [{s.Id}]", value = index < 2 };
            fold.style.marginBottom = 4;
            fold.style.backgroundColor = UiKit.Background;
            UiKit.SetBorder(fold, UiKit.Border, 1);
            var body = new VisualElement();
            body.style.paddingLeft = 6;
            body.style.paddingRight = 6;
            body.style.paddingBottom = 6;
            fold.Add(body);

            var kind = new DropdownField(KindLabels, s.Kind == StationKind.Manager ? 0 : s.Kind == StationKind.ConsoleSign ? 1 : s.Kind == StationKind.Line ? 2 : 3);
            kind.RegisterValueChangedCallback(evt =>
            {
                var idx = KindLabels.IndexOf(evt.newValue);
                s.Kind = idx == 0 ? StationKind.Manager : idx == 1 ? StationKind.ConsoleSign : idx == 2 ? StationKind.Line : StationKind.Copy;
                var current = _data.Building(s.ReuseMapObjectId);
                if (s.Kind == StationKind.ConsoleSign && (current?.Model ?? "").Contains("Signboard") == false) s.ReuseMapObjectId = "Signboard";
                if (s.Kind == StationKind.Line && current?.Model != "PalMapObjectConvertItemModel") s.ReuseMapObjectId = "AncientWorkBench";
                if (s.Kind == StationKind.Manager && current == null) s.ReuseMapObjectId = "BaseCampWorkHard";
                RebuildStations();
            });
            body.Add(UiKit.Row("Kind", kind, null, 110, 0));
            body.Add(TextRow("Id", s.Id, v => { s.Id = ModPackager.SafeId(v); fold.text = $"{s.Name}  [{s.Id}]"; }, "row key in the game tables: letters, digits, underscores; must not clash with a vanilla id", 0, 110));
            body.Add(TextRow("Name", s.Name, v => { s.Name = v; fold.text = $"{s.Name}  [{s.Id}]"; }, null, 0, 110));
            var desc = new TextField { value = s.Description, multiline = true };
            desc.style.height = 48;
            desc.RegisterValueChangedCallback(evt => s.Description = evt.newValue);
            body.Add(UiKit.Row("Description", desc, "build wheel + technology text", 110, 0));
            if (s.Kind == StationKind.Line)
            {
                body.Add(TextRow("Line name", s.Line, v => s.Line = v, "targets set at this station belong to this line", 160, 110));
                var linked = s.AllowedItems?.Count ?? 0;
                body.Add(UiKit.Note(linked == 0
                    ? "Item links: everything its bench can make (edit on the 'Producers & item links' tab)."
                    : $"Item links: {linked} item(s) linked on the 'Producers & item links' tab.", UiKit.Muted));
            }

            var reuse = new UiKit.SearchPicker(() => ReuseSource(s.Kind), null, 120) { Value = s.ReuseMapObjectId };
            var reuseRow = UiKit.Row("Reuse building", reuse, ReuseHint(s), 110, 0);
            reuse.OnSelected += v => { s.ReuseMapObjectId = v; UiKit.SetRowHint(reuseRow, ReuseHint(s)); Revalidate(); };
            reuse.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.ReuseBuildings(_data, s.Kind);
                ShowBrowser($"Buildings to reuse - {s.Name}", cols, rows, v => { s.ReuseMapObjectId = v; reuse.SetValueSilently(v); UiKit.SetRowHint(reuseRow, ReuseHint(s)); Revalidate(); });
            }));
            body.Add(reuseRow);

            var icon = new UiKit.SearchPicker(() => _data.Icons.OrderBy(k => k.Value.Name).Select(k => (k.Key, string.IsNullOrEmpty(k.Value.Name) ? k.Key : k.Value.Name + "  (" + k.Key + ")")), null, 120)
            { Value = s.IconMapObjectId };
            icon.OnSelected += v => { s.IconMapObjectId = v; Revalidate(); };
            icon.AddTrailing(UiKit.BrowseButton(() =>
            {
                var (cols, rows) = BrowserDatasets.Icons(_data);
                ShowBrowser("Build icons", cols, rows, v => { s.IconMapObjectId = v; icon.SetValueSilently(v); Revalidate(); });
            }));
            body.Add(UiKit.Row("Icon", icon, "vanilla build icon to show in the wheel and tech tree", 110, 0));
            body.Add(TextRow("Custom icon PNG", s.CustomIconPng, v => s.CustomIconPng = v, "optional; overrides the vanilla icon via PalSchema resources", 0, 110));

            var mats = new VisualElement();
            body.Add(mats);
            void RebuildMaterials()
            {
                mats.Clear();
                for (var i = 0; i < s.Materials.Count; i++)
                {
                    var m = s.Materials[i];
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 2;
                    var cap = new Label(i == 0 ? "Build cost" : "");
                    cap.style.width = 110;
                    cap.style.flexShrink = 0;
                    cap.style.color = UiKit.Text;
                    cap.style.fontSize = 12;
                    row.Add(cap);
                    var pick = new UiKit.SearchPicker(() => _data.Items.Select(it => (it.Id, it.Display)), null, 120) { Value = m.ItemId };
                    pick.style.flexGrow = 1;
                    pick.style.flexShrink = 1;
                    pick.OnSelected += v => { m.ItemId = v; Revalidate(); };
                    pick.AddTrailing(UiKit.BrowseButton(() =>
                    {
                        var (cols, rows) = BrowserDatasets.Items(_data, false);
                        ShowBrowser("Items - build material", cols, rows, v => { m.ItemId = v; pick.SetValueSilently(v); Revalidate(); });
                    }));
                    row.Add(pick);
                    var count = new IntegerField { value = m.Count };
                    count.style.width = 56;
                    count.style.flexShrink = 0;
                    count.RegisterValueChangedCallback(evt => m.Count = Math.Max(1, evt.newValue));
                    row.Add(count);
                    var idx = i;
                    row.Add(UiKit.SmallButton("x", () => { s.Materials.RemoveAt(idx); RebuildMaterials(); Revalidate(); }));
                    mats.Add(row);
                }
                if (s.Materials.Count < 4)
                {
                    var add = UiKit.SmallButton("+ material", () => { s.Materials.Add(new MaterialCost { ItemId = "Wood", Count = 10 }); RebuildMaterials(); Revalidate(); });
                    add.style.marginLeft = 110;
                    add.style.marginBottom = 4;
                    add.style.alignSelf = Align.FlexStart;
                    mats.Add(add);
                }
            }
            RebuildMaterials();

            var numbers = UiKit.Columns(UiKit.Column(), UiKit.Column());
            var n1 = (VisualElement)numbers[0];
            var n2 = (VisualElement)numbers[1];
            n1.Add(FloatRow("Build work", s.BuildWorkAmount, v => s.BuildWorkAmount = v, "500 = workbench, 2000 = monitoring stand"));
            n1.Add(IntRow("HP", s.Hp, v => s.Hp = v, null, 90, 70));
            n1.Add(IntRow("Tech level", s.TechLevel, v => s.TechLevel = v, "stand: 7; 28 = High Quality tier; 1 = no gate for testing", 90, 70));
            n1.Add(IntRow("Tech cost", s.TechCost, v => s.TechCost = v, null, 90, 70));
            n2.Add(ToggleRow("Ancient tech", s.AncientTech, v => s.AncientTech = v, "costs ancient tech points instead", 110));
            n2.Add(IntRow("Limit per base", s.LimitPerBase, v => s.LimitPerBase = v, "0 = unlimited; 1 keeps one per base", 110, 60));
            var ui = new DropdownField(_data.Enums.TypeUIDisplay.Count > 0 ? _data.Enums.TypeUIDisplay : new List<string> { s.TypeUIDisplay },
                Math.Max(0, _data.Enums.TypeUIDisplay.IndexOf(s.TypeUIDisplay)));
            ui.RegisterValueChangedCallback(evt => s.TypeUIDisplay = evt.newValue);
            n2.Add(UiKit.Row("Build category", ui, null, 110, 0));
            body.Add(numbers);

            var remove = UiKit.SecondaryButton("Remove station", () => { _project.Stations.Remove(s); RebuildStations(); });
            remove.style.marginTop = 4;
            remove.style.alignSelf = Align.FlexStart;
            body.Add(remove);
            return fold;
        }

        private string ReuseHint(StationSpec s)
        {
            var p = _data.Building(s.ReuseMapObjectId);
            if (p == null) return "unknown building";
            switch (s.Kind)
            {
                case StationKind.ConsoleSign:
                    return (p.Model ?? "").Contains("Signboard") ? "sign with text input - good" : "not a sign: the console needs a signboard building";
                case StationKind.Line:
                    if (p.Model != "PalMapObjectConvertItemModel") return "not a crafting bench";
                    var n = _data.ItemsFor(p).Count();
                    return $"{n} craftable items appear in its menu ({p.TypeSummary}){(p.NeedsEnergyComponent ? "; needs power" : "")}";
                default:
                    if (string.IsNullOrEmpty(p.Model)) return "no concrete model class: the Lua side could not find it";
                    return $"model {ShortModel(p.Model)}; interacting opens the same menu as the {p.Name}";
            }
        }

        // ------------------------------------------------------------------ right column

        private VisualElement BuildAutomationColumn()
        {
            var col = UiKit.Column();
            var a = _project.Automation;
            var section = UiKit.Section("Automation defaults (pm_config.lua)", out var body, "How the Lua side behaves. Players can override targets in game; these are the starting rules.");
            col.Add(section);
            body.Add(IntRow("Scan every (s)", a.ScanIntervalSeconds, v => a.ScanIntervalSeconds = v, "server check interval"));
            body.Add(IntRow("Frame budget (ms)", a.TickBudgetMs, v => a.TickBudgetMs = Math.Max(1, v), "a scan runs in stages; each game-thread step runs stages until this is spent"));
            body.Add(IntRow("Step every (ms)", a.TickStepMs, v => a.TickStepMs = Math.Max(5, v), "gap between the steps of one scan"));
            body.Add(ToggleRow("Require manager", a.RequireManagerStation, v => a.RequireManagerStation = v, "only bases with a Production Manager run"));
            body.Add(IntRow("Restock below (%)", a.DefaultTriggerPercent, v => a.DefaultTriggerPercent = v, "keep 1200 at 66% restocks below 792"));
            body.Add(IntRow("Default keep", a.DefaultKeep, v => a.DefaultKeep = v, "used when an infinite order creates a target"));
            body.Add(IntRow("Max order batch", a.MaxOrderBatch, v => a.MaxOrderBatch = v, "crafts per work order per bench"));
            body.Add(IntRow("Idle grace (s)", a.IdleGraceSeconds, v => a.IdleGraceSeconds = v, "leave freshly idle benches to players this long"));
            body.Add(ToggleRow("Material check", a.MaterialCheck, v => a.MaterialCheck = v, "only order when the base has the materials; otherwise announce what is missing"));
            body.Add(ToggleRow("Auto-chain materials", a.AutoChainMaterials, v => a.AutoChainMaterials = v, "also make missing craftable materials (one level)"));
            body.Add(ToggleRow("Follow priority order", a.FollowPriorityOrder, v => a.FollowPriorityOrder = v, "finish the top target of a line before the next"));
            body.Add(ToggleRow("Best Pal to producer", a.AssignBestWorker, v => a.AssignBestWorker = v, "fixed-assign the base Pal with the highest rank in the bench's work type (Handiwork, Kindling, ...) when an order is placed"));
            body.Add(ToggleRow("Release when done", a.ReleaseWorkerWhenDone, v => a.ReleaseWorkerWhenDone = v, "unassign that Pal when the bench goes idle again"));
            body.Add(IntRow("Pals per order", a.WorkersPerOrder, v => a.WorkersPerOrder = v, "how many Pals to assign to one bench (capped by the bench's own limit)"));
            body.Add(IntRow("Hold after stop (s)", a.InterruptHoldSeconds, v => a.InterruptHoldSeconds = v, "a player stops or takes over a bench the manager ordered at: that item's target waits this long (0 = until resumed in the menu)"));
            body.Add(IntRow("Stop = crafts left", a.InterruptMinRemaining, v => a.InterruptMinRemaining = v, "an order that vanishes with at least this many crafts left counts as stopped by hand, not finished"));
            body.Add(ToggleRow("Open menu on interact", a.OpenMenuOnInteract, v => a.OpenMenuOnInteract = v, "standing in front of the manager item and interacting opens the mod's two-tab menu (hooks the Monitoring menu's OnSetup for this building id)"));
            body.Add(ToggleRow("Close vanilla menu", a.CloseVanillaMenuOnInteract, v => a.CloseVanillaMenuOnInteract = v, "remove the stand's own work-mode menu when ours opens from an interaction"));
            body.Add(IntRow("Announce level", a.AnnounceLevel, v => a.AnnounceLevel = v, "0 silent, 1 feedback + problems, 2 every order"));
            body.Add(IntRow("Repeat issues (s)", a.IssueRepeatSeconds, v => a.IssueRepeatSeconds = v, "same problem announced at most this often"));
            body.Add(IntRow("Remove-order count", a.RemoveOrderCount, v => a.RemoveOrderCount = v, "ordering exactly this many at a line removes the target"));
            body.Add(ToggleRow("Diagnostics dump", a.Diagnostics, v => a.Diagnostics = v, "write class/function dump on first run"));
            body.Add(ToggleRow("In-game menu", a.ClientMenu, v => a.ClientMenu = v, "host / single player: Monitoring + Production tabs"));
            body.Add(TextRow("Menu key", a.MenuKey, v => a.MenuKey = v, "UE4SS key name; opens and closes the menu", 70));
            body.Add(TextRow("Menu fallback key", a.MenuFallbackKey, v => a.MenuFallbackKey = v, "used when the menu key cannot be bound", 70));
            body.Add(IntRow("Menu width", a.MenuWidth, v => a.MenuWidth = v, "pixels"));
            body.Add(IntRow("Menu height", a.MenuHeight, v => a.MenuHeight = v, "pixels"));
            body.Add(IntRow("Search rows", a.MenuSearchRows, v => a.MenuSearchRows = v, "item results shown under the search box"));
            body.Add(TextRow("Status hotkey", a.StatusKey, v => a.StatusKey = v, "optional extra key announcing a status line; empty = off", 70));
            body.Add(ToggleRow("Status onto sign", a.WriteStatusToSign, v => a.WriteStatusToSign = v, "experimental: replace executed console commands with a status line"));

            var presets = UiKit.Section("Preset targets (optional)", out var pbody, "Applied once to every base that gets a manager and has no targets yet. Players change them in game afterwards.");
            col.Add(presets);
            _presetList = new VisualElement();
            pbody.Add(_presetList);
            var addPreset = UiKit.SecondaryButton("+ Preset target", () => { _project.Presets.Add(new PresetTarget { ItemId = "IronIngot", Keep = 500 }); RebuildPresets(); });
            addPreset.style.alignSelf = Align.FlexStart;
            pbody.Add(addPreset);
            RebuildPresets();
            return col;
        }

        private void RebuildPresets()
        {
            _presetList.Clear();
            // "default" is always a valid line (targets without a line station); a
            // stale name is kept as a choice rather than silently rewritten.
            var lines = new List<string> { "default" };
            lines.AddRange(_project.Stations.Where(s => s.Kind == StationKind.Line).Select(s => s.Line).Where(l => !string.IsNullOrEmpty(l) && l != "default").Distinct());
            foreach (var stale in _project.Presets.Select(p => p.Line).Where(l => !string.IsNullOrEmpty(l) && !lines.Contains(l)).Distinct().ToList()) lines.Add(stale);
            foreach (var p in _project.Presets)
            {
                var card = new VisualElement();
                card.style.marginBottom = 4;
                card.style.paddingLeft = 4;
                card.style.paddingRight = 4;
                card.style.paddingBottom = 4;
                UiKit.SetBorder(card, UiKit.Border, 1);
                var pick = new UiKit.SearchPicker(() => _data.CraftableItems().Select(it => (it.Id, it.Display)), null, 150) { Value = p.ItemId };
                pick.OnSelected += v => { p.ItemId = v; Revalidate(); };
                pick.AddTrailing(UiKit.BrowseButton(() =>
                {
                    var (cols, rows) = BrowserDatasets.Items(_data, true);
                    ShowBrowser("Craftable items - preset target", cols, rows, v => { p.ItemId = v; pick.SetValueSilently(v); Revalidate(); });
                }));
                card.Add(UiKit.Row("Item", pick, "craftable items only", 70, 0));
                var nums = UiKit.Columns(UiKit.Column(), UiKit.Column());
                ((VisualElement)nums[0]).Add(IntRow("Keep", p.Keep, v => p.Keep = v, null, 70, 60));
                ((VisualElement)nums[0]).Add(IntRow("Restock <", p.Trigger, v => p.Trigger = v, "0 = default percent", 70, 60));
                ((VisualElement)nums[1]).Add(IntRow("Priority", p.Priority, v => p.Priority = v, "1 = highest", 70, 60));
                ((VisualElement)nums[1]).Add(ToggleRow("Top", p.Top, v => p.Top = v, null, 70));
                card.Add(nums);
                if (string.IsNullOrEmpty(p.Line)) p.Line = "default";
                var line = new DropdownField(lines, lines.IndexOf(p.Line));
                line.RegisterValueChangedCallback(evt => p.Line = evt.newValue);
                card.Add(UiKit.Row("Line", line, null, 70, 0));
                var preset = p;
                var rm = UiKit.SmallButton("remove", () => { _project.Presets.Remove(preset); RebuildPresets(); Revalidate(); });
                rm.style.alignSelf = Align.FlexStart;
                card.Add(rm);
                _presetList.Add(card);
            }
            Revalidate();
        }

        // ------------------------------------------------------------------ footer

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement();
            footer.style.paddingLeft = 12;
            footer.style.paddingRight = 12;
            footer.style.paddingTop = 6;
            footer.style.paddingBottom = 8;
            footer.style.backgroundColor = UiKit.Panel;
            UiKit.SetBorder(footer, UiKit.Border, 1);

            // Problems scroll (a long list used to be cut off after three lines).
            var problemScroll = new ScrollView();
            problemScroll.style.maxHeight = 150;
            problemScroll.style.marginBottom = 4;
            _problems = problemScroll.contentContainer;
            footer.Add(problemScroll);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.Add(UiKit.PrimaryButton("Generate package", Generate));
            row.Add(UiKit.SecondaryButton("Open output folder", OpenOutput));
            row.Add(UiKit.SecondaryButton("Generate + install", InstallIntoGame));
            row.Add(UiKit.SecondaryButton("Uninstall from game", UninstallFromGame));
            row.Add(UiKit.SecondaryButton("Save", () => SaveProject(false)));
            row.Add(UiKit.SecondaryButton("Save as...", () => SaveProject(true)));
            row.Add(UiKit.SecondaryButton("Load...", LoadProjectDialog));
            row.Add(UiKit.SecondaryButton("New project...", NewProject));
            _projectLabel = new Label(ProjectLabelText());
            _projectLabel.style.color = UiKit.Muted;
            _projectLabel.style.fontSize = 10;
            _projectLabel.style.marginRight = 8;
            row.Add(_projectLabel);
            _status = new Label("");
            _status.style.color = UiKit.Muted;
            _status.style.fontSize = 11;
            _status.style.flexGrow = 1;
            _status.style.flexShrink = 1;
            _status.style.whiteSpace = WhiteSpace.Normal;
            row.Add(_status);
            footer.Add(row);
            return footer;
        }

        private TextField _modNameField;
        private TextField _packageNameField;
        private string _lastItemName;

        /// <summary>
        /// The item's name names everything: renaming the item renames the
        /// package, the project folder and the output folder, as long as those
        /// still carried the previous item name (a typed-in package name is kept).
        /// </summary>
        private void SyncNamesFromItem()
        {
            var mgr = _project.Stations.FirstOrDefault(s => s.Kind == StationKind.Manager);
            if (mgr == null) return;
            var name = mgr.Name ?? "";
            if (_lastItemName == null) { _lastItemName = name; return; }
            if (name == _lastItemName) return;
            var safe = new string(name.Where(char.IsLetterOrDigit).ToArray());
            var oldName = _lastItemName;
            var oldSafe = new string(oldName.Where(char.IsLetterOrDigit).ToArray());
            _lastItemName = name;
            if (string.IsNullOrEmpty(safe)) return;
            if (string.IsNullOrEmpty(_project.PackageName) || _project.PackageName == oldSafe)
            {
                _project.PackageName = safe;
                _packageNameField?.SetValueWithoutNotify(safe);
            }
            if (string.IsNullOrEmpty(_project.ModName) || _project.ModName == oldName || _project.ModName == oldSafe)
            {
                _project.ModName = name;
                _modNameField?.SetValueWithoutNotify(name);
            }
            if (_project.PackageName != safe) return;
            // the project file and the output move with it when they sat in the default projects folder
            if (string.IsNullOrEmpty(_project.ProjectPath) || _project.ProjectPath.StartsWith(DataPaths.DefaultProjectFolder, StringComparison.OrdinalIgnoreCase))
            {
                var wasDefaultOutput = string.IsNullOrEmpty(_project.OutputFolder) || _project.OutputFolder == DataPaths.DefaultOutputFolder
                    || _project.OutputFolder.StartsWith(DataPaths.DefaultProjectFolder, StringComparison.OrdinalIgnoreCase);
                _project.ProjectPath = DefaultProjectPath();
                if (wasDefaultOutput) _project.OutputFolder = Path.GetDirectoryName(_project.ProjectPath);
                if (_projectLabel != null) _projectLabel.text = ProjectLabelText();
            }
        }

        private void Revalidate()
        {
            SyncNamesFromItem();
            if (_problems == null) return;
            _problems.Clear();
            var problems = _project.Validate(_data);
            if (problems.Count == 0)
            {
                _problems.Add(UiKit.Note("Ready to generate.", UiKit.Success));
                return;
            }
            foreach (var p in problems.Take(6)) _problems.Add(UiKit.Note("• " + p, UiKit.Warning));
            if (problems.Count > 6) _problems.Add(UiKit.Note($"... and {problems.Count - 6} more", UiKit.Warning));
        }

        private void Generate()
        {
            // Problems never block a package: this is a dev tool and the files it
            // writes are meant to be edited. They are listed as warnings instead.
            var problems = _project.Validate(_data);
            try
            {
                if (string.IsNullOrEmpty(ProjectName()) && string.IsNullOrEmpty(_project.ProjectPath))
                {
                    var named = AskForNameAndPath();
                    if (named == null) return;
                    _project.ProjectPath = named;
                    Rebuild();
                    problems = _project.Validate(_data);
                }
                // the package is written into the project's folder (next to the project file)
                var projectPath = string.IsNullOrEmpty(_project.ProjectPath) ? DefaultProjectPath() : _project.ProjectPath;
                Directory.CreateDirectory(Path.GetDirectoryName(projectPath));
                PointOutputAtProjectFolder(projectPath);
                var result = ModPackager.Write(_project, _data, DataPaths.LuaDirectory, DataPaths.DefaultThumbnail);
                var saved = SaveProjectFile(projectPath);
                var warn = result.Warnings.Count > 0 ? "  Warnings: " + string.Join(" ", result.Warnings) : "";
                var fix = problems.Count > 0 ? $"  {problems.Count} problem(s) listed above still need fixing before the package is shipped." : "";
                var installed = "";
                // Management Station mode shows no install toggle, so it never installs on Generate
                // (2026-09-15 audit: the flag was on in every saved project and the package went into
                // the game silently). Install from the full creator window, where the toggle is visible.
                if (ExactCopyOnly && _project.InstallAfterGenerate)
                    installed = " Not installed into the game: Management Station mode never installs on Generate. Open Tools > Pal Itemgen (Production Manager creator), tick 'Install after Generate' and generate there.";
                else if (_project.InstallAfterGenerate && !string.IsNullOrWhiteSpace(_project.GameFolder))
                {
                    try
                    {
                        var dests = ModPackager.InstallIntoGame(_project, result.RootFolder, _project.GameFolder);
                        installed = " Installed into the game: " + string.Join(" and ", dests) + ". This install is not tracked by the Palworld mod loader (no Mods\\ManagedMods receipt): a UE4SS or PalSchema Workshop update can re-sync that folder and remove it; keep the package and Install again then.";
                    }
                    catch (Exception ie) { installed = " NOT installed into the game: " + ie.Message; }
                }
                RefreshInstalled();
                _status.text = $"Wrote {result.Files.Count} files to {result.RootFolder}.{installed} Project saved to {saved}.{warn}{fix}";
                _status.style.color = problems.Count > 0 || result.Warnings.Count > 0 ? UiKit.Warning : UiKit.Success;
                _log($"[PalItemgen] package written to {result.RootFolder}; project {saved}; problems {problems.Count}");
            }
            catch (Exception e)
            {
                _status.text = "Generate failed: " + e.Message;
                _status.style.color = UiKit.Danger;
                _log("[PalItemgen] " + e);
            }
        }

        /// <summary>Removes this package's folders and mods.txt line from the game (dismantle placed items first).</summary>
        private void UninstallFromGame()
        {
            try
            {
                var pkg = ModPackager.SafeName(_project.PackageName);
                var removed = ModPackager.UninstallFromGame(_project.PackageName, _project.GameFolder);
                _status.text = removed.Count == 0 ? $"Nothing of {pkg} (this project) was installed in {_project.GameFolder}; other packages are listed under Output with their own Uninstall."
                    : $"Removed {pkg} (this project) from the game: " + string.Join(", ", removed) + ". Other packages were not touched. Placed copies of the item vanish from saves once the row is gone.";
                _status.style.color = removed.Count == 0 ? UiKit.Muted : UiKit.Success;
                RefreshInstalled();
            }
            catch (Exception e) { _status.text = "Uninstall failed: " + e.Message; _status.style.color = UiKit.Danger; }
        }

        /// <summary>
        /// Generates the package from the project as it stands right now, then copies it
        /// into the game. Install always regenerates: on 2026-09-06 a test ran a pm_config.lua
        /// stamped an hour before the project was saved, because Install copied whatever the
        /// output folder happened to hold. The files have to be written to be installed, so
        /// there is nothing to gain by installing a stale copy.
        /// </summary>
        private void InstallIntoGame()
        {
            try
            {
                var projectPath = string.IsNullOrEmpty(_project.ProjectPath) ? DefaultProjectPath() : _project.ProjectPath;
                Directory.CreateDirectory(Path.GetDirectoryName(projectPath));
                PointOutputAtProjectFolder(projectPath);
                var result = ModPackager.Write(_project, _data, DataPaths.LuaDirectory, DataPaths.DefaultThumbnail);
                var dests = ModPackager.InstallIntoGame(_project, result.RootFolder, _project.GameFolder);
                var warn = result.Warnings.Count > 0 ? "  Warnings: " + string.Join(" ", result.Warnings) : "";
                _status.text = $"Generated {result.Files.Count} files and installed into the game: " + string.Join(" and ", dests) + ". Restart the game to load it." + warn;
                RefreshInstalled();
                _status.style.color = result.Warnings.Count > 0 ? UiKit.Warning : UiKit.Success;
                _log($"[PalItemgen] generated + installed from {result.RootFolder}");
            }
            catch (Exception e)
            {
                _status.text = "Install failed: " + e.Message;
                _status.style.color = UiKit.Danger;
            }
        }

        private void OpenOutput()
        {
            try
            {
                Directory.CreateDirectory(_project.OutputFolder);
                Process.Start(new ProcessStartInfo { FileName = _project.OutputFolder, UseShellExecute = true });
            }
            catch (Exception e)
            {
                _status.text = "Could not open folder: " + e.Message;
            }
        }

        private Label _projectLabel;

        private string ProjectLabelText() =>
            string.IsNullOrEmpty(_project.ProjectPath)
                ? (string.IsNullOrEmpty(ProjectName())
                    ? "project: unsaved, no name yet (Save asks for one; the folder is named after it)"
                    : "project: unsaved (Save writes " + DefaultProjectPath() + ")")
                : "project: " + _project.ProjectPath;

        /// <summary>The project's own name: the package name, cleaned for a folder. Empty when the mod has no name yet.</summary>
        private string ProjectName()
        {
            var cleaned = new string((_project.PackageName ?? "").Where(char.IsLetterOrDigit).ToArray());
            return cleaned;
        }

        /// <summary>Projects\&lt;Name&gt;\&lt;Name&gt;.palitemgen.json, or null while the mod has no name.</summary>
        private string DefaultProjectPath()
        {
            var name = ProjectName();
            if (string.IsNullOrEmpty(name)) return null;
            return Path.Combine(DataPaths.ProjectFolderFor(name), name + "." + DataPaths.ProjectExtension);
        }

        /// <summary>
        /// Every file of a project lives in its folder: the package is generated
        /// next to the project file unless the user pointed Output elsewhere.
        /// </summary>
        private void PointOutputAtProjectFolder(string projectPath)
        {
            var folder = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(folder)) return;
            if (string.IsNullOrWhiteSpace(_project.OutputFolder) || _project.OutputFolder == DataPaths.DefaultOutputFolder
                || _project.OutputFolder.StartsWith(DataPaths.DefaultProjectFolder, StringComparison.OrdinalIgnoreCase))
                _project.OutputFolder = folder;
        }

        /// <summary>Writes the project file and returns the path it went to. Always tells the user where.</summary>
        private string SaveProjectFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) path = DefaultProjectPath();
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("the mod needs a package name first: the project folder is named after it");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DataPaths.DefaultProjectFolder);
            PointOutputAtProjectFolder(path);
            File.WriteAllText(path, _project.ToJson());
            _project.ProjectPath = path;
            // the folder explains itself: what it holds and where each part goes by hand, in order
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(path) ?? ".", "README - install.txt"), ModPackager.ProjectReadmeText(_project)); }
            catch (Exception) { /* the README is a convenience; the project itself was saved */ }
            try { File.WriteAllText(DataPaths.LastProjectPointer, path); } catch (Exception) { /* pointer is a convenience only */ }
            if (_projectLabel != null) _projectLabel.text = ProjectLabelText();
            return path;
        }

        /// <summary>Save (to the current path, or the default one) or Save as (dialog when the host has one).</summary>
        /// <summary>
        /// Asks for a name when the mod has none: the Save-as dialog's file name
        /// becomes the package name (letters and digits). Returns the path to
        /// save to, or null when the user cancelled or no dialog exists.
        /// </summary>
        private string AskForNameAndPath()
        {
            if (_status == null) return null;   // no footer yet (lookup data missing)
            if (SaveFileDialog == null)
            {
                _status.text = "Give the mod a package name on the Package & stations tab first: the project folder is named after it.";
                _status.style.color = UiKit.Warning;
                return null;
            }
            Directory.CreateDirectory(DataPaths.DefaultProjectFolder);
            var chosen = SaveFileDialog("Name the project (this names its folder and the package)", DataPaths.DefaultProjectFolder, "MyMod." + DataPaths.ProjectExtension, "json");
            if (string.IsNullOrEmpty(chosen)) { _status.text = "Save cancelled: the project needs a name."; _status.style.color = UiKit.Muted; return null; }
            var stem = Path.GetFileName(chosen);
            var dot = stem.IndexOf('.');
            if (dot > 0) stem = stem.Substring(0, dot);
            var name = new string(stem.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(name)) { _status.text = "That file name has no letters or digits to use as a package name."; _status.style.color = UiKit.Warning; return null; }
            _project.PackageName = name;
            if (string.IsNullOrWhiteSpace(_project.ModName)) _project.ModName = name;
            // the file goes into its own project folder when saved at the projects root
            var dir = Path.GetDirectoryName(chosen) ?? DataPaths.DefaultProjectFolder;
            if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\', '/'), Path.GetFullPath(DataPaths.DefaultProjectFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                chosen = Path.Combine(DataPaths.ProjectFolderFor(name), name + "." + DataPaths.ProjectExtension);
            return chosen;
        }

        private void SaveProject(bool saveAs)
        {
            try
            {
                var path = _project.ProjectPath;
                if (string.IsNullOrEmpty(ProjectName()) && string.IsNullOrEmpty(path))
                {
                    path = AskForNameAndPath();
                    if (path == null) return;
                    Rebuild();
                }
                else if (saveAs || string.IsNullOrEmpty(path))
                {
                    var suggested = string.IsNullOrEmpty(path) ? DefaultProjectPath() : path;
                    if (saveAs && SaveFileDialog != null)
                    {
                        var chosen = SaveFileDialog("Save PalItemgen project", Path.GetDirectoryName(suggested), Path.GetFileName(suggested), "json");
                        if (string.IsNullOrEmpty(chosen)) { _status.text = "Save cancelled."; _status.style.color = UiKit.Muted; return; }
                        path = chosen;
                    }
                    else
                    {
                        path = suggested;
                    }
                }
                var saved = SaveProjectFile(path);
                _status.text = $"Project saved to {saved}";
                _status.style.color = UiKit.Success;
                _log("[PalItemgen] project saved to " + saved);
            }
            catch (Exception e)
            {
                _status.text = "Could not save project: " + e.Message;
                _status.style.color = UiKit.Danger;
                _log("[PalItemgen] could not save project: " + e.Message);
            }
        }

        private void LoadProjectDialog()
        {
            try
            {
                string path = null;
                if (OpenFileDialog != null)
                {
                    path = OpenFileDialog("Open PalItemgen project", DataPaths.DefaultProjectFolder, "json");
                    if (string.IsNullOrEmpty(path)) { _status.text = "Load cancelled."; _status.style.color = UiKit.Muted; return; }
                }
                else
                {
                    path = string.IsNullOrEmpty(_project.ProjectPath) ? DefaultProjectPath() : _project.ProjectPath;
                    if (!File.Exists(path)) { _status.text = "No project file at " + path; _status.style.color = UiKit.Warning; return; }
                }
                _project = LoadFrom(path);
                _lastItemName = null;   // a loaded project is not an item rename
                try { File.WriteAllText(DataPaths.LastProjectPointer, path); } catch (Exception) { /* convenience only */ }
                Rebuild();
                _status.text = $"Project loaded from {path}";
                _status.style.color = UiKit.Success;
            }
            catch (Exception e)
            {
                _status.text = "Could not load project: " + e.Message;
                _status.style.color = UiKit.Danger;
            }
        }

        /// <summary>The last project opened, else the legacy autosave, else the defaults. Never blocks the tool.</summary>
        private static ModProject LoadProjectOrDefault()
        {
            try
            {
                if (File.Exists(DataPaths.LastProjectPointer))
                {
                    var last = File.ReadAllText(DataPaths.LastProjectPointer).Trim();
                    if (File.Exists(last)) return LoadFrom(last);
                }
                if (File.Exists(DataPaths.ProjectFile))
                    return Upgrade(ModProject.FromJson(File.ReadAllText(DataPaths.ProjectFile)));
            }
            catch (Exception)
            {
                // fall through to defaults: a broken project file must not block the tool
            }
            return ModProject.NewUnnamed();
        }

        /// <summary>Creates a project: the name comes first, then its folder and file exist before any editing.</summary>
        private void NewProject()
        {
            var previous = _project;
            _project = ModProject.NewUnnamed();
            var path = AskForNameAndPath();
            if (path == null) { _project = previous; Rebuild(); return; }   // cancel keeps the project that was open
            _lastItemName = null;
            try
            {
                var saved = SaveProjectFile(path);
                try { File.WriteAllText(DataPaths.LastProjectPointer, saved); } catch (Exception) { /* convenience only */ }
                Rebuild();
                _status.text = $"New project created: {saved}. Its folder holds the project file and every mod file generated for it.";
                _status.style.color = UiKit.Success;
            }
            catch (Exception e)
            {
                Rebuild();
                _status.text = "Could not create the project: " + e.Message;
                _status.style.color = UiKit.Danger;
            }
        }

        /// <summary>Reads a project file the same way everywhere: parse, upgrade, remember the path.</summary>
        private static ModProject LoadFrom(string path)
        {
            var p = Upgrade(ModProject.FromJson(File.ReadAllText(path)));
            p.ProjectPath = path;
            return p;
        }

        /// <summary>
        /// A project written by the first build (format 0: manager = sign, no
        /// console) gets the current station kinds. A current-format project
        /// that happens to reuse a signboard is left alone.
        /// </summary>
        private static ModProject Upgrade(ModProject loaded)
        {
            if (loaded.FormatVersion < 1 && loaded.Stations.Count > 0 && !loaded.Stations.Any(s => s.Kind == StationKind.ConsoleSign)
                && loaded.Stations.Any(s => s.Kind == StationKind.Manager && s.ReuseMapObjectId == "Signboard"))
                loaded.Stations = ModProject.DefaultFull().Stations;   // the format-0 presets name those lines
            loaded.FormatVersion = ModProject.CurrentFormat;
            // Files saved before the serializer fix carry doubled build materials
            // (every load appended the defaults again); keep one entry per item.
            foreach (var s in loaded.Stations)
                if (s.Materials != null && s.Materials.Count > 0)
                    s.Materials = s.Materials.GroupBy(m => m.ItemId).Select(g => g.First()).ToList();
            return loaded;
        }

        // ------------------------------------------------------------------ field helpers

        /// <param name="width">0 = fill the line.</param>
        private VisualElement TextRow(string label, string value, Action<string> set, string hint, float width = 0, float labelWidth = LabelWidth)
        {
            var f = new TextField { value = value ?? "" };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Revalidate(); });
            return UiKit.Row(label, f, hint, labelWidth, width);
        }

        private VisualElement IntRow(string label, int value, Action<int> set, string hint, float labelWidth = LabelWidth, float width = 70)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Revalidate(); });
            return UiKit.Row(label, f, hint, labelWidth, width);
        }

        private VisualElement FloatRow(string label, float value, Action<float> set, string hint)
        {
            var f = new FloatField { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Revalidate(); });
            return UiKit.Row(label, f, hint, 90, 70);
        }

        private VisualElement ToggleRow(string label, bool value, Action<bool> set, string hint, float labelWidth = LabelWidth)
        {
            var f = new Toggle { value = value };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Revalidate(); });
            return UiKit.Row(label, f, hint, labelWidth, 30);
        }
    }
}
