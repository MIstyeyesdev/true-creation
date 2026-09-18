using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrueServer
{
    /// <summary>One dedicated-server installation.</summary>
    public sealed class ServerInstall
    {
        public string InstallPath;

        public ServerInstall(string path) { InstallPath = path ?? string.Empty; }

        public string ExePath => Path.Combine(InstallPath, "PalServer.exe");

        public string ShippingExePath => Path.Combine(
            InstallPath, "Pal", "Binaries", "Win64", "PalServer-Win64-Shipping.exe");

        public string ConfigPath => Path.Combine(
            InstallPath, "Pal", "Saved", "Config", "WindowsServer", "PalWorldSettings.ini");

        public string DefaultConfigPath => Path.Combine(InstallPath, "DefaultPalWorldSettings.ini");

        public string SaveGamesRoot => Path.Combine(InstallPath, "Pal", "Saved", "SaveGames", "0");

        /// <summary>Palworld's own mod manager settings. ONE per install -- never per world (F-0018).</summary>
        public string ModSettingsPath => Path.Combine(InstallPath, "Mods", "PalModSettings.ini");

        /// <summary>
        /// Per-install local settings. Holds DedicatedServerName, which names the save folder the
        /// server uses -- the closest thing to a "which world is live" switch (F-0026).
        /// </summary>
        public string LocalSettingsPath => Path.Combine(
            InstallPath, "Pal", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");

        public bool IsValid => !string.IsNullOrEmpty(InstallPath) && File.Exists(ExePath);

        public long ConfigBytes => File.Exists(ConfigPath) ? new FileInfo(ConfigPath).Length : -1;

        /// <summary>
        /// A clean server ships a 2-byte PalWorldSettings.ini with no OptionSettings line and runs
        /// on built-in defaults. See F-0010.
        /// </summary>
        public bool ConfigIsEmpty => ConfigBytes >= 0 && ConfigBytes < 16;
    }

    /// <summary>One world save folder under SaveGames/0.</summary>
    public sealed class ServerWorld
    {
        /// <summary>The save folder name. Until LevelMeta.sav is parsed this is all we have (F-0020).</summary>
        public string Guid;
        public string DisplayName;
        public string FolderPath;
        public DateTime LastModifiedUtc;
        public int PlayerCount;
        public bool HasBackups;

        /// <summary>
        /// Reported to silently override PalWorldSettings.ini (F-0011). If this exists the ini
        /// the panel shows may not be what the world actually runs, so it is surfaced, not hidden.
        /// </summary>
        public bool HasWorldOption;

        /// <summary>
        /// This world is the one named by DedicatedServerName, so it is the one the server loads.
        /// An install can list many worlds; exactly one is selected (F-0026).
        /// </summary>
        public bool IsSelected;

        public string LevelSavPath => Path.Combine(FolderPath ?? string.Empty, "Level.sav");
        public string WorldOptionPath => Path.Combine(FolderPath ?? string.Empty, "WorldOption.sav");
    }

    /// <summary>
    /// Per-install local settings (`GameUserSettings.ini`, `[/Script/Pal.PalGameLocalSettings]`).
    ///
    /// The field that matters here is `DedicatedServerName` -- a `Str` holding a 32-hex GUID in
    /// the same shape as a `SaveGames/0/&lt;guid&gt;` folder name. See F-0026 for what is proven
    /// about it and what is not.
    /// </summary>
    public sealed class LocalSettingsSnapshot
    {
        public bool Exists;
        public string DedicatedServerName = string.Empty;

        public bool HasSelection => !string.IsNullOrWhiteSpace(DedicatedServerName);

        public static LocalSettingsSnapshot Read(string path)
        {
            var s = new LocalSettingsSnapshot();
            if (!File.Exists(path)) return s;
            s.Exists = true;

            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("DedicatedServerName", StringComparison.OrdinalIgnoreCase)) continue;
                    var eq = line.IndexOf('=');
                    if (eq > 0) s.DedicatedServerName = line.Substring(eq + 1).Trim().Trim('"');
                    break;
                }
            }
            catch { /* unreadable: leave empty */ }

            return s;
        }
    }

    public static class WorldRepository
    {
        public static List<ServerWorld> Discover(ServerInstall install)
        {
            var worlds = new List<ServerWorld>();
            if (install == null || !Directory.Exists(install.SaveGamesRoot)) return worlds;

            var selected = LocalSettingsSnapshot.Read(install.LocalSettingsPath).DedicatedServerName;

            foreach (var dir in Directory.GetDirectories(install.SaveGamesRoot))
            {
                var level = Path.Combine(dir, "Level.sav");
                if (!File.Exists(level)) continue;   // not a world folder

                var players = Path.Combine(dir, "Players");
                worlds.Add(new ServerWorld
                {
                    Guid = Path.GetFileName(dir),
                    DisplayName = Path.GetFileName(dir),
                    FolderPath = dir,
                    LastModifiedUtc = File.GetLastWriteTimeUtc(level),
                    PlayerCount = Directory.Exists(players)
                        ? Directory.GetFiles(players, "*.sav").Length
                        : 0,
                    HasBackups = Directory.Exists(Path.Combine(dir, "backup")),
                    HasWorldOption = File.Exists(Path.Combine(dir, "WorldOption.sav")),
                    IsSelected = !string.IsNullOrWhiteSpace(selected)
                                 && string.Equals(Path.GetFileName(dir), selected,
                                                  StringComparison.OrdinalIgnoreCase),
                });
            }

            // Selected world first -- it is the one that runs -- then most recently played.
            return worlds
                .OrderByDescending(w => w.IsSelected)
                .ThenByDescending(w => w.LastModifiedUtc)
                .ToList();
        }
    }

    /// <summary>
    /// Read-only view of the live config. This class NEVER writes -- the write path is gated on
    /// the server being stopped and on an explicit confirmation, and is not built yet (F-0019).
    /// </summary>
    public sealed class ConfigSnapshot
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Loaded { get; private set; }
        public int KeyCount => _values.Count;

        /// <summary>Keys the tool must never display. Reading them is fine; showing them is not.</summary>
        private static readonly HashSet<string> Secret = new HashSet<string>(
            new[] { "AdminPassword", "ServerPassword", "PublicIP" }, StringComparer.OrdinalIgnoreCase);

        public static ConfigSnapshot Read(string iniPath)
        {
            var snap = new ConfigSnapshot();
            if (!File.Exists(iniPath)) return snap;

            string text;
            try { text = File.ReadAllText(iniPath); }
            catch { return snap; }

            // The whole option block is one line: OptionSettings=(Key=Value,Key=Value,...)
            var open = text.IndexOf("OptionSettings=(", StringComparison.OrdinalIgnoreCase);
            if (open < 0) return snap;                      // clean server: nothing to read
            open += "OptionSettings=(".Length;

            var close = text.LastIndexOf(')');
            if (close <= open) return snap;

            var body = text.Substring(open, close - open);
            snap.Loaded = true;

            // Split on commas that are not inside quotes or parentheses -- CrossplayPlatforms is
            // itself a parenthesised list, so a naive Split(',') shreds it.
            var depth = 0;
            var inQuote = false;
            var start = 0;
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c == '"') inQuote = !inQuote;
                else if (!inQuote && c == '(') depth++;
                else if (!inQuote && c == ')') depth--;
                else if (!inQuote && depth == 0 && c == ',')
                {
                    snap.Take(body.Substring(start, i - start));
                    start = i + 1;
                }
            }
            snap.Take(body.Substring(start));
            return snap;
        }

        private void Take(string pair)
        {
            if (string.IsNullOrWhiteSpace(pair)) return;
            var eq = pair.IndexOf('=');
            if (eq <= 0) return;
            var key = pair.Substring(0, eq).Trim();
            var val = pair.Substring(eq + 1).Trim();
            if (key.Length > 0) _values[key] = val;
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public string Raw(string key) => _values.TryGetValue(key, out var v) ? v : null;

        /// <summary>Value safe to put on screen. Secrets become a fixed marker, never the value.</summary>
        public string Display(string key)
        {
            if (!_values.TryGetValue(key, out var v)) return null;
            if (Secret.Contains(key))
                return string.IsNullOrEmpty(v) || v == "\"\"" ? "(not set)" : "(set)";
            return v.Trim('"');
        }

        public bool IsTrue(string key) =>
            string.Equals(Raw(key)?.Trim('"'), "True", StringComparison.OrdinalIgnoreCase);

        /// <summary>A secret counts as set only if it is present AND non-empty.</summary>
        public bool SecretIsSet(string key)
        {
            var v = Raw(key);
            if (v == null) return false;
            v = v.Trim().Trim('"');
            return v.Length > 0;
        }
    }

    /// <summary>bGlobalEnableMod / WorkshopRootDir / ActiveModList -- one set per install.</summary>
    public sealed class ModSettingsSnapshot
    {
        public bool Exists;
        public bool GlobalEnable;
        public string WorkshopRootDir = string.Empty;
        public List<string> ActiveMods = new List<string>();

        public static ModSettingsSnapshot Read(string path)
        {
            var s = new ModSettingsSnapshot();
            if (!File.Exists(path)) return s;
            s.Exists = true;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                if (key.Equals("bGlobalEnableMod", StringComparison.OrdinalIgnoreCase))
                    s.GlobalEnable = val.Equals("True", StringComparison.OrdinalIgnoreCase);
                else if (key.Equals("WorkshopRootDir", StringComparison.OrdinalIgnoreCase))
                    s.WorkshopRootDir = val.Trim('"');
                else if (key.Equals("ActiveModList", StringComparison.OrdinalIgnoreCase))
                    s.ActiveMods = val.Trim('(', ')')
                        .Split(',')
                        .Select(x => x.Trim().Trim('"'))
                        .Where(x => x.Length > 0)
                        .ToList();
            }
            return s;
        }
    }

    public static class ServerProcess
    {
        /// <summary>
        /// True while the dedicated server is running. Config writes are refused in that state --
        /// the server reads its ini only at boot and can overwrite it on shutdown.
        /// </summary>
        public static bool IsRunning()
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName("PalServer-Win64-Shipping").Length > 0
                    || System.Diagnostics.Process.GetProcessesByName("PalServer").Length > 0;
            }
            catch { return false; }
        }
    }
}
