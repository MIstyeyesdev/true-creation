using UnityEngine;
using UnityEngine.UIElements;

namespace TrueCreation.App
{
    /// <summary>
    /// The exe's start: the one component in the app scene, next to a UIDocument whose Panel Settings carry the
    /// True Creation theme. It fills the screen with the same tab shell the editor window shows. The scene is made
    /// by Tools > True Creation > Set up app scene (and again by every build).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppBoot : MonoBehaviour
    {
        private EngineShell _shell;

        private void OnEnable()
        {
            // a tool, not a game: nothing on screen moves fast, so do not spend the GPU on frames
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 30;

            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                Debug.LogError("[TrueCreation] The UIDocument has no Panel Settings, so nothing can be drawn. " +
                               "Run Tools > True Creation > Set up app scene.");
                return;
            }
            var root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            _shell = new EngineShell(TabCatalog.Create());
            _shell.style.position = Position.Absolute;
            _shell.style.left = 0; _shell.style.right = 0; _shell.style.top = 0; _shell.style.bottom = 0;
            root.Add(_shell);
            Debug.Log("[TrueCreation] started; settings and Player.log are in " + Application.persistentDataPath);
        }

        private void OnDisable()
        {
            _shell?.DisposeAll();
            _shell = null;
        }
    }
}
