using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PalPanel.Io
{
    /// <summary>
    /// Round-trip reader/writer for PalWorldSettings.ini.
    ///
    /// The whole server config is a single line of the form:
    ///     OptionSettings=(Key=Value,Key=Value,CrossplayPlatforms=(Steam,Xbox),ServerName="x")
    ///
    /// Rules this codec must never break:
    ///   * Split only on commas at paren-depth 0 and outside quotes. CrossplayPlatforms is a
    ///     nested paren group and ServerDescription may legitimately contain a comma.
    ///   * Keys are case-insensitive, and the struct casing does NOT always match the INI
    ///     casing (struct "autoSaveSpan" vs INI "AutoSaveSpan"). Look up case-insensitively
    ///     and always write back the spelling already in the file, or the server sees a
    ///     duplicate key.
    ///   * Floats are written with exactly 6 decimals under InvariantCulture. A comma decimal
    ///     separator from a localised machine would silently shred the file.
    ///   * Unknown/most keys are preserved verbatim: this class edits, it never regenerates.
    /// </summary>
    public sealed class PalIniDocument
    {
        private readonly List<string> _tokens;   // raw "Key=Value" strings, order preserved
        private readonly string _header;         // through "OptionSettings=("
        private readonly string _tail;           // ")" + trailing whitespace

        private PalIniDocument(string header, List<string> tokens, string tail)
        {
            _header = header;
            _tokens = tokens;
            _tail = tail;
        }

        public int Count => _tokens.Count;

        /// <summary>Keys in file order, using each key's spelling as it appears in the file.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var t in _tokens)
                {
                    var i = t.IndexOf('=');
                    yield return i < 0 ? t : t.Substring(0, i);
                }
            }
        }

        // ---------------------------------------------------------------- parse

        private static readonly Regex BlockRx =
            new Regex(@"(OptionSettings=\()(.*)(\)\s*)$",
                      RegexOptions.Singleline | RegexOptions.Multiline);

        public static PalIniDocument Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var m = BlockRx.Match(text);
            if (!m.Success)
                throw new FormatException(
                    "OptionSettings=(...) block not found. This does not look like a " +
                    "PalWorldSettings.ini.");

            var header = text.Substring(0, m.Groups[1].Index) + m.Groups[1].Value;
            var tokens = SplitTopLevel(m.Groups[2].Value);
            return new PalIniDocument(header, tokens, m.Groups[3].Value);
        }

        /// <summary>The ini section the dedicated server reads OptionSettings from.</summary>
        public const string SectionHeader = "[/Script/Pal.PalGameWorldSettings]";

        /// <summary>
        /// True when <paramref name="text"/> holds an OptionSettings=(...) block. A clean
        /// dedicated server writes PalWorldSettings.ini with 2 bytes and no block, and runs on
        /// built-in defaults (Docs/FIXES_AND_ERRORS.md F-0010).
        /// </summary>
        public static bool HasOptionSettings(string text) =>
            !string.IsNullOrEmpty(text) && BlockRx.IsMatch(text);

        /// <summary>
        /// Builds a live PalWorldSettings.ini from DefaultPalWorldSettings.ini: the section header
        /// and its OptionSettings line only. The default file's comments say edits to it are
        /// ignored, so they are not copied. CRLF, like the file the server writes.
        /// </summary>
        public static string BuildSeedFromDefault(string defaultText)
        {
            var m = BlockRx.Match(defaultText ?? string.Empty);
            if (!m.Success)
                throw new FormatException(
                    "DefaultPalWorldSettings.ini has no OptionSettings=(...) block.");

            var block = m.Groups[1].Value + m.Groups[2].Value + ")";
            return SectionHeader + "\r\n" + block + "\r\n";
        }

        /// <summary>Split on commas that sit at paren-depth 0 and outside double quotes.</summary>
        internal static List<string> SplitTopLevel(string body)
        {
            var result = new List<string>();
            var cur = new StringBuilder();
            var depth = 0;
            var quoted = false;

            foreach (var ch in body)
            {
                if (ch == '"') quoted = !quoted;

                if (!quoted)
                {
                    if (ch == '(') depth++;
                    else if (ch == ')') depth--;
                    else if (ch == ',' && depth == 0)
                    {
                        result.Add(cur.ToString());
                        cur.Clear();
                        continue;
                    }
                }
                cur.Append(ch);
            }
            result.Add(cur.ToString());
            return result;
        }

        // ---------------------------------------------------------------- access

        private int IndexOfKey(string key)
        {
            for (var i = 0; i < _tokens.Count; i++)
            {
                var eq = _tokens[i].IndexOf('=');
                if (eq < 0) continue;
                if (string.Equals(_tokens[i].Substring(0, eq), key,
                                  StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public bool Contains(string key) => IndexOfKey(key) >= 0;

        /// <summary>Raw value text, still quoted/parenthesised as stored. False if absent.</summary>
        public bool TryGetRaw(string key, out string raw)
        {
            var i = IndexOfKey(key);
            if (i < 0) { raw = null; return false; }
            var eq = _tokens[i].IndexOf('=');
            raw = _tokens[i].Substring(eq + 1);
            return true;
        }

        /// <summary>
        /// Overwrite a key's value, keeping the file's own spelling of the key.
        /// Returns false if the key is absent (use <see cref="InsertAfter"/> for new keys).
        /// </summary>
        public bool SetRaw(string key, string rawValue)
        {
            var i = IndexOfKey(key);
            if (i < 0) return false;
            var eq = _tokens[i].IndexOf('=');
            _tokens[i] = _tokens[i].Substring(0, eq + 1) + rawValue;
            return true;
        }

        /// <summary>
        /// Insert a key that the file does not yet have, positioned directly after
        /// <paramref name="afterKey"/> so file order keeps matching the struct order.
        /// Appends at the end when the anchor is missing.
        /// </summary>
        public void InsertAfter(string afterKey, string key, string rawValue)
        {
            if (Contains(key))
                throw new InvalidOperationException($"Key already present: {key}");

            var token = key + "=" + rawValue;
            var anchor = IndexOfKey(afterKey);
            if (anchor < 0) _tokens.Add(token);
            else _tokens.Insert(anchor + 1, token);
        }

        public string ToText() => _header + string.Join(",", _tokens) + _tail;

        // ---------------------------------------------------------------- value formatting
        //
        // Every conversion pins InvariantCulture on purpose. Do not "simplify" these.

        public static string FormatFloat(float v) =>
            v.ToString("0.000000", CultureInfo.InvariantCulture);

        public static string FormatInt(int v) =>
            v.ToString(CultureInfo.InvariantCulture);

        public static string FormatBool(bool v) => v ? "True" : "False";

        /// <summary>Quote a string value. Embedded quotes are stripped, not escaped -- the
        /// game's own parser has no escape syntax, so a quote in a server name would break it.</summary>
        public static string FormatString(string s) =>
            "\"" + (s ?? string.Empty).Replace("\"", string.Empty) + "\"";

        public static string FormatEnum(string member) => member ?? string.Empty;

        public static string FormatArray(IEnumerable<string> members) =>
            "(" + string.Join(",", members) + ")";

        public static float ParseFloat(string raw, float fallback = 0f) =>
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v : fallback;

        public static int ParseInt(string raw, int fallback = 0) =>
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v : fallback;

        public static bool ParseBool(string raw) =>
            string.Equals(raw, "True", StringComparison.OrdinalIgnoreCase);

        public static string ParseString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                return raw.Substring(1, raw.Length - 2);
            return raw;
        }

        public static List<string> ParseArray(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(raw)) return list;
            var inner = raw.Trim();
            if (inner.StartsWith("(") && inner.EndsWith(")"))
                inner = inner.Substring(1, inner.Length - 2);
            if (inner.Length == 0) return list;
            foreach (var part in inner.Split(','))
            {
                var p = part.Trim();
                if (p.Length > 0) list.Add(p);
            }
            return list;
        }
    }
}
