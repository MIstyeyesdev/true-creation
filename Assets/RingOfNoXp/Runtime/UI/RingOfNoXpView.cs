using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RingOfNoXp.Export;
using RingOfNoXp.Lookup;
using RingOfNoXp.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace RingOfNoXp.UI
{
    /// <summary>
    /// The one screen. Package settings across the top, then two tabs over the
    /// same project - the drinkable and the worn ring - then validation and the
    /// Generate button. Plain VisualElement tree so the editor window and the
    /// runtime app host the same thing.
    /// </summary>
    public sealed class RingOfNoXpView : VisualElement
    {
        private const float LabelWidth = 130f;

        private readonly GameData _data;
        private readonly Action<string> _log;
        private RingProject _project;

        private ConsumableView _consumable;
        private EquipmentView _equipment;
        private XpRatesView _rates;
        private Label _status;
        private VisualElement _problems;
        private VisualElement _problemStrip;

        /// <summary>Host-supplied dialogs; the runtime app leaves them null and saves to the default folder.</summary>
        public Func<string, string, string, string, string> SaveFileDialog;
        public Func<string, string, string, string> OpenFileDialog;
        public Func<string, string, string> OpenFolderDialog;
        public Action<string> RevealFolder;

        /// <summary>Host-supplied yes/no prompt (title, message). Uninstall deletes folders, so it always asks.</summary>
        public Func<string, string, bool> Confirm;

        public RingOfNoXpView(GameData data, Action<string> log = null)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _log = log ?? Debug.Log;
            _project = LoadLastOrDefault();
            if (string.IsNullOrEmpty(_project.OutputFolder)) _project.OutputFolder = DataPaths.DefaultOutputFolder;
            if (string.IsNullOrEmpty(_project.GameFolder)) _project.GameFolder = GameInstaller.GuessGameFolder(_project.OutputFolder);

            style.flexGrow = 1;
            style.backgroundColor = UiKit.Background;
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

            scroll.Add(Header());

            if (!_data.IsLoaded)
                scroll.Add(DataMissingNotice());

            scroll.Add(PackageSection());

            var tabs = new UiKit.Tabs();
            _consumable = new ConsumableView(_data, () => _project, Revalidate);
            _equipment = new EquipmentView(_data, () => _project, Revalidate);
            _rates = new XpRatesView(_data, () => _project);
            tabs.AddPage("Consumable  (potion / food)", _consumable);
            tabs.AddPage("Equipment  (ring / accessory)", _equipment);
            tabs.AddPage("XP rates", _rates);
            tabs.OnSelected += i =>
            {
                if (i == 0) _consumable.Refresh();
                if (i == 1) _equipment.Refresh();
                // The rates tab reads the consumable's current value, so it is
                // rebuilt on entry rather than left showing a stale multiplier.
                if (i == 2) _rates.Refresh();
                Revalidate();
            };
            scroll.Add(tabs);

            Add(ActionBar());
            Revalidate();
        }

        private VisualElement Header()
        {
            var wrap = new VisualElement();
            wrap.style.marginBottom = 10;

            var title = new Label("Ring of No XP");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = UiKit.Text;
            wrap.Add(title);

            var sub = new Label("Two ways to stop XP, built from the shipped tables. The drinkable reaches player XP with no Lua; the ring reaches Pal XP with no Lua and needs help for the rest.");
            sub.style.color = UiKit.Muted;
            sub.style.fontSize = 11;
            sub.style.whiteSpace = WhiteSpace.Normal;
            wrap.Add(sub);
            return wrap;
        }

        private VisualElement DataMissingNotice()
        {
            var section = UiKit.Section("Lookup data not found", out var body,
                $"Expected items.json and enums.json in {DataPaths.LookupDirectory}");
            body.Add(UiKit.Note("The tool still works - every field is free text - but the item pickers and the Ring of Mercy reference will be empty.", UiKit.Warning));
            foreach (var error in _data.LoadErrors) body.Add(UiKit.Note(error, UiKit.Danger));
            return section;
        }

        private VisualElement PackageSection()
        {
            var section = UiKit.Section("Package", out var body, "What the mod folder is called and where it is written.");

            var left = UiKit.Column();
            left.Add(TextRow("Mod name", _project.ModName, v => _project.ModName = v, "shown in the mod list"));
            left.Add(TextRow("Package name", _project.PackageName, v => _project.PackageName = v, "folder name; letters and digits only"));
            left.Add(TextRow("Author", _project.Author, v => _project.Author = v, null));

            var right = UiKit.Column();
            right.Add(TextRow("Version", _project.Version, v => _project.Version = v, "bump it or the update is not recognised"));

            var outRow = new VisualElement();
            outRow.style.flexDirection = FlexDirection.Row;
            outRow.style.alignItems = Align.Center;
            var outField = new TextField { value = _project.OutputFolder ?? "" };
            outField.style.flexGrow = 1;
            outField.RegisterValueChangedCallback(evt => { _project.OutputFolder = evt.newValue; Revalidate(); });
            outRow.Add(outField);
            outRow.Add(UiKit.SmallButton("browse", () =>
            {
                var picked = OpenFolderDialog?.Invoke("Output folder", _project.OutputFolder);
                if (!string.IsNullOrEmpty(picked)) { _project.OutputFolder = picked; Rebuild(); }
            }));
            right.Add(UiKit.Row("Output folder", outRow, null, LabelWidth));

            var server = new Toggle { value = _project.AlsoDedicatedServer };
            server.RegisterValueChangedCallback(evt => { evt.StopPropagation(); _project.AlsoDedicatedServer = evt.newValue; Revalidate(); });
            right.Add(UiKit.Row("Also for server", server, "duplicates each InstallRule with IsServer", LabelWidth, 30));

            body.Add(UiKit.Columns(left, right));
            body.Add(GameFolderRow());
            return section;
        }

        /// <summary>Where Install and Uninstall act, plus what is in there right now.</summary>
        private VisualElement GameFolderRow()
        {
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;

            var field = new TextField { value = _project.GameFolder ?? "" };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt => { _project.GameFolder = evt.newValue; RefreshInstallState(); });
            line.Add(field);

            line.Add(UiKit.SmallButton("browse", () =>
            {
                var picked = OpenFolderDialog?.Invoke("Palworld folder", _project.GameFolder);
                if (!string.IsNullOrEmpty(picked)) { _project.GameFolder = picked; Rebuild(); }
            }));
            line.Add(UiKit.SmallButton("detect", () =>
            {
                var found = GameInstaller.GuessGameFolder(_project.OutputFolder);
                if (string.IsNullOrEmpty(found)) SetStatus("Could not find a Palworld install; browse to it.", UiKit.Warning);
                else { _project.GameFolder = found; Rebuild(); SetStatus("Found " + found, UiKit.Success); }
            }));

            var wrapper = UiKit.Row("Game folder", line, "the folder holding Pal and Mods", LabelWidth);
            _installState = UiKit.Note("", UiKit.Muted);
            _installState.style.marginLeft = LabelWidth;
            var block = new VisualElement();
            block.Add(wrapper);
            block.Add(_installState);
            RefreshInstallState();
            return block;
        }

        private Label _installState;

        private void RefreshInstallState()
        {
            if (_installState == null) return;
            var problem = GameInstaller.Problem(_project.GameFolder);
            if (problem != null)
            {
                _installState.text = problem;
                _installState.style.color = UiKit.Warning;
                return;
            }
            var state = GameInstaller.InstalledState(_project.PackageName, _project.GameFolder);
            _installState.text = "In game: " + state;
            _installState.style.color = state.StartsWith("not installed") ? UiKit.Muted : UiKit.Success;
        }

        private VisualElement ActionBar()
        {
            var bar = new VisualElement();
            bar.style.borderTopWidth = 1;
            bar.style.borderTopColor = UiKit.Border;
            bar.style.paddingLeft = 12;
            bar.style.paddingRight = 12;
            bar.style.paddingTop = 8;
            bar.style.paddingBottom = 8;
            bar.style.backgroundColor = UiKit.Panel;
            // Without this the bar is squeezed by the ScrollView above it and the
            // problem text ends up drawn underneath the buttons.
            bar.style.flexShrink = 0;

            // Problems scroll in their own strip: a long list must never push the
            // buttons off the bottom of the window.
            var problemScroll = new ScrollView();
            problemScroll.style.maxHeight = 96;
            problemScroll.style.flexShrink = 0;
            problemScroll.style.marginBottom = 6;
            _problems = problemScroll.contentContainer;
            _problemStrip = problemScroll;
            bar.Add(problemScroll);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            row.style.flexWrap = Wrap.Wrap;
            row.Add(UiKit.PrimaryButton("Generate mod", Generate));
            row.Add(UiKit.PrimaryButton("Install to game", InstallToGame));
            row.Add(UiKit.SecondaryButton("Uninstall from game", UninstallFromGame));
            row.Add(UiKit.SecondaryButton("Save project", () => SaveProject(true)));
            row.Add(UiKit.SecondaryButton("Open project", OpenProject));
            row.Add(UiKit.SecondaryButton("Open output", () =>
            {
                var folder = Path.Combine(_project.OutputFolder ?? DataPaths.DefaultOutputFolder, _project.PackageName ?? "");
                if (Directory.Exists(folder)) RevealFolder?.Invoke(folder);
                else SetStatus("Nothing generated yet.", UiKit.Muted);
            }));

            _status = new Label("");
            _status.style.marginLeft = 8;
            _status.style.fontSize = 11;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.flexShrink = 1;
            row.Add(_status);

            bar.Add(row);
            return bar;
        }

        // ------------------------------------------------------------- validation

        /// <summary>Everything that would make the generated package wrong, listed before it is written.</summary>
        private List<string> Problems()
        {
            var p = new List<string>();
            if (string.IsNullOrWhiteSpace(_project.PackageName) || !Regex.IsMatch(_project.PackageName, "^[A-Za-z0-9]+$"))
                p.Add("Package name must be letters and digits only - the installer uses it as a folder name.");
            if (!_project.Consumable.Enabled && !_project.Equip.Enabled)
                p.Add("Both tabs are switched off, so the package would contain no items.");

            if (_project.Consumable.Enabled)
            {
                CheckItemId(p, _project.Consumable.Item.Id, "Consumable");
                if (_project.Consumable.Effect1Type == "None")
                    p.Add("Consumable: effect 1 is None, so the drink does nothing.");
                if (_project.Consumable.EffectTime <= 0)
                    p.Add("Consumable: duration must be greater than zero.");
                // Learned the hard way: a Consume/ConsumeOther item is usable but the
                // buff never fires. All 54 shipped DT_StatusEffectFood items are Food.
                if (!string.Equals(_project.Consumable.Item.TypeA, "Food", StringComparison.OrdinalIgnoreCase))
                    p.Add($"Consumable: TypeA is '{_project.Consumable.Item.TypeA}', not Food. The status effect is applied by the eating path - every shipped item with a DT_StatusEffectFood row is TypeA Food, so the buff will not fire.");
                if (_project.Consumable.RestoreSatiety <= 0)
                    p.Add("Consumable: restore satiety is 0. Give it at least 1 (PoisonMushroom restores 1) or the game has no reason to let you eat it.");
            }

            if (_project.Equip.Enabled)
            {
                CheckItemId(p, _project.Equip.Item.Id, "Equipment");
                if (string.IsNullOrWhiteSpace(_project.Equip.PassiveId))
                    p.Add("Equipment: the passive row id is empty, so PassiveSkillName would point at nothing.");
            }

            if (_project.Consumable.Enabled && _project.Equip.Enabled &&
                _project.Consumable.Item.Id == _project.Equip.Item.Id)
                p.Add("Both items share an id; the second would overwrite the first.");

            return p;
        }

        private static void CheckItemId(List<string> problems, string id, string which)
        {
            if (string.IsNullOrWhiteSpace(id)) problems.Add($"{which}: item id is empty.");
            else if (!Regex.IsMatch(id, "^[A-Za-z0-9_]+$")) problems.Add($"{which}: item id may only use letters, digits and underscore.");
        }

        private void Revalidate()
        {
            // The package name is both a validation subject and the install folder name.
            RefreshInstallState();
            if (_problems == null) return;
            _problems.Clear();
            var problems = Problems();

            // The strip only exists while there is something in it, so a clean
            // project gets the whole window.
            if (_problemStrip != null) _problemStrip.style.display = problems.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            foreach (var problem in problems)
            {
                var line = new Label("• " + problem);
                line.style.color = UiKit.Warning;
                line.style.fontSize = 11;
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.marginBottom = 3;
                line.style.flexShrink = 0;
                _problems.Add(line);
            }
        }

        // ------------------------------------------------------------- actions

        private void Generate()
        {
            var problems = Problems();
            if (problems.Count > 0)
            {
                Revalidate();
                SetStatus($"{problems.Count} problem(s) to fix first.", UiKit.Danger);
                return;
            }

            try
            {
                var files = PalSchemaWriter.Write(_project, out var folder, _data);
                SaveProject(false);
                SetStatus($"Wrote {files.Count} file(s) to {folder}", UiKit.Success);
                _log($"[RingOfNoXp] {string.Join(", ", files)}");
            }
            catch (Exception e)
            {
                SetStatus("Generate failed: " + e.Message, UiKit.Danger);
                _log("[RingOfNoXp] " + e);
            }
        }

        /// <summary>
        /// Generates fresh and copies straight into the game, so what is installed
        /// always matches what is on screen. Refuses rather than half-installing.
        /// </summary>
        private void InstallToGame()
        {
            var problems = Problems();
            if (problems.Count > 0)
            {
                Revalidate();
                SetStatus($"{problems.Count} problem(s) to fix first.", UiKit.Danger);
                return;
            }
            var blocked = GameInstaller.Problem(_project.GameFolder);
            if (blocked != null) { SetStatus(blocked, UiKit.Danger); return; }

            try
            {
                var files = PalSchemaWriter.Write(_project, out var packageRoot, _data);
                var written = GameInstaller.Install(_project, packageRoot, _project.GameFolder);
                SaveProject(false);
                RefreshInstallState();
                SetStatus($"Installed {files.Count} file(s) to {written.Count} location(s). Restart the game to load it.", UiKit.Success);
                foreach (var w in written) _log("[RingOfNoXp] installed -> " + w);
            }
            catch (Exception e)
            {
                SetStatus("Install failed: " + e.Message, UiKit.Danger);
                _log("[RingOfNoXp] " + e);
            }
        }

        /// <summary>Removes it from the game. Always asks first - this deletes folders.</summary>
        private void UninstallFromGame()
        {
            var pkg = GameInstaller.SafeName(_project.PackageName);
            var state = GameInstaller.InstalledState(_project.PackageName, _project.GameFolder);
            if (state == "not installed") { SetStatus($"'{pkg}' is not installed in that game folder.", UiKit.Muted); return; }

            // Never delete without a real yes: the runtime app host wires no prompt, so it
            // cannot uninstall (the 2026-09-15 audit found it deleted folders silently).
            if (Confirm == null)
            {
                SetStatus("Uninstall needs a yes/no prompt and this host has none. Use the editor window (Tools > Ring of No XP) to uninstall.", UiKit.Warning);
                return;
            }
            var ok = Confirm("Uninstall from game",
                $"Remove '{pkg}' from:\n{_project.GameFolder}\n\nThis deletes its PalSchema and Scripts folders and its mods.txt line. " +
                "Only folders this tool installed are touched.\n\n" +
                "SAVE HAZARD: if any world still holds these items (in an inventory, a chest or on a Pal), the client PalSchema cannot clean " +
                "invalid items up once the mod is gone and that world can crash on load. Use up or drop every " +
                $"'{_project.Consumable.Item.Id}' and '{_project.Equip.Item.Id}' in game first, or keep the mod installed.\n\n" +
                "Your generated package and project file are not affected.");
            if (!ok) { SetStatus("Uninstall cancelled.", UiKit.Muted); return; }

            try
            {
                var removed = GameInstaller.Uninstall(_project.PackageName, _project.GameFolder);
                RefreshInstallState();
                if (removed.Count == 0)
                    SetStatus($"Nothing removed: no folder for '{pkg}' carries this tool's install manifest.", UiKit.Warning);
                else
                {
                    SetStatus($"Removed {removed.Count} item(s). Restart the game to unload it.", UiKit.Success);
                    foreach (var r in removed) _log("[RingOfNoXp] removed -> " + r);
                }
            }
            catch (Exception e)
            {
                SetStatus("Uninstall failed: " + e.Message, UiKit.Danger);
                _log("[RingOfNoXp] " + e);
            }
        }

        private void SaveProject(bool prompt)
        {
            try
            {
                var path = _project.ProjectPath;
                if (prompt || string.IsNullOrEmpty(path))
                {
                    var folder = DataPaths.ProjectFolderFor(_project.PackageName);
                    Directory.CreateDirectory(folder);
                    var suggested = _project.PackageName + "." + DataPaths.ProjectExtension;
                    path = SaveFileDialog?.Invoke("Save project", folder, suggested, "json")
                           ?? Path.Combine(folder, suggested);
                    if (string.IsNullOrEmpty(path)) return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                _project.ProjectPath = path;
                File.WriteAllText(path, JsonUtility.ToJson(_project, true));
                File.WriteAllText(DataPaths.LastProjectPointer, path);
                SetStatus("Saved " + path, UiKit.Success);
            }
            catch (Exception e)
            {
                SetStatus("Save failed: " + e.Message, UiKit.Danger);
            }
        }

        private void OpenProject()
        {
            var path = OpenFileDialog?.Invoke("Open project", DataPaths.DefaultProjectFolder, "json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var loaded = JsonUtility.FromJson<RingProject>(File.ReadAllText(path));
                if (loaded == null) { SetStatus("That file is not a project.", UiKit.Danger); return; }
                loaded.ProjectPath = path;
                loaded.Consumable ??= new ConsumableSpec();
                loaded.Equip ??= new EquipSpec();
                loaded.Consumable.Recipe.Normalise();
                loaded.Equip.Recipe.Normalise();
                _project = loaded;
                File.WriteAllText(DataPaths.LastProjectPointer, path);
                Rebuild();
                SetStatus("Opened " + path, UiKit.Success);
            }
            catch (Exception e)
            {
                SetStatus("Open failed: " + e.Message, UiKit.Danger);
            }
        }

        private static RingProject LoadLastOrDefault()
        {
            try
            {
                var pointer = DataPaths.LastProjectPointer;
                if (File.Exists(pointer))
                {
                    var path = File.ReadAllText(pointer).Trim();
                    if (File.Exists(path))
                    {
                        var loaded = JsonUtility.FromJson<RingProject>(File.ReadAllText(path));
                        if (loaded != null)
                        {
                            loaded.ProjectPath = path;
                            loaded.Consumable ??= new ConsumableSpec();
                            loaded.Equip ??= new EquipSpec();
                            loaded.Consumable.Recipe.Normalise();
                            loaded.Equip.Recipe.Normalise();
                            return loaded;
                        }
                    }
                }
            }
            catch
            {
                // A bad pointer must never stop the window opening.
            }
            return new RingProject();
        }

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;
            _status.text = message;
            _status.style.color = color;
        }

        // ------------------------------------------------------------- row helper

        private VisualElement TextRow(string label, string value, Action<string> set, string hint)
        {
            var f = new TextField { value = value ?? "" };
            f.RegisterValueChangedCallback(evt => { set(evt.newValue); Revalidate(); });
            return UiKit.Row(label, f, hint, LabelWidth);
        }
    }
}
