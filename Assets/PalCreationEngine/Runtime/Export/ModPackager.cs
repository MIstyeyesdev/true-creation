using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Install types recognised by Palworld's official mod loader, each mapping
    /// to a fixed directory under the game install. From the Mod Uploader's own
    /// docs (04-Tech.md).
    /// </summary>
    public static class InstallType
    {
        public const string UE4SS = "UE4SS";          // Mods\NativeMods\UE4SS
        public const string Lua = "Lua";              // Mods\NativeMods\UE4SS\Mods\{PackageName}
        public const string PalSchema = "PalSchema";  // Mods\NativeMods\UE4SS\Mods\PalSchema\mods
        public const string LogicMods = "LogicMods";  // Pal\Content\Paks\LogicMods
        public const string Paks = "Paks";            // Pal\Content\Paks\~WorkshopMods
    }

    /// <summary>Tags the Workshop accepts. Anything else is rejected on upload.</summary>
    public static class WorkshopTags
    {
        public const string PalSchema = "PalSchema";
        public const string UE4SS = "UE4SS";
        public const string ModelReplacement = "Model Replacement";
        public const string Utilities = "Utilities";
        public const string Gameplay = "Gameplay";
        public const string UserInterface = "User Interface";
    }

    public sealed class InstallRule
    {
        [JsonProperty("Type", Order = 0)] public string Type;

        /// <summary>
        /// Omitted for client rules; true marks a rule the dedicated server
        /// applies. A package that should work on both needs one of each, even
        /// when they name identical targets.
        /// </summary>
        [JsonProperty("IsServer", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsServer;

        [JsonProperty("Targets", Order = 2)] public List<string> Targets = new List<string>();
    }

    /// <summary>
    /// A package's Info.json. Its presence is what makes a folder recognisable
    /// to the mod loader as a mod at all.
    /// </summary>
    public sealed class ModInfo
    {
        [JsonProperty("ModName", Order = 0)] public string ModName;
        [JsonProperty("PackageName", Order = 1)] public string PackageName;
        [JsonProperty("Thumbnail", Order = 2)] public string Thumbnail = "thumbnail.png";

        /// <summary>
        /// Compared as a plain string against the installed copy; any change
        /// triggers a reinstall. It carries no semantic-version meaning, so it
        /// only has to differ from the last upload.
        /// </summary>
        [JsonProperty("Version", Order = 3)] public string Version = "1.0.0";

        /// <summary>Reinstall on every launch even when Version is unchanged.</summary>
        [JsonProperty("DebugMode", Order = 4)] public bool DebugMode;

        /// <summary>Minimum game revision: the last 5 digits of the version in the title screen.</summary>
        [JsonProperty("MinRevision", Order = 5)] public int MinRevision;

        [JsonProperty("Author", Order = 6)] public string Author = "";
        [JsonProperty("Dependencies", Order = 7)] public List<string> Dependencies = new List<string>();
        [JsonProperty("Tags", Order = 8)] public List<string> Tags = new List<string>();
        [JsonProperty("InstallRule", Order = 9)] public List<InstallRule> InstallRule = new List<InstallRule>();
    }

    /// <summary>
    /// Writes a Workshop-ready package folder:
    ///
    ///     &lt;packageRoot&gt;/
    ///       Info.json
    ///       thumbnail.png        (caller-supplied; upload fails without one)
    ///       raw/pals.json
    ///
    /// The loader installs a package into a folder named after its PackageName
    /// and copies each Target inside it, so targeting "./raw" lands the files at
    /// PalSchema\mods\&lt;PackageName&gt;\raw\ -- matching a hand-made mod's layout.
    /// Targeting a named subfolder instead produces the doubled
    /// &lt;PackageName&gt;\&lt;PackageName&gt;\raw path seen in some published mods.
    /// </summary>
    public static class ModPackager
    {
        /// <summary>
        /// The install-rule target every published PalSchema mod uses. Trailing
        /// slash included, matching the shipped Info.json files verbatim.
        /// </summary>
        public const string PalSchemaFolder = "./PalSchema/";

        public static ModInfo BuildInfo(
            string modName,
            string packageName,
            string author,
            string version = "1.0.0",
            int minRevision = 0,
            bool includeServerRule = true,
            bool logicMods = false)
        {
            if (string.IsNullOrWhiteSpace(modName))
                throw new ArgumentException("modName is required", nameof(modName));
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("packageName is required", nameof(packageName));

            var info = new ModInfo
            {
                ModName = modName,
                PackageName = packageName,
                Author = author ?? "",
                Version = version,
                MinRevision = minRevision,
                // Everything this tool emits is interpreted by PalSchema, so the
                // package is useless without the PalSchema core installed.
                // Published PalSchema mods declare both: PalSchema itself and the
                // UE4SS build it runs on. Verified against 30 installed Workshop
                // mods -- Guten, Dazzi and the Ore Pits family all list both.
                Dependencies = new List<string> { "UE4SSExperimentalPW", "PalSchema" },
                Tags = new List<string> { WorkshopTags.PalSchema, WorkshopTags.Gameplay },
            };

            // Target the PalSchema/ folder, NOT ./raw. The loader copies the
            // target's CONTENTS into mods/<PackageName>/, so a package laid out
            // as PalSchema/raw/pals.json installs to
            // mods/<PackageName>/raw/pals.json -- which is where PalSchema looks.
            // Targeting ./raw instead would land pals.json one level too shallow.
            // Verified against every installed Workshop PalSchema mod.
            info.InstallRule.Add(new InstallRule
            {
                Type = InstallType.PalSchema,
                Targets = new List<string> { PalSchemaFolder },
            });

            if (includeServerRule)
            {
                info.InstallRule.Add(new InstallRule
                {
                    Type = InstallType.PalSchema,
                    IsServer = true,
                    Targets = new List<string> { PalSchemaFolder },
                });
            }

            // Route B: the custom mesh pak, installed to Pal\Content\Paks\LogicMods (the working
            // mod PalVariantPandemonium declares exactly this rule for its pak).
            if (logicMods)
            {
                info.InstallRule.Add(new InstallRule { Type = InstallType.LogicMods, Targets = new List<string> { "./LogicMods/" } });
            }

            return info;
        }

        /// <summary>
        /// Writes the package to disk and returns its root path. Overwrites
        /// Info.json and raw/pals.json; leaves any other file in place, so an
        /// existing thumbnail or hand-added folder survives a re-export.
        /// </summary>
        public static string Write(
            string packageRoot,
            ModInfo info,
            PalSchemaMod mod,
            PalSchemaBlueprints blueprints = null,
            string fileStem = "pals")
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (mod == null) throw new ArgumentNullException(nameof(mod));

            var schemaDir = Path.Combine(packageRoot, "PalSchema");
            var rawDir = Path.Combine(schemaDir, "raw");
            Directory.CreateDirectory(rawDir);

            File.WriteAllText(
                Path.Combine(packageRoot, "Info.json"),
                JsonConvert.SerializeObject(info, PalSchemaMod.SerializerSettings));

            File.WriteAllText(Path.Combine(rawDir, fileStem + ".json"), mod.ToJson());

            // blueprints/ sits beside raw/ INSIDE PalSchema/, so the single "./PalSchema/"
            // target already installs it; no extra rule is needed.
            if (blueprints != null && !blueprints.IsEmpty)
            {
                var blueprintDir = Path.Combine(schemaDir, "blueprints");
                Directory.CreateDirectory(blueprintDir);
                File.WriteAllText(
                    Path.Combine(blueprintDir, fileStem + ".json"), blueprints.ToJson());
            }

            return packageRoot;
        }

        /// <summary>
        /// Writes a complete new-Pal package: Info.json plus every loader file the package holds
        /// under PalSchema/ (enums, pals, raw per table, translations, spawns, blueprints, items),
        /// the layout of the verified working mod. Existing files with the same names are
        /// overwritten; other files (thumbnail, hand-added folders) are left alone. Returns the
        /// relative paths written, in order.
        /// </summary>
        public static List<string> Write(string packageRoot, ModInfo info, PalSchemaPackage package, string modName)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (string.IsNullOrWhiteSpace(packageRoot)) throw new ArgumentException("packageRoot is required", nameof(packageRoot));

            Directory.CreateDirectory(packageRoot);
            var written = new List<string>();
            File.WriteAllText(Path.Combine(packageRoot, "Info.json"), JsonConvert.SerializeObject(info, PalSchemaMod.SerializerSettings));
            written.Add("Info.json");
            var schemaDir = Path.Combine(packageRoot, "PalSchema");
            foreach (var kv in package.ToFiles(modName))
            {
                var rel = kv.Key.Replace('/', Path.DirectorySeparatorChar);
                var full = Path.Combine(schemaDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, kv.Value);
                written.Add("PalSchema/" + kv.Key);
            }
            return written;
        }
    }
}
