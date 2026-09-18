#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TrueCreation.Host
{
    /// <summary>
    /// The Unity editor's own settings (EditorPrefs, per machine) and dialogs (EditorUtility): exactly what the tabs
    /// called before the exe existed, so the editor behaves as it did. Compiled only in the editor.
    /// </summary>
    public sealed class EditorHost : IAppHost
    {
        public string Name => "Unity editor";
        public bool IsEditor => true;

        public string GetString(string key, string fallback) => EditorPrefs.GetString(key, fallback);
        public void SetString(string key, string value) => EditorPrefs.SetString(key, value ?? "");
        public int GetInt(string key, int fallback) => EditorPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) => EditorPrefs.SetInt(key, value);
        public void DeleteKey(string key) => EditorPrefs.DeleteKey(key);

        public string OpenFilePanel(string title, string directory, string extension) =>
            EditorUtility.OpenFilePanel(title ?? "", directory ?? "", extension ?? "") ?? "";

        public string SaveFilePanel(string title, string directory, string defaultName, string extension) =>
            EditorUtility.SaveFilePanel(title ?? "", directory ?? "", defaultName ?? "", extension ?? "") ?? "";

        public string OpenFolderPanel(string title, string directory) =>
            EditorUtility.OpenFolderPanel(title ?? "", directory ?? "", "") ?? "";

        public bool Confirm(string title, string message, string ok, string cancel) =>
            EditorUtility.DisplayDialog(title ?? "", message ?? "", string.IsNullOrEmpty(ok) ? "OK" : ok, string.IsNullOrEmpty(cancel) ? "Cancel" : cancel);

        public void Message(string title, string message) => EditorUtility.DisplayDialog(title ?? "", message ?? "", "OK");

        public void Reveal(string path)
        {
            if (!string.IsNullOrEmpty(path)) EditorUtility.RevealInFinder(path);
        }

        public void OpenExternal(string pathOrUrl)
        {
            if (string.IsNullOrEmpty(pathOrUrl)) return;
            var isPath = File.Exists(pathOrUrl) || Directory.Exists(pathOrUrl);
            Application.OpenURL(isPath ? "file:///" + pathOrUrl.Replace('\\', '/') : pathOrUrl);
        }
    }
}
#endif
