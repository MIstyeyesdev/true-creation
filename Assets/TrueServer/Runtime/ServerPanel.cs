using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueServer
{
    /// <summary>
    /// True Server -- the Palworld dedicated-server host, as one panel: True Creation's Server tab, in the
    /// editor and in its exe. Merged into True Creation on 2026-09-18 from the True Server project; this is
    /// True Creation's own copy, edited here, and runs without that project. Settings and dialogs come
    /// from ServerHost.
    ///
    /// Three tabs, not one per noun. Everything needed while operating a server is on ONE screen
    /// in two columns: what the server IS down the left, what you can DO to it down the right.
    /// Splitting worlds, mods, metrics and paths across separate tabs meant reading one to act on
    /// another.
    ///
    /// Everything here is READ-ONLY. Nothing writes to the game install, the config, or the mod
    /// settings. Controls whose mechanism is verified but whose write path is not built are shown
    /// disabled with the reason, rather than hidden -- an operator needs to know a capability
    /// exists and why it is unavailable.
    /// </summary>
    public sealed class ServerPanel : VisualElement
    {
        private sealed class Tab
        {
            public string Title;
            public string Hint;
            public Func<VisualElement> Build;
            public VisualElement Built;
            public Button Button;
        }

        private const string ActiveTabPref = "TrueServer.ActiveTab";

        private List<Tab> _tabs;
        private VisualElement _content;
        private Label _hint;
        private int _active = -1;

        private ServerInstall _install;
        private List<ServerWorld> _worlds = new List<ServerWorld>();
        private ConfigSnapshot _config = new ConfigSnapshot();
        private ModSettingsSnapshot _mods = new ModSettingsSnapshot();
        private LocalSettingsSnapshot _local = new LocalSettingsSnapshot();
        private bool _running;
        private int _selectedWorld;

        public ServerPanel()
        {
            style.flexGrow = 1;
            BuildInto(this);
        }

        private void ReadState()
        {
            _install = new ServerInstall(ServerPaths.InstallPath);
            _worlds = WorldRepository.Discover(_install);
            _config = ConfigSnapshot.Read(_install.ConfigPath);
            _mods = ModSettingsSnapshot.Read(_install.ModSettingsPath);
            _local = LocalSettingsSnapshot.Read(_install.LocalSettingsPath);
            _running = ServerProcess.IsRunning();
            if (_selectedWorld >= _worlds.Count) _selectedWorld = 0;
        }

        /// <summary>Builds the tool into <paramref name="root"/> (this panel).</summary>
        private void BuildInto(VisualElement root)
        {
            ReadState();

            _tabs = new List<Tab>
            {
                new Tab
                {
                    Title = "Server",
                    Hint = "Everything about the server on one screen: worlds, mods, live controls, metrics and paths.",
                    Build = BuildServer,
                },
                new Tab
                {
                    Title = "World settings",
                    Hint = "All 123 options by category. Sliders use the game's own ranges where it publishes them.",
                    Build = () => new CreateWorldTab(_config),
                },
                new Tab
                {
                    Title = "Docs",
                    Hint = "Docs/ at the project root -- Unity's Project window only shows Assets/.",
                    Build = BuildDocs,
                },
            };

            root.style.backgroundColor = Ui.Page;
            root.style.flexDirection = FlexDirection.Column;

            root.Add(BuildTabBar());

            _hint = Ui.Text(string.Empty, 10, Ui.TextDim);
            _hint.style.marginLeft = 14;
            _hint.style.marginTop = 6;
            _hint.style.marginBottom = 6;
            _hint.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_hint);

            _content = Ui.Col();
            _content.style.flexGrow = 1;
            _content.style.paddingLeft = 12; _content.style.paddingRight = 12;
            _content.style.paddingBottom = 10;
            root.Add(_content);

            Select(Mathf.Clamp(ServerHost.GetInt(ActiveTabPref, 0), 0, _tabs.Count - 1));
        }

        private VisualElement BuildTabBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.FlexEnd;
            bar.style.backgroundColor = Ui.Rail;
            bar.style.paddingLeft = 10; bar.style.paddingRight = 10;
            bar.style.paddingTop = 8;
            bar.style.flexShrink = 0;

            var brand = Ui.Col();
            brand.style.marginRight = 18;
            brand.style.paddingBottom = 6;
            brand.Add(Ui.Text("TRUE SERVER", 11, Ui.TextMid, true));
            brand.Add(Ui.Text("Palworld host", 9, Ui.TextDim));
            bar.Add(brand);

            for (var i = 0; i < _tabs.Count; i++)
            {
                var idx = i;
                var t = _tabs[i];
                t.Button = new Button(() => Select(idx)) { text = t.Title };
                t.Button.style.fontSize = 12;
                t.Button.style.height = 30;
                t.Button.style.paddingLeft = 16; t.Button.style.paddingRight = 16;
                t.Button.style.marginLeft = 0; t.Button.style.marginRight = 2;
                t.Button.style.marginTop = 0; t.Button.style.marginBottom = 0;
                t.Button.style.borderTopWidth = 0; t.Button.style.borderLeftWidth = 0;
                t.Button.style.borderRightWidth = 0; t.Button.style.borderBottomWidth = 3;
                Ui.SetRadius(t.Button, 0);
                bar.Add(t.Button);
            }

            var pad = new VisualElement();
            pad.style.flexGrow = 1;
            bar.Add(pad);

            var state = Ui.Col();
            state.style.paddingBottom = 8;
            state.Add(_running ? Ui.Dot("Server running", Ui.Good, 10) : Ui.Dot("Server stopped", Ui.TextDim, 10));
            bar.Add(state);

            var refresh = Ui.Btn("Refresh", RefreshActive);
            refresh.style.marginBottom = 6;
            refresh.style.marginLeft = 10;
            bar.Add(refresh);
            return bar;
        }

        private void StyleTabs()
        {
            for (var i = 0; i < _tabs.Count; i++)
            {
                var on = i == _active;
                var b = _tabs[i].Button;
                if (b == null) continue;
                b.style.backgroundColor = on ? Ui.Page : Ui.Rail;
                b.style.color = on ? Color.white : Ui.TextMid;
                b.style.borderBottomColor = on ? Ui.Accent : Ui.Rail;
                b.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void Select(int i)
        {
            if (_tabs == null || i < 0 || i >= _tabs.Count) return;
            _active = i;
            ServerHost.SetInt(ActiveTabPref, i);

            var tab = _tabs[i];
            if (tab.Built == null) tab.Built = tab.Build();

            _content.Clear();
            _content.Add(tab.Built);
            _hint.text = tab.Hint;
            StyleTabs();
        }

        public void RefreshActive()
        {
            ReadState();
            OptionCatalog.Reload();
            if (_tabs == null) return;
            foreach (var t in _tabs) t.Built = null;
            Select(_active < 0 ? 0 : _active);
        }

        // ================================================================== Server

        private VisualElement BuildServer()
        {
            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1;

            // left: what the server IS
            var left = Ui.Scroll();
            left.style.width = 430;
            left.style.flexShrink = 0;
            left.style.marginRight = 10;

            left.Add(StatusCard());
            foreach (var n in Blockers()) left.Add(n);
            left.Add(WorldsCard());
            left.Add(ModsCard());
            left.Add(PathsCard());
            split.Add(left);

            // right: what you can DO to it
            var right = Ui.Scroll();
            right.style.flexGrow = 1;

            right.Add(StatsCard());
            right.Add(LiveOpsCard());
            right.Add(CapabilitiesCard());
            split.Add(right);

            return split;
        }

        private ServerWorld SelectedWorld() =>
            _worlds.Count == 0 ? null : _worlds[Mathf.Clamp(_selectedWorld, 0, _worlds.Count - 1)];

        private VisualElement StatusCard()
        {
            var card = Ui.CardBox(14);

            var left = Ui.Col();
            left.Add(_running
                ? Ui.Dot("LIVE", Ui.Good)
                : Ui.Dot(_install.IsValid ? "OFFLINE" : "NO INSTALL", _install.IsValid ? Ui.TextDim : Ui.Bad));

            var world = SelectedWorld();
            var title = Ui.Text(world?.DisplayName ?? "No world yet", 18, Ui.TextHi, true);
            title.style.marginTop = 2;
            left.Add(title);

            var bits = new List<string> { _install.IsValid ? "Palworld dedicated" : "install not found" };
            if (_mods.Exists) bits.Add(_mods.GlobalEnable ? "mods on" : "mods off");
            bits.Add(_worlds.Count + " world" + (_worlds.Count == 1 ? "" : "s"));
            left.Add(Ui.Text(string.Join("  ·  ", bits), 11, Ui.TextDim));
            card.Add(left);

            var acts = Ui.Row();
            acts.style.marginTop = 10;
            acts.Add(Disabled(Ui.Btn(_running ? "Take down" : "Go live", () => { },
                    _running ? new Color(0.227f, 0.122f, 0.137f) : new Color(0.110f, 0.227f, 0.165f),
                    _running ? Ui.Bad : Ui.Good),
                "process control not built"));
            card.Add(acts);

            var addr = Ui.Row();
            addr.style.marginTop = 10;
            var ip = _config.Display("PublicIP");
            var port = _config.Display("PublicPort") ?? "8211";
            var acol = Ui.Col();
            acol.style.flexGrow = 1;
            acol.Add(Ui.Caption("ADDRESS"));
            acol.Add(Ui.Text(
                (string.IsNullOrEmpty(ip) || ip == "(not set)" ? "<public ip not set>" : ip) + " : " + port,
                13, Ui.TextHi));
            addr.Add(acol);
            addr.Add(Ui.Btn("Open install", () => ServerPaths.Reveal(_install.InstallPath)));
            card.Add(addr);
            return card;
        }

        private VisualElement WorldsCard()
        {
            var card = Ui.CardBox(12);
            card.Add(Ui.SectionLabel("WORLDS ON THIS INSTALL  (" + _worlds.Count + ")"));
            card.Add(Ui.Text(
                "An install can hold many worlds. Exactly one runs — the one named by "
                + "DedicatedServerName in GameUserSettings.ini (F-0026).", 9, Ui.TextDim));
            card.Add(Ui.Spacer(4));

            if (_worlds.Count == 0)
            {
                card.Add(Ui.Text("SaveGames/0 has no world folder. One appears the first time the server starts.",
                    11, Ui.TextDim));
                if (_local.HasSelection)
                    card.Add(Ui.Text("GameUserSettings already names one: " + _local.DedicatedServerName
                                     + " — the folder appears when the server first runs.", 9, Ui.Accent));
                return card;
            }

            for (var i = 0; i < _worlds.Count; i++)
            {
                var idx = i;
                var w = _worlds[i];
                var on = idx == _selectedWorld;

                var b = new Button(() => { _selectedWorld = idx; RebuildServerTab(); });
                // grows with its text (a long world name or the WorldOption.sav note wraps); a fixed 44 px clipped it
                b.style.minHeight = 44;
                b.style.flexShrink = 0;
                b.style.flexDirection = FlexDirection.Column;
                b.style.alignItems = Align.Stretch;
                b.style.justifyContent = Justify.Center;
                b.style.marginLeft = 0; b.style.marginRight = 0; b.style.marginBottom = 3;
                b.style.paddingLeft = 8; b.style.paddingRight = 6;
                b.style.paddingTop = 5; b.style.paddingBottom = 5;
                b.style.borderTopWidth = 0; b.style.borderRightWidth = 0; b.style.borderBottomWidth = 0;
                b.style.borderLeftWidth = 3;
                b.style.borderLeftColor = on ? Ui.Accent : Ui.Card;
                b.style.backgroundColor = on ? new Color(0.137f, 0.137f, 0.227f) : Ui.Sunken;
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                Ui.SetRadius(b, 4);

                var col = Ui.Col();
                col.pickingMode = PickingMode.Ignore;

                var nameRow = Ui.Row();
                nameRow.pickingMode = PickingMode.Ignore;
                var nm = Ui.Text(w.DisplayName, 11, Ui.TextHi);
                nm.pickingMode = PickingMode.Ignore;
                nameRow.Add(nm);
                if (w.IsSelected)
                {
                    var tag = Ui.Pill(_running ? "LIVE" : "SELECTED",
                        _running ? new Color(0.62f, 0.90f, 0.76f) : new Color(0.62f, 0.80f, 0.70f),
                        new Color(0.102f, 0.176f, 0.145f));
                    tag.pickingMode = PickingMode.Ignore;
                    tag.style.marginLeft = 8; tag.style.marginBottom = 0;
                    nameRow.Add(tag);
                }
                col.Add(nameRow);
                var sub = Ui.Text(
                    w.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    + " · " + w.PlayerCount + " player file" + (w.PlayerCount == 1 ? "" : "s")
                    + (w.HasBackups ? " · backups" : "")
                    + (w.HasWorldOption ? "  ·  WorldOption.sav" : ""),
                    9, w.HasWorldOption ? Ui.Bad : Ui.TextDim);
                sub.pickingMode = PickingMode.Ignore;
                col.Add(sub);
                b.Add(col);
                card.Add(b);
            }

            var sel = SelectedWorld();
            if (sel != null)
            {
                var row = Ui.Row();
                row.style.marginTop = 4;
                row.Add(Ui.Btn("Open folder", () => ServerPaths.Reveal(sel.FolderPath)));
                card.Add(row);
                if (!sel.IsSelected)
                    row.Add(Disabled(Ui.Btn("Make this the live world", () => { }),
                        "writes DedicatedServerName; needs the server stopped"));
                card.Add(Ui.Text("Names are folder GUIDs until LevelMeta.sav is parsed (F-0020).", 9, Ui.TextDim));
            }
            return card;
        }

        private void RebuildServerTab()
        {
            if (_tabs == null || _active != 0) return;
            _tabs[0].Built = null;
            Select(0);
        }

        private VisualElement ModsCard()
        {
            var card = Ui.CardBox(12);
            card.Add(Ui.SectionLabel("MODS ON THIS INSTALL"));

            if (!_mods.Exists)
            {
                card.Add(Ui.Text("No PalModSettings.ini at this install.", 11, Ui.TextDim));
                return card;
            }

            card.Add(_mods.GlobalEnable
                ? Ui.Dot("bGlobalEnableMod = True", Ui.Good)
                : Ui.Dot("bGlobalEnableMod = False — nothing loads", Ui.TextDim));

            if (_mods.ActiveMods.Count == 0)
                card.Add(Ui.Text("ActiveModList is empty.", 11, Ui.TextDim));
            else
            {
                var wrap = WrapRow();
                foreach (var m in _mods.ActiveMods)
                    wrap.Add(Ui.Pill(m, new Color(0.56f, 0.72f, 0.94f), new Color(0.118f, 0.176f, 0.259f)));
                card.Add(wrap);
            }

            card.Add(Ui.Text(
                "Per-install, not per-world (F-0018). A per-world set can only be a host-side profile written "
                + "at launch — and two worlds cannot run at once off one install with different mods.",
                9, Ui.TextDim));
            return card;
        }

        private VisualElement PathsCard()
        {
            var card = Ui.CardBox(12);
            var fold = new Foldout { text = "Paths", value = !_install.IsValid };
            var lbl = fold.Q<Label>();
            if (lbl != null) { lbl.style.color = Ui.TextHi; lbl.style.fontSize = 12; }

            var inst = Ui.Field(ServerPaths.InstallPath);
            inst.RegisterValueChangedCallback(e => ServerPaths.InstallPath = e.newValue);
            fold.Add(Ui.FormRow("Server install", inst,
                ServerPaths.InstallIsOverridden ? "overridden" : "auto-discovered from Steam",
                false, 120f, 250f));

            var r1 = Ui.Row();
            r1.Add(Ui.Btn("Browse…", () =>
            {
                var p = ServerHost.OpenFolderPanel("Locate the dedicated server install",
                    ServerPaths.InstallPath);
                if (!string.IsNullOrEmpty(p)) { ServerPaths.InstallPath = p; RefreshActive(); }
            }));
            r1.Add(Ui.Btn("Re-detect", () => { ServerPaths.InstallPath = null; RefreshActive(); }));
            fold.Add(r1);

            var outp = Ui.Field(ServerPaths.OutputRoot);
            outp.RegisterValueChangedCallback(e => ServerPaths.OutputRoot = e.newValue);
            fold.Add(Ui.FormRow("Backups", outp, "outside the project and the install", false, 120f, 250f));

            var refp = Ui.Field(ServerPaths.ReferenceRoot);
            refp.RegisterValueChangedCallback(e => ServerPaths.ReferenceRoot = e.newValue);
            fold.Add(Ui.FormRow("Reference project", refp, "optional; only to regenerate the catalogue",
                false, 120f, 250f));

            fold.Add(Ui.Text(
                "Stored per user (EditorPrefs in the editor, the program's own settings file in True Creation), "
                + "never in the project, so a copy of it carries no trace of the machine it was set up on.", 9, Ui.TextDim));

            fold.Add(Ui.Btn("Forget all overrides", () =>
            {
                if (ServerHost.Confirm("Forget stored paths",
                        "Clears the saved install, output and reference paths. Discovery and defaults take "
                        + "over. No files are changed.", "Forget", "Cancel"))
                { ServerPaths.ResetAll(); RefreshActive(); }
            }));

            card.Add(fold);
            return card;
        }

        private VisualElement StatsCard()
        {
            var card = Ui.CardBox(12);
            card.Add(Ui.SectionLabel("LIVE METRICS"));

            var live = _running && RestReady();
            var tiles = Ui.Row();
            tiles.Add(Ui.Stat("PLAYERS", live ? "-" : "--",
                live ? "/ " + (_config.Display("ServerPlayerMaxNum") ?? "32") : "needs REST"));
            tiles.Add(Ui.Stat("SERVER FPS", live ? "-" : "--", live ? "avg -" : "needs REST"));
            tiles.Add(Ui.Stat("UPTIME", live ? "-" : "--", live ? "in-game day -" : "needs REST"));
            tiles.Add(Ui.Stat("CONFIG", ServerPaths.HumanSize(_install.ConfigBytes),
                _config.Loaded ? _config.KeyCount + " keys" : "no OptionSettings"));
            card.Add(tiles);

            card.Add(Ui.Text(
                "GET /v1/api/metrics returns serverfps, currentplayernum, maxplayernum, uptime, days, "
                + "basecampnum and serverframetime. Every tile maps to a field — no scraping.",
                9, Ui.TextDim));
            return card;
        }

        private VisualElement LiveOpsCard()
        {
            var card = Ui.CardBox(12);
            card.Add(Ui.SectionLabel("LIVE OPS"));

            var msg = new TextField { value = "Server restarting in 6 minutes" };
            msg.style.fontSize = 11;
            card.Add(Ui.FormRow("Message", msg, "sent to everyone on the world", false, 110f, 420f));

            var row = Ui.Row();
            row.Add(Disabled(Ui.Btn("Broadcast", () => { }), "needs REST + admin password"));
            row.Add(Disabled(Ui.Btn("Shutdown in 6 min", () => { },
                new Color(0.227f, 0.180f, 0.094f), Ui.Warn), "needs REST + admin password"));
            row.Add(Disabled(Ui.Btn("Save world", () => { }), "needs REST + admin password"));
            card.Add(row);

            card.Add(Ui.Text(
                "Shutdown is native: POST /v1/api/shutdown {\"waittime\":360,\"message\":\"…\"} and the server "
                + "broadcasts its own countdown. We do not build a timer.", 9, Ui.TextDim));
            return card;
        }

        private VisualElement CapabilitiesCard()
        {
            var card = Ui.CardBox(12);
            card.Add(Ui.SectionLabel("WHAT THIS SERVER CAN DO"));

            card.Add(Ui.Text("Console / RCON — read from the shipping binary and the vendor docs:", 11, Ui.TextMid));
            var cmds = WrapRow();
            foreach (var c in new[] { "Shutdown", "Broadcast", "Save", "ShowPlayers", "KickPlayer",
                                      "BanPlayer", "UnBanPlayer", "TeleportToPlayer", "TeleportToMe",
                                      "Info", "ToggleSpectate", "DoExit" })
                cmds.Add(Ui.Pill(c, new Color(0.56f, 0.72f, 0.94f), new Color(0.118f, 0.176f, 0.259f)));
            card.Add(cmds);

            card.Add(Ui.Spacer(6));
            card.Add(Ui.Text("REST endpoints:", 11, Ui.TextMid));
            var eps = WrapRow();
            foreach (var e in new[] { "info", "players", "metrics", "game-data", "announce", "save",
                                      "shutdown", "ban", "unban", "kick", "stop", "settings" })
                eps.Add(Ui.Pill("/v1/api/" + e, new Color(0.62f, 0.80f, 0.70f), new Color(0.102f, 0.176f, 0.145f)));
            card.Add(eps);

            card.Add(Ui.Text("settings is GET only — no option changes live. Every change is restart-gated.",
                10, Ui.Warn));
            return card;
        }

        private IEnumerable<VisualElement> Blockers()
        {
            var list = new List<VisualElement>();

            if (!_install.IsValid)
            {
                list.Add(Ui.Notice("Server install not found",
                    "PalServer.exe is not at the configured path, and Steam discovery found nothing. "
                    + "Set it under Paths below.", Ui.Bad));
                return list;
            }

            if (!RestReady())
            {
                var why = new List<string>();
                if (!_config.Loaded) why.Add("the live ini has no OptionSettings block");
                else
                {
                    if (!_config.IsTrue("RESTAPIEnabled")) why.Add("RESTAPIEnabled is False");
                    if (!_config.SecretIsSet("AdminPassword")) why.Add("AdminPassword is empty");
                }
                list.Add(Ui.Notice("Live controls unavailable  (F-0017)",
                    string.Join("; ", why)
                    + ". The server rejects REST with \"Unauthorized (AdminPassword is empty)\". "
                    + "You set the password — this tool never generates or stores it.", Ui.Warn));
            }

            if (_install.ConfigIsEmpty)
                list.Add(Ui.Notice("Clean server: the config is " + _install.ConfigBytes + " bytes  (F-0010)",
                    "The server is running on built-in defaults. Seeding from DefaultPalWorldSettings.ini "
                    + "makes the options editable without changing behaviour.", Ui.Accent));

            if (_worlds.Count > 0 && !_worlds.Any(w => w.IsSelected))
                list.Add(Ui.Notice("No world is selected  (F-0026)",
                    _local.HasSelection
                        ? "DedicatedServerName is \"" + _local.DedicatedServerName
                          + "\", which matches none of the world folders present."
                        : "GameUserSettings.ini names no world. Which of the "
                          + _worlds.Count + " worlds the server would load is unknown.",
                    Ui.Warn));

            foreach (var w in _worlds.Where(x => x.HasWorldOption))
                list.Add(Ui.Notice("WorldOption.sav in " + w.Guid + "  (F-0011)",
                    "Reported to silently override PalWorldSettings.ini. Until tested, treat the ini as NOT "
                    + "authoritative for that world.", Ui.Bad));

            return list;
        }

        private bool RestReady() =>
            _config.Loaded && _config.IsTrue("RESTAPIEnabled") && _config.SecretIsSet("AdminPassword");

        // ================================================================== Docs

        private VisualElement BuildDocs()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1;

            var listCol = Ui.Col();
            listCol.style.width = 220;
            listCol.style.flexShrink = 0;
            listCol.style.marginRight = 10;

            var body = Ui.Scroll();
            body.style.flexGrow = 1;

            var dir = ServerPaths.Docs;
            if (!Directory.Exists(dir))
            {
                row.Add(Application.isEditor
                    ? Ui.Notice("No Docs folder", dir + " does not exist.", Ui.Bad)
                    : Ui.Notice("Docs", "The True Server docs are part of the project, not the program.", Ui.Accent));
                return row;
            }

            var files = Directory.GetFiles(dir, "*.md").OrderBy(x => x).ToList();

            var hdr = Ui.CardBox(10);
            hdr.Add(Ui.Text("Docs/", 12, Ui.TextHi, true));
            hdr.Add(Ui.Text("At the project root. Unity's Project window only shows Assets/, which is why "
                            + "these are invisible there.", 9, Ui.TextDim));
            hdr.Add(Ui.Btn("Reveal", () => ServerPaths.Reveal(dir)));
            listCol.Add(hdr);

            foreach (var file in files)
            {
                var path = file;
                var b = Ui.Btn(Path.GetFileName(path), () => ShowDoc(body, path));
                b.style.marginBottom = 3;
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                listCol.Add(b);
            }

            row.Add(listCol);
            row.Add(body);

            var first = files.FirstOrDefault(f => Path.GetFileName(f) == "README.md") ?? files.FirstOrDefault();
            if (first != null) ShowDoc(body, first);
            return row;
        }

        private static void ShowDoc(ScrollView target, string path)
        {
            target.Clear();
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { target.Add(Ui.Text(e.Message, 11, Ui.Bad)); return; }

            var head = Ui.Row();
            head.Add(Ui.Text(Path.GetFileName(path), 13, Ui.TextHi, true));
            head.Add(Ui.Btn("Reveal", () => ServerPaths.Reveal(path)));
            target.Add(head);
            target.Add(Ui.Spacer(6));

            var inFence = false;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw;

                if (line.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }

                if (inFence || line.StartsWith("|"))
                {
                    var mono = Ui.Text(line, 10, Ui.TextMid);
                    mono.style.backgroundColor = Ui.Sunken;
                    mono.style.paddingLeft = 6; mono.style.paddingRight = 6;
                    mono.style.whiteSpace = WhiteSpace.NoWrap;
                    target.Add(mono);
                    continue;
                }

                if (line.StartsWith("### ")) { target.Add(Ui.Spacer(6)); target.Add(Ui.Text(line.Substring(4), 12, Ui.TextHi, true)); }
                else if (line.StartsWith("## ")) { target.Add(Ui.Spacer(10)); target.Add(Ui.Text(line.Substring(3), 14, Ui.TextHi, true)); }
                else if (line.StartsWith("# ")) { target.Add(Ui.Spacer(4)); target.Add(Ui.Text(line.Substring(2), 17, Ui.TextHi, true)); }
                else if (line.StartsWith("> ")) target.Add(Ui.Text(line.Substring(2), 11, Ui.Accent));
                else if (line.Trim() == "---") target.Add(Ui.Spacer(8));
                else if (line.Trim().Length == 0) target.Add(Ui.Spacer(4));
                else target.Add(Ui.Text(line, 11, Ui.TextMid));
            }
        }

        // ================================================================== helpers

        private static VisualElement WrapRow()
        {
            var v = new VisualElement();
            v.style.flexDirection = FlexDirection.Row;
            v.style.flexWrap = Wrap.Wrap;
            v.style.marginTop = 4;
            return v;
        }

        /// <summary>
        /// A control that exists but cannot act yet, with the reason under it. Hiding it would
        /// leave the operator guessing whether the capability exists at all.
        /// </summary>
        private static VisualElement Disabled(Button b, string reason)
        {
            b.SetEnabled(false);
            b.tooltip = reason;
            var col = Ui.Col();
            col.Add(b);
            var r = Ui.Text(reason, 9, Ui.TextDim);
            r.style.marginLeft = 2;
            col.Add(r);
            return col;
        }
    }
}
