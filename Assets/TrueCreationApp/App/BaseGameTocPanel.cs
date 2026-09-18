using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using TrueCreation.Host;

namespace TrueCreation.App
{
    /// <summary>
    /// Base Game TOC tab: the one-pass table of contents of the vanilla 1.0.5 export (Docs/BaseGame/toc)
    /// plus the pre-build reference (Docs/BaseGame/reference.json). Five views, each a search field over a
    /// list with a detail pane: Reference (what touches where, how the game reads it, issues, fixes),
    /// Tables (908 DataTables), Folders, Files (every file of the export), Changes vs 1.0.4.
    /// Reads only the TOC files in Docs/BaseGame. It never walks the export; Reveal/Open go to ONE file.
    /// The FModel export is optional (header block, folder chosen per machine): everything listed is built in;
    /// only Reveal/Open on a game file need it.
    /// Runs in the editor and in the exe: the exe ships the files this tab reads in StreamingAssets/BaseGame (the build
    /// copies them, TrueCreationBuild.BaseGameShipped); paths, dialogs and settings come from TocPaths and AppHost.
    /// </summary>
    public sealed class BaseGameTocPanel : VisualElement
    {
        // ------------------------------------------------------------ reference.json model (JsonUtility)
        [Serializable] public class RefWhere { public string name; public string path; public string note; }
        [Serializable] public class RefIssue { public string id; public string status; public string what; public string doc; }
        [Serializable] public class RefFix { public string what; public string doc; }
        [Serializable] public class RefSection
        {
            public string id; public string title; public string in_game; public List<RefWhere> where; public string how_read;
            public string loader_piece; public List<string> tools; public List<RefIssue> issues; public List<RefFix> fixes;
            public string open; public string confidence; public List<string> toc_lookup;
        }
        [Serializable] public class RefDoc { public string version; public string purpose; public List<RefSection> sections; }

        private sealed class TableRow { public string Rel, Table, Struct, Parents, Rows, Fields; }
        private sealed class FolderRow { public string Folder, Files, Bytes, Types; }
        private sealed class ChangeRow { public string Rel, Old, New; }

        private const int MaxListed = 3000;
        private static readonly string[] FileCols =
            { "rel", "bytes", "type", "name", "object_path", "class_parent", "rows", "row_keys_first5", "fields_top", "refs_out_count", "note" };

        private readonly string _baseDir;
        private readonly string _tocDir;
        private RefDoc _reference;
        private List<TableRow> _tables = new List<TableRow>();
        private List<FolderRow> _folders = new List<FolderRow>();
        private List<ChangeRow> _changes = new List<ChangeRow>();
        private string[] _fileLines; // toc_files.tsv, loaded on first use (30 MB)
        // explained TOC (expand_toc.py): rel -> what, why, row key, source, reference sections; folder -> what, why, source
        private readonly Dictionary<string, string[]> _tableExplained = new Dictionary<string, string[]>();
        private readonly Dictionary<string, string[]> _folderExplained = new Dictionary<string, string[]>();

        private readonly List<Button> _viewButtons = new List<Button>();
        private readonly List<VisualElement> _views = new List<VisualElement>();
        private TextField _tableSearch, _fileSearch, _folderSearch;
        private ListView _tableList, _fileList, _folderList, _changeList, _sectionList;
        private Label _fileStatus, _tableCount, _folderCount;
        private int _activeView = -1;
        // FModel export (optional): header block, and one redraw per view so path buttons follow a folder change
        private Label _exportPath, _exportStatus;
        private Button _revealExport;
        private readonly Dictionary<int, Action> _redraw = new Dictionary<int, Action>();
        private const string ExportSentinel = "Pal/Content/Pal/DataTable/Character/DT_PalBPClass.json";

        public BaseGameTocPanel()
        {
            style.flexGrow = 1;
            _baseDir = AppPaths.BaseGameDocs;
            _tocDir = Path.Combine(_baseDir, "toc");
            LoadSmallFiles();
            BuildHeader();
            BuildViewBar();
            var host = new VisualElement(); host.style.flexGrow = 1; Add(host);
            _views.Add(BuildReferenceView());
            _views.Add(BuildTablesView());
            _views.Add(BuildFoldersView());
            _views.Add(BuildFilesView());
            _views.Add(BuildChangesView());
            foreach (var v in _views) { v.style.flexGrow = 1; v.style.display = DisplayStyle.None; host.Add(v); }
            ShowView(0);
        }

        // ------------------------------------------------------------ loading
        private void LoadSmallFiles()
        {
            var refPath = Path.Combine(_baseDir, "reference.json");
            if (File.Exists(refPath))
            {
                try { _reference = JsonUtility.FromJson<RefDoc>(File.ReadAllText(refPath)); }
                catch (Exception e) { Debug.LogError("[BaseGameTOC] reference.json failed to parse: " + e.Message); }
            }
            foreach (var p in ReadTsv("toc_tables.tsv"))
                if (p.Length >= 6) _tables.Add(new TableRow { Rel = p[0], Table = p[1], Struct = p[2], Parents = p[3], Rows = p[4], Fields = p[5] });
            foreach (var p in ReadTsv("toc_folders.tsv"))
                if (p.Length >= 4) _folders.Add(new FolderRow { Folder = p[0], Files = p[1], Bytes = p[2], Types = p[3] });
            foreach (var p in ReadTsv("changed_vs_1.0.4.tsv"))
                if (p.Length >= 3) _changes.Add(new ChangeRow { Rel = p[0], Old = p[1], New = p[2] });
            foreach (var p in ReadTsv("toc_tables_explained.tsv"))
                if (p.Length >= 10) _tableExplained[p[0]] = new[] { p[5], p[6], p[7], p[8], p[9] };
            foreach (var p in ReadTsv("toc_folders_explained.tsv"))
                if (p.Length >= 7) _folderExplained[p[0]] = new[] { p[4], p[5], p[6] };
            _tables.Sort((a, b) => string.CompareOrdinal(a.Table, b.Table));
        }

