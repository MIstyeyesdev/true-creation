using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RingOfNoXp.Model;

namespace RingOfNoXp.Export
{
    /// <summary>
    /// Copies a generated package into the game and takes it out again.
    ///
    /// Install targets, matching what the loader actually does with each type
    /// (verified on disk 2026-09-04, and the old Pal\Binaries\Win64\ue4ss layout
    /// is obsolete):
    ///   PalSchema/  -> Mods\NativeMods\UE4SS\Mods\PalSchema\mods\&lt;pkg&gt;\
    ///   Scripts/    -> Mods\NativeMods\UE4SS\Mods\&lt;pkg&gt;\Scripts\
    ///   plus the mods.txt line, only when the package ships Lua.
    ///
    /// Uninstall only ever deletes a folder carrying our own manifest, so it can
    /// never take out a mod this tool did not put there.
    /// </summary>
    public static class GameInstaller
    {
        public const string ManifestName = "ringofnoxp_install.json";

        /// <summary>
        /// The marker that makes a folder ours. Written by hand rather than through
        /// JsonUtility so this whole class stays free of the Unity runtime and can
        /// be exercised outside the editor.
        /// </summary>
        private static string ManifestJson(RingProject project, string source)
        {
            string Q(string s)
            {
                var b = new StringBuilder("\"");
                foreach (var c in s ?? "")
                {
                    if (c == '"') b.Append("\\\"");
                    else if (c == '\\') b.Append("\\\\");
                    else if (c >= 0x20) b.Append(c);
                }
                return b.Append('"').ToString();
            }
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"package\": ").Append(Q(SafeName(project.PackageName))).Append(",\n");
            sb.Append("  \"modName\": ").Append(Q(project.ModName)).Append(",\n");
            sb.Append("  \"version\": ").Append(Q(project.Version)).Append(",\n");
            sb.Append("  \"project\": ").Append(Q(project.ProjectPath)).Append(",\n");
            sb.Append("  \"source\": ").Append(Q(source)).Append(",\n");
            sb.Append("  \"installedAt\": ").Append(Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------- paths

        private static string ModsRoot(string gameFolder) =>
            Path.Combine(gameFolder ?? "", "Mods", "NativeMods", "UE4SS", "Mods");

        /// <summary>A package name usable as a folder: letters and digits only.</summary>
        public static string SafeName(string packageName)
        {
            var cleaned = new string((packageName ?? "").Where(char.IsLetterOrDigit).ToArray());
            return cleaned.Length > 0 ? cleaned : "RingOfNoXp";
        }

        /// <summary>Best guess at the Palworld install: near the output folder, then the usual Steam spots.</summary>
        public static string GuessGameFolder(string outputFolder)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(outputFolder))
            {
                var i = outputFolder.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) candidates.Add(Path.Combine(outputFolder.Substring(0, i), "steamapps", "common", "Palworld"));
            }
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

