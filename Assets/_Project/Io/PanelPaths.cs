using System;
using System.IO;

namespace PalPanel.Io
{
    /// <summary>
    /// Where the panel puts everything it produces.
    ///
    /// The Unity project lives under Desktop\Unity Projects\True Creation; its output --
    /// pre-change backups and post-save snapshots of PalWorldSettings.ini -- goes to a
    /// separate folder so config history is never mixed into source control or wiped by a
    /// project reimport.
    /// </summary>
    public static class PanelPaths
    {
        public const string DefaultOutputRoot = @"C:\Users\YourName\Desktop\Palworld Server";

        private static string _outputRoot = DefaultOutputRoot;

        /// <summary>Root for backups and snapshots. Falls back to the default when cleared.</summary>
        public static string OutputRoot
        {
            get => string.IsNullOrWhiteSpace(_outputRoot) ? DefaultOutputRoot : _outputRoot;
            set => _outputRoot = value;
        }

        /// <summary>Copies of the config as it was immediately before each save.</summary>
        public static string BackupsDir => Path.Combine(OutputRoot, "Backups");

        /// <summary>Copies of the config as written, one per save.</summary>
        public static string SnapshotsDir => Path.Combine(OutputRoot, "Snapshots");

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(SnapshotsDir);
        }

        /// <summary>Timestamped snapshot path for a just-written config.</summary>
        public static string NewSnapshotPath(string serverName)
        {
            var safe = string.IsNullOrWhiteSpace(serverName) ? "server" : serverName;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(SnapshotsDir, $"{safe}.{stamp}.ini");
        }
    }
}
