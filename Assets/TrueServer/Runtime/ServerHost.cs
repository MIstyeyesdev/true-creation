using System;
using System.Collections.Generic;

namespace TrueServer
{
    /// <summary>
    /// What True Server needs from the program hosting it: per-user settings, a folder picker, a save-file dialog,
    /// a yes/no question, a message, and Explorer. In the Unity editor the defaults are the editor's own
    /// (EditorPrefs, EditorUtility), so this project's window needs no setup. True Creation points these at its own
    /// host when it builds the Server tab, so the same panel runs in its exe. Settings are per user and never part
    /// of a project.
    /// </summary>
    public static class ServerHost
    {
#if UNITY_EDITOR
        public static Func<string, string, string> GetString = (key, fallback) => UnityEditor.EditorPrefs.GetString(key, fallback);
        public static Action<string, string> SetString = (key, value) => UnityEditor.EditorPrefs.SetString(key, value ?? string.Empty);
        public static Func<string, int, int> GetInt = (key, fallback) => UnityEditor.EditorPrefs.GetInt(key, fallback);
        public static Action<string, int> SetInt = (key, value) => UnityEditor.EditorPrefs.SetInt(key, value);
        public static Action<string> DeleteKey = key => UnityEditor.EditorPrefs.DeleteKey(key);

        /// <summary>(title, start folder) -> the chosen folder, or "" when cancelled.</summary>
        public static Func<string, string, string> OpenFolderPanel =
            (title, start) => UnityEditor.EditorUtility.OpenFolderPanel(title, start ?? string.Empty, string.Empty) ?? string.Empty;

        /// <summary>(title, folder, default name, extension) -> the chosen file, or "" when cancelled.</summary>
        public static Func<string, string, string, string, string> SaveFilePanel =
            (title, dir, name, ext) => UnityEditor.EditorUtility.SaveFilePanel(title, dir ?? string.Empty, name ?? string.Empty, ext ?? string.Empty) ?? string.Empty;

        /// <summary>(title, message, ok, cancel) -> true for ok.</summary>
        public static Func<string, string, string, string, bool> Confirm =
            (title, message, ok, cancel) => UnityEditor.EditorUtility.DisplayDialog(title, message, ok, cancel);

        public static Action<string, string> Message = (title, message) => UnityEditor.EditorUtility.DisplayDialog(title, message, "OK");
        public static Action<string> Reveal = path => UnityEditor.EditorUtility.RevealInFinder(path);
#else
        // Outside the editor a host sets these (True Creation does). Until one does: remembered for this run only,
        // and every dialog answers "cancelled".
        private static readonly Dictionary<string, string> Memory = new Dictionary<string, string>();

        public static Func<string, string, string> GetString = (key, fallback) => Memory.TryGetValue(key, out var v) ? v : fallback;
        public static Action<string, string> SetString = (key, value) => Memory[key] = value ?? string.Empty;
        public static Func<string, int, int> GetInt = (key, fallback) => Memory.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;
        public static Action<string, int> SetInt = (key, value) => Memory[key] = value.ToString();
        public static Action<string> DeleteKey = key => Memory.Remove(key);
        public static Func<string, string, string> OpenFolderPanel = (title, start) => string.Empty;
        public static Func<string, string, string, string, string> SaveFilePanel = (title, dir, name, ext) => string.Empty;
        public static Func<string, string, string, string, bool> Confirm = (title, message, ok, cancel) => false;
        public static Action<string, string> Message = (title, message) => UnityEngine.Debug.Log("[TrueServer] " + title + ": " + message);
        public static Action<string> Reveal = path => { };
#endif
    }
}
