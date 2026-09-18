using System;
using System.Collections.Generic;
using PalItemgen.UI;
using UnityEditor;
using UnityEngine;

namespace PalItemgen.EditorTools
{
    /// <summary>
    /// Hosts a <see cref="DataBrowser"/> in its own editor window (the Creation
    /// Engine's "Paldex slots" style), so a 2,466-row table is browsed in a
    /// real window instead of a dropdown. The runtime app hosts the same
    /// browser as a modal overlay.
    /// </summary>
    public sealed class DataBrowserWindow : EditorWindow
    {
        private static DataBrowserWindow _open;

        public static void Show(string title, List<BrowserColumn> columns, List<string[]> rows, Action<string> onPick)
        {
            if (_open != null)
            {
                _open.Close();
                _open = null;
            }
            var window = CreateInstance<DataBrowserWindow>();
            _open = window;
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(760, 420);
            window.rootVisualElement.Add(new DataBrowser(title, columns, rows,
                picked => { onPick?.Invoke(picked); window.Close(); },
                window.Close));
            window.ShowUtility();
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }
    }
}