        /// <summary>Why this game folder cannot be installed into, or null when it can.</summary>
        public static string Problem(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder))
                return "No game folder set.";
            if (!Directory.Exists(Path.Combine(gameFolder, "Pal")))
                return "That is not a Palworld install (no Pal folder inside it).";
            // No UE4SS means nothing to install into. Fabricating the tree would
            // report success and load nothing, which is worse than refusing.
            var modsRoot = ModsRoot(gameFolder);
            if (!Directory.Exists(modsRoot) || !File.Exists(Path.Combine(modsRoot, "mods.txt")))
                return "UE4SS is not installed here (no Mods\\NativeMods\\UE4SS\\Mods\\mods.txt). Subscribe to UE4SS Experimental and PalSchema, start the game once, then try again.";
            if (!Directory.Exists(Path.Combine(modsRoot, "PalSchema")))
                return "PalSchema is not installed here (no Mods\\NativeMods\\UE4SS\\Mods\\PalSchema). The data half of this mod needs it.";
            return null;
        }

        // ----------------------------------------------------------- install

        /// <summary>
        /// Copies <paramref name="packageRoot"/> into the game. Returns the folders written.
        /// Anything this tool installed under the same name is cleared first, so a file
        /// dropped from the package does not linger and keep loading.
        /// </summary>
        public static List<string> Install(RingProject project, string packageRoot, string gameFolder)
        {
            var problem = Problem(gameFolder);
            if (problem != null) throw new InvalidOperationException(problem);
            if (!Directory.Exists(packageRoot))
                throw new DirectoryNotFoundException("Nothing generated yet: " + packageRoot);

            var pkg = SafeName(project.PackageName);
            var modsRoot = ModsRoot(gameFolder);
            var schemaSrc = Path.Combine(packageRoot, "PalSchema");
            var scriptsSrc = Path.Combine(packageRoot, "Scripts");

            if (!Directory.Exists(schemaSrc))
                throw new DirectoryNotFoundException("The package has no PalSchema folder to install: " + packageRoot);

            var written = new List<string>();
            var schemaDst = Path.Combine(modsRoot, "PalSchema", "mods", pkg);
            var scriptsDst = Path.Combine(modsRoot, pkg, "Scripts");

            // Clear our previous install before writing, but only what we own.
            if (Directory.Exists(schemaDst) && IsOurs(schemaDst)) Directory.Delete(schemaDst, true);
            if (Directory.Exists(scriptsDst) && IsOurs(Path.Combine(modsRoot, pkg))) Directory.Delete(scriptsDst, true);

            CopyTree(schemaSrc, schemaDst);
            WriteManifest(project, packageRoot, schemaDst);
            written.Add(schemaDst);

            var hasLua = Directory.Exists(scriptsSrc) && Directory.GetFiles(scriptsSrc, "*.lua").Length > 0;
            if (hasLua)
            {
                CopyTree(scriptsSrc, scriptsDst);
                WriteManifest(project, packageRoot, Path.Combine(modsRoot, pkg));
                written.Add(scriptsDst);
                EnableInModsTxt(modsRoot, pkg);
                written.Add(Path.Combine(modsRoot, "mods.txt") + " (line added)");
            }
            return written;
        }

        /// <summary>Adds or re-enables the package's mods.txt line, above the built-in keybinds marker.</summary>
        private static void EnableInModsTxt(string modsRoot, string pkg)
        {
            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            var lines = File.Exists(modsTxt) ? File.ReadAllLines(modsTxt).ToList() : new List<string>();
            var at = lines.FindIndex(l => l.Trim().StartsWith(pkg + " :", StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                var keybinds = lines.FindIndex(l => l.StartsWith("; Built-in keybinds"));
                if (keybinds >= 0) lines.Insert(keybinds, pkg + " : 1");
                else lines.Add(pkg + " : 1");
            }
            else if (!lines[at].Trim().EndsWith(": 1"))
            {
                // A line switched off by hand goes back on: an install that leaves the mod off is no install.
                lines[at] = pkg + " : 1";
            }
            else return;
            File.WriteAllLines(modsTxt, lines);
        }

        // --------------------------------------------------------- uninstall

        /// <summary>
        /// Removes the package from the game: both folders and the mods.txt line.
        /// A folder is only deleted when it carries our manifest, and the PalSchema
        /// loader itself is refused outright.
        /// </summary>
        public static List<string> Uninstall(string packageName, string gameFolder)
        {
            var pkg = SafeName(packageName);
            if (pkg.Equals("PalSchema", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PalSchema is the loader every package needs, not one of ours. Nothing was removed.");

            var modsRoot = ModsRoot(gameFolder);
            if (!Directory.Exists(modsRoot))
                throw new DirectoryNotFoundException("No UE4SS mods folder here: " + modsRoot);

            var removed = new List<string>();

            var luaDir = Path.Combine(modsRoot, pkg);
            var luaExists = Directory.Exists(luaDir);
            var luaOurs = luaExists && IsOurs(luaDir);
            if (luaOurs) { Directory.Delete(luaDir, true); removed.Add(luaDir); }

            var schemaDir = Path.Combine(modsRoot, "PalSchema", "mods", pkg);
            if (Directory.Exists(schemaDir) && IsOurs(schemaDir)) { Directory.Delete(schemaDir, true); removed.Add(schemaDir); }

            // Only touch mods.txt when the Lua folder was ours or was never there.
            // Stripping the line off somebody else's mod would disable it while
            // leaving its files behind - worse than doing nothing.
            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            if (File.Exists(modsTxt) && (luaOurs || !luaExists))
            {
                var lines = File.ReadAllLines(modsTxt).ToList();
                var n = lines.RemoveAll(l => l.Trim().StartsWith(pkg + " :", StringComparison.OrdinalIgnoreCase));
                if (n > 0) { File.WriteAllLines(modsTxt, lines); removed.Add(modsTxt + " (line removed)"); }
            }
            return removed;
        }

        /// <summary>What is currently in the game for this package, for the status line.</summary>
        public static string InstalledState(string packageName, string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return "no game folder set";
            var pkg = SafeName(packageName);
            var modsRoot = ModsRoot(gameFolder);
            if (!Directory.Exists(modsRoot)) return "UE4SS not installed";

            var schema = Directory.Exists(Path.Combine(modsRoot, "PalSchema", "mods", pkg));
            var scripts = Directory.Exists(Path.Combine(modsRoot, pkg, "Scripts"));
            if (!schema && !scripts) return "not installed";

            var modsTxt = Path.Combine(modsRoot, "mods.txt");
            var line = File.Exists(modsTxt)
                ? File.ReadAllLines(modsTxt).FirstOrDefault(l => l.Trim().StartsWith(pkg + " :", StringComparison.OrdinalIgnoreCase))
                : null;
            var lua = scripts ? (line == null ? ", lua present but no mods.txt line" : line.Trim().EndsWith(": 1") ? ", lua enabled" : ", lua DISABLED in mods.txt") : "";
            return (schema ? "data installed" : "data missing") + lua;
        }

        // ------------------------------------------------------------- utils

        /// <summary>True when this tool wrote the folder - the only thing uninstall will delete.</summary>
        private static bool IsOurs(string folder) => File.Exists(Path.Combine(folder, ManifestName));

        private static void WriteManifest(RingProject project, string source, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, ManifestName), ManifestJson(project, source));
        }

        private static void CopyTree(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
