using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PalPanel.Io;
using PalPanel.Model;
using PalPanel.Schema;
using PalPanel.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalPanel.EditorUI
{
    /// <summary>
    /// Palworld dedicated-server control panel.
    /// Menu: Palworld / Server Control Panel
    /// </summary>
    public sealed class PalPanelWindow : EditorWindow
    {
        private const string InstallPrefKey = "PalPanel.InstallPath";
        private const string OutputPrefKey = "PalPanel.OutputRoot";
        private const string WorkshopPrefKey = "PalPanel.WorkshopRoot";
        private const string DefaultInstall =
            @"E:\SteamLibrary\steamapps\common\PalServer";

        private OptionSchema _schema;
        private ConfigService _config;
        private ServerInstall _install;
        private List<ServerWorld> _worlds = new();

        private string _search = "";
        private string _category = "(all)";
        private string _filter = "All";
        private bool _revealSecrets;

        private ScrollView _list;
        private PopupField<string> _categoryDrop;
        private Label _status;
        private Label _pendingLabel;
        private Button _saveBtn;
        private Button _seedBtn;

        // vanilla baseline mods (UE4SS + PalSchema only)
        private ModSettingsService _mods;
        private TextField _workshopField;
        private Label _modsStatus;
        private Button _modsOnBtn;
        private Button _modsOffBtn;

        private static readonly string[] Filters =
        {
            "All",
            "Modified from default",
            "No in-game slider",     // tier != ui_slider_clamped
            "Clamped by the game",   // hasStaticClamp
            "Missing from this file",
        };

        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Open()
        {
            var w = GetWindow<PalPanelWindow>();
            w.titleContent = new GUIContent("Palworld Server");
            w.minSize = new Vector2(880, 520);
            w.Show();
        }

        private void CreateGUI() => BuildInto(rootVisualElement);

        /// <summary>
        /// Builds the whole panel into <paramref name="root"/>: the window's own root, or a tab
        /// container in the True Engine window. Header rows and the footer never shrink; only the
        /// option list flexes, so rows cannot collapse onto each other when the host is short
        /// (2026-09-16: the header rows overlapped into an unreadable band).
        /// </summary>
        public void BuildInto(VisualElement root)
        {
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;

            // ---- install row -------------------------------------------------
            var installRow = Row();
            var pathField = new TextField("Server install")
            {
                value = EditorPrefs.GetString(InstallPrefKey, DefaultInstall),
            };
            pathField.style.flexGrow = 1;
            installRow.Add(pathField);

            installRow.Add(new Button(() =>
            {
                var picked = EditorUtility.OpenFolderPanel(
                    "Select PalServer folder", pathField.value, "");
                if (!string.IsNullOrEmpty(picked)) pathField.value = picked.Replace('/', '\\');
            })
            { text = "Browse" });

            installRow.Add(new Button(() => LoadInstall(pathField.value)) { text = "Load" });

            // A clean server's ini is empty (F-0010): this writes the shipped defaults into it.
            _seedBtn = new Button(SeedDefaults)
            {
                text = "Seed defaults",
                tooltip = "Clean server: copy DefaultPalWorldSettings.ini's settings into " +
                          "PalWorldSettings.ini so they can be edited (backup first).",
            };
            _seedBtn.SetEnabled(false);
            installRow.Add(_seedBtn);
            root.Add(Fixed(installRow));

            // ---- output row --------------------------------------------------
            // Backups and as-written snapshots land here, deliberately outside both the
            // Unity project and the game install.
            var outputRow = Row();
            var outField = new TextField("Saves / backups")
            {
                value = EditorPrefs.GetString(OutputPrefKey, PanelPaths.DefaultOutputRoot),
            };
            outField.style.flexGrow = 1;
            PanelPaths.OutputRoot = outField.value;
            outField.RegisterValueChangedCallback(e =>
            {
                PanelPaths.OutputRoot = e.newValue;
                EditorPrefs.SetString(OutputPrefKey, e.newValue);
            });
            outputRow.Add(outField);

            outputRow.Add(new Button(() =>
            {
                var picked = EditorUtility.OpenFolderPanel(
                    "Select saves / backups folder", outField.value, "");
                if (!string.IsNullOrEmpty(picked)) outField.value = picked.Replace('/', '\\');
            })
            { text = "Browse" });

            outputRow.Add(new Button(() =>
            {
                PanelPaths.EnsureDirs();
                EditorUtility.RevealInFinder(PanelPaths.BackupsDir);
            })
            { text = "Open" });

            root.Add(Fixed(outputRow));

            // ---- mods row: vanilla baseline = the game + UE4SS + PalSchema -------------
            var modsRow = Row();
            _workshopField = new TextField("Workshop folder")
            {
                value = EditorPrefs.GetString(WorkshopPrefKey,
                    Directory.Exists(ModSettingsService.DefaultWorkshopRoot)
                        ? ModSettingsService.DefaultWorkshopRoot
                        : string.Empty),
                tooltip = "Folder holding the Workshop item folders (WorkshopRootDir). " +
                          "The client's PalModSettings.ini uses Steam's workshop\\content\\1623730.",
            };
            _workshopField.style.flexGrow = 1;
            _workshopField.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetString(WorkshopPrefKey, e.newValue);
                RefreshModsStatus();
            });
            modsRow.Add(_workshopField);

            _modsOnBtn = new Button(() => WriteModBaseline(true))
            {
                text = "Enable UE4SS + PalSchema",
                tooltip = "Write PalModSettings.ini: mods on, this Workshop folder, " +
                          "ActiveModList = UE4SSExperimentalPW, PalSchema (nothing else).",
            };
            _modsOffBtn = new Button(() => WriteModBaseline(false))
            {
                text = "Mods off",
                tooltip = "Write PalModSettings.ini: mods off, empty ActiveModList.",
            };
            modsRow.Add(_modsOnBtn);
            modsRow.Add(_modsOffBtn);
            root.Add(Fixed(modsRow));

            _modsStatus = new Label("Mods: no install loaded.");
            _modsStatus.style.whiteSpace = WhiteSpace.Normal;
            _modsStatus.style.marginBottom = 2;
            root.Add(Fixed(_modsStatus));
            SetModButtons(false);

            _status = new Label("No install loaded.");
            _status.style.marginTop = 2;
            _status.style.marginBottom = 4;
            _status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(Fixed(_status));

            // ---- filter row --------------------------------------------------
            var filterRow = Row();

            var searchField = new TextField("Search") { value = _search };
            searchField.style.flexGrow = 1;
            searchField.RegisterValueChangedCallback(e => { _search = e.newValue; Rebuild(); });
            filterRow.Add(searchField);

            var catDrop = new PopupField<string>("Category", new List<string> { "(all)" }, 0);
            catDrop.RegisterValueChangedCallback(e => { _category = e.newValue; Rebuild(); });
            catDrop.name = "category";
            _categoryDrop = catDrop;
            filterRow.Add(catDrop);

            var filterDrop = new PopupField<string>("Show", Filters.ToList(), 0);
            filterDrop.RegisterValueChangedCallback(e => { _filter = e.newValue; Rebuild(); });
            filterRow.Add(filterDrop);

            root.Add(Fixed(filterRow));

            var revealToggle = new Toggle("Reveal passwords / IP") { value = false };
            revealToggle.RegisterValueChangedCallback(e =>
            {
                _revealSecrets = e.newValue;
                Rebuild();
            });
            root.Add(Fixed(revealToggle));

            // ---- option list -------------------------------------------------
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            // the list takes ONLY the remaining height: without these two the scroll view asks for
            // its full content height and squeezes every sibling to nothing
            _list.style.flexBasis = 0;
            _list.style.minHeight = 0;
            _list.style.flexShrink = 1;
            _list.style.marginTop = 6;
            _list.style.borderTopWidth = 1;
            _list.style.borderTopColor = new Color(0, 0, 0, 0.25f);
            root.Add(_list);

            // ---- footer ------------------------------------------------------
            var footer = Row();
            _pendingLabel = new Label("0 pending");
            _pendingLabel.style.flexGrow = 1;
            _pendingLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            footer.Add(_pendingLabel);

            footer.Add(new Button(() => { _config?.Revert(); Rebuild(); }) { text = "Revert" });

            _saveBtn = new Button(SaveConfig) { text = "Save to server" };
            footer.Add(_saveBtn);
            root.Add(Fixed(footer));

            TryLoadSchema();
            if (Directory.Exists(pathField.value)) LoadInstall(pathField.value);
        }

        private static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            r.style.marginBottom = 2;
            return r;
        }

        /// <summary>A header or footer element keeps its natural height whatever the host size.</summary>
        private static VisualElement Fixed(VisualElement e)
        {
            e.style.flexShrink = 0;
            return e;
        }

        // ---------------------------------------------------------------- loading

        private void TryLoadSchema()
        {
            try
            {
                _schema = OptionSchema.Load();
            }
            catch (Exception e)
            {
                _schema = null;
                _status.text = "Schema not loaded: " + e.Message;
            }
        }

        private void LoadInstall(string path)
        {
            if (_schema == null) { TryLoadSchema(); if (_schema == null) return; }

            _install = new ServerInstall
            {
                Id = "primary",
                DisplayName = "PalServer",
                InstallPath = path,
            };

            if (!_install.IsValid)
            {
                _status.text = $"PalServer.exe not found under {path}";
                _seedBtn?.SetEnabled(false);
                RefreshModsStatus();
                return;
            }

            EditorPrefs.SetString(InstallPrefKey, path);

            try
            {
                _config = new ConfigService(_schema);
                _config.Load(_install);
                _worlds = WorldRepository.Discover(_install);

                var worldLine = _worlds.Count == 0
                    ? "no world yet (the server creates it on first start)"
                    : string.Join(", ", _worlds.Select(w =>
                        $"{w.Guid.Substring(0, 8)}… ({w.PlayerCount} players)"));

                if (_config.NeedsSeed)
                {
                    _status.text =
                        "PalWorldSettings.ini has no settings yet (clean server: it runs on the " +
                        "built-in defaults). Press \"Seed defaults\" to write " +
                        "DefaultPalWorldSettings.ini's values into it, then edit.  |  " +
                        $"worlds: {worldLine}";
                }
                else
                {
                    var serverName = PalIniDocument.ParseString(_config.GetRaw("ServerName"));
                    var missing = _config.MissingFromFile().Count();
                    _status.text =
                        $"\"{serverName}\"  |  {_schema.Options.Count} options  |  " +
                        $"{_config.ModifiedFromDefault().Count()} changed from default  |  " +
                        $"{missing} missing from file  |  worlds: {worldLine}";
                }

                // refresh category dropdown now that the schema is known
                if (_categoryDrop is { } cd)
                {
                    var cats = new List<string> { "(all)" };
                    cats.AddRange(_schema.Categories);
                    cd.choices = cats;
                }
            }
            catch (Exception e)
            {
                _status.text = "Load failed: " + e.Message;
                _config = null;
            }

            _seedBtn?.SetEnabled(_config != null && _config.NeedsSeed);
            RefreshModsStatus();
            Rebuild();
        }

        /// <summary>Clean server: copy the shipped defaults into the empty live ini (F-0010).</summary>
        private void SeedDefaults()
        {
            if (_config == null || _install == null || !_config.NeedsSeed) return;

            if (ConfigService.AnyServerRunning())
            {
                EditorUtility.DisplayDialog("Server is running",
                    "Stop PalServer before writing its ini.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Seed PalWorldSettings.ini from the defaults?",
                    "Copies the OptionSettings line of\n" + _install.DefaultConfigPath +
                    "\ninto\n" + _install.ConfigPath + "\n\n" +
                    "The values are the game's own defaults, so the server behaves the same; the " +
                    "settings just become editable here. A timestamped backup of the current file " +
                    "is taken first.",
                    "Seed", "Cancel"))
                return;

            try
            {
                var backup = _config.SeedFromDefault();
                LoadInstall(_install.InstallPath);
                _status.text = "Seeded from DefaultPalWorldSettings.ini" +
                               (backup == null ? "" : " (backup: " + Path.GetFileName(backup) + ")") +
                               ".  |  " + _status.text;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Seed failed", e.Message, "OK");
            }
        }

        // ---------------------------------------------------------------- mods (vanilla baseline)

        private void SetModButtons(bool on)
        {
            _modsOnBtn?.SetEnabled(on);
            _modsOffBtn?.SetEnabled(on);
        }

        private void RefreshModsStatus()
        {
            if (_modsStatus == null) return;

            if (_install == null || !_install.IsValid)
            {
                _modsStatus.text = "Mods: no install loaded.";
                SetModButtons(false);
                return;
            }

            try
            {
                _mods ??= new ModSettingsService();
                _mods.Load(_install);
                var s = _mods.Settings;

                var packages = string.Join(", ", ModSettingsService.Describe(_workshopField?.value)
                    .Select(p => !p.Found
                        ? p.PackageName + " NOT FOUND"
                        : p.PackageName + " " + p.Version +
                          (p.HasServerRule ? "" : " (no dedicated-server install rule)")));
                var extra = _mods.DisallowedActive.ToList();

                _modsStatus.text =
                    $"Mods {(s.GlobalEnableMod ? "ON" : "off")}  |  active: " +
                    (s.ActiveModList.Count == 0 ? "none" : string.Join(", ", s.ActiveModList)) +
                    "  |  WorkshopRootDir: " +
                    (string.IsNullOrEmpty(s.WorkshopRootDir) ? "(empty)" : s.WorkshopRootDir) +
                    "  |  Workshop folder has: " + packages +
                    (extra.Count > 0
                        ? "  |  NOT in the vanilla baseline: " + string.Join(", ", extra)
                        : "") +
                    (_mods.FileExists ? "" : "  |  PalModSettings.ini does not exist yet");
                SetModButtons(true);
            }
            catch (Exception e)
            {
                _modsStatus.text = "Mods: " + e.Message;
                SetModButtons(false);
            }
        }

        private void WriteModBaseline(bool enable)
        {
            if (_mods == null || _install == null) return;

            if (ConfigService.AnyServerRunning())
            {
                EditorUtility.DisplayDialog("Server is running",
                    "Stop PalServer before changing its mod settings.", "OK");
                return;
            }

            var root = _workshopField?.value ?? string.Empty;
            var dropped = _mods.DisallowedActive.ToList();
            var message = enable
                ? "Write " + _install.ModSettingsPath + ":\n\n" +
                  "  bGlobalEnableMod=True\n  WorkshopRootDir=" + root + "\n" +
                  "  ActiveModList=UE4SSExperimentalPW\n  ActiveModList=PalSchema\n"
                : "Write " + _install.ModSettingsPath + ":\n\n" +
                  "  bGlobalEnableMod=False\n  ActiveModList: none\n";
            if (dropped.Count > 0)
                message += "\nRemoved from ActiveModList (not in the vanilla baseline): " +
                           string.Join(", ", dropped) + "\n";
            message += "\nA timestamped backup is taken first. The server applies this on its next start.";

            if (!EditorUtility.DisplayDialog(
                    enable ? "Enable UE4SS + PalSchema?" : "Turn mods off?",
                    message, "Write", "Cancel"))
                return;

            try
            {
                var backup = _mods.WriteBaseline(enable, root);
                _status.text = "PalModSettings.ini written" +
                               (backup == null ? "." : " (backup: " + Path.GetFileName(backup) + ").");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Mod settings not written", e.Message, "OK");
            }

            RefreshModsStatus();
        }

        // ---------------------------------------------------------------- list

        private IEnumerable<PalOptionField> Visible()
        {
            if (_schema == null || _config == null || !_config.IsLoaded)
                return Enumerable.Empty<PalOptionField>();

            var q = _schema.Search(_search);

            if (_category != "(all)")
                q = q.Where(o => o.Category == _category);

            q = _filter switch
            {
                "Modified from default" => q.Where(o =>
                    !string.Equals(_config.GetRaw(o.IniKey ?? o.Name), o.DefaultRaw,
                                   StringComparison.Ordinal)),
                "No in-game slider" => q.Where(o => o.Tier != OptionTier.UiSliderClamped),
                "Clamped by the game" => q.Where(o => o.HasStaticClamp),
                "Missing from this file" => q.Where(o =>
                    o.IniPresence != "struct_only" && !_config.ExistsOnDisk(o.IniKey ?? o.Name)),
                _ => q,
            };

            return q;
        }

        private void Rebuild()
        {
            if (_list == null) return;
            _list.Clear();

            foreach (var o in Visible())
                _list.Add(BuildRow(o));

            var pending = _config?.PendingCount ?? 0;
            _pendingLabel.text = pending == 0 ? "0 pending" : $"{pending} pending change(s)";
            if (_saveBtn != null) _saveBtn.SetEnabled(pending > 0);
        }

        private VisualElement BuildRow(PalOptionField o)
        {
            var key = o.IniKey ?? o.Name;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 1;
            row.style.paddingBottom = 1;

            var control = BuildControl(o, key);
            control.style.flexGrow = 1;
            row.Add(control);

            row.Add(Badge(TierBadge(o.Tier), TierColor(o.Tier)));

            if (!_config.ExistsOnDisk(key))
                row.Add(Badge("absent", new Color(0.85f, 0.55f, 0.10f)));

            if (!string.Equals(_config.GetRaw(key), o.DefaultRaw, StringComparison.Ordinal))
                row.Add(Badge("≠ default", new Color(0.20f, 0.55f, 0.90f)));

            var reset = new Button(() => { _config.StageDefault(key); Rebuild(); })
            { text = "↺", tooltip = "Reset to stock default: " + o.DefaultRaw };
            reset.style.width = 24;
            row.Add(reset);

            return row;
        }

        private VisualElement BuildControl(PalOptionField o, string key)
        {
            var raw = _config.GetRaw(key) ?? o.DefaultRaw ?? "";
            var label = o.Name;

            switch (o.Kind)
            {
                case OptionValueKind.Bool:
                {
                    var f = new Toggle(label) { value = PalIniDocument.ParseBool(raw) };
                    f.RegisterValueChangedCallback(e =>
                    {
                        _config.StageRaw(key, PalIniDocument.FormatBool(e.newValue));
                        Rebuild();
                    });
                    return f;
                }
                case OptionValueKind.Float:
                {
                    var f = new FloatField(label) { value = PalIniDocument.ParseFloat(raw) };
                    f.RegisterValueChangedCallback(e =>
                    {
                        _config.StageRaw(key, PalIniDocument.FormatFloat(e.newValue));
                        Rebuild();
                    });
                    return f;
                }
                case OptionValueKind.Int:
                {
                    var f = new IntegerField(label) { value = PalIniDocument.ParseInt(raw) };
                    f.RegisterValueChangedCallback(e =>
                    {
                        _config.StageRaw(key, PalIniDocument.FormatInt(e.newValue));
                        Rebuild();
                    });
                    return f;
                }
                case OptionValueKind.Enum:
                {
                    var choices = o.EnumValues ?? new List<string>();
                    var cur = raw;
                    var idx = Mathf.Max(0, choices.IndexOf(cur));
                    if (choices.Count == 0) goto default;
                    var f = new PopupField<string>(label, choices, idx);
                    f.RegisterValueChangedCallback(e =>
                    {
                        _config.StageRaw(key, PalIniDocument.FormatEnum(e.newValue));
                        Rebuild();
                    });
                    return f;
                }
                case OptionValueKind.EnumArray:
                {
                    // e.g. CrossplayPlatforms=(Steam,Xbox,PS5,Mac)
                    var selected = PalIniDocument.ParseArray(raw);
                    var box = new VisualElement();
                    box.style.flexDirection = FlexDirection.Row;
                    box.style.alignItems = Align.Center;

                    var head = new Label(label);
                    head.style.width = 220;
                    box.Add(head);

                    foreach (var member in o.EnumValues ?? new List<string>())
                    {
                        var local = member;
                        var t = new Toggle(local) { value = selected.Contains(local) };
                        t.style.marginRight = 8;
                        t.RegisterValueChangedCallback(e =>
                        {
                            var now = PalIniDocument.ParseArray(_config.GetRaw(key) ?? "");
                            if (e.newValue) { if (!now.Contains(local)) now.Add(local); }
                            else now.Remove(local);
                            _config.StageRaw(key, PalIniDocument.FormatArray(now));
                            Rebuild();
                        });
                        box.Add(t);
                    }
                    return box;
                }
                default:
                {
                    var isSecret = o.IsSecret && !_revealSecrets;
                    var f = new TextField(label)
                    {
                        value = PalIniDocument.ParseString(raw),
                        isPasswordField = isSecret,
                    };
                    f.RegisterValueChangedCallback(e =>
                    {
                        // Name/Str go back quoted; bare Name arrays keep their raw form.
                        var quoted = o.Kind == OptionValueKind.NameArray
                            ? e.newValue
                            : PalIniDocument.FormatString(e.newValue);
                        _config.StageRaw(key, quoted);
                        Rebuild();
                    });
                    return f;
                }
            }
        }

        private static Label Badge(string text, Color c)
        {
            var l = new Label(text);
            l.style.marginLeft = 4;
            l.style.paddingLeft = 4;
            l.style.paddingRight = 4;
            l.style.color = Color.white;
            l.style.backgroundColor = c;
            l.style.fontSize = 10;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return l;
        }

        private static string TierBadge(OptionTier t) => t switch
        {
            OptionTier.UiSliderClamped => "UI slider",
            OptionTier.ClampedNotWidened => "clamped",
            OptionTier.NoStaticClamp => "no clamp",
            _ => "not in ini",
        };

        private static Color TierColor(OptionTier t) => t switch
        {
            OptionTier.UiSliderClamped => new Color(0.30f, 0.45f, 0.30f),
            OptionTier.ClampedNotWidened => new Color(0.45f, 0.40f, 0.20f),
            OptionTier.NoStaticClamp => new Color(0.35f, 0.30f, 0.50f),
            _ => new Color(0.55f, 0.20f, 0.20f),
        };

        // ---------------------------------------------------------------- save

        private void SaveConfig()
        {
            if (_config == null || _config.PendingCount == 0) return;

            if (_config.IsServerRunning())
            {
                EditorUtility.DisplayDialog(
                    "Server is running",
                    "PalServer is running. It reads PalWorldSettings.ini only at boot, so " +
                    "saving now would have no effect and the server may overwrite the file " +
                    "on shutdown.\n\nStop the server, then save.",
                    "OK");
                return;
            }

            var summary = string.Join("\n", _config.Pending.Take(15).Select(c =>
                $"  {c.Key}: {(c.IsInsert ? "(absent)" : c.OldRaw)}  ->  {c.NewRaw}"));
            if (_config.PendingCount > 15)
                summary += $"\n  … and {_config.PendingCount - 15} more";

            if (!EditorUtility.DisplayDialog(
                    "Write server config?",
                    $"{_config.PendingCount} change(s) will be written to:\n" +
                    $"{_install.ConfigPath}\n\n{summary}\n\nA timestamped backup is taken first.",
                    "Write", "Cancel"))
                return;

            try
            {
                var backup = _config.Save();
                var snap = _config.LastSnapshotPath;
                _status.text = backup == null
                    ? "Saved."
                    : $"Saved. Backup: {Path.GetFileName(backup)}  |  " +
                      $"Snapshot: {Path.GetFileName(snap)}  ->  {PanelPaths.OutputRoot}";
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Save failed", e.Message, "OK");
                _status.text = "Save failed: " + e.Message;
            }

            Rebuild();
        }
    }
}
