using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueServer
{
    /// <summary>
    /// The exact file that would be written, before anything is written.
    ///
    /// Not a diff and not a summary: the whole `OptionSettings=(...)` line, all 122 ini keys in
    /// struct order, because that is what the server actually reads. Changed keys are marked so
    /// the diff is still visible inside the full picture.
    ///
    /// Shown as an overlay over the World settings tab (Close returns to it), so it runs wherever
    /// the panel runs. Read-only: this panel has no write path at all.
    /// </summary>
    public sealed class PreviewPanel : VisualElement
    {
        private struct Line
        {
            public string Key;
            public string Value;
            public bool Changed;
            public bool Secret;
        }

        private List<Line> _lines;
        private string _iniText;
        private string _launch;
        private string _targetPath;
        private Label _status;

        /// <summary>Covers <paramref name="host"/> with the preview; Close removes it.</summary>
        public static void Show(VisualElement host, IReadOnlyDictionary<string, string> staged, ConfigSnapshot live,
                                string adminPassword, bool publicLobby, string targetPath)
        {
            if (host == null) return;
            var panel = new PreviewPanel();
            panel.style.position = Position.Absolute;
            panel.style.left = 0; panel.style.right = 0; panel.style.top = 0; panel.style.bottom = 0;
            panel.Build(staged, live, adminPassword, publicLobby, targetPath);
            host.Add(panel);
            panel.BringToFront();
        }

        private void Build(IReadOnlyDictionary<string, string> staged, ConfigSnapshot live,
                           string adminPassword, bool publicLobby, string targetPath)
        {
            _targetPath = targetPath;
            _lines = new List<Line>();

            foreach (var o in OptionCatalog.All.Where(x => x.inIni))
            {
                string value;
                var changed = false;

                if (o.isSecret && o.name == "AdminPassword" && !string.IsNullOrEmpty(adminPassword))
                {
                    value = "\"" + new string('*', Math.Min(adminPassword.Length, 12)) + "\"";
                    changed = true;
                }
                else if (staged.TryGetValue(o.iniKey, out var s))
                {
                    value = Quote(o, s);
                    changed = true;
                }
                else
                {
                    var liveRaw = live?.Raw(o.iniKey);
                    value = liveRaw ?? o.@default ?? string.Empty;
                }

                _lines.Add(new Line
                {
                    Key = o.iniKey,
                    Value = value,
                    Changed = changed,
                    Secret = o.isSecret,
                });
            }

            _iniText =
                "[/Script/Pal.PalGameWorldSettings]\r\n"
                + "OptionSettings=("
                + string.Join(",", _lines.Select(l => l.Key + "=" + l.Value))
                + ")\r\n";

            _launch = "PalServer.exe" + (publicLobby ? " -publiclobby" : string.Empty);

            Render();
        }

        private static string Quote(OptionDef o, string v)
        {
            v = v ?? string.Empty;
            if (!o.quoted) return v;
            if (v.StartsWith("\"") && v.EndsWith("\"")) return v;
            return "\"" + v + "\"";
        }

        private void Render()
        {
            var root = this;
            root.Clear();
            root.style.backgroundColor = Ui.Page;
            root.style.paddingLeft = 12; root.style.paddingRight = 12;
            root.style.paddingTop = 10; root.style.paddingBottom = 10;

            var changed = _lines.Count(l => l.Changed);

            var head = Ui.CardBox(12);
            head.Add(Ui.Text("This is the whole file that would be written", 15, Ui.TextHi, true));
            head.Add(Ui.Text(
                _lines.Count + " keys · " + changed + " changed by you · the rest carried from the "
                + "server's current config, or the game's default where the config has none.",
                11, Ui.TextMid));
            head.Add(Ui.Text("Target: " + _targetPath, 10, Ui.TextDim));

            var btns = Ui.Row();
            btns.style.marginTop = 8;
            btns.Add(Ui.Btn("Copy ini text", () =>
            {
                GUIUtility.systemCopyBuffer = _iniText;
                Say("Copied the ini text.");
            }));
            btns.Add(Ui.Btn("Copy launch line", () =>
            {
                GUIUtility.systemCopyBuffer = _launch;
                Say("Copied the launch line.");
            }));
            btns.Add(Ui.Btn("Save a copy…", SaveCopy));
            btns.Add(Ui.Btn("Close", RemoveFromHierarchy));
            head.Add(btns);
            _status = Ui.Text(string.Empty, 10, Ui.Good);
            head.Add(_status);
            root.Add(head);

            root.Add(Ui.Notice("Nothing has been written",
                "This panel has no write path. Copy it, or save a copy somewhere of your choosing — "
                + "the live config is untouched either way.", Ui.Accent));

            // launch line
            var lc = Ui.CardBox(10);
            lc.Add(Ui.SectionLabel("LAUNCH"));
            lc.Add(Mono(_launch, Ui.TextHi));
            root.Add(lc);

            // key list
            var listCard = Ui.CardBox(10);
            listCard.style.flexGrow = 1;
            listCard.Add(Ui.SectionLabel("OPTIONSETTINGS  —  " + _lines.Count + " KEYS, STRUCT ORDER"));

            var scroll = Ui.Scroll();
            scroll.style.flexGrow = 1;
            foreach (var l in _lines)
            {
                var row = Ui.Row();
                row.style.minHeight = 17;

                var marker = Ui.Text(l.Changed ? "●" : " ", 10, l.Changed ? Ui.Good : Ui.TextDim);
                marker.style.width = 14;
                marker.style.flexShrink = 0;
                row.Add(marker);

                var k = Mono(l.Key, l.Changed ? Ui.TextHi : Ui.TextMid);
                k.style.width = 330;
                k.style.flexShrink = 0;
                row.Add(k);

                row.Add(Mono("= " + l.Value, l.Changed ? Ui.Good : Ui.TextDim));
                scroll.Add(row);
            }
            listCard.Add(scroll);
            root.Add(listCard);
        }

        private static Label Mono(string s, Color c)
        {
            var l = Ui.Text(s, 11, c);
            l.style.whiteSpace = WhiteSpace.NoWrap;
            return l;
        }

        private void Say(string text)
        {
            if (_status != null) _status.text = text;
        }

        /// <summary>
        /// Saves to a path the user picks. Deliberately NOT defaulted to the live config folder --
        /// this is a preview, and a save dialog that opens on the real file invites an accident.
        /// </summary>
        private void SaveCopy()
        {
            var path = ServerHost.SaveFilePanel(
                "Save a copy of the preview",
                ServerPaths.OutputRoot,
                "PalWorldSettings.preview.ini",
                "ini");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, _iniText);
                Say("Saved: " + path);
                ServerHost.Reveal(path);
            }
            catch (Exception e)
            {
                ServerHost.Message("Could not save", e.Message);
            }
        }
    }
}
