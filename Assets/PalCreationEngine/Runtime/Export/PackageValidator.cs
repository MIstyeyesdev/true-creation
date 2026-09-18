using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    public sealed class PackageProblem
    {
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Note = "note";

        public string Severity;
        public string Piece;
        public string Message;

        public override string ToString() => $"[{Severity}] {Piece}: {Message}";
    }

    /// <summary>
    /// Checks a new-Pal package against the export scan and the working mod's footprint before
    /// it is written: new keys collide with nothing vanilla, every reference resolves, no vanilla
    /// class is patched on Route A, both rows are complete, and the required pieces are present.
    /// Shared by the harness and the editor so both report the same problems.
    /// </summary>
    public static class PackageValidator
    {
        /// <summary>Loader targets the verified working mod ships for every new Pal (contract section 6, pak-only pieces excluded).</summary>
        public static readonly string[] RequiredTargets = { "pals", RawTables.PalBPClass, RawTables.CharacterIcon, "translations" };
        public static readonly string[] RecommendedTargets =
        {
            RawTables.PartnerSkillIcon, RawTables.PartnerSkillParameter, RawTables.WazaMasterLevel, RawTables.DropItem,
            RawTables.CaptureCameraOffset, RawTables.Randomizer, RawTables.PaldexDistribution, "spawns",
        };

        public static List<PackageProblem> Validate(PalSchemaPackage package, NewPalPlan plan, GameData data)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (data == null) throw new ArgumentNullException(nameof(data));
            var scan = data.Scan ?? ScanIndex.Empty;
            var problems = new List<PackageProblem>();
            void Add(string sev, string piece, string msg) => problems.Add(new PackageProblem { Severity = sev, Piece = piece, Message = msg });

            if (scan.IsEmpty)
                Add(PackageProblem.Warning, "scan", "scan_index.json is not loaded: collision and reference checks against the export were skipped.");

            // (a) new keys must be absent from the vanilla data
            foreach (var id in package.Pals.Keys)
            {
                if (scan.TableRowExists("DT_PalMonsterParameter", id)) Add(PackageProblem.Error, "pals", $"'{id}' is a shipped Pal row (DT_PalMonsterParameter); a new Pal needs an unused id.");
                if (scan.TableRowExists("DT_PalHumanParameter", id)) Add(PackageProblem.Error, "pals", $"'{id}' is a shipped human/NPC row (DT_PalHumanParameter).");
                if (data.Pals != null && data.Pals.ContainsKey(id)) Add(PackageProblem.Error, "pals", $"'{id}' exists in pals.json (shipped roster).");
            }
            foreach (var kv in package.Enums)
                foreach (var member in kv.Value)
                {
                    if (scan.Exists("enum_member", kv.Key + "::" + member)) Add(PackageProblem.Error, "enums", $"{kv.Key}::{member} already exists in the game's enum.");
                    if (data.Enums != null && data.Enums.TryGetValue(kv.Key, out var list) && list.Any(m => string.Equals(m, member, StringComparison.OrdinalIgnoreCase)))
                        Add(PackageProblem.Error, "enums", $"{kv.Key}::{member} is in enums.json (shipped).");
                }
            foreach (var kv in package.Raw)
                foreach (var rowKey in kv.Value.Keys)
                    if (scan.TableRowExists(kv.Key, rowKey))
                        Add(PackageProblem.Error, "raw/" + kv.Key, $"row '{rowKey}' already exists in the shipped table; a new Pal's rows must be new keys.");
            if (plan.ModModel != null)
            {
                // A model from a mod on this PC (rule 10): the new keys must not reuse that mod's rows either.
                foreach (var key in package.Pals.Keys.Concat(package.Raw.Values.SelectMany(r => r.Keys)).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (plan.ModModel.ModRowKeys.Contains(key))
                        Add(PackageProblem.Error, "model", $"'{key}' is already a row of {plan.ModModel.Mod}, whose model this Pal uses; pick an id that mod does not use.");
                if (plan.ModModel.Package == null)
                    Add(PackageProblem.Warning, "model", $"{plan.ModModel.Mod} is not a Workshop package on this PC (installed by hand or by Vortex), so Info.json cannot list it as a dependency; anyone using this Pal must install it themselves.");
                else
                    Add(PackageProblem.Note, "model", $"model from {plan.ModModel.Label}: Info.json lists {plan.ModModel.Package} as a dependency; the Pal renders only where it is installed.");
            }
            foreach (var key in package.TextKeys())
                foreach (var section in PalSchemaPackage.TextSections.Order)
                    if (scan.Exists("text_key", section + "::" + key) || scan.Exists("text_key", section + "_Common::" + key) || scan.Exists("text_key", section.Replace("_Common", "") + "::" + key))
                        Add(PackageProblem.Error, "translations", $"text key '{key}' already exists in {section}.");
            foreach (var entry in package.Spawns)
            {
                if (scan.Exists("spawner_name", entry.SpawnerName) || scan.Exists("placed_spawner", entry.SpawnerName))
                    Add(PackageProblem.Error, "spawns", $"SpawnerName '{entry.SpawnerName}' is a vanilla spawner name.");
            }

            // (b) every reference resolves
            var bpRows = package.Raw.TryGetValue(RawTables.PalBPClass, out var bp) ? bp : new Dictionary<string, JObject>();
            foreach (var kv in package.Pals)
            {
                var row = kv.Value;
                var bpKey = row.BPClass ?? "";
                if (!bpRows.ContainsKey(bpKey) && !scan.TableRowExists(RawTables.PalBPClass, bpKey) && !(data.Pals != null && data.Pals.ContainsKey(bpKey)))
                    Add(PackageProblem.Error, "pals", $"{kv.Key}: BPClass '{bpKey}' is neither a DT_PalBPClass row this package writes nor a shipped one; the Pal would have no model.");
                var tribe = row.Tribe ?? "";
                var tribeMember = tribe.StartsWith("EPalTribeID::", StringComparison.Ordinal) ? tribe.Substring("EPalTribeID::".Length) : null;
                if (tribeMember == null)
                    Add(PackageProblem.Error, "pals", $"{kv.Key}: Tribe '{tribe}' must be namespaced inside a row (EPalTribeID::Name).");
                else
                {
                    var inPackage = package.Enums.TryGetValue("EPalTribeID", out var added) && added.Contains(tribeMember);
                    var inGame = scan.Exists("enum_member", "EPalTribeID::" + tribeMember)
                                 || (data.Enums != null && data.Enums.TryGetValue("EPalTribeID", out var tribes) && tribes.Contains(tribeMember));
                    if (!inPackage && !inGame)
                        Add(PackageProblem.Error, "pals", $"{kv.Key}: Tribe EPalTribeID::{tribeMember} is not a game enum member and this package adds no enums/ entry for it (the loader rejects an invalid enum value).");
                }
                if (row.OverrideNameTextID != null && row.OverrideNameTextID != "None"
                    && !package.TextKeys().Contains(row.OverrideNameTextID)
                    && !scan.Exists("text_key", "DT_PalNameText_Common::" + row.OverrideNameTextID))
                    Add(PackageProblem.Error, "pals", $"{kv.Key}: OverrideNameTextID '{row.OverrideNameTextID}' is not a text key in translations/ or in the game; display names go to translations/, the row keeps None.");
                if (!string.IsNullOrEmpty(row.AIResponse) && row.AIResponse != "None" && !scan.IsEmpty)
                {
                    var preset = "/Game/Pal/Blueprint/Controller/AIResponsePreset/BP_AIResponsePreset_" + row.AIResponse;
                    var ok = scan.Exists("object_path", preset) || scan.Exists("object_path", preset + "." + "BP_AIResponsePreset_" + row.AIResponse)
                             || scan.Exists("object_path", preset + "." + "BP_AIResponsePreset_" + row.AIResponse + "_C");
                    if (!ok) Add(PackageProblem.Warning, "pals", $"{kv.Key}: AIResponse '{row.AIResponse}' has no BP_AIResponsePreset_* blueprint in the scan (vanilla presets: Warlike, Boss, NotInterested, Escape_to_Battle, friendly, escape).");
                }
            }
            foreach (var kv in bpRows)
            {
                var path = kv.Value["BPClass"]?.Type == JTokenType.String ? (string)kv.Value["BPClass"] : null;
                if (string.IsNullOrEmpty(path)) Add(PackageProblem.Error, "raw/" + RawTables.PalBPClass, $"{kv.Key}: BPClass path is empty.");
                else if (plan.ModModel != null && plan.ModModel.KnownClassPaths.Contains(path))
                    continue;   // a class of the mod on this PC: checked against that mod's own rows, not the vanilla export (rule 10)
                else if (plan.Route == ModelRoute.A_SharedVanillaBlueprint && !scan.IsEmpty && !scan.ObjectPathExists(path))
                    Add(PackageProblem.Error, "raw/" + RawTables.PalBPClass, $"{kv.Key}: {path} is not a blueprint class in the export{(plan.ModModel != null ? " or in " + plan.ModModel.Mod : "")}; the Pal would not render.");
            }
            if (package.Raw.TryGetValue(RawTables.CharacterIcon, out var icons))
                foreach (var kv in icons)
                {
                    var path = kv.Value["Icon"]?.Type == JTokenType.String ? (string)kv.Value["Icon"] : null;
                    if (string.IsNullOrEmpty(path)) Add(PackageProblem.Error, "raw/" + RawTables.CharacterIcon, $"{kv.Key}: Icon path is empty.");
                    else if (plan.ModModel != null && plan.ModModel.KnownIconPaths.Contains(path))
                        continue;   // the mod's own icon (rule 10)
                    else if (plan.Route == ModelRoute.A_SharedVanillaBlueprint && !scan.IsEmpty && !scan.ObjectPathExists(path))
                        Add(PackageProblem.Error, "raw/" + RawTables.CharacterIcon, $"{kv.Key}: icon {path} is not a texture in the export.");
                }
            if (package.Raw.TryGetValue(RawTables.WazaMasterLevel, out var waza))
                foreach (var kv in waza)
                {
                    var id = kv.Value["WazaID"]?.Type == JTokenType.String ? (string)kv.Value["WazaID"] : "";
                    var member = id.StartsWith("EPalWazaID::", StringComparison.Ordinal) ? id.Substring("EPalWazaID::".Length) : null;
                    var known = member != null && (scan.Exists("enum_member", "EPalWazaID::" + member)
                                                    || (data.Enums != null && data.Enums.TryGetValue("EPalWazaID", out var moves) && moves.Any(m => string.Equals(m, member, StringComparison.OrdinalIgnoreCase))));
                    if (member == null) Add(PackageProblem.Error, "raw/" + RawTables.WazaMasterLevel, $"{kv.Key}: WazaID '{id}' must be namespaced (EPalWazaID::Name).");
                    else if (!known && (!scan.IsEmpty || (data.Enums != null && data.Enums.ContainsKey("EPalWazaID"))))
                        Add(PackageProblem.Error, "raw/" + RawTables.WazaMasterLevel, $"{kv.Key}: EPalWazaID::{member} is not a move in the game.");
                    var palId = kv.Value["PalId"]?.Type == JTokenType.String ? (string)kv.Value["PalId"] : "";
                    if (!package.Pals.ContainsKey(palId)) Add(PackageProblem.Error, "raw/" + RawTables.WazaMasterLevel, $"{kv.Key}: PalId '{palId}' is not a row of this package.");
                }
            if (package.Raw.TryGetValue(RawTables.DropItem, out var drops))
                foreach (var kv in drops)
                {
                    for (var i = 1; i <= 10; i++)
                    {
                        var item = kv.Value["ItemId" + i]?.Type == JTokenType.String ? (string)kv.Value["ItemId" + i] : null;
                        if (string.IsNullOrEmpty(item) || item == "None") continue;
                        if (data.Items != null && data.Items.Count > 0 && !data.Items.ContainsKey(item) && !data.Items.Keys.Any(k => string.Equals(k, item, StringComparison.OrdinalIgnoreCase)))
                            Add(PackageProblem.Error, "raw/" + RawTables.DropItem, $"{kv.Key}: ItemId{i} '{item}' is not an item.");
                    }
                    var cid = kv.Value["CharacterID"]?.Type == JTokenType.String ? (string)kv.Value["CharacterID"] : "";
                    if (!package.Pals.ContainsKey(cid)) Add(PackageProblem.Error, "raw/" + RawTables.DropItem, $"{kv.Key}: CharacterID '{cid}' is not a row of this package.");
                }
            if (package.Raw.TryGetValue(RawTables.CombiUnique, out var combi))
                foreach (var kv in combi)
                {
                    foreach (var side in new[] { "ParentTribeA", "ParentTribeB" })
                    {
                        var tribe = kv.Value[side]?.Type == JTokenType.String ? (string)kv.Value[side] : "";
                        var member = tribe.StartsWith("EPalTribeID::", StringComparison.Ordinal) ? tribe.Substring("EPalTribeID::".Length) : null;
                        if (member == null) { Add(PackageProblem.Error, "raw/" + RawTables.CombiUnique, $"{kv.Key}: {side} '{tribe}' must be namespaced (EPalTribeID::Name)."); continue; }
                        var ours = package.Enums.TryGetValue("EPalTribeID", out var added) && added.Contains(member);
                        var shipped = scan.Exists("enum_member", "EPalTribeID::" + member)
                                      || (data.Enums != null && data.Enums.TryGetValue("EPalTribeID", out var tl) && tl.Any(m => string.Equals(m, member, StringComparison.OrdinalIgnoreCase)));
                        if (!ours && !shipped) Add(PackageProblem.Error, "raw/" + RawTables.CombiUnique, $"{kv.Key}: EPalTribeID::{member} is not a tribe in the game or in this package.");
                    }
                    foreach (var side in new[] { "ParentGenderA", "ParentGenderB" })
                    {
                        var g = kv.Value[side]?.Type == JTokenType.String ? (string)kv.Value[side] : "";
                        if (g != "EPalGenderType::None" && g != "EPalGenderType::Male" && g != "EPalGenderType::Female")
                            Add(PackageProblem.Error, "raw/" + RawTables.CombiUnique, $"{kv.Key}: {side} '{g}' must be EPalGenderType::None, Male or Female.");
                    }
                    var child = kv.Value["ChildCharacterID"]?.Type == JTokenType.String ? (string)kv.Value["ChildCharacterID"] : "";
                    if (!package.Pals.ContainsKey(child)) Add(PackageProblem.Error, "raw/" + RawTables.CombiUnique, $"{kv.Key}: ChildCharacterID '{child}' is not a row of this package.");
                }
            var texts = package.Translations.TryGetValue(plan.Language ?? "en", out var lang) ? lang : null;
            foreach (var id in package.Pals.Keys.Where(k => !k.StartsWith("BOSS_", StringComparison.OrdinalIgnoreCase)))
            {
                Require(texts, PalSchemaPackage.TextSections.PalName, "PAL_NAME_" + id, Add);
                Require(texts, PalSchemaPackage.TextSections.SkillName, "PARTNERSKILL_" + id, Add, PackageProblem.Warning);
                Require(texts, PalSchemaPackage.TextSections.FirstActivated, "PAL_FIRST_SPAWN_DESC_" + id, Add, PackageProblem.Warning);
                Require(texts, PalSchemaPackage.TextSections.LongDescription, "PAL_LONG_DESC_" + id, Add, PackageProblem.Warning);
                if (package.Pals.ContainsKey("BOSS_" + id)) Require(texts, PalSchemaPackage.TextSections.NamePrefix, "BOSS_NAME_" + id, Add, PackageProblem.Warning);
            }
            var spawnTypes = data.Enums != null && data.Enums.TryGetValue(SpawnedCharacterType.EnumName, out var st) ? st : null;
            var areaPlacements = (plan.AreaIds ?? new List<string>())
                .Select(id => data.MapAreas != null && data.MapAreas.TryGetValue(id ?? "", out var a) ? a : null)
                .Where(a => a != null).SelectMany(a => a.Placements).ToList();
            foreach (var entry in package.Spawns)
            {
                if (entry.SpawnerType == null || entry.SpawnerType.Contains("::")) Add(PackageProblem.Error, "spawns", $"'{entry.SpawnerName}': SpawnerType must be a bare EPalSpawnedCharacterType member.");
                else if (spawnTypes != null && !spawnTypes.Contains(entry.SpawnerType) && !scan.Exists("enum_member", SpawnedCharacterType.EnumName + "::" + entry.SpawnerType))
                    Add(PackageProblem.Error, "spawns", $"'{entry.SpawnerName}': SpawnerType '{entry.SpawnerType}' is not an EPalSpawnedCharacterType member.");
                if (entry.Type != "Sheet") Add(PackageProblem.Error, "spawns", $"'{entry.SpawnerName}': Type must be 'Sheet'.");
                foreach (var pal in entry.SpawnGroupList.SelectMany(g => g.PalList))
                    if (!package.Pals.ContainsKey(pal.PalId ?? "")) Add(PackageProblem.Error, "spawns", $"'{entry.SpawnerName}': PalId '{pal.PalId}' is not a row of this package.");
                if (areaPlacements.Count > 0 && !areaPlacements.Any(p => Same(p.X, entry.Location.X) && Same(p.Y, entry.Location.Y) && Same(p.Z, entry.Location.Z)))
                    Add(PackageProblem.Error, "spawns", $"'{entry.SpawnerName}': Location is not a vanilla placement of the chosen area(s).");
            }

            // (c) route
            foreach (var e in RouteGuard.Check(package, plan, scan)) Add(PackageProblem.Error, "route", e);

            // (d) completeness
            foreach (var kv in package.Pals)
            {
                var missing = RowCompleteness.MissingFields(kv.Value).ToList();
                if (missing.Count > 0) Add(PackageProblem.Error, "pals", $"{kv.Key}: {missing.Count} field(s) unset ({string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? ", ..." : "")}); a new row has nothing to merge into.");
            }

            // (e) footprint against the working mod
            var targets = package.LoaderTargets();
            if (plan.Tribe == TribeMode.NewMember && !targets.Contains("enums")) Add(PackageProblem.Error, "enums", "a new tribe member needs enums/ (the working mod adds every new tribe there).");
            foreach (var t in RequiredTargets.Where(t => !targets.Contains(t))) Add(PackageProblem.Error, t, "required piece missing (every new Pal of the working mod ships it).");
            foreach (var t in RecommendedTargets.Where(t => !targets.Contains(t))) Add(PackageProblem.Warning, t, "recommended piece missing (the working mod ships it for all 50 new Pals).");

            return problems;
        }

        private static void Require(Dictionary<string, Dictionary<string, string>> texts, string section, string key, Action<string, string, string> add, string severity = PackageProblem.Error)
        {
            var present = texts != null && texts.TryGetValue(section, out var entries) && entries.ContainsKey(key) && !string.IsNullOrEmpty(entries[key]);
            if (!present) add(severity, "translations", $"{section}: '{key}' is missing (the Pal would show the raw key in game).");
        }

        private static bool Same(double a, double b) => Math.Abs(a - b) < 0.01;
    }
}
