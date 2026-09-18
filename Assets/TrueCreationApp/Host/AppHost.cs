namespace TrueCreation.Host
{
    /// <summary>
    /// The host the running code talks to. Nobody has to set it: in the Unity editor it is the editor's own
    /// (EditorPrefs, EditorUtility dialogs), in the exe Windows' own. Tests may set their own.
    /// </summary>
    public static class AppHost
    {
        private static IAppHost _current;

        public static IAppHost Current
        {
            get
            {
                if (_current != null) return _current;
#if UNITY_EDITOR
                _current = new EditorHost();
#else
                _current = new WindowsHost();
#endif
                return _current;
            }
            set => _current = value;
        }
    }
}
