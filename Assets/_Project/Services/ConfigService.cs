using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PalPanel.Io;
using PalPanel.Model;
using PalPanel.Schema;

namespace PalPanel.Services
{
    /// <summary>A single pending change, kept so the UI can show a review-before-save diff.</summary>
    public sealed class PendingChange
    {
        public string Key;
        public string OldRaw;
        public string NewRaw;
        public bool IsInsert;
    }

    /// <summary>
    /// Owns the loaded PalWorldSettings.ini for one install, stages edits, and commits them
    /// with a backup. Edits are staged rather than written through so a mistake costs nothing
    /// until Save is pressed.
    /// </summary>
    public sealed class ConfigService
    {
        private readonly OptionSchema _schema;
        private PalIniDocument _doc;
        private readonly Dictionary<string, PendingChange> _pending =
            new(StringComparer.OrdinalIgnoreCase);

        public ServerInstall Install { get; private set; }

        /// <summary>Path of the snapshot written by the most recent successful save.</summary>
        public string LastSnapshotPath { get; private set; }
        public bool IsLoaded => _doc != null;
        public int PendingCount => _pending.Count;
        public IEnumerable<PendingChange> Pending => _pending.Values;

        public ConfigService(OptionSchema schema) =>
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));

        // ---------------------------------------------------------------- load / save

        /// <summary>
        /// True when the live ini holds no OptionSettings block. A clean dedicated server writes a
        /// 2-byte PalWorldSettings.ini on first start and runs on built-in defaults
        /// (Docs/FIXES_AND_ERRORS.md F-0010); the panel offers <see cref="SeedFromDefault"/>.
        /// </summary>
        public bool NeedsSeed { get; private set; }

        public void Load(ServerInstall install)
        {
            Install = install ?? throw new ArgumentNullException(nameof(install));
            _pending.Clear();
            _doc = null;
            NeedsSeed = false;

            var text = File.Exists(install.ConfigPath)
                ? File.ReadAllText(install.ConfigPath)
                : string.Empty;

            if (!PalIniDocument.HasOptionSettings(text))
            {
                NeedsSeed = true;   // clean server: nothing to edit until seeded
                return;
            }

            _doc = PalIniDocument.Parse(text);
        }

        /// <summary>
        /// Writes DefaultPalWorldSettings.ini's OptionSettings block into the live ini (backup
        /// first) and reloads. The values are the shipped defaults, so the server behaves the same;
        /// the ini just becomes editable. Refused while PalServer runs.
        /// </summary>
        public string SeedFromDefault()
        {
            if (Install == null) throw new InvalidOperationException("No install loaded.");
            if (!NeedsSeed)
                throw new InvalidOperationException("PalWorldSettings.ini already has settings.");
            if (IsServerRunning())
                throw new InvalidOperationException("Stop PalServer before writing its ini.");
            if (!File.Exists(Install.DefaultConfigPath))
                throw new FileNotFoundException("DefaultPalWorldSettings.ini not found.",
                                                Install.DefaultConfigPath);

            var seed = PalIniDocument.BuildSeedFromDefault(
                File.ReadAllText(Install.DefaultConfigPath));

            var dir = Path.GetDirectoryName(Install.ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var backup = SafeConfigWriter.Write(Install.ConfigPath, seed, PanelPaths.BackupsDir);
            Load(Install);
            return backup;
        }

        /// <summary>
        /// The server reads its config once at boot and does not reread it. Writing while it
        /// runs is silently ineffective, so the UI blocks Save on this.
        /// </summary>
        public bool IsServerRunning() => AnyServerRunning();

        /// <summary>Same check without a loaded config (used before a load succeeds).</summary>
        public static bool AnyServerRunning()
        {
            try
            {
                return Process.GetProcessesByName("PalServer").Length > 0
                    || Process.GetProcessesByName("PalServer-Win64-Shipping").Length > 0
                    || Process.GetProcessesByName("PalServer-Win64-Shipping-Cmd").Length > 0;
            }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>Commit staged edits. Returns the backup path, or null if nothing changed.</summary>
        public string Save()
        {
            if (!IsLoaded) throw new InvalidOperationException("No config loaded.");
            if (_pending.Count == 0) return null;

            foreach (var c in _pending.Values)
            {
                if (c.IsInsert)
                {
                    var field = _schema.Find(c.Key);
                    _doc.InsertAfter(PrecedingKeyOf(field), c.Key, c.NewRaw);
                }
                else if (!_doc.SetRaw(c.Key, c.NewRaw))
                {
                    throw new InvalidOperationException(
                        $"Key vanished between staging and save: {c.Key}");
                }
            }

            var text = _doc.ToText();
            var backup = SafeConfigWriter.Write(Install.ConfigPath, text, PanelPaths.BackupsDir);

            // Keep an as-written copy alongside the backup so the output folder holds a
            // complete before/after pair for every save.
            _doc.TryGetRaw("ServerName", out var rawName);
            LastSnapshotPath = SafeConfigWriter.WriteSnapshot(
                text, PalIniDocument.ParseString(rawName));

            SafeConfigWriter.PruneBackups(Install.ConfigPath, PanelPaths.BackupsDir);

            _pending.Clear();
            _doc = PalIniDocument.Parse(File.ReadAllText(Install.ConfigPath)); // reload canonical
            return backup;
        }

        public void Revert() => _pending.Clear();

        /// <summary>Struct-order neighbour, so an inserted key lands where the game puts it.</summary>
        private string PrecedingKeyOf(PalOptionField field)
        {
            if (field == null) return null;
            var idx = _schema.Options.IndexOf(field);
            for (var i = idx - 1; i >= 0; i--)
            {
                var candidate = _schema.Options[i].IniKey ?? _schema.Options[i].Name;
                if (_doc.Contains(candidate)) return candidate;
            }
            return null;
        }

        // ---------------------------------------------------------------- values

        /// <summary>Staged value if edited, else the on-disk value, else the stock default.</summary>
        public string GetRaw(string key)
        {
            if (_pending.TryGetValue(key, out var p)) return p.NewRaw;
            if (_doc != null && _doc.TryGetRaw(key, out var raw)) return raw;
            return _schema.Find(key)?.DefaultRaw;
        }

        public bool ExistsOnDisk(string key) => _doc != null && _doc.Contains(key);

        /// <summary>Stage an edit. A value equal to what is on disk clears the staged change.</summary>
        public void StageRaw(string key, string newRaw)
        {
            if (!IsLoaded) throw new InvalidOperationException("No config loaded.");

            var onDisk = _doc.TryGetRaw(key, out var cur) ? cur : null;

            if (string.Equals(onDisk, newRaw, StringComparison.Ordinal))
            {
                _pending.Remove(key);
                return;
            }

            _pending[key] = new PendingChange
            {
                Key = key,
                OldRaw = onDisk,
                NewRaw = newRaw,
                IsInsert = onDisk == null,
            };
        }

        public bool IsStaged(string key) => _pending.ContainsKey(key);

        /// <summary>Reset one option to its stock default.</summary>
        public void StageDefault(string key)
        {
            var field = _schema.Find(key);
            if (field?.DefaultRaw != null) StageRaw(key, field.DefaultRaw);
        }

        /// <summary>Options whose effective value differs from the stock default.</summary>
        public IEnumerable<PalOptionField> ModifiedFromDefault() =>
            _schema.Options.Where(o =>
            {
                var key = o.IniKey ?? o.Name;
                var effective = GetRaw(key);
                return effective != null &&
                       !string.Equals(effective, o.DefaultRaw, StringComparison.Ordinal);
            });

        /// <summary>Keys the running build knows about but this config file lacks.</summary>
        public IEnumerable<PalOptionField> MissingFromFile() =>
            _schema.Options.Where(o =>
                o.IniPresence != "struct_only" && !ExistsOnDisk(o.IniKey ?? o.Name));
    }
}
