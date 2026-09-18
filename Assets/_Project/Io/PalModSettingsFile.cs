using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PalPanel.Io
{
    /// <summary>
    /// Round-trip reader/writer for Mods\PalModSettings.ini, the settings file of Palworld's
    /// built-in mod manager. Shape on the client and on the dedicated server (CRLF):
    /// <code>
    /// [PalModSettings]
    /// bGlobalEnableMod=True|False
    /// WorkshopRootDir=&lt;folder holding the Workshop item folders&gt;
    /// ConfigVersion=1.0
    /// ActiveModList=&lt;PackageName&gt;      (one line per active package)
    /// </code>
    /// A clean dedicated server writes bGlobalEnableMod=False, an empty WorkshopRootDir and no
    /// ActiveModList lines. Lines this class does not own (bNeedShowErrorOnNextStart, ...) are
    /// kept in their original order; the ActiveModList lines are written last, as the client does.
    /// </summary>
    public sealed class PalModSettingsFile
    {
        public const string SectionHeader = "[PalModSettings]";
        private const string KeyEnable = "bGlobalEnableMod";
        private const string KeyRoot = "WorkshopRootDir";
        private const string KeyVersion = "ConfigVersion";
        private const string KeyActive = "ActiveModList";

        private readonly List<string> _lines;   // every non-ActiveModList line, in order

        public bool GlobalEnableMod { get; set; }
        public string WorkshopRootDir { get; set; } = string.Empty;

        /// <summary>Active package names in file order (order is kept on write).</summary>
        public List<string> ActiveModList { get; } = new List<string>();

        private PalModSettingsFile(List<string> lines) => _lines = lines;

        /// <summary>The file a clean dedicated server writes on first start.</summary>
        public static PalModSettingsFile CreateDefault() => Parse(
            SectionHeader + "\r\n" + KeyEnable + "=False\r\n" + KeyRoot + "=\r\n" +
            KeyVersion + "=1.0\r\n");

        public static PalModSettingsFile Parse(string text)
        {
            var lines = new List<string>();
            var file = new PalModSettingsFile(lines);

            foreach (var raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var eq = line.IndexOf('=');
                var key = eq > 0 ? line.Substring(0, eq).Trim() : null;
                var value = eq > 0 ? line.Substring(eq + 1).Trim() : null;

                if (Is(key, KeyActive))
                {
                    if (value.Length > 0 &&
                        !file.ActiveModList.Contains(value, StringComparer.OrdinalIgnoreCase))
                        file.ActiveModList.Add(value);
                    continue;
                }

                if (Is(key, KeyEnable))
                    file.GlobalEnableMod = string.Equals(value, "True",
                                                         StringComparison.OrdinalIgnoreCase);
                else if (Is(key, KeyRoot))
                    file.WorkshopRootDir = value;

                lines.Add(line);
            }

            // Trailing blank lines are the file's closing blank line; ToText writes it back.
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);

            if (lines.Count == 0 ||
                !lines[0].Trim().Equals(SectionHeader, StringComparison.OrdinalIgnoreCase))
                lines.Insert(0, SectionHeader);

            EnsureKey(lines, KeyEnable, "False");
            EnsureKey(lines, KeyRoot, string.Empty);
            EnsureKey(lines, KeyVersion, "1.0");
            return file;
        }

        private static bool Is(string key, string expected) =>
            key != null && key.Equals(expected, StringComparison.OrdinalIgnoreCase);

        private static void EnsureKey(List<string> lines, string key, string value)
        {
            if (lines.Any(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))) return;
            lines.Add(key + "=" + value);
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (var line in _lines)
            {
                if (line.StartsWith(KeyEnable + "=", StringComparison.OrdinalIgnoreCase))
                    sb.Append(KeyEnable).Append('=').Append(GlobalEnableMod ? "True" : "False");
                else if (line.StartsWith(KeyRoot + "=", StringComparison.OrdinalIgnoreCase))
                    sb.Append(KeyRoot).Append('=').Append(WorkshopRootDir ?? string.Empty);
                else
                    sb.Append(line);
                sb.Append("\r\n");
            }

            foreach (var package in ActiveModList)
                sb.Append(KeyActive).Append('=').Append(package).Append("\r\n");

            sb.Append("\r\n");
            return sb.ToString();
        }
    }
}
