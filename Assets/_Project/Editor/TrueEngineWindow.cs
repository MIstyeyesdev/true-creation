using TrueCreation.App;
using UnityEditor;
using UnityEngine;

namespace TrueEngine.EditorTools
{
    /// <summary>
    /// True Engine in the Unity editor: the same tab shell the exe runs (TrueCreation.App.EngineShell, tabs from
    /// TabCatalog), in a window. What only the editor adds: the table browser and the options dump open in real
    /// windows (the exe shows them as overlays inside the tab). Every tab runs in both; True Server is the Server
    /// tab only (no window or menu of its own here).
    /// </summary>
    public sealed class TrueEngineWindow : EditorWindow
    {
        private EngineShell _shell;

        [MenuItem("Tools/True Engine")]
        public static void Open()
        {
            var window = GetWindow<TrueEngineWindow>();
            window.titleContent = new GUIContent("True Engine");
            window.minSize = new Vector2(1280, 800);
            window.Show();
        }

        [MenuItem("Tools/True Engine (Reload Data)")]
        public static void Reload()
        {
            if (!HasOpenInstances<TrueEngineWindow>()) { Open(); return; }
            GetWindow<TrueEngineWindow>().ReloadActive();
        }

        private void CreateGUI()
        {
            _shell = new EngineShell(TabCatalog.Create(new TabHooks
            {
                ConfigureItemgen = view =>
                {
                    view.OpenDump = PalItemgen.EditorTools.DumpWindow.Show;
                    view.OpenBrowser = PalItemgen.EditorTools.DataBrowserWindow.Show;
                },
                ConfigureCreationEngine = view => view.OpenBrowser = PalCreationEngine.EditorTools.DataBrowserWindow.Show,
                ConfigureNewPal = panel => panel.OpenBrowser = PalCreationEngine.EditorTools.DataBrowserWindow.Show,
            }));
            rootVisualElement.Add(_shell);
        }

        /// <summary>Drops the active tab's cached view and rebuilds it, re-reading the lookup data from disk.</summary>
        public void ReloadActive() => _shell?.ReloadActive();

        private void OnDestroy() => _shell?.DisposeAll();
    }
}
