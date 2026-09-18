using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TrueServer
{
    /// <summary>
    /// Every path True Server uses.
    ///
    /// Nothing here is hard-coded to one machine. The dedicated-server install is discovered from
    /// Steam's own library list, output defaults under the user's Documents folder, and both can
    /// be overridden in the Paths tab. Overrides are per-user settings (ServerHost: EditorPrefs in the
    /// editor, the program's settings file in True Creation's exe), never part of the project, so a
    /// copy of this project carries no trace of whoever set it up.
    ///
    /// Docs/ and Tools/ sit at the PROJECT ROOT, not under Assets/, so Unity's Project window
    /// never shows them -- that is why the window has a Docs tab.
    /// </summary>
    public static class ServerPaths
    {
        private const string InstallPref = "TrueServer.InstallPath";
        private const string OutputPref = "TrueServer.OutputRoot";
        private const string ReferencePref = "TrueServer.ReferenceRoot";

        /// <summary>Folder containing Assets/ -- i.e. the Unity project root.</summary>
        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string Docs => Hosted(Path.Combine(ProjectRoot, "Docs"));
        public static string Tools => Hosted(Path.Combine(ProjectRoot, "Tools"));

        /// <summary>
        /// Docs/TrueServer and Tools/TrueServer when this code runs inside True Creation (its Server
        /// tab, 2026-09-18), where Docs/ and Tools/ hold True Creation's own files; otherwise the
        /// folder itself.
        /// </summary>
        private static string Hosted(string dir)
        {
            var nested = Path.Combine(dir, "TrueServer");
            return Directory.Exists(nested) ? nested : dir;
        }

        // ------------------------------------------------------------------ server install

        /// <summary>
        /// The managed dedicated-server install. Falls back to whatever discovery finds, so a
        /// fresh copy of the project works with no configuration on a normal Steam setup.
        /// </summary>
        public static string InstallPath
        {
            get
            {
                var stored = ServerHost.GetString(InstallPref, string.Empty);
                if (!string.IsNullOrWhiteSpace(stored)) return stored;
                return DiscoverInstall() ?? string.Empty;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) ServerHost.DeleteKey(InstallPref);
                else ServerHost.SetString(InstallPref, value.Trim());
            }
        }

        public static bool InstallIsOverridden =>
            !string.IsNullOrWhiteSpace(ServerHost.GetString(InstallPref, string.Empty));

        private const string ServerFolder = "PalServer";
        private const string ServerExe = "PalServer.exe";

        /// <summary>
        /// Find the dedicated server without being told where it is.
        ///
        /// Steam records every library root in libraryfolders.vdf, so read that first; only fall
        /// back to walking drive letters if Steam is not where it usually is. Returns null rather
        /// than guessing when nothing matches.
        /// </summary>
        public static string DiscoverInstall()
        {
            foreach (var lib in SteamLibraries())
            {
                var candidate = Path.Combine(lib, "steamapps", "common", ServerFolder);
                if (File.Exists(Path.Combine(candidate, ServerExe))) return candidate;
            }

            foreach (var drive in SafeDrives())
            {
                foreach (var root in new[] { "SteamLibrary", @"Program Files (x86)\Steam", "Steam" })
                {
                    var candidate = Path.Combine(drive, root, "steamapps", "common", ServerFolder);
                    try
                    {
                        if (File.Exists(Path.Combine(candidate, ServerExe))) return candidate;
                    }
                    catch { /* unreadable drive: skip */ }
                }
            }
            return null;
        }

        /// <summary>Steam library roots, read from Steam's own libraryfolders.vdf.</summary>
        private static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();

            foreach (var steam in SteamRoots())
            {
                roots.Add(steam);

                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;

                string text;
                try { text = File.ReadAllText(vdf); }
                catch { continue; }

                // Entries look like:  "path"   "D:\\SteamLibrary"
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                {
                    var p = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!string.IsNullOrWhiteSpace(p)) roots.Add(p);
                }
            }
            return roots.Distinct();
        }

        private static IEnumerable<string> SteamRoots()
        {
            var found = new List<string>();
            foreach (var drive in SafeDrives())
            {
                foreach (var rel in new[] { @"Program Files (x86)\Steam", @"Program Files\Steam", "Steam" })
                {
                    var p = Path.Combine(drive, rel);
                    try { if (Directory.Exists(p)) found.Add(p); }
                    catch { }
                }
            }
            return found;
        }

        private static IEnumerable<string> SafeDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch { return Enumerable.Empty<string>(); }

            return drives
                .Where(d =>
                {
                    try { return d.IsReady && d.DriveType == DriveType.Fixed; }
                    catch { return false; }
                })
                .Select(d => d.RootDirectory.FullName);
        }

        // ------------------------------------------------------------------ output

        /// <summary>
        /// Backups and snapshots. Deliberately outside both the Unity project and the game
        /// install: a reimport cannot wipe it and a game file verification cannot touch it.
        /// </summary>
        public static string OutputRoot
        {
            get
            {
                var stored = ServerHost.GetString(OutputPref, string.Empty);
                return string.IsNullOrWhiteSpace(stored) ? DefaultOutputRoot : stored;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) ServerHost.DeleteKey(OutputPref);
                else ServerHost.SetString(OutputPref, value.Trim());
            }
        }

        public static string DefaultOutputRoot
        {
            get
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = Path.Combine(ProjectRoot, "..");
                return Path.Combine(docs, "TrueServer");
            }
        }

        public static string BackupsDir => Path.Combine(OutputRoot, "Backups");
        public static string SnapshotsDir => Path.Combine(OutputRoot, "Snapshots");

        public static void EnsureOutputDirs()
        {
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(SnapshotsDir);
        }

        // ------------------------------------------------------------------ reference data

        /// <summary>
        /// Optional. A companion project holding the game's reference data and export TOC. Only
        /// the option-catalogue generator needs it, and only when regenerating; the generated
        /// catalogue ships with this project, so the tool runs fine without it.
        /// </summary>
        public static string ReferenceRoot
        {
            get => ServerHost.GetString(ReferencePref, string.Empty);
            set
            {
                if (string.IsNullOrWhiteSpace(value)) ServerHost.DeleteKey(ReferencePref);
                else ServerHost.SetString(ReferencePref, value.Trim());
            }
        }

        public static bool HasReferenceRoot =>
            !string.IsNullOrWhiteSpace(ReferenceRoot) && Directory.Exists(ReferenceRoot);

        // ------------------------------------------------------------------ helpers

        public static bool Exists(string path) =>
            !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        public static void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            ServerHost.Reveal(path);
        }

        public static string HumanSize(long bytes)
        {
            if (bytes < 0) return "-";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.#") + " MB";
        }

        /// <summary>Forget every stored override. Discovery and defaults take over again.</summary>
        public static void ResetAll()
        {
            ServerHost.DeleteKey(InstallPref);
            ServerHost.DeleteKey(OutputPref);
            ServerHost.DeleteKey(ReferencePref);
        }
    }
}
