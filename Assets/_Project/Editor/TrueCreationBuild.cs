using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TrueCreation.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TrueEngine.EditorTools
{
    /// <summary>
    /// Makes True Creation a Windows exe in one click (Tools > True Creation > Build Windows exe).
    ///   1. The app scene: a camera and one GameObject with a UIDocument (True Creation panel settings + theme) and
    ///      AppBoot, which fills the screen with the same tab shell as the editor window. Made once; the build list
    ///      holds only this scene (SampleScene stays in the project, out of the build).
    ///   2. Player settings for a desktop tool: company Mistyeyes, windowed 1280x800, resizable, runs in the background,
    ///      Player.log on. The version is left as it is (Edit > Project Settings > Player).
    ///   3. BuildPipeline.BuildPlayer to a folder outside the project, then, for every Windows build (also one made from
    ///      File > Build Profiles), only what an end user can use:
    ///      - the Base Game TOC files the tab reads into StreamingAssets/BaseGame (BaseGameShipped); the build records
    ///        and the project's README/REFERENCE.md stay in the project;
    ///      - StreamingAssets items no tab reads in the exe taken out of the build output (NotShipped);
    ///      - in every shipped text file, this PC's user folder becomes %USERPROFILE%, its user's name becomes
    ///        NameInShippedText and the ShippedTextCuts sentences go (the project keeps its files as they are);
    ///      - README.txt and Reference.txt (the end-user reference, also the exe's Reference tab) next to the exe;
    ///      - our assemblies' build path (the .pdb path the compiler writes) cut to the .pdb's file name;
    ///      - the project's Unity Cloud link (its organization ID, which is the Unity account's name, and its project ID),
    ///        which Unity copies into globalgamemanagers, overwritten there with zeros (the project keeps its link);
    ///      - a scan of the shipped text files for passwords, tokens and IP addresses (names only, never values);
    ///      - a check that nothing that ships still names this PC's user folder or the Unity account, or its user in a
    ///        text file;
    ///      - "True Creation &lt;version&gt;.zip" next to the build folder, unless a step failed or the scan or the check
    ///        found something. An earlier zip is never replaced. Nothing is uploaded.
    /// </summary>
    public static class TrueCreationBuild
    {
        public const string Folder = "Assets/TrueCreationApp/UI";
        public const string ThemePath = Folder + "/TrueCreationTheme.tss";
        public const string PanelSettingsPath = Folder + "/TrueCreationPanelSettings.asset";
        public const string ScenePath = "Assets/Scenes/TrueCreationApp.unity";
        private const string OutPref = "TrueCreation.Build.Out";
        public const string CompanyName = "Mistyeyes";
        public const string ExeName = "True Creation.exe";

        /// <summary>
        /// What the exe ships from Docs/BaseGame: the files the Base Game TOC tab reads or opens, including the changes
        /// between game versions (changed_vs_1.0.4.tsv) and the reference with its issues and fixes (reference.json).
        /// The build records (listing, read_log, errors, VERIFICATION) and the project's README and REFERENCE.md stay in
        /// the project. Add a path here to ship it.
        /// </summary>
        public static readonly string[] BaseGameShipped =
        {
            "reference.json",
            "toc/TOC.md",
            "toc/toc_lookup.md",
            "toc/toc_tables.tsv",
            "toc/toc_tables_explained.tsv",
            "toc/toc_folders.tsv",
            "toc/toc_folders_explained.tsv",
            "toc/toc_files.tsv",
            "toc/changed_vs_1.0.4.tsv",
        };

        /// <summary>
        /// StreamingAssets items no tab reads in the exe, taken out of the build output (the project keeps them):
        /// PalworldData (no code reads it) and the server option schema (read only by the editor's option editor).
        /// </summary>
        public static readonly string[] NotShipped = { "PalworldData", "palworld_option_schema.json" };

        /// <summary>What the user's name (the name of this PC's user folder) becomes in the shipped copies of the notes.</summary>
        public const string NameInShippedText = "the developer";

        /// <summary>
        /// Sentences in the project notes about the developer's own PC (its mod setup), cut from the shipped copies. The
        /// build says when one no longer matches, so a reworded note is noticed.
        /// </summary>
        public static readonly string[] ShippedTextCuts =
        {
            "on the author's PC 69, all Vortex symlinks - ",
            "The Vortex NSFW paks replace vanilla models; they add nothing. ",
        };

        // The runtime default theme, with the editor's dark look for the stock controls. Inline styles the tabs set
        // still win (they are more specific); selectors a Unity version does not have are ignored.
        private const string Theme =
            "@import url(\"unity-theme://default\");\n" +
            "\n" +
            "/* True Creation: the editor's dark look for the stock controls (inline styles set by the tabs still win). */\n" +
            ".unity-label { color: #D2D2D2; }\n" +
            ".unity-base-field__label { color: #C4C4C4; }\n" +
            ".unity-button { background-color: #585858; color: #EEEEEE; border-color: #303030; }\n" +
            ".unity-button:hover { background-color: #676767; }\n" +
            ".unity-button:active { background-color: #6E6E6E; }\n" +
            ".unity-base-text-field__input { background-color: #2A2A2A; color: #D2D2D2; border-color: #212121; }\n" +
            ".unity-base-popup-field__input { background-color: #515151; color: #E4E4E4; border-color: #303030; }\n" +
            ".unity-toggle__checkmark { background-color: #2A2A2A; border-color: #212121; }\n" +
            ".unity-foldout__text { color: #C4C4C4; }\n" +
            ".unity-collection-view__item--selected { background-color: #2C5D87; }\n" +
            ".unity-base-dropdown__container-outer { background-color: #383838; border-color: #212121; }\n" +
            ".unity-base-dropdown__item { color: #D2D2D2; }\n" +
            ".unity-base-dropdown__item:hover { background-color: #2C5D87; }\n";

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/True Creation/Set up app scene")]
        public static void SetUpMenu()
        {
            var notes = EnsureAppScene();
            EditorUtility.DisplayDialog("True Creation app scene", string.Join("\n", notes) +
                "\n\nPress Play in " + ScenePath + " to see the exe's screen inside the editor.", "OK");
        }

        [MenuItem("Tools/True Creation/Build Windows exe...")]
        public static void BuildMenu()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var start = EditorPrefs.GetString(OutPref, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "True Creation Build"));
            var outDir = EditorUtility.SaveFolderPanel("Build True Creation into (an empty folder outside the project)", start, "");
            if (string.IsNullOrEmpty(outDir)) return;
            outDir = Path.GetFullPath(outDir);
            if (outDir.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Not inside the project", "Pick a folder outside " + projectRoot + " (a build inside the project would be imported as assets).", "OK");
                return;
            }
            EditorPrefs.SetString(OutPref, outDir);
            if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any() &&
                !EditorUtility.DisplayDialog("The folder is not empty",
                    outDir + " already has files in it. Anything the build does not replace goes into the zip too; an empty folder is safest.",
                    "Build here anyway", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var notes = EnsureAppScene();
            notes.AddRange(ApplyPlayerSettings());
            var exe = Path.Combine(outDir, ExeName);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            var s = report.summary;
            var ok = s.result == BuildResult.Succeeded;
            var text = ok
                ? $"Built {exe}\n{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0} s, {s.totalWarnings} warning(s).\n\n" +
                  string.Join("\n", notes) + "\n\n" + string.Join("\n", LastPostBuildNotes) +
                  "\n\nTest it on a PC (or Windows account) without Unity before sharing it."
                : $"The build did not succeed ({s.result}, {s.totalErrors} error(s)). The Console has the errors.";
            Debug.Log("[TrueCreation] " + text.Replace("\n", " | "));
            EditorUtility.DisplayDialog(ok ? "True Creation built" : "Build failed", text, "OK");
            if (ok) EditorUtility.RevealInFinder(LastZip ?? exe);
        }

        // ------------------------------------------------------------------ scene, panel settings, theme

        /// <summary>Creates what is missing (theme, panel settings, scene) and makes the scene the only one in the build.</summary>
        public static List<string> EnsureAppScene()
        {
            var notes = new List<string>();
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/TrueCreationApp")) AssetDatabase.CreateFolder("Assets", "TrueCreationApp");
                AssetDatabase.CreateFolder("Assets/TrueCreationApp", "UI");
            }

            if (!File.Exists(Path.GetFullPath(ThemePath)))
            {
                File.WriteAllText(Path.GetFullPath(ThemePath), Theme);
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
                notes.Add("Created the theme " + ThemePath + ".");
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) notes.Add("WARNING: " + ThemePath + " did not import as a theme; the exe falls back to Unity's default look.");

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.themeStyleSheet = theme;
                panel.scaleMode = PanelScaleMode.ConstantPhysicalSize;   // text keeps its size on high-DPI screens
                panel.referenceDpi = 96;
                panel.fallbackDpi = 96;
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
                notes.Add("Created the panel settings " + PanelSettingsPath + ".");
            }
            else if (panel.themeStyleSheet == null && theme != null)
            {
                panel.themeStyleSheet = theme;
                EditorUtility.SetDirty(panel);
                notes.Add("Gave the panel settings the theme.");
            }

            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                // made next to the open scene (as the active one, so nothing lands in the open scene) and closed
                // again, so whatever is open stays as it was
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var camera = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camera.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.13f, 0.13f, 0.14f);
                var app = new GameObject("True Creation");
                var document = app.AddComponent<UIDocument>();
                document.panelSettings = panel;
                app.AddComponent<AppBoot>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
                notes.Add("Created the app scene " + ScenePath + ".");
            }

            var inBuild = EditorBuildSettings.scenes;
            if (inBuild.Length != 1 || inBuild[0].path != ScenePath || !inBuild[0].enabled)
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                notes.Add("The build list now holds only " + ScenePath + " (SampleScene stays in the project).");
            }
            AssetDatabase.SaveAssets();
            if (notes.Count == 0) notes.Add("The app scene, panel settings and theme are already set up.");
            return notes;
        }

        /// <summary>A desktop tool's window, under the company name Mistyeyes. The version is not touched.</summary>
        public static List<string> ApplyPlayerSettings()
        {
            var notes = new List<string>();
            void Set(string what, bool changed) { if (changed) notes.Add("Player setting: " + what + "."); }
            // the company also names the settings and log folder: AppData\LocalLow\Mistyeyes\True Creation
            Set("company " + CompanyName, PlayerSettings.companyName != CompanyName);
            PlayerSettings.companyName = CompanyName;
            Set("windowed", PlayerSettings.fullScreenMode != FullScreenMode.Windowed);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            Set("1280 x 800 window", PlayerSettings.defaultScreenWidth != 1280 || PlayerSettings.defaultScreenHeight != 800);
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            Set("resizable window", !PlayerSettings.resizableWindow);
            PlayerSettings.resizableWindow = true;
            Set("keeps running when not focused", !PlayerSettings.runInBackground);
            PlayerSettings.runInBackground = true;
            Set("Player.log on", !PlayerSettings.usePlayerLog);
            PlayerSettings.usePlayerLog = true;
            notes.Add($"Company \"{PlayerSettings.companyName}\", product \"{PlayerSettings.productName}\", version {PlayerSettings.bundleVersion} (version unchanged; Edit > Project Settings > Player).");
            return notes;
        }

        // ------------------------------------------------------------------ after every Windows build

        internal static readonly List<string> LastPostBuildNotes = new List<string>();
        internal static string LastZip;

        /// <summary>
        /// Runs after every Windows build (TrueCreationPostBuild): what ships, the text scrub, README.txt and Reference.txt,
        /// the build-path strip, the Unity Cloud link, the secret scan and the trace check, then the zip. A failed step, a
        /// flagged file or a trace of this PC's user or the Unity account means no zip.
        /// </summary>
        internal static void AfterWindowsBuild(BuildReport report)
        {
            LastPostBuildNotes.Clear();
            LastZip = null;
            var exe = report.summary.outputPath;
            var exeDir = Path.GetDirectoryName(exe) ?? "";
            var dataDir = Path.Combine(exeDir, Path.GetFileNameWithoutExtension(exe) + "_Data");
            var streaming = Path.Combine(dataDir, "StreamingAssets");
            try
            {
                var cloudLink = CloudLink();
                LastPostBuildNotes.Add(CopyBaseGame(streaming));
                LastPostBuildNotes.Add(RemoveNotShipped(streaming));
                LastPostBuildNotes.Add(ScrubShippedText(streaming));
                File.WriteAllText(Path.Combine(exeDir, "README.txt"), Readme(Path.GetFileName(exe)), new UTF8Encoding(false));
                LastPostBuildNotes.Add("Wrote README.txt next to the exe.");
                LastPostBuildNotes.Add(CopyReferenceDoc(streaming, exeDir));
                LastPostBuildNotes.Add(StripBuildPaths(Path.Combine(dataDir, "Managed")));
                LastPostBuildNotes.Add(BlankCloudLink(dataDir, cloudLink));
                var clean = ScanForSecrets(streaming, out var scan);
                LastPostBuildNotes.Add(scan);
                var traceFree = CheckForTraces(exeDir, cloudLink, out var traces);
                LastPostBuildNotes.Add(traces);
                LastPostBuildNotes.Add(!clean ? "No zip made: the secret scan flagged shipped files. Check them, then build again."
                    : !traceFree ? "No zip made: shipped files still name this PC's user or the Unity account (listed above)."
                    : Zip(exeDir));
            }
            catch (Exception e)
            {
                LastPostBuildNotes.Add("After-build step failed: " + e.Message + " No zip was made.");
                Debug.LogError("[TrueCreation] after-build step failed: " + e);
            }
            foreach (var n in LastPostBuildNotes) Debug.Log("[TrueCreation] " + n);
        }

        private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("0.0");

        /// <summary>The Base Game TOC files the tab reads (BaseGameShipped) into the build: Docs stays the one copy in the project.</summary>
        private static string CopyBaseGame(string streaming)
        {
            var src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "BaseGame"));
            if (!Directory.Exists(src)) return "WARNING: Docs/BaseGame not found; the Base Game TOC tab will say its data is missing.";
            var r = TrueCreationShip.CopyListed(src, Path.Combine(streaming, "BaseGame"), BaseGameShipped);
            return $"Copied the Base Game TOC into the build: {r.Files} files, {Mb(r.Bytes)} MB (StreamingAssets/BaseGame). " +
                   $"Left in the project (build records, project docs): {r.LeftOutFiles} files, {Mb(r.LeftOutBytes)} MB." +
                   (r.Missing.Count > 0 ? " WARNING - not found in Docs/BaseGame: " + string.Join(", ", r.Missing) : "");
        }

        /// <summary>StreamingAssets items no tab reads in the exe (NotShipped) out of the build output; the project keeps them.</summary>
        private static string RemoveNotShipped(string streaming)
        {
            var removed = TrueCreationShip.RemoveFromBuildOutput(streaming, NotShipped);
            return removed.Count == 0
                ? "Nothing to take out of the build's StreamingAssets."
                : "Not shipped (no tab reads them in the exe; the project keeps them): " + string.Join(", ", removed) + ".";
        }

        /// <summary>Reference.txt, which the exe's Reference tab reads from StreamingAssets, also next to the exe.</summary>
        private static string CopyReferenceDoc(string streaming, string exeDir)
        {
            var doc = Path.Combine(streaming, ReferenceDocPanel.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(doc))
                return "WARNING: " + ReferenceDocPanel.RelativePath + " is not in the build (Assets/StreamingAssets); the Reference tab will say it is missing.";
            File.Copy(doc, Path.Combine(exeDir, "Reference.txt"), true);
            return "Copied Reference.txt next to the exe (the Reference tab reads the copy in StreamingAssets).";
        }

        /// <summary>
        /// "True Creation &lt;version&gt;.zip" next to the build folder: the whole folder under one "True Creation" folder,
        /// without Unity's *_DoNotShip / *_ButDontShipItWithYourGame folders. An earlier zip is never replaced.
        /// </summary>
        private static string Zip(string exeDir)
        {
            var parent = Directory.GetParent(exeDir);
            if (parent == null) return "No zip made: the build folder has no parent folder to put it in.";
            var zipPath = TrueCreationShip.FreeZipPath(Path.Combine(parent.FullName, $"{PlayerSettings.productName} {PlayerSettings.bundleVersion}.zip"));
            var z = TrueCreationShip.ZipFolder(exeDir, zipPath, PlayerSettings.productName, "_DoNotShip", "_ButDontShipItWithYourGame");
            LastZip = z.ZipPath;
            return $"Zipped {z.Files} files ({Mb(z.Bytes)} MB) into {z.ZipPath} ({Mb(z.ZipBytes)} MB). Share the zip: unzip it and run {ExeName}; nothing to install.";
        }

        private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// Every shipped text file (StreamingAssets): this PC's user folder becomes %USERPROFILE%, the user's name (the
        /// user folder's name) becomes NameInShippedText, and the ShippedTextCuts sentences go, so the program carries no
        /// trace of whose PC built it. The project's files are not touched.
        /// </summary>
        private static string ScrubShippedText(string streaming)
        {
            var profile = Profile;
            if (string.IsNullOrEmpty(profile)) return "Text scrub skipped (no profile folder).";
            var notes = new List<string>();
            var folder = TrueCreationShip.ReplaceInText(streaming, TrueCreationShip.UserFolderPattern(profile), "%USERPROFILE%");
            notes.Add($"{folder[1]} path(s) to this PC's user folder in {folder[0]} file(s) now read %USERPROFILE%");
            var name = TrueCreationShip.NamePattern(Path.GetFileName(profile));
            if (name != null)
            {
                var n = TrueCreationShip.ReplaceInText(streaming, name, NameInShippedText);
                notes.Add($"the user's name {n[1]} time(s) in {n[0]} file(s) now reads \"{NameInShippedText}\"");
            }
            foreach (var cut in ShippedTextCuts)
            {
                var c = TrueCreationShip.ReplaceInText(streaming, new Regex(Regex.Escape(cut)), "");
                notes.Add(c[1] == 0 ? $"WARNING - cut not found (the note changed?): \"{cut.Trim()}\"" : $"cut {c[1]} time(s) in {c[0]} file(s): \"{cut.Trim()}\"");
            }
            return "Text scrub: " + string.Join("; ", notes) + ".";
        }

        /// <summary>
        /// Our assemblies carry the path they were compiled at (the .pdb path the compiler writes; the .pdb does not ship).
        /// In the build output that path is cut to the .pdb's file name. Unity's own assemblies are not touched.
        /// </summary>
        private static string StripBuildPaths(string managed)
        {
            var profile = Profile;
            if (string.IsNullOrEmpty(profile)) return "Build-path strip skipped (no profile folder).";
            var changed = TrueCreationShip.StripPdbPaths(managed, TrueCreationShip.UserFolderPattern(profile));
            return changed.Count == 0
                ? "No assembly carries this PC's folders."
                : $"Build path cut to the .pdb file name in {changed.Count} assemblies: {string.Join(", ", changed)}.";
        }

        /// <summary>
        /// The project's Unity Cloud link as Player Settings holds it (Edit > Project Settings > Services): the
        /// organization ID, which is the Unity account's name, and the cloud project ID. Unity copies both into every
        /// build's globalgamemanagers. Empty when the project is not linked.
        /// </summary>
        private static string[] CloudLink()
        {
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (settings == null) return new string[0];
            using (var so = new SerializedObject(settings))
                return new[] { "organizationId", "cloudProjectId" }
                    .Select(key => so.FindProperty(key)?.stringValue)
                    .Where(v => !string.IsNullOrEmpty(v)).ToArray();
        }

        /// <summary>
        /// The exe uses no Unity service, so in the build's globalgamemanagers the Unity Cloud link is overwritten with
        /// zeros of the same length (nothing in the file moves). The project keeps its link.
        /// </summary>
        private static string BlankCloudLink(string dataDir, string[] cloudLink)
        {
            if (cloudLink.Length == 0) return "The project has no Unity Cloud link; nothing to take out of globalgamemanagers.";
            var n = TrueCreationShip.BlankSerializedStrings(Path.Combine(dataDir, "globalgamemanagers"), cloudLink);
            return n == 0
                ? "WARNING - the Unity Cloud link (organization and project ID) was not found in globalgamemanagers; the trace check says whether it ships."
                : $"Unity Cloud link (organization ID = the Unity account's name, and project ID) overwritten with zeros in globalgamemanagers: {n} value(s). The project keeps its link.";
        }

        /// <summary>
        /// The last check before the zip: nothing that ships (Unity's do-not-ship folders are not read) may name this PC's
        /// user folder or the Unity account (the Unity Cloud link), or its user in a text file. False when something does
        /// (then no zip is made).
        /// </summary>
        private static bool CheckForTraces(string exeDir, string[] cloudLink, out string note)
        {
            var profile = Profile;
            if (string.IsNullOrEmpty(profile)) { note = "Trace check skipped (no profile folder)."; return true; }
            var traces = TrueCreationShip.FindTraces(exeDir, TrueCreationShip.UserFolderPattern(profile),
                TrueCreationShip.NamePattern(Path.GetFileName(profile)), TrueCreationShip.AnyOfPattern(cloudLink),
                "_DoNotShip", "_ButDontShipItWithYourGame");
            note = traces.Count == 0
                ? "Trace check: nothing that ships names this PC's user folder or the Unity account, or its user in a text file."
                : $"WARNING - trace check: {traces.Count} shipped file(s) still name this PC's user or the Unity account: " + string.Join(", ", traces.Take(10));
            return traces.Count == 0;
        }

        private static readonly Regex[] Secrets =
        {
            new Regex(@"(AdminPassword|ServerPassword)\s*[=:]\s*""?(?!""|,|\)|\s|None\b|null\b)[^""\s,)]{1,}", RegexOptions.IgnoreCase),
            new Regex(@"PublicIP\s*[=:]\s*""?\d{1,3}(\.\d{1,3}){3}", RegexOptions.IgnoreCase),
            new Regex(@"(access_?token|api_?key)\s*[=:]\s*""?[A-Za-z0-9_\-]{16,}", RegexOptions.IgnoreCase),
        };

        /// <summary>
        /// Names the shipped text files that look like they hold a password, token or IP. Values are never printed.
        /// False when something was flagged (then no zip is made).
        /// </summary>
        private static bool ScanForSecrets(string streaming, out string note)
        {
            if (!Directory.Exists(streaming)) { note = "No StreamingAssets in the build to scan."; return true; }
            var hits = new List<string>(); var scanned = 0;
            foreach (var file in Directory.EnumerateFiles(streaming, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".json" && ext != ".jsonl" && ext != ".md" && ext != ".txt" && ext != ".tsv" && ext != ".ini" && ext != ".lua" && ext != ".csv") continue;
                scanned++;
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                if (Secrets.Any(r => r.IsMatch(text))) hits.Add(file.Substring(streaming.Length).TrimStart('\\', '/'));
            }
            note = hits.Count == 0
                ? $"Secret scan: {scanned} shipped text files, nothing that looks like a password, token or IP."
                : $"WARNING - secret scan: {hits.Count} of {scanned} shipped files look like they hold a password, token or IP (values not shown): " + string.Join(", ", hits.Take(10));
            return hits.Count == 0;
        }

        private static string Readme(string exeName) =>
            "True Creation\r\n" +
            "=============\r\n\r\n" +
            "Run " + exeName + ". Nothing needs installing; keep this folder together (unzip all of it first).\r\n" +
            "If Windows says it protected your PC: More info, then Run anyway (the program is not signed).\r\n\r\n" +
            "- Reference.txt (also the Reference tab): what each tab does, where your files go, installing the mods you\r\n" +
            "  make, what changed between game versions, and the problems we hit and what fixed them.\r\n" +
            "- Everything the tools list (Pals, items, tables, the Base Game TOC) is built in. FModel is optional: only\r\n" +
            "  'Open file' / 'Reveal' on a game file need your own FModel export (Base Game TOC tab, top block).\r\n" +
            "- What you save or export goes to Documents\\True Creation unless you pick another folder.\r\n" +
            "- Your settings and the log (Player.log) are in %USERPROFILE%\\AppData\\LocalLow\\" + PlayerSettings.companyName + "\\" + PlayerSettings.productName + ".\r\n" +
            "- Packages are written to the output folder only. Nothing is installed into Palworld unless you press an\r\n" +
            "  Install button; Uninstall removes only what the tool installed.\r\n\r\n" +
            "Version " + PlayerSettings.bundleVersion + ", built " + DateTime.Now.ToString("yyyy-MM-dd") + ".\r\n";
    }

    /// <summary>Every Windows build, from the menu or from File > Build Profiles, gets the after-build step.</summary>
    internal sealed class TrueCreationPostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows)
                TrueCreationBuild.AfterWindowsBuild(report);
        }
    }
}
