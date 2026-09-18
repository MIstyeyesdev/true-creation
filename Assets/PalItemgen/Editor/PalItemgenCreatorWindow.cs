using PalItemgen.Lookup;
using PalItemgen.UI;
using UnityEditor;
using UnityEngine;

namespace PalItemgen.EditorTools
{
    /// <summary>Hosts the full creator inside Unity: Tools > Pal Itemgen. Pages: Package &amp; stations, Producers &amp; item links, Manager item, Exact copy.
    /// Carried over from the retired PalItemgen project, where it was the window's only mode; the Management Station window is <see cref="PalItemgenWindow"/>.</summary>
    public sealed class PalItemgenCreatorWindow : EditorWindow
    {
        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Open()
        {
            var window = GetWindow<PalItemgenCreatorWindow>();
            window.titleContent = new GUIContent("Pal Itemgen");
            window.minSize = new Vector2(1280, 780);
            window.Show();
        }

        private void CreateGUI()
        {
            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[PalItemgen] {error}");
            var view = new PalItemgenView(data, Debug.Log);
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
            if (!HasOpenInstances<PalItemgenCreatorWindow>()) { Open(); return; }
            var window = GetWindow<PalItemgenCreatorWindow>();
            window.rootVisualElement.Clear();
            window.CreateGUI();
        }
    }
}
