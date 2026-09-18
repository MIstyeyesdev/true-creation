using System;
using System.Collections.Generic;
using PalCreationEngine.UI;
using UnityEditor;
using UnityEngine;

namespace PalCreationEngine.EditorTools
{
    /// <summary>
    /// Hosts a <see cref="DataBrowser"/> in its own editor window, so browsing
    /// a 2,466-row item table does not happen inside a 200-row dropdown pinned
    /// to a form field.
    ///
    /// The browser itself is a plain VisualElement; this only supplies the
    /// window. The runtime app hosts the same browser as a modal overlay.
    /// </summary>
    public sealed class DataBrowserWindow : EditorWindow
    {
        private static DataBrowserWindow _open;

        public static void Show(
            string title,
            List<BrowserColumn> columns,
            List<string[]> rows,
            Action<string> onPick)
        {
            // One browser at a time: opening a second while the first is up
            // leaves two windows racing to fill the same field.
            if (_open != null)
            {
                _open.Close();
                _open = null;
            }

            var window = CreateInstance<DataBrowserWindow>();
            _open = window;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(620, 380);

            window.rootVisualElement.Add(new DataBrowser(
                title, columns, rows,
                picked =>
                {
                    onPick?.Invoke(picked);
                    window.Close();
                },
                window.Close));

            window.ShowUtility();
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }
    }
}
