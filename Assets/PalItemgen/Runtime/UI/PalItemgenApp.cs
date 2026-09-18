using PalItemgen.Lookup;
using UnityEngine;
using UnityEngine.UIElements;

namespace PalItemgen.UI
{
    /// <summary>
    /// Runtime host for the standalone app: put this on a GameObject with a
    /// UIDocument (PanelSettings assigned) and it shows the same single screen
    /// the editor window uses.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PalItemgenApp : MonoBehaviour
    {
        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                Debug.LogError("[PalItemgen] The UIDocument has no PanelSettings asset assigned (Assets > Create > UI Toolkit > Panel Settings Asset).");
                return;
            }
            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[PalItemgen] {error}");
            document.rootVisualElement.Clear();
            document.rootVisualElement.Add(new PalItemgenView(data, Debug.Log, exactCopyOnly: true));
        }
    }
}
