using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using PalItemgen.Lookup;
using PalItemgen.Model;

namespace PalItemgen.Export
{
    public sealed class PackageResult
    {
        public string RootFolder;
        public List<string> Files = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Writes a complete mod package to disk. Every file type keeps its own
    /// install target, matching what the game's loader does with them:
    ///
    ///   Steam Workshop package (what the Mod Uploader and the loader expect)
    ///     &lt;out&gt;/&lt;PackageName or Steam item id&gt;/
    ///       Info.json, thumbnail.png, README.txt
    ///       Scripts/                 main.lua + pm_*.lua   -> Mods\NativeMods\UE4SS\Mods\&lt;Package&gt;
    ///       PalSchema/buildings/     stations json         -> Mods\NativeMods\UE4SS\Mods\PalSchema\mods\&lt;Package&gt;\buildings
    ///       PalSchema/raw/           extra table patches   -> ...\PalSchema\mods\&lt;Package&gt;\raw
    ///       PalSchema/blueprints/    extra blueprint edits -> ...\PalSchema\mods\&lt;Package&gt;\blueprints
    ///       PalSchema/resources/images/  custom icons
    ///       Paks/                    *.pak (optional)      -> Pal\Content\Paks\~WorkshopMods\&lt;Package&gt;
    ///       LogicMods/               *.pak (optional)      -> Pal\Content\Paks\LogicMods
    ///
    ///   Manual install (files already arranged like the game folder)
    ///     &lt;out&gt;/&lt;PackageName&gt;_ManualInstall/
    ///       Mods/NativeMods/UE4SS/Mods/&lt;Package&gt;/Scripts/*.lua
    ///       Mods/NativeMods/UE4SS/Mods/PalSchema/mods/&lt;Package&gt;/{buildings,raw,blueprints,resources}/
    ///       Pal/Content/Paks/~mods/*.pak            (loose paks)
    ///       Pal/Content/Paks/LogicMods/*.pak
    ///       INSTALL.txt
    ///
    /// The Lua runtime scripts are copied from <paramref name="luaSourceDir"/>
    /// (the tool's StreamingAssets/PalItemgen/lua); generated files are written fresh.
    /// </summary>
    public static class ModPackager
    {
        public static readonly string[] RuntimeScripts =
        {
            "main.lua", "pm_util.lua", "pm_archive.lua", "pm_items.lua", "pm_targets.lua",
            "pm_commands.lua", "pm_planner.lua", "pm_game.lua", "pm_engine.lua", "pm_workers.lua",
            "pm_menu.lua", "pm_ui.lua", "pm_board.lua",
        };

