using RingOfNoXp.Lookup;
using RingOfNoXp.UI;
using UnityEditor;
using UnityEngine;

namespace RingOfNoXp.EditorTools
{
    /// <summary>Hosts the Ring of No XP creator inside Unity: Tools &gt; Ring of No XP.</summary>
    public sealed class RingOfNoXpWindow : EditorWindow
    {
        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Open()
        {
            var window = GetWindow<RingOfNoXpWindow>();
            window.titleContent = new GUIContent("Ring of No XP");
            window.minSize = new Vector2(1180, 760);
            window.Show();
        }

        private void CreateGUI()
        {
            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[RingOfNoXp] {error}");

            var view = new RingOfNoXpView(data, Debug.Log)
            {
                SaveFileDialog = (title, dir, name, ext) => EditorUtility.SaveFilePanel(title, dir, name, ext),
                OpenFileDialog = (title, dir, ext) => EditorUtility.OpenFilePanel(title, dir, ext),
                OpenFolderDialog = (title, dir) => EditorUtility.OpenFolderPanel(title, dir, ""),
                RevealFolder = EditorUtility.RevealInFinder,
                Confirm = (title, message) => EditorUtility.DisplayDialog(title, message, "Uninstall", "Cancel")
            };
            rootVisualElement.Add(view);
        }

        // merged 2026-09-16 into the one tool: Tools > True Engine (TrueEngineWindow tab)
        public static void Reload()
        {
            if (!HasOpenInstances<RingOfNoXpWindow>()) { Open(); return; }
            var window = GetWindow<RingOfNoXpWindow>();
            window.rootVisualElement.Clear();
            window.CreateGUI();
        }
    }
}
