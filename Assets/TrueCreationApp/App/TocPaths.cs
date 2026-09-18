using System;
using System.IO;
using TrueCreation.Host;
using UnityEngine;

namespace TrueCreation.App
{
    /// <summary>
    /// Paths for the Base Game TOC tab, in the editor and in the exe. The TOC itself is built in (AppPaths.BaseGameDocs).
    /// The FModel export is optional and chosen per user; it only lets Open file / Reveal reach a game file. The keys
    /// are the ones the Reference tab has always used (PalRef.PalworldStuff, PalRef.Exports), so a folder chosen in
    /// either tab is the same folder.
    /// </summary>
    public static class TocPaths
    {
        private const string KeyStuff = "PalRef.PalworldStuff";
        private const string KeyExports = "PalRef.Exports";

        public static string PalworldStuff =>
            AppHost.Current.GetString(KeyStuff, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Palworld Stuff"));

        /// <summary>FModel's export folder when none is chosen: Palworld Stuff\FModel\Output\Exports.</summary>
        public static string DefaultExports => Path.Combine(PalworldStuff, "FModel", "Output", "Exports");

        /// <summary>The FModel export folder (the one holding Pal\Content); unset means <see cref="DefaultExports"/>.</summary>
        public static string Exports
        {
            get
            {
                var v = AppHost.Current.GetString(KeyExports, "");
                return string.IsNullOrWhiteSpace(v) ? DefaultExports : v;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) AppHost.Current.DeleteKey(KeyExports);
                else AppHost.Current.SetString(KeyExports, value.Trim());
            }
        }

        public static bool ExportsIsDefault => string.IsNullOrWhiteSpace(AppHost.Current.GetString(KeyExports, ""));

        public static void ResetExports() => AppHost.Current.DeleteKey(KeyExports);

        /// <summary>
        /// The Exports folder for what a person is likely to pick: the Exports folder itself, FModel's Output folder,
        /// Exports\Pal or Exports\Pal\Content. Anything else comes back as picked. Two or three Directory.Exists calls;
        /// nothing is listed.
        /// </summary>
        public static string NormalizeExports(string picked)
        {
            if (string.IsNullOrWhiteSpace(picked)) return picked;
            var p = picked.Trim().Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(Path.Combine(p, "Pal", "Content"))) return p;
            if (Directory.Exists(Path.Combine(p, "Exports", "Pal", "Content"))) return Path.Combine(p, "Exports");
            var name = Path.GetFileName(p);
            var parent = Path.GetDirectoryName(p);
            if (parent != null && name.Equals("Content", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(parent).Equals("Pal", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(parent) ?? p;
            if (parent != null && name.Equals("Pal", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(Path.Combine(p, "Content")))
                return parent;
            return p;
        }

        /// <summary>Folder picker for the FModel export: the normalised Exports folder, or null when cancelled.</summary>
        public static string PickExports()
        {
            var start = Directory.Exists(Exports) ? Exports : Directory.Exists(PalworldStuff) ? PalworldStuff : "";
            var picked = AppHost.Current.OpenFolderPanel("FModel export folder (the one holding Pal\\Content)", start);
            return string.IsNullOrEmpty(picked) ? null : NormalizeExports(picked);
        }

        /// <summary>Absolute path of an exported asset, from a TOC-relative path.</summary>
        public static string ExportAbs(string rel) =>
            Path.Combine(Exports, (rel ?? "").Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// A project-relative path (Docs/..., Assets/..., Tools/...) on this machine. The editor: the project. The exe:
        /// the shipped copy for what ships (Docs/BaseGame -> StreamingAssets/BaseGame, Assets/StreamingAssets -> StreamingAssets);
        /// anything else is not part of the program and shows as not on disk.
        /// </summary>
        public static string ProjectPath(string rel)
        {
            var r = (rel ?? "").Replace('\\', '/');
            if (!Application.isEditor)
            {
                if (r.StartsWith("Docs/BaseGame/", StringComparison.OrdinalIgnoreCase)) return AppPaths.Shipped("BaseGame/" + r.Substring("Docs/BaseGame/".Length));
                if (r.Equals("Docs/BaseGame", StringComparison.OrdinalIgnoreCase)) return AppPaths.Shipped("BaseGame");
                if (r.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase)) return AppPaths.Shipped(r.Substring("Assets/StreamingAssets/".Length));
            }
            return Path.Combine(AppPaths.ProjectRoot, r.Replace('/', Path.DirectorySeparatorChar));
        }

        public static bool Exists(string p) => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p));

        /// <summary>Show a file or folder in Explorer; says so when it is not on disk.</summary>
        public static void Reveal(string path)
        {
            if (!Exists(path)) { AppHost.Current.Message("Not found", path ?? ""); return; }
            AppHost.Current.Reveal(path);
        }

        /// <summary>Open a file with what the system uses for it; says so when it is not on disk.</summary>
        public static void OpenExternal(string path)
        {
            if (!Exists(path)) { AppHost.Current.Message("Not found", path ?? ""); return; }
            AppHost.Current.OpenExternal(path);
        }
    }
}
