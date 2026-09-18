using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PalPanel.Io;
using PalPanel.Model;

namespace PalPanel.Services
{
    /// <summary>One allowed package and what the Workshop folder holds for it.</summary>
    public sealed class WorkshopPackageStatus
    {
        public string PackageName;
        public bool Found;
        public string Folder;
        public string Version;
        public bool HasServerRule;
    }

    /// <summary>
    /// Manages Mods\PalModSettings.ini for the vanilla baseline: the game plus UE4SS and PalSchema,
    /// nothing else (2026-09-17). Other packages are never enabled from here; if the file
    /// lists any, the baseline write removes them and says so first.
    /// </summary>
    public sealed class ModSettingsService
    {
        /// <summary>Order matters: PalSchema depends on UE4SSExperimentalPW.</summary>
        public static readonly IReadOnlyList<string> AllowedPackages =
            new[] { "UE4SSExperimentalPW", "PalSchema" };

        /// <summary>Steam's Workshop download folder for Palworld (app 1623730); the client's
        /// PalModSettings.ini points here.</summary>
        public const string DefaultWorkshopRoot =
            @"C:\Program Files (x86)\Steam\steamapps\workshop\content\1623730";

        public ServerInstall Install { get; private set; }
        public PalModSettingsFile Settings { get; private set; }

        /// <summary>False until the server has been started once (it creates the file).</summary>
        public bool FileExists { get; private set; }

        public void Load(ServerInstall install)
        {
            Install = install ?? throw new ArgumentNullException(nameof(install));
            FileExists = File.Exists(install.ModSettingsPath);
            Settings = FileExists
                ? PalModSettingsFile.Parse(File.ReadAllText(install.ModSettingsPath))
                : PalModSettingsFile.CreateDefault();
        }

        /// <summary>Mods on and exactly the allowed packages active.</summary>
        public bool BaselineActive =>
            Settings != null && Settings.GlobalEnableMod &&
            Settings.ActiveModList.Count == AllowedPackages.Count &&
            AllowedPackages.All(p => Settings.ActiveModList.Contains(p, StringComparer.OrdinalIgnoreCase));

        /// <summary>Active packages the vanilla baseline does not allow.</summary>
        public IEnumerable<string> DisallowedActive =>
            Settings == null
                ? Enumerable.Empty<string>()
                : Settings.ActiveModList.Where(p =>
                    !AllowedPackages.Contains(p, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Finds each allowed package under <paramref name="workshopRoot"/> by the PackageName in
        /// its Info.json, and whether its InstallRule has an entry for dedicated servers.
        /// </summary>
        public static List<WorkshopPackageStatus> Describe(string workshopRoot)
        {
            var result = AllowedPackages
                .Select(p => new WorkshopPackageStatus { PackageName = p })
                .ToList();
            if (string.IsNullOrWhiteSpace(workshopRoot) || !Directory.Exists(workshopRoot))
                return result;

            foreach (var dir in Directory.GetDirectories(workshopRoot))
            {
                var infoPath = Path.Combine(dir, "Info.json");
                if (!File.Exists(infoPath)) continue;

                JObject info;
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch (Exception) { continue; }   // someone else's malformed Info.json

                var name = (string)info["PackageName"];
                var status = result.FirstOrDefault(s =>
                    string.Equals(s.PackageName, name, StringComparison.OrdinalIgnoreCase));
                if (status == null || status.Found) continue;

                status.Found = true;
                status.Folder = dir;
                status.Version = (string)info["Version"];
                status.HasServerRule = info["InstallRule"] is JArray rules &&
                    rules.OfType<JObject>().Any(r => (bool?)r["IsServer"] == true);
            }

            return result;
        }

        /// <summary>
        /// enable = true: mods on, WorkshopRootDir = <paramref name="workshopRoot"/>, ActiveModList =
        /// UE4SSExperimentalPW + PalSchema. enable = false: mods off, empty ActiveModList.
        /// Backup first; refused while PalServer runs. The server installs the packages on its
        /// next start. Returns the backup path, or null when the file did not exist yet.
        /// </summary>
        public string WriteBaseline(bool enable, string workshopRoot)
        {
            if (Install == null || Settings == null)
                throw new InvalidOperationException("No install loaded.");
            if (ConfigService.AnyServerRunning())
                throw new InvalidOperationException("Stop PalServer before changing its mod settings.");

            var root = (workshopRoot ?? string.Empty).Trim();
            if (enable)
            {
                var notReady = Describe(root)
                    .Where(s => !s.Found || !s.HasServerRule)
                    .Select(s => s.PackageName)
                    .ToList();
                if (notReady.Count > 0)
                    throw new InvalidOperationException(
                        "Not found (with a dedicated-server install rule) under " +
                        (root.Length == 0 ? "an empty Workshop folder" : root) + ": " +
                        string.Join(", ", notReady));
            }

            Settings.GlobalEnableMod = enable;
            if (enable) Settings.WorkshopRootDir = root;
            Settings.ActiveModList.Clear();
            if (enable) Settings.ActiveModList.AddRange(AllowedPackages);

            var dir = Path.GetDirectoryName(Install.ModSettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var backup = SafeConfigWriter.Write(Install.ModSettingsPath, Settings.ToText(),
                                                PanelPaths.BackupsDir);
            SafeConfigWriter.PruneBackups(Install.ModSettingsPath, PanelPaths.BackupsDir);
            Load(Install);
            return backup;
        }
    }
}
