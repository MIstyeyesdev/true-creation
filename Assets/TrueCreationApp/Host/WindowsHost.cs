using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TrueCreation.Host
{
    /// <summary>
    /// The exe's host. Dialogs are Windows' own, called directly (comdlg32 file dialogs, the shell32 folder browser,
    /// user32 message boxes; no plugin, nothing downloaded). Like the editor's, they are modal: the app waits for the
    /// answer. Reveal uses Explorer. Settings are one JSON file per Windows user, settings.json in
    /// Application.persistentDataPath (%USERPROFILE%\AppData\LocalLow\&lt;company&gt;\&lt;product&gt;); nothing is written
    /// next to the program. Every call is caught: a failed dialog logs to Player.log and returns "cancelled".
    /// </summary>
    public sealed class WindowsHost : IAppHost
    {
        public string Name => "Windows";
        public bool IsEditor => false;

        // ------------------------------------------------------------ settings (one file per Windows user)
        private Dictionary<string, string> _settings;

        public static string SettingsPath => Path.Combine(Application.persistentDataPath, "settings.json");

        private Dictionary<string, string> Settings
        {
            get
            {
                if (_settings != null) return _settings;
                _settings = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(SettingsPath))
                    {
                        var read = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                        if (read != null) _settings = new Dictionary<string, string>(read, StringComparer.Ordinal);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TrueCreation] settings.json could not be read; starting with defaults: " + e.Message);
                }
                return _settings;
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? ".");
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TrueCreation] settings.json could not be saved: " + e.Message);
            }
        }

        public string GetString(string key, string fallback) =>
            key != null && Settings.TryGetValue(key, out var v) ? v : fallback;

        public void SetString(string key, string value)
        {
            if (key == null) return;
            Settings[key] = value ?? "";
            Save();
        }

        public int GetInt(string key, int fallback) =>
            key != null && Settings.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        public void SetInt(string key, int value) => SetString(key, value.ToString(CultureInfo.InvariantCulture));

        public void DeleteKey(string key)
        {
            if (key != null && Settings.Remove(key)) Save();
        }

        // ------------------------------------------------------------ dialogs
        public string OpenFilePanel(string title, string directory, string extension)
        {
            try { return FileDialog(save: false, title, directory, null, extension); }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] the open-file dialog failed: " + e.Message); return ""; }
        }

        public string SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            try { return FileDialog(save: true, title, directory, defaultName, extension); }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] the save-file dialog failed: " + e.Message); return ""; }
        }

        public string OpenFolderPanel(string title, string directory)
        {
            try { return FolderDialog(title, directory); }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] the folder dialog failed: " + e.Message); return ""; }
        }

        public bool Confirm(string title, string message, string ok, string cancel)
        {
            // A Windows message box has fixed OK / Cancel buttons, so the question says which one does what.
            var text = (message ?? "") + (string.IsNullOrEmpty(ok) || ok == "OK"
                ? ""
                : "\n\nOK = " + ok + ".   Cancel = " + (string.IsNullOrEmpty(cancel) ? "Cancel" : cancel) + ".");
            try { return MessageBox(GetActiveWindow(), text, title ?? "", MB_OKCANCEL | MB_ICONQUESTION) == IDOK; }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] the message box failed: " + e.Message); return false; }
        }

        public void Message(string title, string message)
        {
            try { MessageBox(GetActiveWindow(), message ?? "", title ?? "", MB_OK | MB_ICONINFORMATION); }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] the message box failed: " + e.Message); }
        }

        // ------------------------------------------------------------ Explorer
        public void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) Process.Start("explorer.exe", "/select,\"" + full + "\"");
                else if (Directory.Exists(full)) Process.Start("explorer.exe", "\"" + full + "\"");
                else
                {
                    var parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) Process.Start("explorer.exe", "\"" + parent + "\"");
                }
            }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] Explorer could not be opened: " + e.Message); }
        }

        public void OpenExternal(string pathOrUrl)
        {
            if (string.IsNullOrEmpty(pathOrUrl)) return;
            try
            {
                if (File.Exists(pathOrUrl) || Directory.Exists(pathOrUrl))
                    Process.Start(new ProcessStartInfo { FileName = Path.GetFullPath(pathOrUrl), UseShellExecute = true });
                else
                    Application.OpenURL(pathOrUrl);
            }
            catch (Exception e) { Debug.LogWarning("[TrueCreation] could not open " + pathOrUrl + ": " + e.Message); }
        }

        // ------------------------------------------------------------ Win32
        private const uint MB_OK = 0x0, MB_OKCANCEL = 0x1, MB_ICONQUESTION = 0x20, MB_ICONINFORMATION = 0x40;
        private const int IDOK = 1;

        private const int OFN_OVERWRITEPROMPT = 0x2, OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8,
                          OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;

        private const uint BIF_RETURNONLYFSDIRS = 0x1, BIF_EDITBOX = 0x10, BIF_NEWDIALOGSTYLE = 0x40;
        private const uint BFFM_INITIALIZED = 1, BFFM_SETSELECTIONW = 0x0400 + 103;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class OpenFileName
        {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner = IntPtr.Zero;
            public IntPtr hInstance = IntPtr.Zero;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex = 1;
            public string lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData = IntPtr.Zero;
            public IntPtr lpfnHook = IntPtr.Zero;
            public string lpTemplateName;
            public IntPtr pvReserved = IntPtr.Zero;
            public int dwReserved;
            public int FlagsEx;
        }

        private delegate int BrowseCallbackProc(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BrowseInfo
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
            public uint ulFlags;
            public BrowseCallbackProc lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW", SetLastError = true)]
        private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHBrowseForFolderW")]
        private static extern IntPtr SHBrowseForFolder(ref BrowseInfo bi);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetPathFromIDListW")]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

        [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);
        [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")] private static extern void OleUninitialize();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);

        private static string FileDialog(bool save, string title, string directory, string defaultName, string extension)
        {
            var ext = (extension ?? "").Trim().TrimStart('.');
            const int max = 4096;
            var start = defaultName ?? "";
            if (start.Length >= max) start = "";
            var ofn = new OpenFileName
            {
                hwndOwner = GetActiveWindow(),
                lpstrFilter = ext.Length > 0
                    ? ext.ToUpperInvariant() + " files (*." + ext + ")\0*." + ext + "\0All files (*.*)\0*.*\0\0"
                    : "All files (*.*)\0*.*\0\0",
                lpstrFile = start + new string('\0', max - start.Length),
                nMaxFile = max,
                lpstrFileTitle = new string('\0', 512),
                nMaxFileTitle = 512,
                lpstrInitialDir = Directory.Exists(directory ?? "") ? directory : null,
                lpstrTitle = title ?? "",
                lpstrDefExt = ext.Length > 0 ? ext : null,
                Flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY
                        | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
            };
            var ok = save ? GetSaveFileName(ofn) : GetOpenFileName(ofn);
            if (!ok) return "";
            var path = ofn.lpstrFile ?? "";
            var end = path.IndexOf('\0');
            return end >= 0 ? path.Substring(0, end) : path;
        }

        // SHBrowseForFolder calls back once the dialog is up; the start folder is selected then.
        private static string _browseStart;
        private static readonly BrowseCallbackProc BrowseCallbackDelegate = BrowseCallback;

        [AOT.MonoPInvokeCallback(typeof(BrowseCallbackProc))]
        private static int BrowseCallback(IntPtr hwnd, uint msg, IntPtr lParam, IntPtr data)
        {
            if (msg == BFFM_INITIALIZED && !string.IsNullOrEmpty(_browseStart))
                SendMessage(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, _browseStart);
            return 0;
        }

        private static string FolderDialog(string title, string start)
        {
            // The resizable dialog with a path box (BIF_NEWDIALOGSTYLE) needs OLE on this thread; each successful
            // OleInitialize (S_OK or S_FALSE) is balanced below. If the thread refuses (another COM mode), the plain
            // dialog is used instead.
            var hr = OleInitialize(IntPtr.Zero);
            var ole = hr >= 0;
            var display = Marshal.AllocCoTaskMem(260 * 2);
            try
            {
                _browseStart = Directory.Exists(start ?? "") ? start : null;
                var bi = new BrowseInfo
                {
                    hwndOwner = GetActiveWindow(),
                    pszDisplayName = display,
                    lpszTitle = title ?? "",
                    ulFlags = BIF_RETURNONLYFSDIRS | (ole ? BIF_NEWDIALOGSTYLE | BIF_EDITBOX : 0u),
                    lpfn = BrowseCallbackDelegate,
                };
                var pidl = SHBrowseForFolder(ref bi);
                if (pidl == IntPtr.Zero) return "";
                try
                {
                    var sb = new StringBuilder(1024);
                    return SHGetPathFromIDList(pidl, sb) ? sb.ToString() : "";
                }
                finally { CoTaskMemFree(pidl); }
            }
            finally
            {
                Marshal.FreeCoTaskMem(display);
                _browseStart = null;
                if (ole) OleUninitialize();
            }
        }
    }
}
