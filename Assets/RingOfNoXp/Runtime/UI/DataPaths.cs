using System.IO;
using UnityEngine;

namespace RingOfNoXp.UI
{
    /// <summary>Where the generated lookup data lives, in editor and in a build.</summary>
    public static class DataPaths
    {
        public const string FolderName = "RingOfNoXp";

        /// <summary>StreamingAssets resolves to Assets/StreamingAssets in the editor and to the packaged folder in a Windows build.</summary>
        public static string LookupDirectory => Path.Combine(Application.streamingAssetsPath, FolderName);

        /// <summary>The editor keeps Desktop\Palworld Stuff; the exe uses Documents\True Creation, the user's own place.</summary>
        private static string UserRoot => Application.isEditor
            ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "Palworld Stuff")
            : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "True Creation");

        public static string DefaultOutputFolder => Path.Combine(UserRoot, "mods_out");

        /// <summary>Root for projects: one folder per project name, holding the project file and its generated package.</summary>
        public static string DefaultProjectFolder => Path.Combine(UserRoot, "RingOfNoXp Projects");

        public static string ProjectFolderFor(string name) => Path.Combine(DefaultProjectFolder, name);

        public const string ProjectExtension = "ringofnoxp.json";

        /// <summary>Pointer to the last project opened, so the tool reopens it next time.</summary>
        public static string LastProjectPointer => Path.Combine(Application.persistentDataPath, "ringofnoxp_last_project.txt");
    }
}
