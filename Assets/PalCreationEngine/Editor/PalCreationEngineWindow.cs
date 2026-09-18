using PalCreationEngine.Lookup;
using PalCreationEngine.UI;
using UnityEditor;
using UnityEngine;

namespace PalCreationEngine.EditorTools
{
    /// <summary>
    /// Hosts the editor inside Unity, opened from Tools > Pal Creation Engine.
    ///
    /// This exists so the UI can be exercised without building and launching a
    /// standalone player on every change. It is a host only: every line of the
    /// interface lives in PalEditorView, which the runtime app hosts the same
    /// way, so there is one implementation rather than two that drift.
    /// </summary>
    public sealed class PalCreationEngineWindow : EditorWindow
    {
        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Open()
        {
            var window = GetWindow<PalCreationEngineWindow>();
            window.titleContent = new GUIContent("Pal Creation Engine");
            window.minSize = new Vector2(760, 560);
            window.Show();
        }

        private void CreateGUI()
        {
            var data = GameData.Load(DataPaths.LookupDirectory);

            foreach (var error in data.LoadErrors)
                Debug.LogWarning($"[PalCreationEngine] {error}");

            var view = new PalEditorView(data, Debug.Log);
            // Gives the view a real separate window for dataset browsing. Without
            // this it falls back to a modal overlay, which is what the runtime
            // app uses.
            view.OpenBrowser = DataBrowserWindow.Show;
            rootVisualElement.Add(view);
        }

        /// <summary>
        /// Rebuilds the view so edits to the generated lookup data are picked up
        /// without reopening the window.
        /// </summary>
        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Reload()
        {
            if (!HasOpenInstances<PalCreationEngineWindow>())
            {
                Open();
                return;
            }

            var window = GetWindow<PalCreationEngineWindow>();
            window.rootVisualElement.Clear();
            window.CreateGUI();
        }
    }
}
