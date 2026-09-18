using System.Collections.Generic;
using Newtonsoft.Json;
using PalItemgen.Model;

namespace PalItemgen.Export
{
    /// <summary>Install types recognised by Palworld's official mod loader (Mod Uploader docs, 04-Tech.md).</summary>
    public static class InstallType
    {
        public const string UE4SS = "UE4SS";
        public const string Lua = "Lua";              // Mods\NativeMods\UE4SS\Mods\{PackageName}
        public const string PalSchema = "PalSchema";  // Mods\NativeMods\UE4SS\Mods\PalSchema\mods
        public const string LogicMods = "LogicMods";
        public const string Paks = "Paks";
    }

    public sealed class InstallRule
    {
        [JsonProperty("Type", Order = 0)] public string Type;
        [JsonProperty("IsServer", Order = 1, NullValueHandling = NullValueHandling.Ignore)] public bool? IsServer;
        [JsonProperty("Targets", Order = 2)] public List<string> Targets = new List<string>();
    }

    /// <summary>A package's Info.json. Its presence is what makes a folder a mod.</summary>
    public sealed class ModInfo
    {
        [JsonProperty("ModName", Order = 0)] public string ModName;
        [JsonProperty("PackageName", Order = 1)] public string PackageName;
        [JsonProperty("Thumbnail", Order = 2)] public string Thumbnail = "thumbnail.png";
        [JsonProperty("Version", Order = 3)] public string Version = "0.1.0";
        [JsonProperty("DebugMode", Order = 4)] public bool DebugMode;
        [JsonProperty("MinRevision", Order = 5)] public int MinRevision;
        [JsonProperty("Author", Order = 6)] public string Author = "";
        [JsonProperty("Dependencies", Order = 7)] public List<string> Dependencies = new List<string>();
        [JsonProperty("Tags", Order = 8)] public List<string> Tags = new List<string>();
        [JsonProperty("InstallRule", Order = 9)] public List<InstallRule> InstallRule = new List<InstallRule>();
    }

    public static class InfoJsonWriter
    {
        public const string ScriptsTarget = "./Scripts";
        public const string PalSchemaTarget = "./PalSchema/";
        public const string PaksTarget = "./Paks/";
        public const string LogicModsTarget = "./LogicMods/";

        /// <param name="hasPaks">Add a Paks rule (installs to Pal\Content\Paks\~WorkshopMods\&lt;Package&gt;).</param>
        /// <param name="hasLogicMods">Add a LogicMods rule (installs to Pal\Content\Paks\LogicMods).</param>
        public static ModInfo Build(ModProject p, bool hasPaks = false, bool hasLogicMods = false)
        {
            var info = new ModInfo
            {
                ModName = p.ModName,
                PackageName = ModPackager.SafeName(p.PackageName),
                Version = p.Version,
                DebugMode = p.DebugMode,
                MinRevision = p.MinRevision,
                Author = p.Author ?? "",
                // Lua needs UE4SS; the stations need PalSchema. Both are Workshop packages.
                Dependencies = new List<string> { "UE4SSExperimentalPW", "PalSchema" },
                Tags = new List<string> { "UE4SS", "PalSchema", "Gameplay", "Utilities" },
            };
            // One rule per file type: paks and JSON never share a target.
            info.InstallRule.Add(new InstallRule { Type = InstallType.Lua, Targets = new List<string> { ScriptsTarget } });
            info.InstallRule.Add(new InstallRule { Type = InstallType.PalSchema, Targets = new List<string> { PalSchemaTarget } });
            if (hasPaks) info.InstallRule.Add(new InstallRule { Type = InstallType.Paks, Targets = new List<string> { PaksTarget } });
            if (hasLogicMods) info.InstallRule.Add(new InstallRule { Type = InstallType.LogicMods, Targets = new List<string> { LogicModsTarget } });
            if (p.IncludeServerRules)
            {
                info.InstallRule.Add(new InstallRule { Type = InstallType.Lua, IsServer = true, Targets = new List<string> { ScriptsTarget } });
                info.InstallRule.Add(new InstallRule { Type = InstallType.PalSchema, IsServer = true, Targets = new List<string> { PalSchemaTarget } });
                if (hasPaks) info.InstallRule.Add(new InstallRule { Type = InstallType.Paks, IsServer = true, Targets = new List<string> { PaksTarget } });
                if (hasLogicMods) info.InstallRule.Add(new InstallRule { Type = InstallType.LogicMods, IsServer = true, Targets = new List<string> { LogicModsTarget } });
            }
            return info;
        }

        public static string ToJson(ModInfo info) => JsonConvert.SerializeObject(info, Formatting.Indented);
    }
}
