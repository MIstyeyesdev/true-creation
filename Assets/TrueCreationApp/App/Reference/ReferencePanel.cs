using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueCreation.App
{
    /// <summary>
    /// Reference tab: the browser for the 1.0.4 export - the reference docs, the full asset index (78,596 records),
    /// its DataTables and its Blueprint CDOs - in the editor and in the exe. Replaces the editor-only IMGUI window
    /// (PalworldReferenceWindow) with the same six views. The index and docs are built in (ReferencePaths); "Open" on
    /// an asset needs the user's own FModel export and says so. The Base Game TOC tab covers 1.0.5.
    /// </summary>
    public sealed class ReferencePanel : VisualElement
    {
        private static readonly string[] ViewNames = { "Overview", "Docs", "Assets", "Tables", "Blueprints", "Settings" };
        private static readonly Regex LinkRx = new Regex(@"\[([^\]]+)\]\(([^)#]+?)(#[^)]*)?\)", RegexOptions.Compiled);
        private const int LinesPerPage = 400;

        private static readonly Color Dim = new Color(0.62f, 0.62f, 0.66f);
        private static readonly Color Soft = new Color(0.72f, 0.72f, 0.76f);
        private static readonly Color Bad = new Color(0.95f, 0.5f, 0.5f);
        private static readonly Color Ok = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color Box = new Color(0.17f, 0.17f, 0.19f);

        private readonly List<Button> _viewButtons = new List<Button>();
        private readonly VisualElement[] _views = new VisualElement[6];
        private readonly VisualElement _host;
        private int _active = -1;
        private bool _loading;

        public ReferencePanel()
        {
            style.flexGrow = 1;
            var head = new VisualElement();
            head.style.paddingLeft = 10; head.style.paddingRight = 10; head.style.paddingTop = 8; head.style.paddingBottom = 6; head.style.flexShrink = 0;
            head.Add(Title("Palworld 1.0.4 - Reference", 16));
            head.Add(Small("Game 1.0.4 build 25094871 - mappings Mapping104.usmap - export rebuilt 2026-09-07. The Base Game TOC tab covers 1.0.5.", Soft));
            Add(head);

            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row; bar.style.flexShrink = 0; bar.style.paddingLeft = 10;
            bar.style.borderBottomWidth = 1; bar.style.borderBottomColor = new Color(0.07f, 0.07f, 0.08f);
            for (var i = 0; i < ViewNames.Length; i++)
            {
                var index = i;
                var b = new Button(() => ShowView(index)) { text = ViewNames[i] };
                b.style.height = 26; b.style.fontSize = 12; b.style.paddingLeft = 14; b.style.paddingRight = 14;
                b.style.marginLeft = 0; b.style.marginRight = 3; b.style.marginBottom = 0;
                b.style.borderBottomWidth = 3; b.style.borderTopLeftRadius = 4; b.style.borderTopRightRadius = 4;
                b.style.borderBottomLeftRadius = 0; b.style.borderBottomRightRadius = 0;
                _viewButtons.Add(b); bar.Add(b);
            }
            Add(bar);

            _host = new VisualElement(); _host.style.flexGrow = 1;
            Add(_host);
            ShowView(0);
        }

        private void ShowView(int index, bool rebuild = false)
        {
            if (index < 0 || index >= _views.Length) return;
            _active = index;
            if (rebuild || _views[index] == null)
            {
                _views[index]?.RemoveFromHierarchy();
                _views[index] = index switch
                {
                    0 => BuildOverview(),
                    1 => BuildDocs(),
                    2 => BuildAssets(),
                    3 => BuildFiltered(tables: true),
                    4 => BuildFiltered(tables: false),
                    _ => BuildSettings(),
                };
                _views[index].style.flexGrow = 1;
            }
            _host.Clear();
            _host.Add(_views[index]);
            for (var i = 0; i < _viewButtons.Count; i++)
            {
                var on = i == index;
                _viewButtons[i].style.backgroundColor = on ? new Color(0.33f, 0.34f, 0.40f) : new Color(0.21f, 0.21f, 0.23f);
                _viewButtons[i].style.color = on ? Color.white : new Color(0.74f, 0.74f, 0.78f);
                _viewButtons[i].style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
                _viewButtons[i].style.borderBottomColor = on ? new Color(0.36f, 0.64f, 1f) : new Color(0.21f, 0.21f, 0.23f);
            }
        }

        /// <summary>Rebuilds the index-backed views after the index loads or unloads.</summary>
        private void IndexChanged()
        {
            for (var i = 0; i < _views.Length; i++) { _views[i]?.RemoveFromHierarchy(); _views[i] = null; }
            ShowView(_active < 0 ? 0 : _active);
        }

        // ------------------------------------------------------------------ shared pieces

        private static Label Title(string text, int size)
        {
            var l = new Label(text); l.style.fontSize = size; l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = WhiteSpace.Normal; return l;
        }

        private static Label Small(string text, Color color)
        {
            var l = new Label(text ?? ""); l.style.fontSize = 11; l.style.color = color; l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 2; l.style.marginBottom = 2; return l;
        }

        private static Label Head(string text)
        {
            var l = new Label(text); l.style.fontSize = 13; l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 10; l.style.marginBottom = 4; return l;
        }

        private static Button Btn(string text, Action onClick, float height = 22)
        {
            var b = new Button(onClick) { text = text };
            b.style.height = height; b.style.marginLeft = 0; b.style.marginRight = 4;
            return b;
        }

        private static TextField Copyable(string text)
        {
            var f = new TextField { value = text ?? "", isReadOnly = true };
            f.style.flexGrow = 1; f.style.marginLeft = 0; f.style.marginRight = 4;
            return f;
        }

        private static VisualElement Row()
        {
            var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; r.style.marginBottom = 2;
            return r;
        }

        private static ScrollView Page()
        {
            var s = new ScrollView(); s.style.flexGrow = 1; s.style.paddingLeft = 10; s.style.paddingRight = 10; s.style.paddingTop = 6;
            return s;
        }

        /// <summary>Loads the index on a worker thread; <paramref name="done"/> runs on the UI thread afterwards.</summary>
        private void LoadIndex(bool force, Label status, Action done)
        {
            if (_loading) return;
            _loading = true;
            if (status != null) { status.text = "Reading the index (18.7 MB, 78,596 records)..."; status.style.color = Soft; }
            var path = ReferencePaths.IndexJsonl;
            var task = Task.Run(() => ReferenceIndex.Load(path, force));
            IVisualElementScheduledItem poll = null;
            poll = schedule.Execute(() =>
            {
                if (!task.IsCompleted) return;
                poll.Pause();
                _loading = false;
                if (task.IsFaulted && status != null) { status.text = "Reading the index failed: " + task.Exception?.GetBaseException().Message; status.style.color = Bad; }
                done?.Invoke();
            }).Every(100);
        }

        /// <summary>The "load the index first" block shown by the index-backed views.</summary>
        private VisualElement NeedIndex()
        {
            var page = Page();
            page.Add(Small("The asset index is not loaded. It is 18.7 MB / 78,596 records and takes a few seconds; it loads once per run.", Soft));
            if (!string.IsNullOrEmpty(ReferenceIndex.Error)) page.Add(Small(ReferenceIndex.Error, Bad));
            var status = Small("", Soft);
            page.Add(Btn("Load index", () => LoadIndex(false, status, IndexChanged), 24));
            page.Add(status);
            return page;
        }

        // ------------------------------------------------------------------ Overview

        private VisualElement BuildOverview()
        {
            var page = Page();
            page.Add(Head("Sources"));
            SourceRow(page, "Docs", ReferencePaths.Docs);
            SourceRow(page, "Full index (.jsonl)", ReferencePaths.IndexJsonl);
            SourceRow(page, "FModel export (optional)", ReferencePaths.Exports);
            SourceRow(page, "Field atlas (.json)", ReferencePaths.FieldAtlasJson);
            SourceRow(page, "Field atlas (.md)", ReferencePaths.FieldAtlasMd);
            SourceRow(page, "Blueprint CDO index", ReferencePaths.BlueprintCdo);
            SourceRow(page, "Atlas pipeline", ReferencePaths.AtlasScripts);
            SourceRow(page, "mods.txt", ReferencePaths.ModsTxt);
            SourceRow(page, "UE4SS.log", ReferencePaths.Ue4ssLog);

            page.Add(Head("Index"));
            if (!ReferenceIndex.Loaded)
            {
                page.Add(NeedIndex());
            }
            else
            {
                page.Add(Small($"Loaded {ReferenceIndex.Count:N0} assets.", Ok));
                var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexWrap = Wrap.Wrap;
                var cats = ReferenceIndex.CategoryCounts;
                foreach (var k in cats.Keys.OrderByDescending(k => cats[k]))
                {
                    if (cats[k] < 100) continue;
                    var l = new Label($"{k}  {cats[k]:N0}"); l.style.width = 180; l.style.fontSize = 11; wrap.Add(l);
                }
                page.Add(wrap);
                var buttons = Row();
                var status = Small("", Soft);
                buttons.Add(Btn("Reload", () => LoadIndex(true, status, IndexChanged)));
                buttons.Add(Btn("Unload", () => { ReferenceIndex.Unload(); IndexChanged(); }));
                page.Add(buttons);
                page.Add(status);
            }

            page.Add(Head("Which index answers which question"));
            page.Add(Small("EXPORT_INDEX - what exists, of what type, how big (all asset classes).\n" +
                           "palworld_field_atlas - how fields link to ID domains (DataTables only).\n" +
                           "blueprint_cdo - config that lives in no DataTable.\n" +
                           "pak_index - raw file paths inside the pak.", Soft));
            return page;
        }

        private static void SourceRow(VisualElement parent, string label, string path)
        {
            var ok = TocPaths.Exists(path);
            var row = Row();
            var mark = new Label(ok ? "OK" : "--"); mark.style.width = 26; mark.style.color = ok ? Ok : Dim; row.Add(mark);
            var name = new Label(label); name.style.width = 170; name.style.flexShrink = 0; row.Add(name);
            row.Add(Copyable(path));
            var show = Btn("Show", () => TocPaths.Reveal(path)); show.SetEnabled(ok); row.Add(show);
            parent.Add(row);
        }

        // ------------------------------------------------------------------ Docs

        private VisualElement BuildDocs()
        {
            var split = new VisualElement(); split.style.flexDirection = FlexDirection.Row; split.style.flexGrow = 1;
            var left = new VisualElement(); left.style.width = 240; left.style.flexShrink = 0;
            left.style.borderRightWidth = 1; left.style.borderRightColor = new Color(0.07f, 0.07f, 0.08f);
            var list = new ScrollView(); list.style.flexGrow = 1; list.style.paddingLeft = 6; list.style.paddingTop = 6;
            left.Add(list);
            var right = new VisualElement(); right.style.flexGrow = 1; right.style.paddingLeft = 10; right.style.paddingRight = 10;
            split.Add(left); split.Add(right);

            var dir = ReferencePaths.Docs;
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : new string[0];
            if (files.Length == 0)
            {
                right.Add(Small("No .md files in " + dir + (Application.isEditor ? "" : ". They ship with True Creation: reinstall to restore them."), Bad));
                return split;
            }

            void Open(string file) => ShowDoc(right, file, files);
            foreach (var f in files)
            {
                var file = f;
                var b = Btn(Path.GetFileName(file), () => Open(file));
                b.style.unityTextAlign = TextAnchor.MiddleLeft; b.style.marginBottom = 2;
                list.Add(b);
            }
            right.Add(Small("Select a document.", Soft));
            return split;
        }

        private void ShowDoc(VisualElement pane, string file, string[] all, int page = 0)
        {
            pane.Clear();
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch (Exception e) { pane.Add(Small(e.Message, Bad)); return; }

            var pages = Math.Max(1, (lines.Length + LinesPerPage - 1) / LinesPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            var head = Row(); head.style.marginTop = 6;
            var title = Title(Path.GetFileName(file), 13); title.style.flexGrow = 1; head.Add(title);
            if (pages > 1)
            {
                var prev = Btn("<", () => ShowDoc(pane, file, all, page - 1)); prev.SetEnabled(page > 0); head.Add(prev);
                var at = new Label($"page {page + 1}/{pages}"); at.style.width = 80; at.style.unityTextAlign = TextAnchor.MiddleCenter; head.Add(at);
                var next = Btn(">", () => ShowDoc(pane, file, all, page + 1)); next.SetEnabled(page < pages - 1); head.Add(next);
            }
            head.Add(Btn("Open externally", () => TocPaths.OpenExternal(file)));
            pane.Add(head);

            var body = new ScrollView(); body.style.flexGrow = 1;
            var start = page * LinesPerPage;
            var end = Math.Min(lines.Length, start + LinesPerPage);
            var inCode = false;
            for (var i = start; i < end; i++) MarkdownLine(body, lines[i], ref inCode, pane, all);
            pane.Add(body);
            pane.Add(Small($"{lines.Length:N0} lines - showing {start + 1}-{end}", Dim));
        }

        private void MarkdownLine(VisualElement body, string line, ref bool inCode, VisualElement pane, string[] all)
        {
            if (line.StartsWith("```")) { inCode = !inCode; return; }
            if (inCode || line.StartsWith("|") || line.StartsWith("    "))
            {
                var mono = new Label(line); mono.style.fontSize = 11; mono.style.whiteSpace = WhiteSpace.NoWrap;
                mono.style.backgroundColor = new Color(0.15f, 0.15f, 0.17f); mono.style.paddingLeft = 6;
                body.Add(mono); return;
            }
            if (line.StartsWith("# ")) { body.Add(Title(line.Substring(2), 16)); return; }
            if (line.StartsWith("## ")) { var h = Title(line.Substring(3), 14); h.style.marginTop = 8; body.Add(h); return; }
            if (line.StartsWith("### ")) { var h = Title(line.Substring(4), 12); h.style.marginTop = 4; body.Add(h); return; }
            if (line.StartsWith("---")) { var sp = new VisualElement(); sp.style.height = 6; body.Add(sp); return; }
            if (line.TrimStart().StartsWith("<a id=")) return;

            var matches = LinkRx.Matches(line);
            var text = new Label(matches.Count == 0 ? line : LinkRx.Replace(line, "$1"));
            text.style.whiteSpace = WhiteSpace.Normal; text.style.fontSize = 12;
            if (line.Length == 0) text.style.height = 6;
            body.Add(text);
            if (matches.Count == 0) return;

            var links = Row(); links.style.flexWrap = Wrap.Wrap; links.style.marginLeft = 12;
            foreach (Match m in matches)
            {
                var target = m.Groups[2].Value.Trim();
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                var b = Btn("-> " + target, () =>
                {
                    if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        var hit = all.FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase));
                        if (hit != null) ShowDoc(pane, hit, all);
                        else AppHost.Current.Message("Not in these docs", target);
                    }
                    else TocPaths.OpenExternal(Path.Combine(ReferencePaths.Docs, target));
                });
                b.style.fontSize = 10; links.Add(b);
            }
            body.Add(links);
        }

        // ------------------------------------------------------------------ Assets, Tables, Blueprints

        private static VisualElement MakeAssetRow()
        {
            var r = new VisualElement(); r.style.paddingLeft = 6; r.style.paddingTop = 2; r.style.paddingBottom = 2;
            r.style.borderBottomWidth = 1; r.style.borderBottomColor = new Color(0.12f, 0.12f, 0.13f);
            var top = Row();
            var name = new Label(); name.style.width = 260; name.style.unityFontStyleAndWeight = FontStyle.Bold; name.style.overflow = Overflow.Hidden; top.Add(name);
            var cat = new Label(); cat.style.width = 100; top.Add(cat);
            var size = new Label(); size.style.width = 80; top.Add(size);
            var extra = new Label(); extra.style.width = 100; top.Add(extra);
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; top.Add(spacer);
            var open = new Button { text = "Open" }; open.style.height = 18; top.Add(open);
            var copy = new Button { text = "Copy path" }; copy.style.height = 18; top.Add(copy);
            r.Add(top);
            var rel = new Label(); rel.style.fontSize = 10; rel.style.color = Dim; rel.style.paddingLeft = 12; r.Add(rel);
            return r;
        }

        private static void BindAssetRow(VisualElement e, RefAsset a)
        {
            var top = e[0];
            ((Label)top[0]).text = a.Name;
            ((Label)top[1]).text = a.Cat;
            ((Label)top[2]).text = a.SizeLabel;
            ((Label)top[3]).text = a.Rows >= 0 ? $"{a.Rows:N0} rows" : a.CdoProps > 0 ? $"{a.CdoProps} props" : "";
            var abs = ReferencePaths.ExportAbs(a.Rel);
            var open = (Button)top[5];
            open.clickable = new Clickable(() => TocPaths.OpenExternal(abs));
            var exists = File.Exists(abs);
            open.SetEnabled(exists);
            open.tooltip = exists ? abs : "Needs your own FModel export (set it in Settings here or in the Base Game TOC tab).";
            ((Button)top[6]).clickable = new Clickable(() => GUIUtility.systemCopyBuffer = abs);
            ((Label)e[1]).text = "   " + a.Rel;
        }

        private static ListView AssetList(List<RefAsset> items)
        {
            var lv = new ListView(items, 40, MakeAssetRow, (e, i) => { if (i >= 0 && i < items.Count) BindAssetRow(e, items[i]); });
            lv.style.flexGrow = 1;
            lv.selectionType = SelectionType.None;
            return lv;
        }

        private VisualElement BuildAssets()
        {
            if (!ReferenceIndex.Loaded) return NeedIndex();
            var outer = new VisualElement(); outer.style.flexGrow = 1; outer.style.paddingLeft = 6; outer.style.paddingRight = 6;
            var bar = Row(); bar.style.marginTop = 6;
            var search = new TextField("Search"); search.style.flexGrow = 1; bar.Add(search);
            var cats = ReferenceIndex.Categories();
            var cat = new DropdownField(cats, 0); cat.style.width = 170; bar.Add(cat);
            outer.Add(bar);
            var count = Small("", Soft); outer.Add(count);
            var items = new List<RefAsset>();
            var list = AssetList(items);
            outer.Add(list);
            void Run()
            {
                items.Clear();
                items.AddRange(ReferenceIndex.Search(search.value, cat.value));
                count.text = $"{items.Count:N0} match" + (items.Count == 1 ? "" : "es");
                list.Rebuild();
            }
            search.RegisterValueChangedCallback(e => { if (e.target == search) Run(); });
            cat.RegisterValueChangedCallback(_ => Run());
            Run();
            return outer;
        }

        private VisualElement BuildFiltered(bool tables)
        {
            if (!ReferenceIndex.Loaded) return NeedIndex();
            var source = ReferenceIndex.All.Where(r => tables ? r.Cat == "DataTable" : r.CdoProps > 0).ToList();
            source.Sort((a, b) => tables ? b.Rows.CompareTo(a.Rows) : b.CdoProps.CompareTo(a.CdoProps));
            var outer = new VisualElement(); outer.style.flexGrow = 1; outer.style.paddingLeft = 6; outer.style.paddingRight = 6;
            var filter = new TextField("Filter"); filter.style.marginTop = 6; outer.Add(filter);
            outer.Add(Small(tables
                ? $"{source.Count:N0} DataTables across all roots - the field atlas covers only the 476 in Pal/Content/Pal/DataTable."
                : $"{source.Count:N0} assets carry CDO properties - config that exists in no DataTable. The field atlas has never indexed these.", Soft));
            var items = new List<RefAsset>(source);
            var list = AssetList(items);
            outer.Add(list);
            filter.RegisterValueChangedCallback(e =>
            {
                if (e.target != filter) return;
                var q = (e.newValue ?? "").Trim();
                items.Clear();
                items.AddRange(q.Length == 0 ? source : source.Where(r => r.Rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                list.Rebuild();
            });
            return outer;
        }

        // ------------------------------------------------------------------ Settings

        private VisualElement BuildSettings()
        {
            var page = Page();
            page.Add(Head("Paths"));
            page.Add(Small("The index and the docs are built in. The three below are per user and optional: Palworld Stuff (the atlas files), " +
                           "your own FModel export (Open on an asset; shared with the Base Game TOC tab), and the Palworld install (mods.txt, UE4SS.log).", Soft));

            page.Add(PathSetting("Palworld Stuff", ReferencePaths.PalworldStuff, ReferencePaths.SetPalworldStuff,
                () => AppHost.Current.OpenFolderPanel("Palworld Stuff", ReferencePaths.PalworldStuff)));
            page.Add(PathSetting("FModel export", ReferencePaths.Exports, v => TocPaths.Exports = v, TocPaths.PickExports));
            page.Add(PathSetting("Game root", ReferencePaths.GameRoot, v => ReferencePaths.GameRoot = v,
                () => AppHost.Current.OpenFolderPanel("Palworld install", ReferencePaths.GameRoot)));

            var reset = Btn("Reset to defaults", () => { ReferencePaths.ResetAll(); ShowView(5, rebuild: true); }, 24);
            reset.style.alignSelf = Align.FlexStart; reset.style.marginTop = 8;
            page.Add(reset);

            page.Add(Head("Regenerating the index (developers)"));
            page.Add(Copyable("python build_full_index.py \"<Exports>\" \"<outdir>\"   then   python build_index_md.py \"<outdir>\\_index_summary.json\" \"<Docs>\\EXPORT_INDEX_1.0.4.md\""));
            var reveal = Btn("Reveal atlas_build_scripts", () => TocPaths.Reveal(ReferencePaths.AtlasScripts));
            reveal.style.alignSelf = Align.FlexStart; reveal.style.marginTop = 4;
            page.Add(reveal);
            return page;
        }

        private VisualElement PathSetting(string label, string value, Action<string> save, Func<string> pick)
        {
            var row = Row();
            var name = new Label(label); name.style.width = 120; name.style.flexShrink = 0; row.Add(name);
            var field = new TextField { value = value ?? "" }; field.style.flexGrow = 1; row.Add(field);
            field.RegisterCallback<FocusOutEvent>(_ => save(field.value));
            row.Add(Btn("...", () =>
            {
                var picked = pick();
                if (string.IsNullOrEmpty(picked)) return;
                save(picked);
                field.SetValueWithoutNotify(picked);
            }));
            return row;
        }
    }
}