        private IEnumerable<string[]> ReadTsv(string name)
        {
            var path = Path.Combine(_tocDir, name);
            if (!File.Exists(path)) yield break;
            using (var r = new StreamReader(path))
            {
                r.ReadLine(); // header
                string line;
                while ((line = r.ReadLine()) != null)
                    if (line.Length > 0) yield return line.Split('\t');
            }
        }

        private void EnsureFilesLoaded()
        {
            if (_fileLines != null) return;
            var path = Path.Combine(_tocDir, "toc_files.tsv");
            if (!File.Exists(path)) { _fileLines = new string[0]; return; }
            var all = File.ReadAllLines(path);
            _fileLines = all.Length > 1 ? new string[all.Length - 1] : new string[0];
            Array.Copy(all, 1, _fileLines, 0, _fileLines.Length);
        }

        // ------------------------------------------------------------ header + view bar
        private void BuildHeader()
        {
            var head = new VisualElement();
            head.style.paddingLeft = 10; head.style.paddingRight = 10; head.style.paddingTop = 8; head.style.paddingBottom = 6;
            head.style.flexShrink = 0;
            var title = new Label("Base game TOC - vanilla 1.0.5 export, one pass");
            title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold; head.Add(title);
            var ok = Directory.Exists(_tocDir);
            var status = new Label(ok
                ? $"{_baseDir}   |   tables {_tables.Count}   folders {_folders.Count}   changed vs 1.0.4 {_changes.Count}   |   {(_reference == null ? "reference.json missing" : _reference.version)}"
                : Application.isEditor
                    ? $"TOC data not found in {_tocDir}. Developers: it is built from an FModel export with  "
                      + "python Tools/ExportTOC/export_toc.py --exports \"<FModel>\\Output\\Exports\" --out \"Docs\\BaseGame\\toc\"  (see Docs/BaseGame/README.md)."
                    : $"TOC data not found in {_tocDir}. It ships with True Creation: reinstall True Creation to restore it.");
            status.style.fontSize = 11; status.style.color = ok ? new Color(0.72f, 0.72f, 0.76f) : new Color(0.95f, 0.5f, 0.5f);
            status.style.whiteSpace = WhiteSpace.Normal; status.style.marginTop = 2; head.Add(status);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 6;
            AddDocLink(row, "Open TOC.md (what and why)", Path.Combine(_tocDir, "TOC.md"));
            row.Add(LinkButton("Reveal TOC folder", _baseDir, reveal: true));
            AddDocLink(row, "Open REFERENCE.md", Path.Combine(_baseDir, "REFERENCE.md"));
            AddDocLink(row, "Open toc_lookup.md", Path.Combine(_tocDir, "toc_lookup.md"));
            AddDocLink(row, "Open README", Path.Combine(_baseDir, "README.md"));
            head.Add(row);
            var rule = new Label("Rule: find it here, then open only that one file. The export is never walked again.");
            rule.style.fontSize = 11; rule.style.color = new Color(0.62f, 0.62f, 0.66f); rule.style.marginTop = 4; head.Add(rule);
            head.Add(BuildExportBlock());
            Add(head);
        }

        // ------------------------------------------------------------ FModel export (optional)
        private VisualElement BuildExportBlock()
        {
            var box = new VisualElement();
            box.style.marginTop = 6; box.style.paddingLeft = 8; box.style.paddingRight = 8; box.style.paddingTop = 4; box.style.paddingBottom = 4;
            box.style.backgroundColor = new Color(0.17f, 0.17f, 0.19f); box.style.borderLeftWidth = 3; box.style.borderLeftColor = new Color(0.36f, 0.64f, 1f);
            var line = new VisualElement(); line.style.flexDirection = FlexDirection.Row; line.style.flexWrap = Wrap.Wrap; line.style.alignItems = Align.Center;
            var caption = new Label("FModel export (optional):"); caption.style.unityFontStyleAndWeight = FontStyle.Bold; caption.style.fontSize = 12; caption.style.marginRight = 6; line.Add(caption);
            _exportPath = new Label(); _exportPath.style.fontSize = 11; _exportPath.style.marginRight = 8; line.Add(_exportPath);
            var choose = new Button(ChooseExport)
            {
                text = "Choose folder...",
                tooltip = "The folder FModel wrote: <FModel>\\Output\\Exports, the one holding Pal\\Content. Picking Output, Pal or Pal\\Content finds it too.",
            };
            var reset = new Button(() => { TocPaths.ResetExports(); RefreshExport(); }) { text = "Use default", tooltip = TocPaths.DefaultExports };
            _revealExport = new Button(() => TocPaths.Reveal(TocPaths.Exports)) { text = "Reveal" };
            foreach (var b in new[] { choose, reset, _revealExport }) { b.style.height = 22; b.style.marginLeft = 0; b.style.marginRight = 4; line.Add(b); }
            box.Add(line);
            _exportStatus = new Label(); _exportStatus.style.fontSize = 11; _exportStatus.style.whiteSpace = WhiteSpace.Normal; _exportStatus.style.marginTop = 2;
            box.Add(_exportStatus);
            var help = new Foldout { text = "What works without it, and how to make one", value = false };
            help.style.marginTop = 2;
            foreach (var part in ExportHelp) help.Add(part.StartsWith("# ") ? (VisualElement)Head(part.Substring(2)) : Para(part));
            box.Add(help);
            UpdateExportLabels();
            return box;
        }

