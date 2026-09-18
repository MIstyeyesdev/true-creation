using System;
using System.Collections.Generic;
using TrueCreation.Host;
using UnityEngine;
using UnityEngine.UIElements;

namespace TrueCreation.App
{
    /// <summary>
    /// What the editor adds to the shared tabs: real windows for the table browser and the options dump (the exe
    /// shows both as overlays inside the tab). Every tab itself runs in the editor and in the exe.
    /// </summary>
    public sealed class TabHooks
    {
        public Action<PalItemgen.UI.PalItemgenView> ConfigureItemgen;
        public Action<PalCreationEngine.UI.PalEditorView> ConfigureCreationEngine;
        public Action<PalCreationEngine.UI.NewPalByAreaPanel> ConfigureNewPal;
    }

    /// <summary>
    /// The nine tabs of True Engine, in one place for the editor window and the exe. Every tab gets its dialogs,
    /// settings and Explorer calls from AppHost, so it behaves the same in both.
    /// </summary>
    public static class TabCatalog
    {
        public static List<TabSpec> Create(TabHooks hooks = null)
        {
            hooks = hooks ?? new TabHooks();
            return new List<TabSpec>
            {
                new TabSpec
                {
                    Title = "Management Station",
                    Hint = "Exact copy of the Monitoring Stand as a Production Manager station; Generate writes the package, Install is explicit.",
                    Build = () => BuildPalItemgen(exactCopyOnly: true, hooks),
                },
                new TabSpec
                {
                    Title = "Production Manager",
                    Hint = "Full creator: package and stations, producers and item links, manager item, exact copy. Lua template fixed 2026-09-16 (no phantom engine members, no Win64 writes).",
                    Build = () => BuildPalItemgen(exactCopyOnly: false, hooks),
                },
                new TabSpec
                {
                    Title = "Creation Engine",
                    Hint = "Edit an existing Pal (stats, loot, scale). Blueprint edits keyed by a vanilla class are refused (pce-01). For a NEW Pal use the next tab.",
                    Build = () => BuildCreationEngine(hooks),
                },
                new TabSpec
                {
                    Title = "New Pal by Area",
                    Hint = "A complete new Pal on the verified working mod's footprint: own rows, tribe enum, texts, spawns copied from vanilla placements in a map area, habitat. Validated against the export scan before export.",
                    Build = () => BuildNewPal(hooks),
                },
                // the editor keeps the 1.0.4 export browser; the exe shows Reference.txt, the end-user reference
                AppHost.Current.IsEditor
                    ? new TabSpec
                    {
                        Title = "Reference",
                        Hint = "The 1.0.4 export reference: Docs (Docs/*.md incl. EXPORT_TOC.md), asset index, data tables, blueprint CDOs. The exe shows Reference.txt here instead.",
                        Build = () => new ReferencePanel(),
                    }
                    : new TabSpec
                    {
                        Title = "Reference",
                        Hint = "The basics: what each tab does, where your files go, installing the mods you make, what changed between game versions, and the problems we hit and what fixed them.",
                        Build = () => new ReferenceDocPanel(),
                    },
                new TabSpec
                {
                    Title = "Item Gen",
                    Hint = "Mirror a vanilla item: clone every field of one of the 2,466 items under a new id, edit, export a PalSchema package (items/ + translations + technology row) in the working mod's shape. Never installs.",
                    Build = () => new ItemGenPanel(),
                },
                new TabSpec
                {
                    Title = "Base Game TOC",
                    Hint = "Vanilla 1.0.5 export, one pass: the pre-build reference (what touches where, how the game reads it, issues, fixes), every data table, folder and file, and what changed since 1.0.4. Built in; an FModel export is optional (Open file / Reveal).",
                    Build = () => new BaseGameTocPanel(),
                },
                new TabSpec
                {
                    Title = "Server",
                    Hint = "True Server: worlds, mods, live-ops status, paths; world settings with all 123 options and the game's ranges.",
                    Build = BuildServer,
                },
                new TabSpec
                {
                    Title = "Ring of No XP (dev test)",
                    Hint = "DEV TEST - on hold with the server panel (Michael, 2026-09-16). Items in a save are a hazard only for testing worlds.",
                    Build = BuildRingOfNoXp,
                },
            };
        }

        // ------------------------------------------------------------------ tab builders

        private static VisualElement BuildPalItemgen(bool exactCopyOnly, TabHooks hooks)
        {
            var data = PalItemgen.Lookup.GameData.Load(PalItemgen.UI.DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[PalItemgen] {error}");
            var view = new PalItemgen.UI.PalItemgenView(data, Debug.Log, exactCopyOnly: exactCopyOnly);
            var host = AppHost.Current;
            view.SaveFileDialog = (title, dir, name, ext) => host.SaveFilePanel(title, dir, name, ext);
            view.OpenFileDialog = (title, dir, ext) => host.OpenFilePanel(title, dir, ext);
            hooks.ConfigureItemgen?.Invoke(view);
            return view;
        }

        private static VisualElement BuildCreationEngine(TabHooks hooks)
        {
            var data = PalCreationEngine.Lookup.GameData.Load(PalCreationEngine.UI.DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[PalCreationEngine] {error}");
            var view = new PalCreationEngine.UI.PalEditorView(data, Debug.Log);
            hooks.ConfigureCreationEngine?.Invoke(view);
            return view;
        }

        private static VisualElement BuildNewPal(TabHooks hooks)
        {
            var panel = new PalCreationEngine.UI.NewPalByAreaPanel();
            hooks.ConfigureNewPal?.Invoke(panel);
            return panel;
        }

        private static VisualElement BuildRingOfNoXp()
        {
            var data = RingOfNoXp.Lookup.GameData.Load(RingOfNoXp.UI.DataPaths.LookupDirectory);
            foreach (var error in data.LoadErrors) Debug.LogWarning($"[RingOfNoXp] {error}");
            var host = AppHost.Current;
            return new RingOfNoXp.UI.RingOfNoXpView(data, Debug.Log)
            {
                SaveFileDialog = (title, dir, name, ext) => host.SaveFilePanel(title, dir, name, ext),
                OpenFileDialog = (title, dir, ext) => host.OpenFilePanel(title, dir, ext),
                OpenFolderDialog = (title, dir) => host.OpenFolderPanel(title, dir),
                RevealFolder = host.Reveal,
                Confirm = (title, message) => host.Confirm(title, message, "Uninstall", "Cancel"),
            };
        }

        /// <summary>
        /// True Server's panel (Assets/TrueServer/Runtime, copied from the True Server project), with its settings and dialogs
        /// pointed at this program's host: EditorPrefs and the editor's dialogs in Unity, the settings file and Windows'
        /// dialogs in the exe. True Creation has no separate True Server window or menu.
        /// </summary>
        private static VisualElement BuildServer()
        {
            var host = AppHost.Current;
            TrueServer.ServerHost.GetString = host.GetString;
            TrueServer.ServerHost.SetString = host.SetString;
            TrueServer.ServerHost.GetInt = host.GetInt;
            TrueServer.ServerHost.SetInt = host.SetInt;
            TrueServer.ServerHost.DeleteKey = host.DeleteKey;
            TrueServer.ServerHost.OpenFolderPanel = host.OpenFolderPanel;
            TrueServer.ServerHost.SaveFilePanel = host.SaveFilePanel;
            TrueServer.ServerHost.Confirm = host.Confirm;
            TrueServer.ServerHost.Message = host.Message;
            TrueServer.ServerHost.Reveal = host.Reveal;
            return new TrueServer.ServerPanel();
        }
    }
}
