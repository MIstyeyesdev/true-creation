using RingOfNoXp.Lookup;
using UnityEngine;
using UnityEngine.UIElements;

namespace RingOfNoXp.UI
{
    /// <summary>
    /// Runtime host for the standalone app: put this on a GameObject with a
    /// UIDocument (PanelSettings assigned) and it shows the same screen the
    /// editor window uses. Without the editor's dialogs it saves to the default
    /// project folder rather than prompting.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class RingOfNoXpApp : MonoBehaviour
    {
        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                Debug.LogError("[RingOfNoXp] The UIDocument has no PanelSettings asset assigned (Assets > Create > UI Toolkit > Panel Settings Asset).");
                return;
            }

            var data = GameData.Load(DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[RingOfNoXp] {error}");

            document.rootVisualElement.Clear();
            document.rootVisualElement.Add(new RingOfNoXpView(data, Debug.Log));
        }
    }
}
