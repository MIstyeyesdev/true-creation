using System.IO;
using UnityEngine;

namespace PalItemgen.UI
{
    /// <summary>Where the generated lookup data and the Lua runtime live, in editor and in a build.</summary>
    public static class DataPaths
    {
        public const string FolderName = "PalItemgen";

        /// <summary>StreamingAssets resolves to Assets/StreamingAssets in the editor and to the packaged folder in a Windows build.</summary>
        public static string LookupDirectory => Path.Combine(Application.streamingAssetsPath, FolderName);

        public static string LuaDirectory => Path.Combine(LookupDirectory, "lua");

        public static string DefaultThumbnail => Path.Combine(LookupDirectory, "thumbnail.png");

        /// <summary>The editor keeps Desktop\Palworld Stuff; the exe uses Documents\True Creation, the user's own place.</summary>
        private static string UserRoot => Application.isEditor
            ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "Palworld Stuff")
            : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "True Creation");

        public static string DefaultOutputFolder => Path.Combine(UserRoot, "mods_out");

        /// <summary>
        /// Root for projects: one folder per project name under it, holding the
        /// project file and every mod file generated for it.
        /// </summary>
        public static string DefaultProjectFolder => Path.Combine(UserRoot, "PalItemgen Projects");

        /// <summary>The folder of one named project (project file + generated package).</summary>
        public static string ProjectFolderFor(string name) => Path.Combine(DefaultProjectFolder, name);

        public const string ProjectExtension = "palitemgen.json";

        /// <summary>Pointer to the last project opened, so the tool reopens it next time.</summary>
        public static string LastProjectPointer => Path.Combine(Application.persistentDataPath, "palitemgen_last_project.txt");

        /// <summary>Legacy autosave from the first build; still read once so nothing is lost.</summary>
        public static string ProjectFile => Path.Combine(Application.persistentDataPath, "palitemgen_project.json");
    }
}