        /// <summary>
        /// Hard-copies a written package into the game: PalSchema/ to
        /// Mods\NativeMods\UE4SS\Mods\PalSchema\mods\&lt;pkg&gt;, Scripts/ to
        /// Mods\NativeMods\UE4SS\Mods\&lt;pkg&gt;\Scripts, and the mods.txt line
        /// (below the PalSchema line, above the keybinds marker). Returns the
        /// two destination folders. Works from either layout's root.
        /// </summary>
        public static List<string> InstallIntoGame(ModProject project, string packageRoot, string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(Path.Combine(gameFolder, "Pal")))
                throw new DirectoryNotFoundException("Game folder not found (it must contain the Pal and Mods folders): " + gameFolder);
            var pkg = SafeName(project.PackageName);
            var modsRoot = Path.Combine(gameFolder, "Mods", "NativeMods", "UE4SS", "Mods");
            // no UE4SS = nothing to install into: a fabricated mod tree would report success and load nothing
            if (!Directory.Exists(modsRoot) || !File.Exists(Path.Combine(modsRoot, "mods.txt")))
                throw new DirectoryNotFoundException("UE4SS is not installed in this game folder (no Mods\\NativeMods\\UE4SS\\Mods\\mods.txt). Subscribe to the UE4SS Experimental and PalSchema Workshop items and start the game once, then install again: " + gameFolder);
            // the package root holds PalSchema/ + Scripts/ (Workshop layout) or the manual tree
            var schemaSrc = Path.Combine(packageRoot, "PalSchema");
            var scriptsSrc = Path.Combine(packageRoot, "Scripts");
            if (!Directory.Exists(schemaSrc))
            {
                schemaSrc = Path.Combine(packageRoot, "Mods", "NativeMods", "UE4SS", "Mods", "PalSchema", "mods", pkg);
                scriptsSrc = Path.Combine(packageRoot, "Mods", "NativeMods", "UE4SS", "Mods", pkg, "Scripts");
            }
            // both sources must exist BEFORE anything installed is deleted
            if (!Directory.Exists(schemaSrc) || !Directory.Exists(scriptsSrc))
                throw new DirectoryNotFoundException("The package has no PalSchema and Scripts folders to install (generate it first): " + packageRoot);
            var schemaDst = Path.Combine(modsRoot, "PalSchema", "mods", pkg);
            var scriptsDst = Path.Combine(modsRoot, pkg, "Scripts");
            // Ownership (2026-09-15 audit pm-csharp-01): never delete or take over a folder this
            // tool did not install. A reserved loader name, a Workshop receipt under Mods\ManagedMods,
            // a Lua folder of that name without our manifest, or a PalSchema folder without one of
            // our *_stations.json all mean the name belongs to someone else. Nothing is touched then.
            var reserved = new[] { "PalSchema", "UE4SS", "shared", "Keybinds", "BPModLoaderMod", "BPML_GenericFunctions",
                "ConsoleCommandsMod", "ConsoleEnablerMod", "ActorDumperMod", "LineTraceMod", "jsbLuaProfilerMod",
                "SplitScreenMod", "CheatManagerEnablerMod" };
            if (reserved.Any(r => r.Equals(pkg, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("'" + pkg + "' is a loader or built-in mod name; pick another package name. Nothing was changed.");
            if (Directory.Exists(Path.Combine(gameFolder, "Mods", "ManagedMods", pkg)))
                throw new InvalidOperationException("'" + pkg + "' is a Workshop-managed mod in this game folder (Mods\\ManagedMods\\" + pkg + " exists); the loader owns it. Pick another package name. Nothing was changed.");
            var luaDir = Path.Combine(modsRoot, pkg);
            var luaOurs = File.Exists(Path.Combine(luaDir, ManifestName)) || File.Exists(Path.Combine(luaDir, "Scripts", "pm_config.lua"));
            if (Directory.Exists(luaDir) && !luaOurs)
                throw new InvalidOperationException("A mod folder named '" + pkg + "' already exists here and was not installed by this tool (no " + ManifestName + "). Nothing was changed; pick another package name or remove that mod yourself.");
            var schemaOurs = luaOurs || (Directory.Exists(Path.Combine(schemaDst, "buildings")) && Directory.GetFiles(Path.Combine(schemaDst, "buildings"), "*_stations.json").Length > 0);
            if (Directory.Exists(schemaDst) && !schemaOurs)
                throw new InvalidOperationException("A PalSchema mod named '" + pkg + "' already exists here and was not written by this tool. Nothing was changed; pick another package name.");
            // the old building file goes first so a renamed or removed building does not linger
            var oldBuildings = Path.Combine(schemaDst, "buildings");
            if (Directory.Exists(oldBuildings)) Directory.Delete(oldBuildings, true);
            var oldTables = Path.Combine(schemaDst, "raw", pkg.ToLowerInvariant() + "_tables.json");
            if (File.Exists(oldTables)) File.Delete(oldTables);
            // the old Scripts folder goes too, so a script dropped from the package does not linger
            if (Directory.Exists(scriptsDst)) Directory.Delete(scriptsDst, true);
            CopyTree(schemaSrc, schemaDst);
            CopyTree(scriptsSrc, scriptsDst);
            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            var lines = File.Exists(modsTxt) ? File.ReadAllLines(modsTxt).ToList() : new List<string>();
            var at = lines.FindIndex(l => l.Trim().StartsWith(pkg + " :", StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                var keybinds = lines.FindIndex(l => l.StartsWith("; Built-in keybinds"));
                if (keybinds >= 0) lines.Insert(keybinds, pkg + " : 1"); else lines.Add(pkg + " : 1");
                File.WriteAllLines(modsTxt, lines);
            }
            else if (!lines[at].Trim().EndsWith(": 1"))
            {
                // a line switched off by hand is switched back on: an install that leaves the mod off is no install
                lines[at] = pkg + " : 1";
                File.WriteAllLines(modsTxt, lines);
            }
            // a manifest next to the scripts: which project put this here, from where and when,
            // so the tool can list every package it installed and uninstall any of them by name
            var manifest = new JObject
            {
                ["package"] = pkg,
                ["modName"] = project.ModName ?? "",
                ["version"] = project.Version ?? "",
                ["project"] = project.ProjectPath ?? "",
                ["source"] = packageRoot ?? "",
                ["installedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["schema"] = schemaDst,
                ["scripts"] = scriptsDst,
            };
            File.WriteAllText(Path.Combine(modsRoot, pkg, ManifestName), manifest.ToString());
            return new List<string> { schemaDst, scriptsDst };
        }

        public const string ManifestName = "palitemgen_install.json";

        /// <summary>One package this tool (or an older run of it) installed into the game folder.</summary>
        public sealed class InstalledPackage
        {
            public string Package;
            public string ModName;
            public string Version;
            public string Project;
            public string Source;
            public string InstalledAt;
            public bool HasScripts;
            public bool HasSchema;
            /// <summary>"enabled", "disabled" or "no line" (mods.txt).</summary>
            public string ModsTxt;
            public string State => (HasScripts && HasSchema ? "scripts + buildings" : HasScripts ? "scripts only" : HasSchema ? "buildings only" : "nothing installed") + ", mods.txt: " + ModsTxt;
            public string Detail => string.IsNullOrEmpty(InstalledAt)
                ? "installed by an older run of the tool (no manifest)"
                : $"installed {InstalledAt}" + (string.IsNullOrEmpty(Project) ? "" : " from project " + Project);
        }

        /// <summary>
        /// Every package in the game's mod folders that came from this tool: a Scripts
        /// folder carrying a manifest (or pm_config.lua, older installs) and / or a
        /// PalSchema mods folder written for it. Sorted by name.
        /// </summary>
        public static List<InstalledPackage> ListInstalled(string gameFolder)
        {
            var list = new List<InstalledPackage>();
            if (string.IsNullOrWhiteSpace(gameFolder)) return list;
            var modsRoot = Path.Combine(gameFolder, "Mods", "NativeMods", "UE4SS", "Mods");
            if (!Directory.Exists(modsRoot)) return list;
            var byName = new Dictionary<string, InstalledPackage>(StringComparer.OrdinalIgnoreCase);
            InstalledPackage Get(string name)
            {
                if (!byName.TryGetValue(name, out var ip)) { ip = new InstalledPackage { Package = name }; byName[name] = ip; }
                return ip;
            }
            foreach (var dir in Directory.GetDirectories(modsRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "PalSchema", StringComparison.OrdinalIgnoreCase)) continue;
                var manifestPath = Path.Combine(dir, ManifestName);
                var ours = File.Exists(manifestPath) || File.Exists(Path.Combine(dir, "Scripts", "pm_config.lua"));
                if (!ours) continue;
                var ip = Get(name);
                ip.HasScripts = Directory.Exists(Path.Combine(dir, "Scripts"));
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var m = JObject.Parse(File.ReadAllText(manifestPath));
                        ip.ModName = m["modName"]?.ToString();
                        ip.Version = m["version"]?.ToString();
                        ip.Project = m["project"]?.ToString();
                        ip.Source = m["source"]?.ToString();
                        ip.InstalledAt = m["installedAt"]?.ToString();
                    }
                    catch (Exception) { /* an unreadable manifest still lists the package */ }
                }
            }
            var schemaMods = Path.Combine(modsRoot, "PalSchema", "mods");
            if (Directory.Exists(schemaMods))
            {
                foreach (var dir in Directory.GetDirectories(schemaMods))
                {
                    var name = Path.GetFileName(dir);
                    // a PalSchema folder counts as ours when its scripts are ours, or when it carries our buildings file shape
                    var known = byName.ContainsKey(name);
                    var buildings = Path.Combine(dir, "buildings");
                    var oursByFile = Directory.Exists(buildings) && Directory.GetFiles(buildings, "*_stations.json").Length > 0;
                    if (!known && !oursByFile) continue;
                    Get(name).HasSchema = true;
                }
            }
            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            var lines = File.Exists(modsTxt) ? File.ReadAllLines(modsTxt) : new string[0];
            foreach (var ip in byName.Values)
            {
                var line = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(ip.Package + " :", StringComparison.OrdinalIgnoreCase));
                ip.ModsTxt = line == null ? "no line" : (line.EndsWith(": 1") ? "enabled" : "disabled");
                list.Add(ip);
            }
            list.Sort((a, b) => string.Compare(a.Package, b.Package, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Removes a package from the game: its PalSchema folder, its Scripts folder and its
        /// mods.txt line. Only folders this tool wrote go: the Lua folder must carry our
        /// manifest or a pm_config.lua, the PalSchema folder our buildings file; the loader
        /// itself ("PalSchema") is refused. <paramref name="exactName"/> uses the folder name
        /// as listed (ListInstalled) instead of re-sanitising it.
        /// </summary>
        public static List<string> UninstallFromGame(string packageName, string gameFolder, bool exactName = false)
        {
            var pkg = exactName ? (packageName ?? "").Trim() : SafeName(packageName);
            if (string.IsNullOrWhiteSpace(pkg) || pkg.Equals("PalSchema", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("'" + packageName + "' is not a package of ours (PalSchema is the loader every package needs); nothing was removed.");
            var modsRoot = Path.Combine(gameFolder ?? "", "Mods", "NativeMods", "UE4SS", "Mods");
            var removed = new List<string>();
            var luaDir = Path.Combine(modsRoot, pkg);
            var ours = File.Exists(Path.Combine(luaDir, ManifestName)) || File.Exists(Path.Combine(luaDir, "Scripts", "pm_config.lua"));
            if (Directory.Exists(luaDir) && ours) { Directory.Delete(luaDir, true); removed.Add(luaDir); }
            var schemaDir = Path.Combine(modsRoot, "PalSchema", "mods", pkg);
            var buildings = Path.Combine(schemaDir, "buildings");
            if (Directory.Exists(buildings) && Directory.GetFiles(buildings, "*_stations.json").Length > 0)
            {
                Directory.Delete(schemaDir, true);
                removed.Add(schemaDir);
            }
            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            if (File.Exists(modsTxt) && (removed.Count > 0 || ours))
            {
                var lines = File.ReadAllLines(modsTxt).ToList();
                var n = lines.RemoveAll(l => l.Trim().StartsWith(pkg + " :", StringComparison.OrdinalIgnoreCase));
                if (n > 0) { File.WriteAllLines(modsTxt, lines); removed.Add(modsTxt + " (line removed)"); }
            }
            return removed;
        }

        /// <summary>Letters, digits and underscores only: what a row key may contain.</summary>
        public static string SafeId(string id) => new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        private static void CopyTree(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        /// <summary>Best guess at the Palworld install from a Workshop content path, or the common Steam location.</summary>
        public static string GuessGameFolder(string outputFolder)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(outputFolder))
            {
                var i = outputFolder.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) candidates.Add(Path.Combine(outputFolder.Substring(0, i), "steamapps", "common", "Palworld"));
            }
            // The live client is the default Steam install on C:; other library drives are
            // tried after it. (The old E:\SteamLibrary client is gone since 2026-09.)
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Palworld");
            foreach (var drive in new[] { "C", "D", "E", "F", "G" })
            {
                candidates.Add($@"{drive}:\SteamLibrary\steamapps\common\Palworld");
                candidates.Add($@"{drive}:\Steam\steamapps\common\Palworld");
                candidates.Add($@"{drive}:\Games\Steam\steamapps\common\Palworld");
            }
            foreach (var c in candidates)
            {
                try { if (Directory.Exists(Path.Combine(c, "Pal"))) return c; }
                catch { /* an unreadable drive is simply not the answer */ }
            }
            return "";
        }

        /// <summary>A package name usable as a folder: letters and digits only, never empty (the Info.json rule).</summary>
        public static string SafeName(string packageName)
        {
            var cleaned = new string((packageName ?? "").Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length > 0 ? cleaned : "PalProductionManager";
        }

        public static PackageResult Write(ModProject project, GameData data, string luaSourceDir, string defaultThumbnailPng = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.OutputFolder)) throw new ArgumentException("Output folder is required.");
            if (!Directory.Exists(luaSourceDir)) throw new DirectoryNotFoundException("Lua runtime folder not found: " + luaSourceDir);

            var result = new PackageResult();
            var workshop = project.Layout == OutputLayout.SteamWorkshop;
            var pkg = SafeName(project.PackageName);
            string scriptsDir, schemaDir, paksDir, logicDir;
            if (workshop)
            {
                var folderName = string.IsNullOrWhiteSpace(project.SteamItemId) ? pkg : project.SteamItemId.Trim();
                result.RootFolder = Path.Combine(project.OutputFolder, folderName);
                Directory.CreateDirectory(result.RootFolder);
                scriptsDir = Path.Combine(result.RootFolder, "Scripts");
                schemaDir = Path.Combine(result.RootFolder, "PalSchema");
                paksDir = Path.Combine(result.RootFolder, "Paks");
                logicDir = Path.Combine(result.RootFolder, "LogicMods");
                var thumb = Path.Combine(result.RootFolder, "thumbnail.png");
                if (!string.IsNullOrEmpty(project.ThumbnailPng) && File.Exists(project.ThumbnailPng))
                {
                    // the chosen thumbnail may already be the package's own (a project pointed at its Workshop folder)
                    if (!string.Equals(Path.GetFullPath(project.ThumbnailPng), Path.GetFullPath(thumb), StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Copy(project.ThumbnailPng, thumb, true); }
                        catch (IOException e) { result.Warnings.Add("thumbnail not copied (" + e.Message + "); the existing thumbnail.png stays."); }
                    }
                    result.Files.Add(thumb);
                }
                else if (!string.IsNullOrEmpty(defaultThumbnailPng) && File.Exists(defaultThumbnailPng))
                {
                    File.Copy(defaultThumbnailPng, thumb, true);
                    result.Files.Add(thumb);
                    result.Warnings.Add("No thumbnail chosen; the placeholder thumbnail.png was used. Replace it before uploading.");
                }
                else
                {
                    result.Warnings.Add("No thumbnail.png written. The Mod Uploader wants one before upload.");
                }
            }
            else
            {
                result.RootFolder = Path.Combine(project.OutputFolder, pkg + "_ManualInstall");
                Directory.CreateDirectory(result.RootFolder);
                scriptsDir = Path.Combine(result.RootFolder, "Mods", "NativeMods", "UE4SS", "Mods", pkg, "Scripts");
                schemaDir = Path.Combine(result.RootFolder, "Mods", "NativeMods", "UE4SS", "Mods", "PalSchema", "mods", pkg);
                paksDir = Path.Combine(result.RootFolder, "Pal", "Content", "Paks", "~mods");
                logicDir = Path.Combine(result.RootFolder, "Pal", "Content", "Paks", "LogicMods");
                WriteText(result, Path.Combine(result.RootFolder, "INSTALL.txt"), ManualInstallText(project));
            }

            // generated output of an earlier Generate must not survive: a dropped script or
            // a stale raw table would otherwise be reinstalled with the new package
            if (Directory.Exists(scriptsDir)) Directory.Delete(scriptsDir, true);
            var staleBuildings = Path.Combine(schemaDir, "buildings");
            if (Directory.Exists(staleBuildings)) Directory.Delete(staleBuildings, true);
            var staleTables = Path.Combine(schemaDir, "raw", pkg.ToLowerInvariant() + "_tables.json");
            if (File.Exists(staleTables)) File.Delete(staleTables);
            Directory.CreateDirectory(scriptsDir);
            Directory.CreateDirectory(schemaDir);

            // Runtime scripts, copied verbatim.
            foreach (var name in RuntimeScripts)
            {
                var src = Path.Combine(luaSourceDir, name);
                if (!File.Exists(src)) throw new FileNotFoundException("Runtime script missing: " + src);
                var dst = Path.Combine(scriptsDir, name);
                File.Copy(src, dst, true);
                result.Files.Add(dst);
            }
            // Generated Lua.
            WriteText(result, Path.Combine(scriptsDir, "pm_config.lua"), LuaWriter.Config(project, data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_items.lua"), LuaWriter.Items(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_recipes.lua"), LuaWriter.Recipes(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_enums.lua"), LuaWriter.Enums(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_benches.lua"), LuaWriter.Benches(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_pals.lua"), LuaWriter.Pals(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_icons.lua"), LuaWriter.Icons(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_workhard.lua"), LuaWriter.WorkHard(data));
            WriteText(result, Path.Combine(scriptsDir, "pm_data_buildings.lua"), LuaWriter.Buildings(data));

            // PalSchema: buildings (ours), then any extra raw / blueprint files, each in its own folder.
            var buildingsDir = Path.Combine(schemaDir, "buildings");
            Directory.CreateDirectory(buildingsDir);
            WriteText(result, Path.Combine(buildingsDir, pkg.ToLowerInvariant() + "_stations.json"),
                BuildingJsonWriter.ToJson(BuildingJsonWriter.Build(project, data)));
            // rows outside the buildings loader (a copied quarry's item product row)
            var rawTables = BuildingJsonWriter.RawTables(project, data);
            if (rawTables != null)
            {
                var rawDir = Path.Combine(schemaDir, "raw");
                Directory.CreateDirectory(rawDir);
                WriteText(result, Path.Combine(rawDir, pkg.ToLowerInvariant() + "_tables.json"), BuildingJsonWriter.ToJson(rawTables));
            }
            foreach (var s in project.Stations.Where(s => s.Enabled && !string.IsNullOrEmpty(s.CustomIconPng)))
            {
                if (!File.Exists(s.CustomIconPng))
                {
                    result.Warnings.Add($"{s.Id}: custom icon '{s.CustomIconPng}' not found; the game will show no icon.");
                    continue;
                }
                var imagesDir = Path.Combine(schemaDir, "resources", "images");
                Directory.CreateDirectory(imagesDir);
                var dst = Path.Combine(imagesDir, BuildingJsonWriter.ResourceIconName(s) + Path.GetExtension(s.CustomIconPng).ToLowerInvariant());
                File.Copy(s.CustomIconPng, dst, true);
                result.Files.Add(dst);
            }

            var hasPaks = false;
            var hasLogic = false;
            foreach (var extra in project.ExtraFiles ?? new List<ExtraFile>())
            {
                if (string.IsNullOrWhiteSpace(extra.Path)) continue;
                if (!File.Exists(extra.Path))
                {
                    result.Warnings.Add($"extra file not found, skipped: {extra.Path}");
                    continue;
                }
                string dstDir;
                switch (extra.Kind)
                {
                    case ExtraFileKind.Paks:
                        dstDir = paksDir;
                        hasPaks = true;
                        break;
                    case ExtraFileKind.LogicMods:
                        dstDir = logicDir;
                        hasLogic = true;
                        break;
                    case ExtraFileKind.PalSchemaRaw:
                        dstDir = Path.Combine(schemaDir, "raw");
                        break;
                    case ExtraFileKind.PalSchemaBlueprints:
                        dstDir = Path.Combine(schemaDir, "blueprints");
                        break;
                    default:
                        dstDir = Path.Combine(schemaDir, "raw");
                        break;
                }
                Directory.CreateDirectory(dstDir);
                var dst = Path.Combine(dstDir, Path.GetFileName(extra.Path));
                File.Copy(extra.Path, dst, true);
                result.Files.Add(dst);
            }

            if (workshop)
            {
                WriteText(result, Path.Combine(result.RootFolder, "Info.json"),
                    InfoJsonWriter.ToJson(InfoJsonWriter.Build(project, hasPaks, hasLogic)));
            }
            WriteText(result, Path.Combine(result.RootFolder, "README.txt"), ReadmeText(project));
            return result;
        }

        private static void WriteText(PackageResult r, string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
            r.Files.Add(path);
        }

        public static string ManualInstallText(ModProject p)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{p.ModName} - manual install");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("Copy the folders in here over your Palworld install (the folder that contains");
            sb.AppendLine("Palworld.exe). Every file type has its own place, the same places the Workshop");
            sb.AppendLine("loader uses:");
            sb.AppendLine($"  Mods\\NativeMods\\UE4SS\\Mods\\{SafeName(p.PackageName)}\\Scripts\\                 UE4SS Lua");
            sb.AppendLine($"  Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\{SafeName(p.PackageName)}\\buildings\\   PalSchema buildings JSON");
            sb.AppendLine($"  Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\{SafeName(p.PackageName)}\\raw\\         PalSchema table patches (if any)");
            sb.AppendLine($"  Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\{SafeName(p.PackageName)}\\blueprints\\  PalSchema blueprint edits (if any)");
            sb.AppendLine("  Pal\\Content\\Paks\\~mods\\                                       loose .pak files (if any)");
            sb.AppendLine("  Pal\\Content\\Paks\\LogicMods\\                                   Blueprint logic .pak files (if any)");
            sb.AppendLine("  (Workshop installs put paks under Pal\\Content\\Paks\\~WorkshopMods\\<package> instead.)");
            sb.AppendLine();
            sb.AppendLine("Then add this line to Mods\\NativeMods\\UE4SS\\Mods\\mods.txt (above the Keybinds line):");
            sb.AppendLine($"  {SafeName(p.PackageName)} : 1");
            sb.AppendLine();
            sb.AppendLine("Requires the UE4SS Experimental (Palworld) and PalSchema Workshop items.");
            sb.AppendLine("Dedicated server: same files under the server's Mods folder.");
            return sb.ToString();
        }

        /// <summary>
        /// The manual placement of a generated package, in order. Shared by the
        /// package README and the project folder README so both say the same.
        /// </summary>
        public static string ManualPlacementText(ModProject p)
        {
            var pkg = SafeName(p.PackageName);
            var sb = new StringBuilder();
            sb.AppendLine("MANUAL INSTALL, IN THIS ORDER");
            sb.AppendLine("  <Palworld> = the folder that contains Palworld.exe,");
            sb.AppendLine("  for example C:\\Program Files (x86)\\Steam\\steamapps\\common\\Palworld");
            sb.AppendLine("  Needs the Workshop items \"UE4SS Experimental (Palworld)\" and \"PalSchema\" installed and enabled first.");
            sb.AppendLine($"  1. {pkg}\\PalSchema\\   ->  <Palworld>\\Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\{pkg}\\");
            sb.AppendLine("        the building rows (buildings\\), table patches (raw\\), blueprint edits (blueprints\\), icons (resources\\)");
            sb.AppendLine($"  2. {pkg}\\Scripts\\     ->  <Palworld>\\Mods\\NativeMods\\UE4SS\\Mods\\{pkg}\\Scripts\\");
            sb.AppendLine("        the Lua: main.lua, pm_config.lua (the settings), pm_*.lua");
            sb.AppendLine($"  3. <Palworld>\\Mods\\NativeMods\\UE4SS\\Mods\\mods.txt: add the line   {pkg} : 1");
            sb.AppendLine("        above the line \"; Built-in keybinds, do not move up!\"");
            sb.AppendLine($"  4. only when the package has them: Paks\\ -> <Palworld>\\Pal\\Content\\Paks\\~WorkshopMods\\{pkg}\\");
            sb.AppendLine("        and LogicMods\\ -> <Palworld>\\Pal\\Content\\Paks\\LogicMods\\");
            sb.AppendLine("  5. start the game. <Palworld>\\Mods\\NativeMods\\UE4SS\\UE4SS.log must show");
            sb.AppendLine($"        \"Starting Lua mod '{pkg}'\" and \"[PalSchema] Added building '<id>'\" for each building.");
            sb.AppendLine("  To remove: delete the folders from steps 1 and 2 and the mods.txt line");
            sb.AppendLine("  (the tool's \"Uninstall from game\" button does exactly that).");
            sb.AppendLine("  Dedicated server: the same folders under the server's Mods folder.");
            return sb.ToString();
        }

        /// <summary>README written next to the project file on every save: what the folder holds and how to place it by hand.</summary>
        public static string ProjectReadmeText(ModProject p)
        {
            var pkg = SafeName(p.PackageName);
            var sb = new StringBuilder();
            sb.AppendLine($"{p.ModName} - project folder");
            sb.AppendLine(new string('=', Math.Max(10, p.ModName.Length + 17)));
            sb.AppendLine("Everything for this item lives here:");
            sb.AppendLine($"  {pkg}.palitemgen.json      the project: open it in the tool with Load...");
            sb.AppendLine($"  {pkg}\\                     the generated package (Generate package button):");
            sb.AppendLine("                              Info.json, thumbnail.png, README.txt, PalSchema\\, Scripts\\");
            sb.AppendLine("                              ready for the Palworld Mod Uploader as it is");
            sb.AppendLine($"  {pkg}_ManualInstall\\       the same files laid out like the game folder");
            sb.AppendLine("                              (only when the manual layout is chosen)");
            sb.AppendLine("  Generate always rewrites the package from the project; edit the project, not the package.");
            sb.AppendLine("  \"Install into game\" copies the package into the game folder and adds the mods.txt line for you.");
            sb.AppendLine();
            sb.Append(ManualPlacementText(p));
            return sb.ToString();
        }

        public static string ReadmeText(ModProject p)
        {
            var sb = new StringBuilder();
            var pkg = SafeName(p.PackageName);
            var manager = p.Stations.FirstOrDefault(s => s.Enabled && s.IsManagerRole);
            var hasLines = p.Stations.Any(s => s.Enabled && s.Kind == StationKind.Line);
            var hasConsole = p.Stations.Any(s => s.Enabled && s.Kind == StationKind.ConsoleSign);
            sb.AppendLine(p.ModName);
            sb.AppendLine(new string('=', Math.Max(10, p.ModName.Length)));
            sb.AppendLine("A production chain station for Palworld bases: one screen for every production");
            sb.AppendLine("bench in the base, Pal assignment as on the Assignment Board, and stock targets.");
            sb.AppendLine("Requires UE4SS Experimental (Palworld) and PalSchema.");
            sb.AppendLine();
            sb.AppendLine("HOW TO USE");
            if (manager != null)
            {
                var fTab = p.Chain?.InteractTab ?? "chain";
                var fOpens = !p.Automation.OpenMenuOnInteract ? "the stand's own work-mode screen"
                    : fTab == "board" ? "the game's own Assignment Board with this base's producers only, grouped by family"
                    : fTab == "targets" ? "our window on the Targets page"
                    : fTab == "monitor" ? "our window on the Monitoring page"
                    : fTab == "prod" ? "our window on the Production page"
                    : "our Chain screen";
                var cTab = p.Chain?.CTab ?? "targets";
                var cOpens = cTab == "vanilla" ? "the game's Assignment Board, unchanged"
                    : cTab == "targets" ? "our Targets page (keep items stocked: item, total to keep, restock %)"
                    : cTab == "prod" ? "our window on the Production page"
                    : cTab == "monitor" ? "our window on the Monitoring page"
                    : "our Chain screen";
                sb.AppendLine($"1. Build \"{manager.Name}\" (Technology tab, level {manager.TechLevel}). It is a copy of the");
                sb.AppendLine("   Monitoring Stand, so it only ever sees the base it stands in.");
                sb.AppendLine($"2. Walk up to it. F opens {fOpens}.");
                sb.AppendLine($"   C opens {cOpens}. V is the stand's work-suitability screen.");
                sb.AppendLine("3. The Chain screen lists the base's production benches only (workbenches, assembly");
                sb.AppendLine("   lines, furnaces, kitchens, mills, medicine, sphere and weapon stations), grouped by");
                sb.AppendLine("   family with the strongest bench first. Each row: building icon, name, required work");
                sb.AppendLine("   suitability, Stopped / Working, the current product, one Pal circle per work spot.");
                sb.AppendLine($"   {(p.Chain?.AllowCancelFromList != false ? "\"stop\" cancels a producing row. " : "")}{(p.Chain?.ShowMakeNow != false ? "\"select\" picks a row for a make-now order on the Production page." : "")}");
                sb.AppendLine($"4. The menu key ({p.Automation.MenuKey}, {p.Automation.MenuFallbackKey} if taken) opens the same window from anywhere in a base (host only).");
                sb.AppendLine("5. Maintain targets (end goal, test the screens first): \"keep 100 Carbon Fiber, start");
                sb.AppendLine("   below 50\". When stock drops under the start line the station orders the shortfall");
                sb.AppendLine("   at the strongest free bench that can make it and fixed-assigns the best-suited base");
                sb.AppendLine("   Pal to it, releasing the Pal when the order is done. Several targets are served in");
                sb.AppendLine("   weighted lottery order. Targets come from the tool's Chain tab or the Chain screen.");
            }
            if (hasConsole)
            {
                sb.AppendLine("Console sign: interact, write one command per line, confirm. Commands:");
                sb.AppendLine("     keep <item> <count> [restock-below] [line]   drop <item>   top <item>");
                sb.AppendLine("     priority <a> <b> ...   follow on|off   pause | resume   clear   status   help");
            }
            if (hasLines)
            {
                sb.AppendLine("Production Line station: place an order at it like at any bench; the count becomes");
                sb.AppendLine("the target, order exactly 1 to remove it, infinite = top priority. It never crafts.");
            }
            sb.AppendLine();
            sb.AppendLine("STATIONS IN THIS PACKAGE");
            foreach (var s in p.Stations.Where(st => st.Enabled))
            {
                var role = s.Kind == StationKind.Manager ? "the chain station (a copy of the Monitoring Stand)"
                    : s.Kind == StationKind.ConsoleSign ? "command sign"
                    : s.Kind == StationKind.Copy ? "exact copy of " + s.MirrorId + (s.ActsAsManager ? ", also the chain station (production manager)" : "")
                    : "line '" + s.Line + "'";
                var model = s.ReuseMapObjectId == s.MirrorId ? "" : $", model {s.ReuseMapObjectId}";
                sb.AppendLine($"  {s.Name} [{s.Id}] - {role} (tech level {s.TechLevel}{model})");
            }
            sb.AppendLine();
            sb.AppendLine("WHERE THE WORKSHOP LOADER PUTS THE FILES");
            sb.AppendLine($"  Scripts\\      -> Mods\\NativeMods\\UE4SS\\Mods\\{pkg}");
            sb.AppendLine($"  PalSchema\\    -> Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\{pkg}  (buildings, raw, blueprints, resources)");
            sb.AppendLine($"  Paks\\         -> Pal\\Content\\Paks\\~WorkshopMods\\{pkg}   (only when the package ships paks)");
            sb.AppendLine("  LogicMods\\    -> Pal\\Content\\Paks\\LogicMods                        (only when the package ships logic paks)");
            sb.AppendLine();
            sb.Append(ManualPlacementText(p));
            sb.AppendLine();
            sb.AppendLine("RUNTIME FILES (written into the mod's own folder, Mods\\NativeMods\\UE4SS\\Mods\\<Mod>\\,");
            sb.AppendLine("next to Scripts\\; nothing is ever written to Pal\\Binaries\\Win64)");
            sb.AppendLine("  PalProductionManager_state.lua  targets per base");
            sb.AppendLine("  PalProductionManager_log.txt    what the station did and why");
            sb.AppendLine("  PalProductionManager_diag.txt   class/function dump (first run)");
            sb.AppendLine("  UE4SS's own log: Mods\\NativeMods\\UE4SS\\UE4SS.log");
            sb.AppendLine();
            sb.AppendLine("STATUS: the item, its rows and the screens are built; ordering at a bench from the");
            sb.AppendLine("station and the size / model overrides have not been proven in the game yet. The");
            sb.AppendLine("Lua tries several ways to place an order and reports which one worked in the log.");
            return sb.ToString();
        }
    }
}
