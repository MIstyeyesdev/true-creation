using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    public sealed class BuildReport
    {
        public readonly List<string> Lines = new List<string>();
        public readonly List<string> Untested = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Builds the complete PalSchema footprint of ONE new Pal from a base Pal, the way the
    /// verified working mod (PalVariantPandemonium) ships each of its 50: a regular and a boss
    /// row in <c>pals/</c>, own DT_PalBPClass and icon rows, copied partner-skill, camera and
    /// randomizer rows, a learnset, drop rows, a tribe enum member, texts in
    /// <c>translations/</c>, spawns copied from vanilla placements in the chosen map areas and
    /// a Paldex habitat row. Every piece that copies a vanilla row takes it from
    /// <c>base_rows.json</c>; when that file is absent the piece is skipped and reported, never
    /// invented.
    /// </summary>
    public static class NewPalBuilder
    {
        public static PalSchemaPackage Build(NewPalPlan plan, GameData data, out BuildReport report)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (data == null) throw new ArgumentNullException(nameof(data));
            report = new BuildReport();
            var package = new PalSchemaPackage();

            // ---- inputs
            if (string.IsNullOrWhiteSpace(plan.PalId) || !plan.PalId.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                report.Errors.Add("PalId must be letters, digits and underscores only (it becomes a row key, an enum member and part of text keys).");
                return package;
            }
            if (!data.TryCanonicalPalKey(plan.BasePalId, out var baseId))
            {
                report.Errors.Add($"base Pal '{plan.BasePalId}' is not in pals.json.");
                return package;
            }
            if (!string.Equals(baseId, plan.BasePalId, StringComparison.Ordinal))
                report.Lines.Add($"base Pal id spelled '{baseId}' in the game (you typed '{plan.BasePalId}')");
            var basePal = data.Pals[baseId];
            var bossBaseId = data.Pals.ContainsKey("BOSS_" + baseId) ? "BOSS_" + baseId : data.Pals.Keys.FirstOrDefault(k => string.Equals(k, "BOSS_" + baseId, StringComparison.OrdinalIgnoreCase));
            var link = data.BaseLinks != null && data.BaseLinks.TryGetValue(baseId, out var l) ? l : null;
            var id = plan.PalId;
            var bossId = plan.BossId;
            var tribeMember = plan.Tribe == TribeMode.NewMember ? id : BareEnum(basePal.Tribe);

            // ---- 1. regular row
            PalMonsterParameterRow row = null;
            var baseRow = data.BaseRow(RawTables.PalMonsterParameterTable, baseId);
            if (plan.RowTemplate != null)
            {
                // The Creation Engine sections already hold the base clone with the author's edits;
                // only identity is applied here, so what the sections show is what ships.
                row = PalFactory.ApplyIdentity(PalFactory.Clone(plan.RowTemplate), id, tribeMember, id);
                report.Lines.Add($"regular row taken from the Creation Engine sections ({baseId}'s row with edits), identity applied");
            }
            else if (plan.Seed == SeedMode.CloneBase && baseRow != null)
            {
                row = PalFactory.CreateFromBase(baseRow, id, tribeMember, id);
                report.Lines.Add($"regular row cloned from {baseId} (90 fields), identity applied");
            }
            else
            {
                if (plan.Seed == SeedMode.CloneBase)
                    report.Lines.Add("base_rows.json has no DT_PalMonsterParameter rows, so the regular row was seeded from size medians instead. "
                                     + "Reinstall True Creation to restore cloning (developers: python Tools/PalCreationEngine/generate_lookups.py --base-rows)");
                try
                {
                    row = PalFactory.CreateNew(data, id, BareEnum(basePal.Size), id, tribeMember);
                }
                catch (ArgumentException e)
                {
                    report.Errors.Add("could not seed the regular row: " + e.Message);
                    return package;
                }
            }
            ApplyZukan(row, plan, basePal, data, report);
            package.Pals[id] = row;

            // ---- 2. boss row
            if (plan.IncludeBoss)
            {
                PalMonsterParameterRow boss;
                var bossBaseRow = bossBaseId != null ? data.BaseRow(RawTables.PalMonsterParameterTable, bossBaseId) : null;
                if (bossBaseRow != null)
                {
                    boss = PalFactory.CreateFromBase(bossBaseRow, bossId, tribeMember, bossId, bossRow: true);
                    boss.IsBoss = true;
                    report.Lines.Add($"boss row cloned from {bossBaseId} (boss flags kept)");
                }
                else
                {
                    boss = PalFactory.Clone(row);
                    boss.IsBoss = true;
                    boss.UseBossHPGauge = true;
                    report.Lines.Add(bossBaseId == null
                        ? $"{baseId} has no BOSS_ row; the boss row is the regular row with boss flags"
                        : "base_rows.json has no boss row; the boss row is the regular row with boss flags");
                }
                boss.OverrideNameTextID = "PAL_NAME_" + id;
                boss.NamePrefixID = "BOSS_NAME_" + id;
                boss.Tribe = "EPalTribeID::" + tribeMember;
                boss.BPClass = bossId;
                boss.ZukanIndex = -1;
                boss.ZukanIndexSuffix = "";
                package.Pals[bossId] = boss;
            }

            // ---- 3. DT_PalBPClass rows (own keys -> the base's vanilla class paths, or a model from a mod on this PC)
            var mod = plan.ModModel;
            var basePath = mod != null ? mod.ClassPath : (link?.BpClassPath ?? data.BlueprintPathFor(basePal.BpClass));
            if (mod != null)
                report.Lines.Add($"model: {mod.Label} ({mod.ClassPath}) from a mod on this PC; the package lists {mod.Requirement} as a dependency and renders only where it is installed");
            if (string.IsNullOrEmpty(basePath))
                report.Errors.Add(mod != null
                    ? $"the mod model {mod.Label} has no class path; the Pal would have no model."
                    : $"no blueprint path known for {baseId} (bp_class_rows.json / bp_paths.json); the Pal would have no model.");
            else
            {
                package.AddRaw(RawTables.PalBPClass, id, new BpClassRow { BPClass = basePath });
                if (plan.IncludeBoss)
                {
                    string bossPath;
                    if (mod != null)
                    {
                        bossPath = mod.BossClassPath;
                        if (string.IsNullOrEmpty(bossPath)) { bossPath = basePath; report.Lines.Add($"{mod.Mod} has no BOSS_{mod.RowKey} row; the boss row uses the same mod class"); }
                    }
                    else
                    {
                        bossPath = link?.BossBpClassPath;
                        if (string.IsNullOrEmpty(bossPath) && bossBaseId != null) bossPath = data.BlueprintPathFor(data.Pals[bossBaseId].BpClass);
                        if (string.IsNullOrEmpty(bossPath)) { bossPath = basePath; report.Lines.Add("boss blueprint path unknown; the boss row uses the regular class path"); }
                    }
                    package.AddRaw(RawTables.PalBPClass, bossId, new BpClassRow { BPClass = bossPath });
                }
            }

            // ---- 4. icon (the mod's own icon row when the model comes from a mod that has one)
            var iconPath = mod?.IconPath ?? link?.IconPath;
            if (mod != null && string.IsNullOrEmpty(mod.IconPath)) report.Lines.Add($"{mod.Mod} has no icon row for {mod.RowKey}; the icon stays {baseId}'s");
            if (string.IsNullOrEmpty(iconPath))
            {
                var guess = $"/Game/Pal/Texture/PalIcon/Normal/T_{baseId}_icon_normal.T_{baseId}_icon_normal";
                if (data.Scan != null && data.Scan.ObjectPathExists(guess)) iconPath = data.Scan.Resolve("object_path", guess);
            }
            if (string.IsNullOrEmpty(iconPath)) report.Errors.Add($"no icon path known for {baseId} (base_links.json); the Pal would have no icon.");
            else package.AddRaw(RawTables.CharacterIcon, id, new CharacterIconRow { Icon = iconPath });

            // ---- 5-7. partner icon, partner parameter, camera, randomizer: clones of the base rows
            CopyRow(package, data, RawTables.PartnerSkillIcon, link?.PartnerIconRow ?? baseId, id, report);
            var partnerRegular = CopyRow(package, data, RawTables.PartnerSkillParameter, link?.PartnerParamRow ?? baseId, id, report);
            if (partnerRegular != null && !string.IsNullOrEmpty(plan.PartnerSkillNameOverride))
                OverridePartnerSkill(partnerRegular, plan.PartnerSkillNameOverride, report);
            if (plan.IncludeBoss && bossBaseId != null)
                CopyRow(package, data, RawTables.PartnerSkillParameter, link?.BossPartnerParamRow ?? bossBaseId, bossId, report, optional: true);
            CopyRow(package, data, RawTables.CaptureCameraOffset, link?.CameraRow ?? baseId, id, report);
            var randomizer = CopyRow(package, data, RawTables.Randomizer, link?.RandomizerRow ?? baseId, id, report, optional: true);
            if (randomizer != null) randomizer["PalId"] = id;

            // ---- 8. learnset
            var learnset = plan.Learnset != null && plan.Learnset.Count > 0
                ? plan.Learnset.Select(c => new LearnsetEntry { WazaId = BareEnum(c.WazaId), Level = c.Level }).ToList()
                : (link?.Learnset ?? new List<LearnsetEntry>());
            if (learnset.Count == 0) report.Lines.Add($"no learnset known for {baseId}; the Pal will have no learned moves (DT_WazaMasterLevel_Common rows not written)");
            foreach (var entry in learnset.OrderBy(e => e.Level))
            {
                package.AddRaw(RawTables.WazaMasterLevel, id + entry.Level.ToString("000", CultureInfo.InvariantCulture),
                    new WazaMasterLevelRow { PalId = id, WazaID = "EPalWazaID::" + BareEnum(entry.WazaId), Level = entry.Level });
                if (plan.IncludeBoss)
                    package.AddRaw(RawTables.WazaMasterLevel, bossId + entry.Level.ToString("000", CultureInfo.InvariantCulture),
                        new WazaMasterLevelRow { PalId = bossId, WazaID = "EPalWazaID::" + BareEnum(entry.WazaId), Level = entry.Level });
            }

            // ---- 9. drops
            AddDrops(package, data, plan.RegularDrops, link?.DropRow ?? PalSchemaMod.DropRowKey(baseId, 0), id, report);
            if (plan.IncludeBoss)
                AddDrops(package, data, plan.BossDrops ?? plan.RegularDrops, link?.BossDropRow ?? (bossBaseId != null ? PalSchemaMod.DropRowKey(bossBaseId, 0) : null), bossId, report, fallbackFrom: id);

            // ---- 9b. fixed breeding pairs (DT_PalCombiUnique), the working mod's way: numeric keys
            // >= 1000, namespaced tribes and genders, ChildCharacterID = this Pal. Keys derive from
            // the id (10000 + hash * 10) so packages of different Pals cannot collide with each other
            // or with the working mod's 1000..5049.
            var pairs = new List<CombiPair>(plan.CombiPairs ?? new List<CombiPair>());
            if (plan.SelfBreedPair) pairs.Add(new CombiPair { ParentTribeA = tribeMember, ParentTribeB = tribeMember });
            if (pairs.Count > 0)
            {
                var knownTribes = new HashSet<string>(data.EnumValues("EPalTribeID") ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase) { tribeMember };
                var seed = 10000 + (int)(Fnv1a(id) % 800000u) * 10;
                var written = 0;
                for (var i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];
                    var a = BareEnum(pair.ParentTribeA);
                    var b = BareEnum(pair.ParentTribeB);
                    if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) { report.Errors.Add($"breeding pair {i + 1}: both parent tribes are required."); continue; }
                    if (!knownTribes.Contains(a)) { report.Errors.Add($"breeding pair {i + 1}: '{a}' is not an EPalTribeID member (nor this Pal's own)."); continue; }
                    if (!knownTribes.Contains(b)) { report.Errors.Add($"breeding pair {i + 1}: '{b}' is not an EPalTribeID member (nor this Pal's own)."); continue; }
                    package.AddRaw(RawTables.CombiUnique, (seed + i).ToString(CultureInfo.InvariantCulture), new CombiUniqueRow
                    {
                        ParentTribeA = "EPalTribeID::" + a,
                        ParentGenderA = "EPalGenderType::" + GenderOrNone(pair.ParentGenderA),
                        ParentTribeB = "EPalTribeID::" + b,
                        ParentGenderB = "EPalGenderType::" + GenderOrNone(pair.ParentGenderB),
                        ChildCharacterID = id,
                    });
                    written++;
                    report.Lines.Add($"breeding pair: {a}{GenderTag(pair.ParentGenderA)} + {b}{GenderTag(pair.ParentGenderB)} -> {id} (DT_PalCombiUnique key {seed + i})");
                }
                if (written > 0) report.Untested.Add("Fixed breeding pairs: the working mod ships them; a pair that vanilla already defines for another child is UNTESTED (vanilla pair parents are not in the index).");
            }
            else report.Lines.Add("no fixed breeding pairs: bred only by CombiRank (the row's value), or never if IgnoreCombi is true");

            // ---- 10. translations
            var lang = string.IsNullOrWhiteSpace(plan.Language) ? "en" : plan.Language;
            string BaseText(string key, string fallback) => link != null && link.Text.TryGetValue(key, out var t) && !string.IsNullOrEmpty(t) ? t : fallback;
            var displayName = FirstNonEmpty(plan.Texts?.DisplayName, BaseText("PAL_NAME", data.Names?.Pals != null && data.Names.Pals.TryGetValue(baseId, out var n) ? n : id));
            package.AddText(lang, PalSchemaPackage.TextSections.PalName, "PAL_NAME_" + id, displayName);
            if (plan.IncludeBoss)
                package.AddText(lang, PalSchemaPackage.TextSections.NamePrefix, "BOSS_NAME_" + id, FirstNonEmpty(plan.Texts?.BossPrefix, BaseText("BOSS_NAME", "Alpha")));
            package.AddText(lang, PalSchemaPackage.TextSections.SkillName, "PARTNERSKILL_" + id, FirstNonEmpty(plan.Texts?.PartnerSkillName, BaseText("PARTNERSKILL", displayName + "'s Partner Skill")));
            package.AddText(lang, PalSchemaPackage.TextSections.FirstActivated, "PAL_FIRST_SPAWN_DESC_" + id, FirstNonEmpty(plan.Texts?.FirstSpawnDesc, BaseText("PAL_FIRST_SPAWN_DESC", displayName + " appeared.")));
            package.AddText(lang, PalSchemaPackage.TextSections.LongDescription, "PAL_LONG_DESC_" + id, FirstNonEmpty(plan.Texts?.LongDesc, BaseText("PAL_LONG_DESC", data.PalDescription(baseId) ?? "")));
            if (link == null) report.Lines.Add($"base_links.json has no entry for {baseId}; texts not given were filled from display_names.json or generic defaults");

            // ---- 11. spawns and habitat
            var spawns = AreaSpawnBuilder.Build(plan, data);
            report.Lines.AddRange(spawns.Report);
            report.Errors.AddRange(spawns.Errors);
            package.Spawns.AddRange(spawns.Entries);
            if (plan.Habitat && plan.AreaIds != null && plan.AreaIds.Count > 0)
            {
                var habitat = HabitatBuilder.Build(plan, data, spawns.Entries.Select(e => e.Location).ToList());
                report.Lines.AddRange(habitat.Report);
                package.AddRaw(RawTables.PaldexDistribution, id, habitat.Row);
            }

            // ---- 12. enums
            if (plan.Tribe == TribeMode.NewMember) package.AddEnumMember("EPalTribeID", id);
            else report.Lines.Add($"tribe borrowed from {baseId} (EPalTribeID::{tribeMember}); no enums/ file");

            // ---- what an in-game run still has to show
            report.Untested.Add("Route A: a new id on a shared vanilla blueprint renders, animates and fights like the base Pal.");
            report.Untested.Add("translations/ names and descriptions display in game (no log line exists for that loader).");
            if (package.Spawns.Any(s => s.SpawnerType == SpawnedCharacterType.Common)) report.Untested.Add("Common spawns through spawns/ (the working mod ships FieldBoss entries only).");
            if (plan.Habitat) report.Untested.Add("DT_PaldexDistributionData has any effect beyond the Paldex habitat map.");
            if (plan.Tribe == TribeMode.BorrowBase) report.Untested.Add("Borrowed tribe side effects (Paldex grouping, combi parents, partner-skill tribe conditions).");
            return package;
        }

        // ------------------------------------------------------------------ helpers

        private static void ApplyZukan(PalMonsterParameterRow row, NewPalPlan plan, PalSummary basePal, GameData data, BuildReport report)
        {
            if (plan.ZukanIndex.HasValue)
            {
                row.ZukanIndex = plan.ZukanIndex.Value;
                row.ZukanIndexSuffix = plan.ZukanIndexSuffix ?? "";
                return;
            }
            row.ZukanIndex = basePal.ZukanIndex;
            var suffix = plan.ZukanIndexSuffix;
            if (string.IsNullOrEmpty(suffix))
                suffix = data.NextFreeZukanSuffix(basePal.ZukanIndex, basePal.ZukanIndexSuffix ?? "") ?? "Z"; // same rule as the editor's Paldex Suffix field
            row.ZukanIndexSuffix = suffix;
            report.Lines.Add($"Paldex slot {basePal.ZukanIndex}{suffix} (base {basePal.ZukanIndex}{basePal.ZukanIndexSuffix})");
        }

        private static JObject CopyRow(PalSchemaPackage package, GameData data, string table, string sourceKey, string newKey, BuildReport report, bool optional = false)
        {
            if (string.IsNullOrEmpty(sourceKey)) return null;
            var src = data.BaseRow(table, sourceKey);
            if (src == null)
            {
                report.Lines.Add($"{table}: no base row '{sourceKey}' in base_rows.json; row for {newKey} not written{(optional ? "" : " (recommended piece)")}");
                return null;
            }
            var clone = (JObject)src.DeepClone();
            package.AddRaw(table, newKey, clone);
            return clone;
        }

        private static void OverridePartnerSkill(JObject partnerRow, string skillName, BuildReport report)
        {
            // PVP shape: PassiveSkills[i].SkillAndParametersArray[j].SkillName.Key
            var replaced = 0;
            foreach (var entry in partnerRow.SelectTokens("$.PassiveSkills[*].SkillAndParametersArray[*].SkillName"))
                if (entry is JObject o && o["Key"] != null) { o["Key"] = skillName; replaced++; }
            report.Lines.Add(replaced > 0 ? $"partner skill set to '{skillName}' in {replaced} slot(s)" : "partner skill override requested but the copied row has no SkillName.Key slots");
        }

        private static void AddDrops(PalSchemaPackage package, GameData data, PalDropItemRow given, string baseDropKey, string newId, BuildReport report, string fallbackFrom = null)
        {
            JObject row = null;
            if (given != null)
                row = JObject.FromObject(given, JsonSerializer.Create(PalSchemaMod.SerializerSettings));
            else if (!string.IsNullOrEmpty(baseDropKey))
            {
                var src = data.BaseRow(RawTables.DropItem, baseDropKey);
                if (src != null) row = (JObject)src.DeepClone();
            }
            if (row == null && fallbackFrom != null && package.Raw.TryGetValue(RawTables.DropItem, out var rows) && rows.TryGetValue(PalSchemaMod.DropRowKey(fallbackFrom, 0), out var regular))
                row = (JObject)regular.DeepClone();
            if (row == null)
            {
                report.Lines.Add($"{RawTables.DropItem}: no drop row to copy for {newId} (base '{baseDropKey}' not in base_rows.json); not written");
                return;
            }
            row["CharacterID"] = newId;
            row["Level"] = 0;
            package.AddRaw(RawTables.DropItem, PalSchemaMod.DropRowKey(newId, 0), row);
        }

        private static string GenderOrNone(string gender)
        {
            var bare = BareEnum(gender);
            return bare == "Male" || bare == "Female" ? bare : "None";
        }

        private static string GenderTag(string gender)
        {
            var g = GenderOrNone(gender);
            return g == "None" ? "" : $" ({g})";
        }

        private static uint Fnv1a(string s)
        {
            uint h = 2166136261;
            foreach (var c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }

        private static string BareEnum(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var i = value.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? value.Substring(i + 2) : value;
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
