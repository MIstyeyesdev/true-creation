using PalCreationEngine.Lookup;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalCreationEngine.UI
{
    /// <summary>
    /// Runtime host for the standalone app: put this on a GameObject alongside
    /// a UIDocument and it fills the document with the same view the editor
    /// window uses.
    ///
    /// Nothing here is editor-only, so it survives into a build. The UIDocument
    /// still needs a PanelSettings asset assigned -- Unity cannot create one
    /// implicitly -- and the check below says so plainly rather than presenting
    /// an empty window.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PalCreationEngineApp : MonoBehaviour
    {
        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();

            if (document.panelSettings == null)
            {
                Debug.LogError(
                    "[PalCreationEngine] The UIDocument has no PanelSettings asset assigned, so " +
                    "nothing can be drawn. Create one via Assets > Create > UI Toolkit > Panel " +
                    "Settings Asset and assign it to this UIDocument.");
                return;
            }

            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors)
                Debug.LogWarning($"[PalCreationEngine] {error}");

            document.rootVisualElement.Clear();
            document.rootVisualElement.Add(new PalEditorView(data, Debug.Log));
        }
    }
}
