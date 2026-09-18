namespace TrueCreation.Host
{
    /// <summary>
    /// What a tab needs from wherever it runs: per-user settings, the system's file and folder dialogs, a yes/no
    /// question, and showing or opening a file. The Unity editor answers with its own (EditorHost); the exe answers
    /// with Windows' own and one settings file per Windows user (WindowsHost). Tabs call AppHost.Current, never
    /// UnityEditor, so the same tab code runs in both.
    /// </summary>
    public interface IAppHost
    {
        /// <summary>"Unity editor" or "Windows".</summary>
        string Name { get; }
        bool IsEditor { get; }

        string GetString(string key, string fallback);
        void SetString(string key, string value);
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        void DeleteKey(string key);

        /// <summary>The chosen file, or "" when cancelled. <paramref name="extension"/> has no dot; "" = any file.</summary>
        string OpenFilePanel(string title, string directory, string extension);
        /// <summary>The chosen file, or "" when cancelled.</summary>
        string SaveFilePanel(string title, string directory, string defaultName, string extension);
        /// <summary>The chosen folder, or "" when cancelled.</summary>
        string OpenFolderPanel(string title, string directory);
        /// <summary>True when the user chose <paramref name="ok"/>.</summary>
        bool Confirm(string title, string message, string ok, string cancel);
        void Message(string title, string message);

        /// <summary>Shows a file or folder in Explorer (the file selected).</summary>
        void Reveal(string path);
        /// <summary>Opens a file, folder or web address with what the system uses for it.</summary>
        void OpenExternal(string pathOrUrl);
    }
}
