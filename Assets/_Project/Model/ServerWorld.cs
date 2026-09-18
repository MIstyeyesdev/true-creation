using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalPanel.Model
{
    /// <summary>
    /// One dedicated-server installation. Modelled as a record from day one so adding a
    /// second instance later is registry data rather than a rewrite.
    /// </summary>
    [Serializable]
    public sealed class ServerInstall
    {
        public string Id;
        public string DisplayName;
        public string InstallPath;

        /// <summary>Overridden only when running several instances on one box.</summary>
        public int PublicPortOverride = -1;

        public string ExePath => Path.Combine(InstallPath ?? string.Empty, "PalServer.exe");

        public string ConfigPath => Path.Combine(
            InstallPath ?? string.Empty, "Pal", "Saved", "Config", "WindowsServer",
            "PalWorldSettings.ini");

        public string DefaultConfigPath => Path.Combine(
            InstallPath ?? string.Empty, "DefaultPalWorldSettings.ini");

        public string SaveGamesRoot => Path.Combine(
            InstallPath ?? string.Empty, "Pal", "Saved", "SaveGames", "0");

        /// <summary>Palworld's own mod manager settings (bGlobalEnableMod, WorkshopRootDir, ActiveModList).</summary>
        public string ModSettingsPath => Path.Combine(
            InstallPath ?? string.Empty, "Mods", "PalModSettings.ini");

        public bool IsValid =>
            !string.IsNullOrEmpty(InstallPath) && File.Exists(ExePath);
    }

    /// <summary>One world save folder under SaveGames/0.</summary>
    [Serializable]
    public sealed class ServerWorld
    {
        /// <summary>The save folder name, e.g. 47D39786468EC68465C50AB6A5A7A6CB.</summary>
        public string Guid;

        /// <summary>Friendly label. Defaults to the server name from the config.</summary>
        public string DisplayName;

        public string FolderPath;
        public DateTime LastModifiedUtc;
        public int PlayerCount;
        public bool HasBackups;

        public string LevelSavPath => Path.Combine(FolderPath ?? string.Empty, "Level.sav");
        public string LevelMetaPath => Path.Combine(FolderPath ?? string.Empty, "LevelMeta.sav");
        public string PlayersPath => Path.Combine(FolderPath ?? string.Empty, "Players");
    }

    /// <summary>Discovers worlds by scanning an install's SaveGames/0 folder.</summary>
    public static class WorldRepository
    {
        public static List<ServerWorld> Discover(ServerInstall install)
        {
            var worlds = new List<ServerWorld>();
            if (install == null || !Directory.Exists(install.SaveGamesRoot)) return worlds;

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
                });
            }

            return worlds.OrderByDescending(w => w.LastModifiedUtc).ToList();
        }
    }
}
