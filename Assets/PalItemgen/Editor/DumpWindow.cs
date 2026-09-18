using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.EditorTools
{
    /// <summary>
    /// The "creative dev: list all options" window: a read-only text dump in
    /// its own editor window, so the full field list never crowds the tab.
    /// </summary>
    public sealed class DumpWindow : EditorWindow
    {
        private static DumpWindow _open;

        public static void Show(string title, string text)
        {
            if (_open != null) { _open.Close(); _open = null; }
            var window = CreateInstance<DumpWindow>();
            _open = window;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(700, 500);
            var scroll = new ScrollView { style = { flexGrow = 1 } };
            var field = new TextField { value = text, multiline = true, isReadOnly = true };
            field.style.flexGrow = 1;
            scroll.Add(field);
            window.rootVisualElement.Add(scroll);
            var copy = new Button(() => EditorGUIUtility.systemCopyBuffer = text) { text = "Copy to clipboard" };
            copy.style.height = 26;
            window.rootVisualElement.Add(copy);
            window.Show();
        }
    }
}
