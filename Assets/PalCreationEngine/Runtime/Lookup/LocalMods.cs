using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PalCreationEngine.Lookup
{
    /// <summary>
    /// Where Palworld is installed on THE PC RUNNING THE TOOL, found the way Steam records it (2026-09-18):
    /// %ProgramFiles(x86)%\Steam, then %ProgramFiles%\Steam -> steamapps\libraryfolders.vdf (the library that lists
    /// app 1623730 first) -> steamapps\appmanifest_1623730.acf ("installdir") -> steamapps\common\&lt;installdir&gt;.
    /// Fixed drives' SteamLibrary / Steam folders are the fallback. Nothing names one machine; read-only.
    /// </summary>
    public sealed class PalworldInstall
    {
        public const string AppId = "1623730";

        public string Path { get; private set; }

        /// <summary>How it was found: "override", "Steam library list ...", "drive scan"; null when not found.</summary>
        public string How { get; private set; }

        public List<string> Tried { get; } = new List<string>();
        public bool Found => !string.IsNullOrEmpty(Path);

        /// <summary>A Palworld install holds Pal\Content\Paks.</summary>
        public static bool LooksLikeInstall(string dir) =>
            !string.IsNullOrWhiteSpace(dir) && Directory.Exists(System.IO.Path.Combine(dir.Trim(), "Pal", "Content", "Paks"));

        public static PalworldInstall Find(string overridePath = null)
        {
            var r = new PalworldInstall();
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                r.Tried.Add("override " + overridePath.Trim());
                if (LooksLikeInstall(overridePath)) { r.Path = overridePath.Trim(); r.How = "override"; return r; }
            }

            foreach (var steam in SteamRoots())
                foreach (var lib in Libraries(steam, r.Tried))
                {
                    var acf = System.IO.Path.Combine(lib, "steamapps", "appmanifest_" + AppId + ".acf");
                    if (!File.Exists(acf)) continue;
                    var candidate = System.IO.Path.Combine(lib, "steamapps", "common", InstallDir(acf) ?? "Palworld");
                    r.Tried.Add(candidate);
                    if (LooksLikeInstall(candidate))
                    {
                        r.Path = candidate;
                        r.How = "Steam library list (libraryfolders.vdf, appmanifest_" + AppId + ".acf)";
                        return r;
                    }
                }

            foreach (var drive in FixedDrives())
                foreach (var root in new[] { "SteamLibrary", "Steam", @"Program Files (x86)\Steam", @"Program Files\Steam", @"Games\Steam" })
                {
                    var candidate = System.IO.Path.Combine(drive, root, "steamapps", "common", "Palworld");
                    r.Tried.Add(candidate);
                    if (LooksLikeInstall(candidate)) { r.Path = candidate; r.How = "drive scan"; return r; }
                }
            return r;
        }

        /// <summary>The default Steam folders, by environment variable: %ProgramFiles(x86)%\Steam, %ProgramFiles%\Steam.</summary>
        private static IEnumerable<string> SteamRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in new[] { "%ProgramFiles(x86)%", "%ProgramFiles%" })
            {
                var expanded = Environment.ExpandEnvironmentVariables(variable);
                if (expanded == variable || string.IsNullOrWhiteSpace(expanded)) continue;   // not defined on this PC
                var steam = System.IO.Path.Combine(expanded, "Steam");
                if (Directory.Exists(steam) && seen.Add(steam)) yield return steam;
            }
        }

        /// <summary>Library roots from libraryfolders.vdf: libraries that list the app first, the Steam folder itself last.</summary>
        private static IEnumerable<string> Libraries(string steam, List<string> tried)
        {
            var withApp = new List<string>();
            var others = new List<string>();
            var vdf = System.IO.Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            tried.Add(vdf);
            var text = ReadText(vdf);
            if (text != null)
            {
                // "path"  "D:\\SteamLibrary"  ...  "apps" { "1623730" "..." }   up to the next "path"
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"(.*?)(?=\"path\"|\\z)", RegexOptions.Singleline))
                {
                    var path = m.Groups[1].Value.Replace(@"\\", @"\");
                    (m.Groups[2].Value.Contains("\"" + AppId + "\"") ? withApp : others).Add(path);
                }
            }
            return withApp.Concat(others).Concat(new[] { steam }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string InstallDir(string acf)
        {
            var text = ReadText(acf);
            var m = text == null ? null : Regex.Match(text, "\"installdir\"\\s*\"([^\"]+)\"");
            return m != null && m.Success ? m.Groups[1].Value : null;
        }

        private static IEnumerable<string> FixedDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (IOException) { yield break; }
            catch (UnauthorizedAccessException) { yield break; }
            foreach (var d in drives)
            {
                bool ok;
                try { ok = d.IsReady && d.DriveType == DriveType.Fixed; }
                catch (IOException) { ok = false; }
                if (ok) yield return d.RootDirectory.FullName;
            }
        }

        internal static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>A new Pal blueprint a mod on this PC adds: one DT_PalBPClass row in a PalSchema mod's raw/.</summary>
    public sealed class ModBlueprint
    {
        public string Mod;        // PalSchema mod folder name
        public string Package;    // Workshop PackageName that installed it; null = installed by hand or by Vortex
        public string RowKey;     // its DT_PalBPClass row key
        public string ClassPath;  // /Game/... class path the row renders with
        public string File;       // source file, relative to the install

        public string PickLabel => Mod + " : " + RowKey;
    }

    /// <summary>A .pak in one of the pak mod folders; listed by file only, its contents are never opened.</summary>
    public sealed class ModPak
    {
        public string Folder;        // ~mods | LogicMods | ~WorkshopMods
        public string RelativePath;  // relative to the install
        public long Bytes;           // 0 when unknown (a link)
        public bool IsLink;          // a symbolic link (Vortex installs these)
        public string Package;       // Workshop PackageName that installed it, or null
    }

    /// <summary>A PalSchema mod's blueprints/ folder: patches to existing classes, listed for information (not models).</summary>
    public sealed class ModPatchSet
    {
        public string Mod;
        public string Package;
        public int Keys;
    }

    /// <summary>
    /// Read-only scan of the mod folders of ONE Palworld install - the PC running the tool - for the "Include mods on
    /// this PC" option (NEW_PAL_CONTRACT.md section 4, rule 10). Only these folders are read:
    ///   Pal\Content\Paks\~mods, LogicMods, ~WorkshopMods       paks, listed by file; contents not opened
    ///   Mods\NativeMods\UE4SS\Mods\PalSchema\mods\*\raw         DT_PalBPClass rows = new blueprints; icon rows
    ///   Mods\NativeMods\UE4SS\Mods\PalSchema\mods\*\pals        row keys (collision check)
    ///   Mods\NativeMods\UE4SS\Mods\PalSchema\mods\*\blueprints  patches to existing classes (information)
    ///   Mods\ManagedMods\*\Info.json, InstallManifest.json      which Workshop package installed which file
    /// Nothing is written anywhere and nothing is cached on disk: the result lives in memory, so a copy of the project
    /// carries none of it and another PC sees only the mods installed there.
    /// </summary>
    public sealed class LocalModScan
    {
        public static readonly string[] PakFolders = { "~mods", "LogicMods", "~WorkshopMods" };

        public string InstallPath { get; private set; }
        public readonly List<ModBlueprint> Blueprints = new List<ModBlueprint>();
        public readonly List<ModPak> Paks = new List<ModPak>();
        public readonly List<ModPatchSet> Patches = new List<ModPatchSet>();
        /// <summary>Files that could not be read or parsed, with the reason.</summary>
        public readonly List<string> Problems = new List<string>();

        private readonly Dictionary<string, Dictionary<string, string>> _icons =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _rowKeys =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonLoadSettings Lenient = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Ignore,
        };

        public static string PalSchemaModsFolder(string install) =>
            Path.Combine(install, "Mods", "NativeMods", "UE4SS", "Mods", "PalSchema", "mods");

        public static LocalModScan Run(string installPath)
        {
            var scan = new LocalModScan { InstallPath = installPath };
            if (!PalworldInstall.LooksLikeInstall(installPath))
            {
                scan.Problems.Add(installPath + ": not a Palworld install (no Pal\\Content\\Paks)");
                return scan;
            }
            var owners = scan.ReadReceipts();
            scan.ReadPaks(owners);
            scan.ReadPalSchemaMods(owners);
            return scan;
        }

        /// <summary>Workshop receipts: installed file (relative, lower case, forward slashes) -> PackageName.</summary>
        private Dictionary<string, string> ReadReceipts()
        {
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = Path.Combine(InstallPath, "Mods", "ManagedMods");
            if (!Directory.Exists(root)) return owners;
            foreach (var dir in SafeDirectories(root))
            {
                var info = ParseFile(Path.Combine(dir, "Info.json")) as JObject;
                var package = (string)info?["PackageName"] ?? Path.GetFileName(dir);
                var manifest = ParseFile(Path.Combine(dir, "InstallManifest.json")) as JObject;
                if (manifest?["Files"] is JArray files)
                    foreach (var f in files.OfType<JValue>().Select(v => v.Value as string).Where(s => !string.IsNullOrEmpty(s)))
                        owners[Norm(f)] = package;
            }
            return owners;
        }

        private void ReadPaks(Dictionary<string, string> owners)
        {
            foreach (var folder in PakFolders)
            {
                var dir = Path.Combine(InstallPath, "Pal", "Content", "Paks", folder);
                if (!Directory.Exists(dir)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.pak", SearchOption.AllDirectories).ToList(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Problems.Add(folder + ": " + e.Message);
                    continue;
                }
                foreach (var file in files)
                {
                    var rel = Relative(file);
                    long bytes = 0;
                    var link = false;
                    try
                    {
                        var fi = new FileInfo(file);
                        link = (fi.Attributes & FileAttributes.ReparsePoint) != 0;
                        bytes = fi.Length;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    Paks.Add(new ModPak
                    {
                        Folder = folder, RelativePath = rel, Bytes = bytes, IsLink = link,
                        Package = owners.TryGetValue(Norm(rel), out var p) ? p : null,
                    });
                }
            }
        }

        private void ReadPalSchemaMods(Dictionary<string, string> owners)
        {
            var modsRoot = PalSchemaModsFolder(InstallPath);
            if (!Directory.Exists(modsRoot)) return;
            foreach (var modDir in SafeDirectories(modsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var mod = Path.GetFileName(modDir);
                var prefix = Norm(Relative(modDir)) + "/";
                var package = owners.FirstOrDefault(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Value;
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _rowKeys[mod] = keys;

                foreach (var file in JsonFiles(Path.Combine(modDir, "raw")))
                {
                    if (!(ParseFile(file) is JObject tables)) continue;
                    foreach (var table in tables.Properties().Where(t => t.Value is JObject))
                    {
                        var isClass = table.Name.IndexOf("BPClass", StringComparison.OrdinalIgnoreCase) >= 0;
                        var isIcon = table.Name.IndexOf("CharacterIcon", StringComparison.OrdinalIgnoreCase) >= 0;
                        foreach (var row in ((JObject)table.Value).Properties())
                        {
                            keys.Add(row.Name);
                            var path = FirstGamePath(row.Value);
                            if (string.IsNullOrEmpty(path)) continue;
                            if (isClass)
                                Blueprints.Add(new ModBlueprint { Mod = mod, Package = package, RowKey = row.Name, ClassPath = path, File = Relative(file) });
                            else if (isIcon)
                            {
                                if (!_icons.TryGetValue(mod, out var icons)) _icons[mod] = icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                icons[row.Name] = path;
                            }
                        }
                    }
                }

                foreach (var file in JsonFiles(Path.Combine(modDir, "pals")))
                    if (ParseFile(file) is JObject pals)
                        foreach (var row in pals.Properties()) keys.Add(row.Name);

                var patched = 0;
                foreach (var file in JsonFiles(Path.Combine(modDir, "blueprints")))
                    if (ParseFile(file) is JObject patches) patched += patches.Count;
                if (patched > 0) Patches.Add(new ModPatchSet { Mod = mod, Package = package, Keys = patched });
            }
        }

        public bool HasBoss(ModBlueprint bp) => BossOf(bp) != null;

        public string IconFor(ModBlueprint bp) =>
            _icons.TryGetValue(bp.Mod, out var icons) && icons.TryGetValue(bp.RowKey, out var icon) ? icon : null;

        private ModBlueprint BossOf(ModBlueprint bp) =>
            Blueprints.FirstOrDefault(b => string.Equals(b.Mod, bp.Mod, StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(b.RowKey, "BOSS_" + bp.RowKey, StringComparison.OrdinalIgnoreCase));

        /// <summary>Everything a new Pal built on this mod blueprint needs, for this build only (never persisted).</summary>
        public ModModelChoice Choose(ModBlueprint bp)
        {
            if (bp == null) throw new ArgumentNullException(nameof(bp));
            var boss = BossOf(bp);
            var choice = new ModModelChoice
            {
                Mod = bp.Mod, Package = bp.Package, RowKey = bp.RowKey, ClassPath = bp.ClassPath,
                BossClassPath = boss?.ClassPath,
                IconPath = IconFor(bp),
                BossIconPath = boss != null ? IconFor(boss) : null,
            };
            foreach (var b in Blueprints.Where(b => string.Equals(b.Mod, bp.Mod, StringComparison.OrdinalIgnoreCase)))
                choice.KnownClassPaths.Add(b.ClassPath);
            if (_icons.TryGetValue(bp.Mod, out var icons))
                foreach (var p in icons.Values) choice.KnownIconPaths.Add(p);
            if (_rowKeys.TryGetValue(bp.Mod, out var keys))
                foreach (var k in keys) choice.ModRowKeys.Add(k);
            return choice;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The first "/Game/..." string in a row: a plain string, or an object's AssetPathName.</summary>
        private static string FirstGamePath(JToken token)
        {
            switch (token)
            {
                case JValue v when v.Value is string s:
                    return s.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? s : null;
                case JObject o:
                    if (o["AssetPathName"] is JValue a && a.Value is string ap && ap.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase)) return ap;
                    foreach (var p in o.Properties())
                    {
                        var hit = FirstGamePath(p.Value);
                        if (hit != null) return hit;
                    }
                    return null;
                case JArray arr:
                    foreach (var item in arr)
                    {
                        var hit = FirstGamePath(item);
                        if (hit != null) return hit;
                    }
                    return null;
                default:
                    return null;
            }
        }

        private JToken ParseFile(string path)
        {
            var text = PalworldInstall.ReadText(path);
            if (text == null) return null;
            try { return JToken.Parse(text, Lenient); }
            catch (JsonException e)
            {
                Problems.Add(Relative(path) + ": not JSON (" + e.Message + ")");
                return null;
            }
        }

        private IEnumerable<string> JsonFiles(string dir)
        {
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            try
            {
                return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Problems.Add(Relative(dir) + ": " + e.Message);
                return Enumerable.Empty<string>();
            }
        }

        private IEnumerable<string> SafeDirectories(string dir)
        {
            try { return Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Problems.Add(Relative(dir) + ": " + e.Message);
                return Enumerable.Empty<string>();
            }
        }

        private string Relative(string full)
        {
            var root = InstallPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        private static string Norm(string rel) => rel.Replace('\\', '/').TrimStart('.', '/');
    }

    /// <summary>
    /// A model from a mod on this PC, picked for one new Pal (the "Include mods on this PC" option). Held in memory for
    /// the build; the package gets the mod's Workshop package as a dependency, so it works only where that mod is installed.
    /// </summary>
    public sealed class ModModelChoice
    {
        public string Mod;
        public string Package;
        public string RowKey;
        public string ClassPath;
        public string BossClassPath;
        public string IconPath;
        public string BossIconPath;

        /// <summary>Every class path this mod's rows provide: what validation accepts in place of the vanilla export.</summary>
        public readonly HashSet<string> KnownClassPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> KnownIconPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The mod's own row keys: a new Pal must not reuse one.</summary>
        public readonly HashSet<string> ModRowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Label => Mod + " : " + RowKey;
        public string Requirement => Package ?? Mod;
    }
}
