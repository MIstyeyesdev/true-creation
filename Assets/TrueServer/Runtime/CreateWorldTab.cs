using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueServer
{
    /// <summary>
    /// Create a new world -- laid out like the game's own World Settings screen: categories down
    /// the left, that category's settings on the right, one row per setting.
    ///
    /// Every numeric field is a slider. Where the game publishes a range the slider uses it and
    /// says so; where it does not, the bounds are ours and the row says that too, because an
    /// invented bound presented as the game's would be a lie.
    ///
    /// Edits are staged in memory. Nothing is written (F-0019).
    /// </summary>
    public sealed class CreateWorldTab : VisualElement
    {
        private const string SetupKey = "Setup";

        /// <summary>
        /// The game ships 18 option categories, several holding one or two fields. Listed raw they
        /// become a scrolling rail that is worse than no grouping at all, so they are folded into
        /// five themes plus Setup. The game's own category is kept as a subheading inside each
        /// group, so nothing is renamed or lost -- only the navigation is shortened.
        /// </summary>
        private static readonly (string Group, string[] Categories)[] Groups =
        {
            ("World",         new[] { "Progression", "Time", "World Events", "Travel", "Persistence" }),
            ("Players",       new[] { "Player", "Death & Respawn", "PvP" }),
            ("Pals",          new[] { "Pals", "Palbox" }),
            ("Base & items",  new[] { "Base Camp", "Building", "Items", "Gathering", "Guild" }),
            ("Server",        new[] { "Server", "Remote API", "Voice Chat" }),
        };

        private static IEnumerable<OptionDef> InGroup(string group) =>
            Groups.Where(g => g.Group == group)
                  .SelectMany(g => g.Categories)
                  .SelectMany(OptionCatalog.InCategory);

        private readonly ConfigSnapshot _live;
        private readonly Dictionary<string, string> _staged =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string _difficulty = "Custom";
        private string _worldMode = "Dedicated_PvE";
        private bool _publicLobby;
        private bool _restApi = true;
        private bool _rcon;
        private string _adminPassword = string.Empty;

        private readonly List<Button> _catButtons = new List<Button>();
        private string _activeCat = SetupKey;
        private ScrollView _pane;
        private Label _status;

        public CreateWorldTab(ConfigSnapshot live)
        {
            _live = live ?? new ConfigSnapshot();
            style.flexGrow = 1;

            if (!OptionCatalog.IsLoaded)
            {
                Add(Ui.Notice("Option catalogue not loaded",
                    (OptionCatalog.LoadError ?? "unknown") + "\n\nRun:  python Tools/build_world_options.py", Ui.Bad));
                return;
            }

            Add(BuildTopBar());

            var split = new VisualElement();
            split.style.flexDirection = FlexDirection.Row;
            split.style.flexGrow = 1;
            Add(split);

            split.Add(BuildCategoryRail());

            _pane = Ui.Scroll();
            _pane.style.flexGrow = 1;
            split.Add(_pane);

            ShowCategory(SetupKey);
        }

        // ---------------------------------------------------------------- top bar

        private VisualElement BuildTopBar()
        {
            var bar = Ui.CardBox(12);
            var row = Ui.Row();

            var left = Ui.Col();
            left.style.flexGrow = 1;
            left.Add(Ui.Text("Create a new world", 16, Ui.TextHi, true));
            _status = Ui.Text(string.Empty, 10, Ui.TextDim);
            left.Add(_status);
            row.Add(left);

            row.Add(Ui.Btn("Preview what would be written", ShowPreview));
            var create = Ui.Btn("Create world", () => { }, new Color(0.110f, 0.227f, 0.165f), Ui.Good);
            create.SetEnabled(false);
            create.tooltip = "No write path exists yet (F-0019). Nothing here can touch your install.";
            row.Add(create);

            bar.Add(row);
            RefreshStatus();
            return bar;
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            var p = OptionCatalog.Payload;
            _status.text = string.Format(
                "{0} · {1} · {2} options, {3} with a published range · {4} staged change{5}",
                _worldMode, _difficulty, p.structFieldCount, p.rangedCount,
                _staged.Count, _staged.Count == 1 ? string.Empty : "s");
        }

        // ---------------------------------------------------------------- category rail

        private VisualElement BuildCategoryRail()
        {
            var rail = Ui.Col();
            rail.style.width = 196;
            rail.style.flexShrink = 0;
            rail.style.marginRight = 10;

            var scroll = Ui.Scroll();
            rail.Add(scroll);

            AddCat(scroll, SetupKey, "identity, mode, access");
            foreach (var g in Groups)
                AddCat(scroll, g.Group,
                    InGroup(g.Group).Count() + " options · " + string.Join(", ", g.Categories));

            return rail;
        }

        private void AddCat(VisualElement parent, string key, string sub)
        {
            var b = new Button(() => ShowCategory(key));
            b.userData = key;
            // grows with its text: a fixed 42 px clipped the subtitle once it wrapped (the exe, 2026-09-18)
            b.style.minHeight = 42;
            b.style.flexShrink = 0;
            b.style.flexDirection = FlexDirection.Column;
            b.style.alignItems = Align.Stretch;
            b.style.justifyContent = Justify.Center;
            b.style.marginLeft = 0; b.style.marginRight = 0;
            b.style.marginTop = 0; b.style.marginBottom = 2;
            b.style.paddingLeft = 10; b.style.paddingRight = 6;
            b.style.paddingTop = 6; b.style.paddingBottom = 6;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderRightWidth = 0;
            b.style.borderLeftWidth = 3;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            Ui.SetRadius(b, 0);

            var col = Ui.Col();
            col.pickingMode = PickingMode.Ignore;
            var title = Ui.Text(key == SetupKey ? "Setup" : key, 12, Ui.TextHi);
            title.pickingMode = PickingMode.Ignore;
            col.Add(title);
            var s = Ui.Text(sub, 9, Ui.TextDim);
            s.style.whiteSpace = WhiteSpace.Normal;
            s.pickingMode = PickingMode.Ignore;
            col.Add(s);
            b.Add(col);

            _catButtons.Add(b);
            parent.Add(b);
        }

        private void PaintRail()
        {
            foreach (var b in _catButtons)
            {
                var on = (string)b.userData == _activeCat;
                b.style.backgroundColor = on ? new Color(0.137f, 0.137f, 0.227f) : new Color(0.098f, 0.098f, 0.118f);
                b.style.borderLeftColor = on ? Ui.Accent : new Color(0.098f, 0.098f, 0.118f);
            }
        }

        private void ShowCategory(string cat)
        {
            _activeCat = cat;
            PaintRail();
            if (_pane == null) return;
            _pane.Clear();

            if (cat == SetupKey) { BuildSetup(_pane); return; }

            var presetGoverned = OptionCatalog.PresetGovernedFields();
            var modeGoverned = OptionCatalog.ModeGovernedFields();

            var group = Groups.FirstOrDefault(g => g.Group == cat);
            if (group.Categories == null) return;

            var all = InGroup(cat).ToList();

            var head = Ui.CardBox(12);
            head.Add(Ui.Text(cat, 16, Ui.TextHi, true));
            head.Add(Ui.Text(all.Count + " options · "
                             + all.Count(o => o.HasScale) + " with a range the game publishes",
                             10, Ui.TextDim));
            _pane.Add(head);

            // One card per original game category, so the game's own grouping survives.
            foreach (var sub in group.Categories)
            {
                var opts = OptionCatalog.InCategory(sub).ToList();
                if (opts.Count == 0) continue;

                var box = Ui.CardBox(12);
                var h = Ui.Row();
                h.Add(Ui.Text(sub, 13, Ui.TextHi, true));
                h.Add(Ui.Text("   " + opts.Count, 11, Ui.TextDim));
                box.Add(h);
                box.Add(Ui.Spacer(4));

                foreach (var o in opts)
                    box.Add(SettingRow(o, presetGoverned.Contains(o.name), modeGoverned.Contains(o.name)));
                _pane.Add(box);
            }
        }

        // ---------------------------------------------------------------- one setting row

        /// <summary>
        /// Name on the left, control in the middle, value and range on the right -- the shape the
        /// in-game settings screen uses.
        /// </summary>
        private VisualElement SettingRow(OptionDef o, bool preset, bool mode)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 30;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(1f, 1f, 1f, 0.04f);
            row.style.paddingTop = 4; row.style.paddingBottom = 4;

            var left = Ui.Col();
            left.style.width = 250;
            left.style.flexShrink = 0;
            left.style.paddingRight = 10;
            left.Add(Ui.Text(string.IsNullOrEmpty(o.title) ? o.name : o.title, 12, Ui.TextHi));
            left.Add(Ui.Text(o.name, 9, Ui.TextDim));

            var notes = new List<string>();
            if (preset) notes.Add("difficulty preset");
            if (mode) notes.Add("world mode");
            if (o.when == "creation") notes.Add("creation only");
            if (!o.luaSettable) notes.Add("not writable from Lua");
            if (o.casingDiffersFromIni) notes.Add("ini: " + o.iniKey);
            if (notes.Count > 0) left.Add(Ui.Text(string.Join(" · ", notes), 9, Ui.TextDim));
            row.Add(left);

            var mid = new VisualElement();
            mid.style.flexGrow = 1;
            mid.style.flexDirection = FlexDirection.Row;
            mid.style.alignItems = Align.Center;
            mid.style.maxWidth = 460;

            if (o.isSecret)
            {
                var f = Ui.Field(string.Empty, password: true);
                f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => Stage(o.iniKey, e.newValue));
                mid.Add(f);
            }
            else if (o.isBool)
            {
                var t = Ui.Check(string.Equals(Current(o), "True", StringComparison.OrdinalIgnoreCase));
                t.RegisterValueChangedCallback(e => Stage(o.iniKey, e.newValue ? "True" : "False"));
                mid.Add(t);
            }
            else if (o.isArray && o.enumValues != null && o.enumValues.Length > 0)
            {
                // An Array<enum> holds SEVERAL values at once. CrossplayPlatforms defaults to all
                // four platforms; a single-pick dropdown would quietly disable three of them.
                var chosen = new HashSet<string>(o.DefaultList(), StringComparer.OrdinalIgnoreCase);
                var wrap = new VisualElement();
                wrap.style.flexDirection = FlexDirection.Row;
                wrap.style.flexWrap = Wrap.Wrap;
                wrap.style.flexGrow = 1;

                foreach (var member in o.enumValues)
                {
                    var m = member;
                    var t = Ui.Check(chosen.Contains(m), m);
                    t.style.marginRight = 14;
                    t.RegisterValueChangedCallback(e =>
                    {
                        if (e.newValue) chosen.Add(m); else chosen.Remove(m);
                        Stage(o.iniKey, "(" + string.Join(",", o.enumValues.Where(chosen.Contains)) + ")");
                    });
                    wrap.Add(t);
                }
                mid.Add(wrap);
            }
            else if (o.isArray)
            {
                var f = Ui.Field(string.Join(",", o.DefaultList()));
                f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => Stage(o.iniKey, "(" + (e.newValue ?? string.Empty) + ")"));
                mid.Add(f);
            }
            else if (o.enumValues != null && o.enumValues.Length > 0)
            {
                var d = Ui.Drop(o.enumValues.ToList(), Current(o));
                d.style.flexGrow = 1;
                d.RegisterValueChangedCallback(e => Stage(o.iniKey, e.newValue));
                mid.Add(d);
            }
            else if (IsNumeric(o))
            {
                var asInt = o.type == "Int";
                float.TryParse(Current(o), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
                GetBounds(o, v, out var lo, out var hi);

                var slider = Ui.SliderRow(v, lo, hi, asInt,
                    nv => Stage(o.iniKey, asInt
                        ? Mathf.RoundToInt(nv).ToString(CultureInfo.InvariantCulture)
                        : nv.ToString("0.000000", CultureInfo.InvariantCulture)),
                    out _);
                slider.style.flexGrow = 1;
                mid.Add(slider);
            }
            else
            {
                var f = Ui.Field(Current(o));
                f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => Stage(o.iniKey, e.newValue));
                mid.Add(f);
            }
            row.Add(mid);

            var right = Ui.Col();
            right.style.width = 260;
            right.style.flexShrink = 0;
            right.style.paddingLeft = 12;

            if (!string.IsNullOrEmpty(o.desc))
                right.Add(Ui.Text(o.desc, 10, Ui.TextMid));

            if (o.HasScale)
                right.Add(Ui.Text("game range " + o.ScaleText, 9, Ui.Good));
            else if (o.clampedRangeUnknown)
                right.Add(Ui.Text("clamped, range not published", 9, Ui.Warn));
            else if (IsNumeric(o))
                right.Add(Ui.Text("no published range — bounds ours", 9, Ui.TextDim));

            if (o.modTouches)
                right.Add(Ui.Text(o.modNarrows
                        ? "mod NARROWS to " + o.ModScaleText
                        : "mod: " + o.ModScaleText,
                    9, o.modNarrows ? Ui.Bad : Ui.TextDim));
            row.Add(right);

            return row;
        }

        private static bool IsNumeric(OptionDef o) => o.type == "Int" || o.type == "Float";

        /// <summary>
        /// Slider bounds: the game's published range when there is one, otherwise a span around
        /// the default. The row labels which it got, so ours is never mistaken for a game limit.
        /// </summary>
        private static void GetBounds(OptionDef o, float current, out float lo, out float hi)
        {
            if (o.hasMin || o.hasMax)
            {
                lo = o.hasMin ? o.min : 0f;
                hi = o.hasMax ? o.max : Mathf.Max(current * 2f, 10f);
                if (hi <= lo) hi = lo + 1f;
                return;
            }

            float.TryParse(o.@default, NumberStyles.Float, CultureInfo.InvariantCulture, out var def);
            var seed = Mathf.Max(Mathf.Abs(def), Mathf.Abs(current), 1f);
            lo = def < 0f ? -1f : 0f;
            hi = Mathf.Ceil(seed * 4f);
            if (hi <= lo) hi = lo + 1f;
        }

        // ---------------------------------------------------------------- setup page

        private void BuildSetup(VisualElement pane)
        {
            var id = Ui.Section("Server identity and access", "What players see, and how you administer it.");

            var name = Ui.Field(_live.Display("ServerName") ?? "My Palworld Server");
            name.RegisterValueChangedCallback(e => Stage("ServerName", e.newValue));
            id.Add(Ui.FormRow("Server name", name, "shown in the browser", true));

            var desc = Ui.Field(_live.Display("ServerDescription") ?? string.Empty);
            desc.RegisterValueChangedCallback(e => Stage("ServerDescription", e.newValue));
            id.Add(Ui.FormRow("Description", desc));

            var admin = Ui.Field(string.Empty, password: true);
            admin.RegisterValueChangedCallback(e => { _adminPassword = e.newValue ?? string.Empty; RefreshStatus(); });
            var pass = Ui.Field(string.Empty, password: true);
            pass.RegisterValueChangedCallback(e => Stage("ServerPassword", e.newValue));
            id.Add(Ui.TwoUp(
                Ui.HalfRow("Admin password", admin, "RCON, REST, every live control", true),
                Ui.HalfRow("Server password", pass, "blank = open server")));

            var maxPlayers = Ui.Field(_live.Display("ServerPlayerMaxNum") ?? "32");
            maxPlayers.RegisterValueChangedCallback(e => Stage("ServerPlayerMaxNum", e.newValue));
            var coop = Ui.Field(_live.Display("CoopPlayerMaxNum") ?? "4");
            coop.RegisterValueChangedCallback(e => Stage("CoopPlayerMaxNum", e.newValue));
            id.Add(Ui.TwoUp(
                Ui.HalfRow("Max players", maxPlayers, "default 32 — no published range"),
                Ui.HalfRow("Co-op players", coop, "invite/listen limit")));

            var port = Ui.Field(_live.Display("PublicPort") ?? "8211");
            port.RegisterValueChangedCallback(e => Stage("PublicPort", e.newValue));
            var region = Ui.Field(_live.Display("Region") ?? string.Empty);
            region.RegisterValueChangedCallback(e => Stage("Region", e.newValue));
            id.Add(Ui.TwoUp(
                Ui.HalfRow("Game port", port, "UDP, reachable from outside"),
                Ui.HalfRow("Region", region, "blank = auto")));
            pane.Add(id);

            var wm = Ui.Section("World mode",
                "Sets six flags. The game applies them, so setting them by hand may not stick (F-0022).");
            wm.Add(Ui.Segmented(new[] { "Dedicated_PvE", "Dedicated_PvP" }, _worldMode, m =>
            {
                _worldMode = m;
                RefreshStatus();
                ShowCategory(SetupKey);
            }));
            var mode = OptionCatalog.Mode(_worldMode);
            if (mode?.fields != null)
            {
                var wrap = Ui.Row();
                wrap.style.flexWrap = Wrap.Wrap;
                wrap.style.marginTop = 8;
                foreach (var f in mode.fields)
                {
                    var on = string.Equals(f.value, "True", StringComparison.OrdinalIgnoreCase);
                    wrap.Add(Ui.Pill(f.key + " = " + f.value,
                        on ? new Color(0.62f, 0.80f, 0.70f) : Ui.TextDim,
                        on ? new Color(0.102f, 0.176f, 0.145f) : new Color(0.141f, 0.141f, 0.176f)));
                }
                wm.Add(wrap);
            }
            pane.Add(wm);

            var df = Ui.Section("Difficulty", "A preset fills a group of rates. Custom keeps whatever you set.");
            df.Add(Ui.Segmented(new[] { "Easy", "Normal", "Hard", "Hardcore", "Custom" }, _difficulty, d =>
            {
                _difficulty = d;
                Stage("Difficulty", d);
                RefreshStatus();
                ShowCategory(SetupKey);
            }));

            var governed = OptionCatalog.PresetGovernedFields();
            if (_difficulty == "Custom")
            {
                df.Add(Ui.Notice("Custom — your values are kept",
                    "The " + governed.Count + " preset-governed fields stay as you set them. "
                    + "This is the safe choice while F-0022 is untested.", Ui.Good));
            }
            else
            {
                var preset = OptionCatalog.Preset(_difficulty);
                if (preset?.fields != null)
                {
                    var wrap = Ui.Row();
                    wrap.style.flexWrap = Wrap.Wrap;
                    wrap.style.marginTop = 8;
                    foreach (var f in preset.fields.Where(x => governed.Contains(x.key)).OrderBy(x => x.key))
                        wrap.Add(Ui.Pill(f.key + " = " + f.value,
                            new Color(0.85f, 0.70f, 0.78f), new Color(0.212f, 0.129f, 0.161f)));
                    df.Add(wrap);
                }
                df.Add(Ui.Notice("These may overwrite what you set  (F-0022)",
                    "UPalOptionSubsystem::ApplyWorldPreset exists in the server binary. If it runs at load, "
                    + "your edits to those " + governed.Count + " fields are replaced. Untested — pick Custom to be safe.",
                    Ui.Warn));
            }
            pane.Add(df);

            var ac = Ui.Section("Remote access and listing",
                "How this tool talks to the server, and whether players can find it.");

            var rest = Ui.Check(_restApi, "Enable  —  required for Broadcast, Shutdown and live stats");
            rest.RegisterValueChangedCallback(e => { _restApi = e.newValue; Stage("RESTAPIEnabled", e.newValue ? "True" : "False"); RefreshStatus(); });
            var restPort = Ui.Field(_live.Display("RESTAPIPort") ?? "8212");
            restPort.RegisterValueChangedCallback(e => Stage("RESTAPIPort", e.newValue));
            ac.Add(Ui.TwoUp(Ui.HalfRow("REST API", rest), Ui.HalfRow("REST port", restPort, "TCP — not the internet")));

            var rcon = Ui.Check(_rcon, "Enable  —  only for teleport and spectate");
            rcon.RegisterValueChangedCallback(e => { _rcon = e.newValue; Stage("RCONEnabled", e.newValue ? "True" : "False"); });
            var rconPort = Ui.Field(_live.Display("RCONPort") ?? "25575");
            rconPort.RegisterValueChangedCallback(e => Stage("RCONPort", e.newValue));
            ac.Add(Ui.TwoUp(Ui.HalfRow("RCON", rcon), Ui.HalfRow("RCON port", rconPort, "TCP — not the internet")));

            var lobby = Ui.Check(_publicLobby, "List in the in-game community server browser  (-publiclobby)");
            lobby.RegisterValueChangedCallback(e => _publicLobby = e.newValue);
            ac.Add(Ui.FormRow("Community listing", lobby, "a launch flag, not an ini key"));

            ac.Add(Ui.Notice("Port hygiene",
                "8211 is the game port and must be reachable from outside — that is the point of a dedicated "
                + "server. 8212 and 25575 are the admin plane and should never be forwarded.", Ui.Accent));
            pane.Add(ac);

            if (string.IsNullOrEmpty(_adminPassword))
                pane.Add(Ui.Notice("Admin password is empty",
                    "The server rejects every REST call with \"Unauthorized (AdminPassword is empty)\", so Broadcast, "
                    + "Shutdown and the live stats will not work.", Ui.Bad));
        }

        // ---------------------------------------------------------------- state

        private string Current(OptionDef o) =>
            _staged.TryGetValue(o.iniKey, out var staged) ? staged
            : (_live.Display(o.iniKey) ?? o.@default ?? string.Empty);

        private void Stage(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _staged[key] = value ?? string.Empty;
            RefreshStatus();
        }

        private void ShowPreview() =>
            PreviewPanel.Show(this, _staged, _live, _adminPassword, _publicLobby,
                new ServerInstall(ServerPaths.InstallPath).ConfigPath);

    }
}
