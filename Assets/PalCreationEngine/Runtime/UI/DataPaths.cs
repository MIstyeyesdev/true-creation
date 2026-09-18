using System.IO;
using UnityEngine;

namespace PalCreationEngine.UI
{
    /// <summary>Where the generated lookup data lives, in editor and in a build.</summary>
    public static class DataPaths
    {
        public const string FolderName = "PalCreationEngine";

        /// <summary>
        /// Application.streamingAssetsPath resolves to Assets/StreamingAssets in
        /// the editor and to the packaged StreamingAssets folder in a Windows
        /// build, so plain file reads work in both. (It would not on Android or
        /// WebGL, which this tool does not target.)
        /// </summary>
        public static string LookupDirectory =>
            Path.Combine(Application.streamingAssetsPath, FolderName);
    }
}
