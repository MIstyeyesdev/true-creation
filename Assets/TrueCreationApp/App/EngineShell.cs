using System;
using System.Collections.Generic;
using System.Linq;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueCreation.App
{
    /// <summary>One tab of the shell: what it is called, the line under the bar, and how it is built and torn down.</summary>
    public sealed class TabSpec
    {
        public string Title;
        public string Hint;
        public Func<VisualElement> Build;
        public Action<VisualElement> Dispose;
        internal VisualElement Built;
        internal Button Button;
    }

    /// <summary>
    /// True Engine's tab shell: the bar of big tabs, the hint line and the content area. The same element runs in
    /// the Unity editor (inside TrueEngineWindow) and in the exe (AppBoot), so there is one implementation of the
    /// tool. Tabs build lazily and are cached; Reload data rebuilds the active tab from the files on disk. The last
    /// tab is remembered per user (AppHost settings).
    /// </summary>
    public sealed class EngineShell : VisualElement
    {
        private const string ActiveTabPref = "TrueEngine.ActiveTab";
        private const int TabHeight = 30;
        private static readonly Color Background = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color TextColor = new Color(0.82f, 0.82f, 0.82f);
        private static readonly Color TabOff = new Color(0.21f, 0.21f, 0.23f);
        private static readonly Color TabHover = new Color(0.28f, 0.28f, 0.31f);
        private static readonly Color TabOn = new Color(0.33f, 0.34f, 0.40f);
        private static readonly Color TabAccent = new Color(0.36f, 0.64f, 1.00f);

        private readonly List<TabSpec> _tabs;
        private readonly VisualElement _content;
        private readonly Label _hint;
        private int _active = -1;

        /// <summary>Active tab: bright background, white bold text, blue underline. Others: dim.</summary>
        private static void StyleTab(Button button, bool on)
        {
            if (button == null) return;
            button.style.backgroundColor = on ? TabOn : TabOff;
            button.style.color = on ? Color.white : new Color(0.74f, 0.74f, 0.78f);
            button.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            button.style.borderBottomColor = on ? TabAccent : TabOff;
        }

        public EngineShell(IEnumerable<TabSpec> tabs)
        {
            _tabs = (tabs ?? Enumerable.Empty<TabSpec>()).Where(t => t != null).ToList();
            style.flexGrow = 1;
            // the editor window supplied these through its skin; the exe has only what is set here
            style.backgroundColor = Background;
            style.color = TextColor;

            // Big, readable tabs: 30 px high, 13 px text, the active one bright with a blue underline.
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.flexWrap = Wrap.Wrap;
            bar.style.flexShrink = 0;
            bar.style.backgroundColor = new Color(0.13f, 0.13f, 0.14f);
            bar.style.paddingLeft = 6; bar.style.paddingRight = 6; bar.style.paddingTop = 6;
            bar.style.borderBottomWidth = 1; bar.style.borderBottomColor = new Color(0.07f, 0.07f, 0.08f);
            for (var i = 0; i < _tabs.Count; i++)
            {
                var index = i;
                var button = new Button(() => Select(index)) { text = _tabs[i].Title };
                button.style.height = TabHeight;
                button.style.fontSize = 13;
                button.style.unityTextAlign = TextAnchor.MiddleCenter;
                button.style.paddingLeft = 16; button.style.paddingRight = 16;
                button.style.marginLeft = 0; button.style.marginRight = 4; button.style.marginTop = 0; button.style.marginBottom = 0;
                button.style.borderTopLeftRadius = 5; button.style.borderTopRightRadius = 5;
                button.style.borderBottomLeftRadius = 0; button.style.borderBottomRightRadius = 0;
                button.style.borderLeftWidth = 0; button.style.borderRightWidth = 0; button.style.borderTopWidth = 0;
                button.style.borderBottomWidth = 3;
                button.RegisterCallback<MouseEnterEvent>(_ => { if (_active != index) button.style.backgroundColor = TabHover; });
                button.RegisterCallback<MouseLeaveEvent>(_ => StyleTab(button, _active == index));
                StyleTab(button, false);
                _tabs[i].Button = button;
                bar.Add(button);
            }
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; bar.Add(spacer);
            var reload = new Button(ReloadActive) { text = "Reload data", tooltip = "Rebuild the active tab and re-read its data from disk" };
            reload.style.height = TabHeight; reload.style.fontSize = 12; reload.style.paddingLeft = 12; reload.style.paddingRight = 12;
            reload.style.marginRight = 0; reload.style.marginTop = 0; reload.style.marginBottom = 0;
            bar.Add(reload);
            Add(bar);

            _hint = new Label();
            _hint.style.fontSize = 12;
            _hint.style.color = new Color(0.72f, 0.72f, 0.76f);
            _hint.style.whiteSpace = WhiteSpace.Normal;
            _hint.style.paddingLeft = 8; _hint.style.paddingRight = 8; _hint.style.paddingTop = 3; _hint.style.paddingBottom = 3;
            _hint.style.flexShrink = 0;
            Add(_hint);

            _content = new VisualElement();
            _content.style.flexGrow = 1;
            Add(_content);

            if (_tabs.Count > 0)
                Select(Mathf.Clamp(AppHost.Current.GetInt(ActiveTabPref, 0), 0, _tabs.Count - 1));
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            _active = index;
            AppHost.Current.SetInt(ActiveTabPref, index);
            for (var i = 0; i < _tabs.Count; i++) StyleTab(_tabs[i].Button, i == index);
            var tab = _tabs[index];
            _hint.text = tab.Hint;
            _content.Clear();
            if (tab.Built == null)
            {
                try
                {
                    tab.Built = tab.Build != null ? tab.Build() : null;
                    if (tab.Built == null) throw new InvalidOperationException("the tab has no content");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TrueEngine] {tab.Title} failed to build: {e}");
                    var error = new Label($"{tab.Title} could not be built:\n{e.Message}\n\n" +
                                          (Application.isEditor ? "See the Console for the full error." : "The full error is in Player.log (AppData\\LocalLow)."));
                    error.style.whiteSpace = WhiteSpace.Normal;
                    error.style.color = new Color(0.92f, 0.45f, 0.45f);
                    error.style.paddingLeft = 12; error.style.paddingTop = 12;
                    tab.Built = error;
                }
            }
            tab.Built.style.flexGrow = 1;
            _content.Add(tab.Built);
        }

        /// <summary>Drops the active tab's cached view and rebuilds it, re-reading its data from disk.</summary>
        public void ReloadActive()
        {
            if (_active < 0 || _active >= _tabs.Count) return;
            var tab = _tabs[_active];
            DisposeTab(tab);
            Select(_active);
        }

        /// <summary>Tears every built tab down (window closed, app quitting).</summary>
        public void DisposeAll()
        {
            foreach (var tab in _tabs) DisposeTab(tab);
        }

        private static void DisposeTab(TabSpec tab)
        {
            try { tab.Dispose?.Invoke(tab.Built); }
            catch (Exception e) { Debug.LogWarning($"[TrueEngine] {tab.Title} did not close cleanly: {e.Message}"); }
            tab.Built = null;
        }
    }
}
