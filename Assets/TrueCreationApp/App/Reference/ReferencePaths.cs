using System;
using System.IO;
using TrueCreation.Host;
using UnityEngine;

namespace TrueCreation.App
{
    /// <summary>
    /// Paths for the Reference tab (the 1.0.4 export browser), in the editor and in the exe. The index and the docs are
    /// built in: the project's Docs/ in the editor, StreamingAssets/Reference in the exe (the build copies the index
    /// and the reference docs there). The FModel export and Palworld Stuff are optional, chosen per user, and shared
    /// with the Base Game TOC tab (TocPaths).
    /// </summary>
    public static class ReferencePaths
    {
        private const string KeyGame = "PalRef.GameRoot";
        public const string IndexFile = ReferenceIndex.IndexFileName;

        public static string Docs => Application.isEditor ? Path.Combine(AppPaths.ProjectRoot, "Docs") : AppPaths.Shipped("Reference/Docs");

        public static string IndexJsonl => Application.isEditor ? Path.Combine(AppPaths.ProjectRoot, "Docs", IndexFile) : AppPaths.Shipped("Reference/" + IndexFile);

        public static string PalworldStuff => TocPaths.PalworldStuff;
        public static string Exports => TocPaths.Exports;
        public static string AtlasScripts => Path.Combine(PalworldStuff, "atlas_build_scripts");
        public static string FieldAtlasJson => Path.Combine(PalworldStuff, "palworld_field_atlas.json");
        public static string FieldAtlasMd => Path.Combine(PalworldStuff, "PALWORLD_FIELD_ATLAS.md");
        public static string BlueprintCdo => Path.Combine(AtlasScripts, "_state", "blueprint_cdo.json");

        /// <summary>This PC's Palworld install: the per-user choice, else what Steam's records say, else "".</summary>
        public static string GameRoot
        {
            get
            {
                var stored = AppHost.Current.GetString(KeyGame, "");
                if (!string.IsNullOrWhiteSpace(stored)) return stored;
                var found = PalCreationEngine.Lookup.PalworldInstall.Find(null);
                return found.Found ? found.Path : "";
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) AppHost.Current.DeleteKey(KeyGame);
                else AppHost.Current.SetString(KeyGame, value.Trim());
            }
        }

        public static string ModsTxt => Path.Combine(GameRoot, "Mods", "NativeMods", "UE4SS", "Mods", "mods.txt");
        public static string Ue4ssLog => Path.Combine(GameRoot, "Mods", "NativeMods", "UE4SS", "UE4SS.log");

        public static string ExportAbs(string rel) => TocPaths.ExportAbs(rel);

        public static void ResetAll()
        {
            AppHost.Current.DeleteKey(KeyGame);
            AppHost.Current.DeleteKey("PalRef.PalworldStuff");
            TocPaths.ResetExports();
        }

        public static void SetPalworldStuff(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) AppHost.Current.DeleteKey("PalRef.PalworldStuff");
            else AppHost.Current.SetString("PalRef.PalworldStuff", value.Trim());
        }
    }
}
