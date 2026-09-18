using System;
using System.IO;
using UnityEngine;

namespace TrueCreation.Host
{
    /// <summary>
    /// Where things live, in the editor and in the exe. Shipped data: StreamingAssets, which Unity copies into every
    /// build as it is. A user's own files: Documents\True Creation, never the program folder (it may be read-only and
    /// is replaced by the next version).
    /// </summary>
    public static class AppPaths
    {
        public const string ProductFolder = "True Creation";

        /// <summary>Documents\True Creation\&lt;sub&gt;: the default place for what a user saves or exports.</summary>
        public static string UserFolder(string sub)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(sub) ? Path.Combine(docs, ProductFolder) : Path.Combine(docs, ProductFolder, sub);
        }

        /// <summary>A file or folder inside the shipped data (StreamingAssets).</summary>
        public static string Shipped(string relative) =>
            Path.Combine(Application.streamingAssetsPath, (relative ?? "").Replace('/', Path.DirectorySeparatorChar));

        /// <summary>The Unity project root in the editor (the folder holding Assets); the program folder in the exe.</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        /// <summary>
        /// The Base Game TOC and reference: Docs/BaseGame in the editor (the one copy), StreamingAssets/BaseGame in the
        /// exe (the build copies it there, Tools > True Creation > Build Windows exe).
        /// </summary>
        public static string BaseGameDocs => Application.isEditor ? Path.Combine(ProjectRoot, "Docs", "BaseGame") : Shipped("BaseGame");
    }
}