        private static readonly string[] ExportHelp =
        {
            "# Built in: no FModel needed",
            "Everything this tab lists: the Reference sections, the tables with their fields and row counts, the folders, the files and the changes. It comes from the TOC files made once from the game, not from an export on this PC.",
            "# What your own export adds",
            "Open file and Reveal open the game file itself: the .json FModel wrote, with every row and value. Without an export those buttons stay off and say why.",
            "# The export this TOC was made from",
            "Palworld 1.0.5.102999 (build 25246127), Pal-Windows.pak loaded on its own so no mod files are mixed in: 78,599 .json files, 28.06 GB (Docs/BaseGame/README.md). An export of another game version still opens; files that changed will not match what this tab says.",
            "# Making one with FModel",
            "1. Get FModel (fmodel.app). Add a game directory: the Palworld install folder that holds Engine and Pal. UE version: GAME_UE5_1.",
            "2. Settings > General: tick Local Mapping File and set Mapping File Path to a .usmap for your game version. UE4SS can write one from your own game: Ctrl + Numpad 6 while the game runs saves Mappings.usmap.",
            "3. Load Pal-Windows.pak only. Leave the mod paks (~mods, LogicMods, ~WorkshopMods) out so the export stays vanilla.",
            "4. Right-click the folder you want (for the data tables, the DataTable folder under Pal) and choose Save Folder's Packages Properties (.json). The whole game is about 28 GB of .json; one folder is enough if that is all you need.",
            "5. FModel writes into <FModel>\\Output\\Exports\\Pal\\Content\\... Pick that Exports folder with Choose folder above.",
            "FModel can report success when it saved nothing: check that the files are there.",
            "Sources: Palworld Modding Docs (pwmodding.wiki) for steps 1-2, the UE4SS docs for the mappings key, this project's own export for steps 3 and 5.",
        };

        private void ChooseExport()
        {
            var picked = TocPaths.PickExports();
            if (string.IsNullOrEmpty(picked)) return;
            TocPaths.Exports = picked;
            RefreshExport();
        }

        /// <summary>After the export folder changes: the header, then every detail pane already shown (its path buttons).</summary>
        private void RefreshExport()
        {
            UpdateExportLabels();
            foreach (var redraw in new List<Action>(_redraw.Values)) redraw();
        }

        private void UpdateExportLabels()
        {
            var root = TocPaths.Exports;
            _exportPath.text = root + (TocPaths.ExportsIsDefault ? "   (default)" : "");
            _exportStatus.text = ExportStatusText(root, out var ok);
            _exportStatus.style.color = ok ? new Color(0.55f, 0.85f, 0.55f) : new Color(0.98f, 0.8f, 0.5f);
            _revealExport.SetEnabled(Directory.Exists(root));
        }

