using System;
using System.IO;
using System.Text;

namespace PalPanel.Io
{
    /// <summary>
    /// Writes PalWorldSettings.ini with a timestamped backup and an atomic replace,
    /// so a crash mid-write can never leave a half-written server config on disk.
    ///
    /// Backups land in <see cref="PanelPaths.BackupsDir"/> rather than beside the INI,
    /// keeping the server's Config folder clean and the history outside the game install
    /// where a Steam validate cannot remove it.
    /// </summary>
    public static class SafeConfigWriter
    {
        /// <summary>ASCII, no BOM, CRLF -- matches what the dedicated server ships.</summary>
        private static readonly UTF8Encoding NoBom = new UTF8Encoding(false);

        public static string BackupPathFor(string iniPath, string backupDir)
        {
            var dir = string.IsNullOrEmpty(backupDir)
                ? (Path.GetDirectoryName(iniPath) ?? ".")
                : backupDir;
            var name = Path.GetFileNameWithoutExtension(iniPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(dir, $"{name}.{stamp}.bak.ini");
        }

        /// <summary>
        /// Back up the current file into <paramref name="backupDir"/>, then atomically
        /// replace it with <paramref name="text"/>. Returns the backup path, or null when
        /// there was no existing file to back up.
        /// </summary>
        public static string Write(string iniPath, string text, string backupDir = null)
        {
            if (string.IsNullOrEmpty(iniPath)) throw new ArgumentNullException(nameof(iniPath));
            if (text == null) throw new ArgumentNullException(nameof(text));

            string backup = null;
            if (File.Exists(iniPath))
            {
                backup = BackupPathFor(iniPath, backupDir);
                var backupParent = Path.GetDirectoryName(backup);
                if (!string.IsNullOrEmpty(backupParent)) Directory.CreateDirectory(backupParent);
                File.Copy(iniPath, backup, overwrite: false);
            }

            // Temp file must sit on the same volume as the target for File.Replace to work.
            var tmp = iniPath + ".tmp";
            File.WriteAllText(tmp, text, NoBom);

            if (File.Exists(iniPath))
                File.Replace(tmp, iniPath, destinationBackupFileName: null);
            else
                File.Move(tmp, iniPath);

            return backup;
        }

        /// <summary>Write a copy of the config as saved, for an auditable history.</summary>
        public static string WriteSnapshot(string text, string serverName)
        {
            PanelPaths.EnsureDirs();
            var path = PanelPaths.NewSnapshotPath(serverName);
            File.WriteAllText(path, text, NoBom);
            return path;
        }

        /// <summary>
        /// Prune backups beyond the newest <paramref name="keep"/>, so a long editing
        /// session does not accumulate history indefinitely.
        /// </summary>
        public static int PruneBackups(string iniPath, string backupDir, int keep = 20)
        {
            if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep));

            var dir = string.IsNullOrEmpty(backupDir)
                ? Path.GetDirectoryName(iniPath)
                : backupDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;

            var name = Path.GetFileNameWithoutExtension(iniPath);
            var files = new DirectoryInfo(dir).GetFiles(name + ".*.bak.ini");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            var removed = 0;
            for (var i = keep; i < files.Length; i++)
            {
                try { files[i].Delete(); removed++; }
                catch (IOException) { /* locked by another tool; leave it */ }
            }
            return removed;
        }
    }
}
