using PalItemgen.Lookup;
using PalItemgen.UI;
using UnityEditor;
using UnityEngine;

namespace PalItemgen.EditorTools
{
    /// <summary>Hosts the Management Station screen inside Unity: Tools > Management Station. One tab: the exact copy.</summary>
    public sealed class PalItemgenWindow : EditorWindow
    {
        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Open()
        {
            var window = GetWindow<PalItemgenWindow>();
            window.titleContent = new GUIContent("Management Station");
            window.minSize = new Vector2(1280, 780);
            window.Show();
        }

        private void CreateGUI()
        {
            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[PalItemgen] {error}");
            var view = new PalItemgenView(data, Debug.Log, exactCopyOnly: true);
            // Real windows in the editor (dump, table browser, file dialogs); the runtime app falls back to overlays.
            view.OpenDump = DumpWindow.Show;
            view.OpenBrowser = DataBrowserWindow.Show;
            view.SaveFileDialog = (title, dir, name, ext) => EditorUtility.SaveFilePanel(title, dir, name, ext);
            view.OpenFileDialog = (title, dir, ext) => EditorUtility.OpenFilePanel(title, dir, ext);
            rootVisualElement.Add(view);
        }

        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Reload()
        {
            if (!HasOpenInstances<PalItemgenWindow>()) { Open(); return; }
            var window = GetWindow<PalItemgenWindow>();
            window.rootVisualElement.Clear();
            window.CreateGUI();
        }
    }
}