        private static bool ExportUsable(string root) =>
            !string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, "Pal", "Content"));

        /// <summary>Two folder checks and one file check; the export is never listed.</summary>
        private static string ExportStatusText(string root, out bool ok)
        {
            ok = false;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return "Not set up on this PC. Everything in this tab still works; only Open file and Reveal on game files need an export.";
            if (!ExportUsable(root))
                return "This folder has no Pal\\Content inside. Choose the Exports folder FModel wrote (it holds Pal\\Content).";
            ok = true;
            return File.Exists(Path.Combine(root, ExportSentinel.Replace('/', Path.DirectorySeparatorChar)))
                ? "Found. Open file and Reveal open the game files in it."
                : "Found, but " + ExportSentinel.Replace('/', '\\') + " is not in it: a partial export. Files you did not save say 'not in your FModel export'.";
        }

        private static Button LinkButton(string text, string path, bool reveal = false)
        {
            var b = new Button(() => { if (reveal) TocPaths.Reveal(path); else TocPaths.OpenExternal(path); }) { text = text };
            b.SetEnabled(TocPaths.Exists(path));
            b.tooltip = path;
            b.style.height = 24; b.style.marginLeft = 0; b.style.marginRight = 6;
            return b;
        }

        /// <summary>
        /// A doc button only when the doc is there: the exe ships TOC.md and toc_lookup.md, not the project's README and
        /// REFERENCE.md (its Reference tab has the end-user reference instead).
        /// </summary>
        private static void AddDocLink(VisualElement row, string text, string path)
        {
            if (TocPaths.Exists(path)) row.Add(LinkButton(text, path));
        }

        private void BuildViewBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row; bar.style.flexShrink = 0;
            bar.style.paddingLeft = 10; bar.style.paddingBottom = 0;
            bar.style.borderBottomWidth = 1; bar.style.borderBottomColor = new Color(0.07f, 0.07f, 0.08f);
            var names = new[] { "Reference", "Tables", "Folders", "Files", "Changes vs 1.0.4" };
            for (var i = 0; i < names.Length; i++)
            {
                var index = i;
                var b = new Button(() => ShowView(index)) { text = names[i] };
                b.style.height = 26; b.style.fontSize = 12; b.style.paddingLeft = 14; b.style.paddingRight = 14;
                b.style.marginLeft = 0; b.style.marginRight = 3; b.style.marginBottom = 0;
                b.style.borderBottomWidth = 3; b.style.borderTopLeftRadius = 4; b.style.borderTopRightRadius = 4;
                b.style.borderBottomLeftRadius = 0; b.style.borderBottomRightRadius = 0;
                _viewButtons.Add(b); bar.Add(b);
            }
            Add(bar);
        }

        private void ShowView(int index)
        {
            if (index < 0 || index >= _views.Count) return;
            if (index == 3) { EnsureFilesLoaded(); RunFileSearch(_fileSearch != null ? _fileSearch.value : ""); }
            _activeView = index;
            for (var i = 0; i < _views.Count; i++)
            {
                _views[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                var on = i == index;
                _viewButtons[i].style.backgroundColor = on ? new Color(0.33f, 0.34f, 0.40f) : new Color(0.21f, 0.21f, 0.23f);
                _viewButtons[i].style.color = on ? Color.white : new Color(0.74f, 0.74f, 0.78f);
                _viewButtons[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
                _viewButtons[i].style.borderBottomColor = on ? new Color(0.36f, 0.64f, 1f) : new Color(0.21f, 0.21f, 0.23f);
            }
        }

        // ------------------------------------------------------------ shared pieces
        private static VisualElement SplitRow(out VisualElement left, out ScrollView right, float leftWidth = 400)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexGrow = 1;
            left = new VisualElement(); left.style.width = leftWidth; left.style.flexShrink = 0; left.style.flexGrow = 0;
            left.style.borderRightWidth = 1; left.style.borderRightColor = new Color(0.07f, 0.07f, 0.08f);
            right = new ScrollView(); right.style.flexGrow = 1; right.style.paddingLeft = 12; right.style.paddingRight = 12; right.style.paddingTop = 8;
            row.Add(left); row.Add(right);
            return row;
        }

        private static TextField SearchField(string label, EventCallback<ChangeEvent<string>> onChange)
        {
            var f = new TextField(label);
            f.labelElement.style.minWidth = 60;
            f.style.marginLeft = 6; f.style.marginRight = 6; f.style.marginTop = 6; f.style.marginBottom = 4;
            // only the field's own value changes count: a text change on its label (or any child) bubbles here too
            f.RegisterValueChangedCallback(e => { if (e.target == f) onChange(e); });
            return f;
        }

        private static Label CountLabel()
        {
            var l = new Label(""); l.style.fontSize = 11; l.style.color = new Color(0.62f, 0.62f, 0.66f); l.style.paddingLeft = 8; l.style.marginBottom = 4;
            return l;
        }

        private static ListView MakeList(Func<VisualElement> makeItem, Action<VisualElement, int> bindItem, float itemHeight = 22)
        {
            var lv = new ListView(new List<object>(), itemHeight, makeItem, bindItem);
            lv.style.flexGrow = 1;
            lv.selectionType = SelectionType.Single;
            return lv;
        }

        private static Label Cell(int size = 12, bool bold = false)
        {
            var l = new Label();
            l.style.fontSize = size; l.style.paddingLeft = 8; l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis; l.style.whiteSpace = WhiteSpace.NoWrap;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static Label Head(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 12; l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.marginTop = 10; l.style.marginBottom = 2;
            l.style.color = new Color(0.85f, 0.85f, 0.9f);
            return l;
        }

        private static Label Para(string text)
        {
            var l = new Label(text ?? "");
            l.style.whiteSpace = WhiteSpace.Normal; l.style.fontSize = 12; l.style.marginBottom = 2;
            return l;
        }

        /// <summary>A copyable single-line value (read-only text field, no frame).</summary>
        private static TextField Copyable(string text)
        {
            var f = new TextField { value = text ?? "", isReadOnly = true };
            f.style.marginLeft = 0; f.style.marginRight = 0; f.style.marginTop = 1; f.style.marginBottom = 1;
            return f;
        }

        /// <summary>
        /// Reveal / Open buttons for a path inside the export (rel) or an absolute path; disabled when it does not exist,
        /// with a note saying why (no FModel export set up, not in that export, or not on disk).
        /// </summary>
        private static VisualElement PathButtons(string relOrAbs)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            var abs = ResolvePath(relOrAbs, out var inExport);
            var reveal = new Button(() => TocPaths.Reveal(abs)) { text = "Reveal", tooltip = abs };
            var open = new Button(() => TocPaths.OpenExternal(abs)) { text = "Open file", tooltip = abs };
            var exists = TocPaths.Exists(abs);
            reveal.SetEnabled(exists); open.SetEnabled(exists && File.Exists(abs));
            reveal.style.height = 22; open.style.height = 22; reveal.style.marginLeft = 0;
            row.Add(reveal); row.Add(open);
            if (!exists)
            {
                var missing = new Label(MissingNote(abs, inExport)); missing.style.fontSize = 10; missing.style.color = new Color(0.9f, 0.55f, 0.5f);
                missing.style.unityTextAlign = TextAnchor.MiddleLeft; missing.style.whiteSpace = WhiteSpace.Normal; row.Add(missing);
            }
            return row;
        }

        private static string MissingNote(string abs, bool inExport) =>
            !inExport ? "not on disk: " + abs
            : ExportUsable(TocPaths.Exports) ? "not in your FModel export: " + abs
            : "needs an FModel export (optional): set one up at the top of this tab";

        private static string ResolvePath(string p, out bool inExport)
        {
            inExport = false;
            if (string.IsNullOrEmpty(p)) return "";
            var trimmed = p.Trim();
            // strip a trailing " (note)" or a struct/usmap hint that is not a path
            var space = trimmed.IndexOf(' ');
            if (space > 0 && (trimmed.StartsWith("Pal/") || trimmed.StartsWith("Engine/") || trimmed.StartsWith("Docs/"))) trimmed = trimmed.Substring(0, space);
            if (Path.IsPathRooted(trimmed)) return trimmed;
            if (trimmed.StartsWith("Docs/") || trimmed.StartsWith("Assets/") || trimmed.StartsWith("Tools/"))
                return TocPaths.ProjectPath(trimmed);
            inExport = true;
            return TocPaths.ExportAbs(trimmed);
        }

        private static string Bytes(string s)
        {
            if (!long.TryParse(s, out var b)) return s ?? "";
            if (b >= 1_000_000_000) return $"{b / 1e9:0.00} GB";
            if (b >= 1_000_000) return $"{b / 1e6:0.0} MB";
            if (b >= 1_000) return $"{b / 1e3:0.0} KB";
            return b + " B";
        }

        // ------------------------------------------------------------ view 1: Reference
        private VisualElement BuildReferenceView()
        {
            var row = SplitRow(out var left, out var right, 300);
            var sections = _reference?.sections ?? new List<RefSection>();
            var intro = new Label(_reference?.purpose ?? "reference.json not found in Docs/BaseGame.");
            intro.style.whiteSpace = WhiteSpace.Normal; intro.style.fontSize = 11; intro.style.color = new Color(0.72f, 0.72f, 0.76f);
            intro.style.paddingLeft = 8; intro.style.paddingRight = 8; intro.style.paddingTop = 6; intro.style.paddingBottom = 6;
            left.Add(intro);
            _sectionList = MakeList(() => Cell(13), (e, i) => ((Label)e).text = sections[i].title, 26);
            _sectionList.itemsSource = sections;
            _sectionList.selectionChanged += _ =>
            {
                if (_sectionList.selectedIndex >= 0 && _sectionList.selectedIndex < sections.Count) ShowSection(sections[_sectionList.selectedIndex], right);
            };
            left.Add(_sectionList);
            if (sections.Count > 0) { _sectionList.selectedIndex = 0; ShowSection(sections[0], right); }
            return row;
        }

        private void ShowSection(RefSection s, ScrollView pane)
        {
            _redraw[0] = () => ShowSection(s, pane);
            pane.Clear();
            var t = new Label(s.title); t.style.fontSize = 16; t.style.unityFontStyleAndWeight = FontStyle.Bold; pane.Add(t);
            pane.Add(Head("In game")); pane.Add(Para(s.in_game));
            pane.Add(Head("Where (open only these)"));
            foreach (var w in s.where ?? new List<RefWhere>())
            {
                var box = new VisualElement();
                box.style.marginBottom = 6; box.style.paddingLeft = 8; box.style.paddingTop = 4; box.style.paddingBottom = 4;
                box.style.backgroundColor = new Color(0.17f, 0.17f, 0.19f); box.style.borderLeftWidth = 3; box.style.borderLeftColor = new Color(0.36f, 0.64f, 1f);
                var n = new Label(w.name); n.style.unityFontStyleAndWeight = FontStyle.Bold; n.style.fontSize = 12; n.style.whiteSpace = WhiteSpace.Normal; box.Add(n);
                box.Add(Copyable(w.path));
                if (!string.IsNullOrEmpty(w.note)) { var note = Para(w.note); note.style.color = new Color(0.78f, 0.78f, 0.82f); box.Add(note); }
                var looksLikeFile = !string.IsNullOrEmpty(w.path) && (w.path.StartsWith("Pal/") || w.path.StartsWith("Engine/") || w.path.StartsWith("Docs/") || w.path.StartsWith("Assets/") || w.path.StartsWith("Tools/") || Path.IsPathRooted(w.path));
                if (looksLikeFile) box.Add(PathButtons(w.path));
                pane.Add(box);
            }
            pane.Add(Head("How the game reads it")); pane.Add(Para(s.how_read));
            if (!string.IsNullOrEmpty(s.loader_piece)) { pane.Add(Head("Mod piece (PalSchema)")); pane.Add(Para(s.loader_piece)); }
            if (s.tools != null && s.tools.Count > 0) { pane.Add(Head("Tools that touch it")); foreach (var x in s.tools) pane.Add(Para("- " + x)); }
            if (s.issues != null && s.issues.Count > 0)
            {
                pane.Add(Head("Issues noticed"));
                foreach (var i in s.issues)
                {
                    var l = Para($"{i.id}  [{i.status}]  {i.what}");
                    var d = Para("    " + i.doc); d.style.fontSize = 11; d.style.color = new Color(0.62f, 0.62f, 0.66f);
                    pane.Add(l); pane.Add(d);
                }
            }
            if (s.fixes != null && s.fixes.Count > 0)
            {
                pane.Add(Head("Fixes used"));
                foreach (var f in s.fixes)
                {
                    pane.Add(Para("- " + f.what));
                    var d = Para("    " + f.doc); d.style.fontSize = 11; d.style.color = new Color(0.62f, 0.62f, 0.66f); pane.Add(d);
                }
            }
            if (!string.IsNullOrEmpty(s.open)) { pane.Add(Head("Still open")); var o = Para(s.open); o.style.color = new Color(0.98f, 0.8f, 0.5f); pane.Add(o); }
            pane.Add(Head("Confidence")); pane.Add(Para(s.confidence));
            if (s.toc_lookup != null && s.toc_lookup.Count > 0)
            {
                pane.Add(Head("Look it up in the TOC"));
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
                foreach (var name in s.toc_lookup)
                {
                    var n = name;
                    var inTables = _tables.Exists(x => x.Table.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                    var b = new Button(() =>
                    {
                        if (inTables) { _tableSearch.value = n; ShowView(1); }
                        else { _fileSearch.value = n; ShowView(3); }
                    }) { text = (inTables ? "Tables: " : "Files: ") + n };
                    b.style.height = 22; b.style.marginLeft = 0; b.style.marginRight = 4; b.style.marginBottom = 4;
                    row.Add(b);
                }
                pane.Add(row);
            }
        }

        // ------------------------------------------------------------ view 2: Tables
        private List<TableRow> _tableHits = new List<TableRow>();

        private VisualElement BuildTablesView()
        {
            var outer = new VisualElement(); outer.style.flexGrow = 1;
            _tableSearch = SearchField("Search", e => RunTableSearch(e.newValue));
            outer.Add(_tableSearch);
            _tableCount = CountLabel(); outer.Add(_tableCount);
            var row = SplitRow(out var left, out var right, 420);
            _tableList = MakeList(() =>
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                var a = Cell(12, true); a.style.width = 250; a.style.flexShrink = 0; r.Add(a);
                var b = Cell(11); b.style.width = 60; b.style.flexShrink = 0; b.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(b);
                var c = Cell(11); c.style.flexGrow = 1; c.style.color = new Color(0.65f, 0.65f, 0.7f); r.Add(c);
                return r;
            }, (e, i) =>
            {
                var t = _tableHits[i];
                ((Label)e[0]).text = t.Table; ((Label)e[1]).text = t.Rows; ((Label)e[2]).text = t.Struct;
            });
            _tableList.selectionChanged += _ =>
            {
                var i = _tableList.selectedIndex;
                if (i >= 0 && i < _tableHits.Count) ShowTable(_tableHits[i], right);
            };
            left.Add(_tableList);
            outer.Add(row);
            RunTableSearch("");
            return outer;
        }

        private void RunTableSearch(string q)
        {
            q = (q ?? "").Trim();
            _tableHits = new List<TableRow>();
            foreach (var t in _tables)
                if (q.Length == 0 || t.Table.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || t.Rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Struct.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || t.Fields.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    _tableHits.Add(t);
            _tableCount.text = $"{_tableHits.Count} of {_tables.Count} tables";
            _tableList.itemsSource = _tableHits; _tableList.Rebuild();
        }

        private void ShowTable(TableRow t, ScrollView pane)
        {
            _redraw[1] = () => ShowTable(t, pane);
            pane.Clear();
            var title = new Label(t.Table); title.style.fontSize = 16; title.style.unityFontStyleAndWeight = FontStyle.Bold; pane.Add(title);
            pane.Add(Para($"{t.Rows} rows   |   row struct {t.Struct}" + (string.IsNullOrEmpty(t.Parents) ? "" : $"   |   parents {t.Parents}")));
            if (_tableExplained.TryGetValue(t.Rel, out var ex))
            {
                pane.Add(Head("What")); pane.Add(Para(ex[0]));
                if (!string.IsNullOrEmpty(ex[1])) { pane.Add(Head("Why it matters")); pane.Add(Para(ex[1])); }
                if (!string.IsNullOrEmpty(ex[2])) { pane.Add(Head("Row key")); pane.Add(Para(ex[2])); }
                var src = Para("source: " + ex[3] + (string.IsNullOrEmpty(ex[4]) ? "" : "   |   reference: " + ex[4]));
                src.style.fontSize = 11; src.style.color = new Color(0.62f, 0.62f, 0.66f); pane.Add(src);
            }
            pane.Add(Head("File (the one to open)")); pane.Add(Copyable(t.Rel)); pane.Add(PathButtons(t.Rel));
            var fields = (t.Fields ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            pane.Add(Head($"Fields ({fields.Length})"));
            var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexWrap = Wrap.Wrap;
            foreach (var f in fields)
            {
                var chip = new Label(f); chip.style.fontSize = 11; chip.style.paddingLeft = 6; chip.style.paddingRight = 6; chip.style.paddingTop = 2; chip.style.paddingBottom = 2;
                chip.style.marginRight = 4; chip.style.marginBottom = 4; chip.style.backgroundColor = new Color(0.2f, 0.2f, 0.23f); chip.style.borderTopLeftRadius = 3; chip.style.borderTopRightRadius = 3; chip.style.borderBottomLeftRadius = 3; chip.style.borderBottomRightRadius = 3;
                wrap.Add(chip);
            }
            pane.Add(wrap);
            var refs = ReferenceHits(t.Table);
            if (refs.Count > 0)
            {
                pane.Add(Head("Reference sections that name this table"));
                foreach (var s in refs)
                {
                    var sec = s;
                    var b = new Button(() => { ShowView(0); SelectSection(sec); }) { text = sec.title };
                    b.style.height = 22; b.style.marginLeft = 0; b.style.alignSelf = Align.FlexStart; pane.Add(b);
                }
            }
        }

        private List<RefSection> ReferenceHits(string name)
        {
            var hits = new List<RefSection>();
            if (_reference?.sections == null || string.IsNullOrEmpty(name)) return hits;
            foreach (var s in _reference.sections)
            {
                var found = false;
                foreach (var w in s.where ?? new List<RefWhere>())
                    if ((w.name ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || (w.path ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
                if (!found) foreach (var n in s.toc_lookup ?? new List<string>()) if (name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; break; }
                if (found) hits.Add(s);
            }
            return hits;
        }

        private void SelectSection(RefSection s)
        {
            var sections = _reference?.sections; if (sections == null) return;
            var i = sections.IndexOf(s); if (i < 0) return;
            _sectionList.selectedIndex = i;
            _sectionList.ScrollToItem(i);
        }

        // ------------------------------------------------------------ view 3: Folders
        private List<FolderRow> _folderHits = new List<FolderRow>();

        private VisualElement BuildFoldersView()
        {
            var outer = new VisualElement(); outer.style.flexGrow = 1;
            _folderSearch = SearchField("Search", e => RunFolderSearch(e.newValue));
            outer.Add(_folderSearch);
            _folderCount = CountLabel(); outer.Add(_folderCount);
            var row = SplitRow(out var left, out var right, 520);
            _folderList = MakeList(() =>
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                var a = Cell(11); a.style.flexGrow = 1; r.Add(a);
                var b = Cell(11); b.style.width = 60; b.style.flexShrink = 0; b.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(b);
                var c = Cell(11); c.style.width = 80; c.style.flexShrink = 0; c.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(c);
                return r;
            }, (e, i) =>
            {
                var f = _folderHits[i];
                ((Label)e[0]).text = f.Folder; ((Label)e[1]).text = f.Files; ((Label)e[2]).text = Bytes(f.Bytes);
            });
            _folderList.selectionChanged += _ =>
            {
                var i = _folderList.selectedIndex;
                if (i >= 0 && i < _folderHits.Count) ShowFolder(_folderHits[i], right);
            };
            left.Add(_folderList);
            outer.Add(row);
            RunFolderSearch("");
            return outer;
        }

        private void RunFolderSearch(string q)
        {
            q = (q ?? "").Trim();
            _folderHits = new List<FolderRow>();
            foreach (var f in _folders)
                if (q.Length == 0 || f.Folder.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || f.Types.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    _folderHits.Add(f);
            _folderCount.text = $"{_folderHits.Count} of {_folders.Count} folders";
            _folderList.itemsSource = _folderHits; _folderList.Rebuild();
        }

        private void ShowFolder(FolderRow f, ScrollView pane)
        {
            _redraw[2] = () => ShowFolder(f, pane);
            pane.Clear();
            var title = new Label(f.Folder); title.style.fontSize = 14; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.whiteSpace = WhiteSpace.Normal; pane.Add(title);
            pane.Add(Para($"{f.Files} files   |   {Bytes(f.Bytes)}"));
            if (_folderExplained.TryGetValue(f.Folder, out var ex))
            {
                pane.Add(Head("What")); pane.Add(Para(ex[0]));
                if (!string.IsNullOrEmpty(ex[1])) { pane.Add(Head("Why it matters")); pane.Add(Para(ex[1])); }
                var src = Para("source: " + ex[2]); src.style.fontSize = 11; src.style.color = new Color(0.62f, 0.62f, 0.66f); pane.Add(src);
            }
            pane.Add(Head("Types inside")); pane.Add(Para((f.Types ?? "").Replace(";", "   ")));
            pane.Add(Head("Folder on disk")); pane.Add(Copyable(f.Folder)); pane.Add(PathButtons(f.Folder));
            var list = new Button(() => { _fileSearch.value = f.Folder + "/"; ShowView(3); }) { text = "List its files" };
            list.style.height = 22; list.style.marginLeft = 0; list.style.alignSelf = Align.FlexStart; pane.Add(list);
        }

        // ------------------------------------------------------------ view 4: Files
        private List<int> _fileHits = new List<int>();

        private VisualElement BuildFilesView()
        {
            var outer = new VisualElement(); outer.style.flexGrow = 1;
            _fileSearch = SearchField("Search", e => RunFileSearch(e.newValue));
            outer.Add(_fileSearch);
            _fileStatus = new Label("Files load on first use (toc_files.tsv, one line per export file).");
            _fileStatus.style.fontSize = 11; _fileStatus.style.color = new Color(0.62f, 0.62f, 0.66f); _fileStatus.style.paddingLeft = 8; _fileStatus.style.marginBottom = 4;
            outer.Add(_fileStatus);
            var row = SplitRow(out var left, out var right, 560);
            _fileList = MakeList(() =>
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                var a = Cell(11); a.style.flexGrow = 1; r.Add(a);
                var b = Cell(11); b.style.width = 150; b.style.flexShrink = 0; b.style.color = new Color(0.65f, 0.65f, 0.7f); r.Add(b);
                var c = Cell(11); c.style.width = 70; c.style.flexShrink = 0; c.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(c);
                return r;
            }, (e, i) =>
            {
                var cols = _fileLines[_fileHits[i]].Split('\t');
                ((Label)e[0]).text = cols.Length > 0 ? cols[0] : ""; ((Label)e[1]).text = cols.Length > 2 ? cols[2] : ""; ((Label)e[2]).text = cols.Length > 1 ? Bytes(cols[1]) : "";
            });
            _fileList.selectionChanged += _ =>
            {
                var i = _fileList.selectedIndex;
                if (i >= 0 && i < _fileHits.Count) ShowFile(_fileLines[_fileHits[i]].Split('\t'), right);
            };
            left.Add(_fileList);
            outer.Add(row);
            return outer;
        }

        private void RunFileSearch(string q)
        {
            if (_fileLines == null) return;
            q = (q ?? "").Trim();
            _fileHits = new List<int>();
            var total = 0;
            for (var i = 0; i < _fileLines.Length; i++)
            {
                if (q.Length > 0 && _fileLines[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                total++;
                if (_fileHits.Count < MaxListed) _fileHits.Add(i);
            }
            _fileStatus.text = total > MaxListed
                ? $"{total} matches, first {MaxListed} listed. Narrow the search (matches any column: path, type, name, class, fields, note)."
                : $"{total} matches (any column: path, type, name, class, fields, note).";
            _fileList.itemsSource = _fileHits; _fileList.Rebuild();
        }

        private void ShowFile(string[] cols, ScrollView pane)
        {
            _redraw[3] = () => ShowFile(cols, pane);
            pane.Clear();
            var rel = cols.Length > 0 ? cols[0] : "";
            var title = new Label(cols.Length > 3 && cols[3].Length > 0 ? cols[3] : Path.GetFileName(rel));
            title.style.fontSize = 14; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.whiteSpace = WhiteSpace.Normal; pane.Add(title);
            pane.Add(Copyable(rel)); pane.Add(PathButtons(rel));
            for (var c = 1; c < FileCols.Length && c < cols.Length; c++)
            {
                if (string.IsNullOrEmpty(cols[c])) continue;
                pane.Add(Head(FileCols[c]));
                var v = c == 1 ? Bytes(cols[c]) + $"  ({cols[c]} bytes)" : cols[c];
                if (c == 7 || c == 8) v = v.Replace(";", "   ");
                pane.Add(c == 4 ? (VisualElement)Copyable(v) : Para(v));
            }
            var refs = ReferenceHits(cols.Length > 3 ? cols[3] : rel);
            if (refs.Count > 0)
            {
                pane.Add(Head("Reference sections that name this"));
                foreach (var s in refs)
                {
                    var sec = s;
                    var b = new Button(() => { ShowView(0); SelectSection(sec); }) { text = sec.title };
                    b.style.height = 22; b.style.marginLeft = 0; b.style.alignSelf = Align.FlexStart; pane.Add(b);
                }
            }
        }

        // ------------------------------------------------------------ view 5: Changes vs 1.0.4
        private VisualElement BuildChangesView()
        {
            var outer = new VisualElement(); outer.style.flexGrow = 1;
            var note = new Label($"{_changes.Count} files differ from the 1.0.4 export (byte-changed, added, removed). Tables and text: 0 changes. Source: toc/changed_vs_1.0.4.tsv.");
            note.style.fontSize = 11; note.style.color = new Color(0.72f, 0.72f, 0.76f); note.style.paddingLeft = 8; note.style.paddingTop = 6; note.style.whiteSpace = WhiteSpace.Normal;
            outer.Add(note);
            var row = SplitRow(out var left, out var right, 620);
            _changeList = MakeList(() =>
            {
                var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row;
                var a = Cell(11); a.style.flexGrow = 1; r.Add(a);
                var b = Cell(11); b.style.width = 80; b.style.flexShrink = 0; b.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(b);
                var c = Cell(11); c.style.width = 80; c.style.flexShrink = 0; c.style.unityTextAlign = TextAnchor.MiddleRight; r.Add(c);
                return r;
            }, (e, i) =>
            {
                var ch = _changes[i];
                ((Label)e[0]).text = ch.Rel;
                ((Label)e[1]).text = ch.Old.Length == 0 ? "added" : Bytes(ch.Old);
                ((Label)e[2]).text = ch.New.Length == 0 ? "removed" : Bytes(ch.New);
            });
            _changeList.itemsSource = _changes;
            _changeList.selectionChanged += _ =>
            {
                var i = _changeList.selectedIndex;
                if (i < 0 || i >= _changes.Count) return;
                ShowChange(_changes[i], right);
            };
            left.Add(_changeList);
            outer.Add(row);
            return outer;
        }

        private void ShowChange(ChangeRow ch, ScrollView pane)
        {
            _redraw[4] = () => ShowChange(ch, pane);
            pane.Clear();
            var t = new Label(ch.Rel); t.style.fontSize = 13; t.style.unityFontStyleAndWeight = FontStyle.Bold; t.style.whiteSpace = WhiteSpace.Normal; pane.Add(t);
            pane.Add(Para(ch.Old.Length == 0 ? "Added in 1.0.5" : ch.New.Length == 0 ? "Removed in 1.0.5 (case rename or gone)" : $"1.0.4: {ch.Old} bytes   ->   1.0.5: {ch.New} bytes"));
            pane.Add(Copyable(ch.Rel)); pane.Add(PathButtons(ch.Rel));
        }
    }
}
