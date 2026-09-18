using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Writes <c>spawns/</c> Sheet entries for a Pal in chosen map areas, the way the verified
    /// working mod does: each entry copies the exact coordinates of a vanilla spawner placement
    /// that lies inside the area's trigger volumes, so the Pal appears where the game already
    /// spawns things there.
    ///
    /// Alpha (FieldBoss) entries copy the area's FieldBoss placements (falling back to Common
    /// ones); regular entries copy Common placements. Levels come from the area's own vanilla
    /// level range. The placement choice is deterministic (first N in the area's placement
    /// order) so the harness can predict it.
    /// </summary>
    public static class AreaSpawnBuilder
    {
        public sealed class Result
        {
            public readonly List<SheetSpawnEntry> Entries = new List<SheetSpawnEntry>();
            public readonly List<string> Report = new List<string>();
            public readonly List<string> Errors = new List<string>();
            /// <summary>The vanilla placement each entry copied, in entry order.</summary>
            public readonly List<AreaPlacement> Copied = new List<AreaPlacement>();
        }

        public static Result Build(NewPalPlan plan, GameData data)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (data == null) throw new ArgumentNullException(nameof(data));
            var result = new Result();

            if (plan.AreaIds == null || plan.AreaIds.Count == 0)
            {
                result.Report.Add("no areas chosen: no spawns/ entries written");
                return result;
            }
            if (data.MapAreas == null || data.MapAreas.Count == 0)
            {
                result.Errors.Add("map_areas.json is missing or empty in True Creation's data folder; spawns by area need it. Reinstall True Creation to restore it "
                                  + "(developers: python Tools/PalCreationEngine/generate_index_lookups.py builds it from Docs/ExportTOC/map-areas).");
                return result;
            }

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var areaId in plan.AreaIds)
            {
                if (!data.MapAreas.TryGetValue(areaId ?? "", out var area))
                {
                    var ci = data.MapAreas.Keys.FirstOrDefault(k => string.Equals(k, areaId, StringComparison.OrdinalIgnoreCase));
                    if (ci == null) { result.Errors.Add($"area '{areaId}' is not in map_areas.json"); continue; }
                    area = data.MapAreas[ci];
                }
                var prefix = string.IsNullOrWhiteSpace(area.TopPrefix) ? area.Id.ToLowerInvariant() : area.TopPrefix;

                if (plan.IncludeBoss && plan.BossSpawnsPerArea > 0)
                {
                    var bossPlacements = area.PlacementsOfType(SpawnedCharacterType.FieldBoss).ToList();
                    var fromFallback = false;
                    if (bossPlacements.Count == 0)
                    {
                        bossPlacements = area.PlacementsOfType(SpawnedCharacterType.Common).ToList();
                        fromFallback = true;
                    }
                    if (bossPlacements.Count == 0)
                    {
                        result.Errors.Add($"area {area.Id} has no core placements to copy for an alpha spawn");
                    }
                    else
                    {
                        var min = plan.BossLevelMin ?? area.Levels?.FieldBossMin ?? area.Levels?.CommonMax ?? 10;
                        var max = plan.BossLevelMax ?? area.Levels?.FieldBossMax ?? min;
                        for (var i = 0; i < Math.Min(plan.BossSpawnsPerArea, bossPlacements.Count); i++)
                        {
                            var p = bossPlacements[i];
                            var name = Unique(usedNames, $"{prefix}_FBOSS_{plan.PalId}", i);
                            result.Entries.Add(Entry(p, name, SpawnedCharacterType.FieldBoss, plan.BossId, min, max, 1, 1));
                            result.Copied.Add(p);
                            result.Report.Add($"{area.Id}: alpha spawn '{name}' at ({F(p.X)}, {F(p.Y)}, {F(p.Z)}) copied from vanilla {p.SpawnerType} placement '{p.SpawnerName}'{(fromFallback ? " (no FieldBoss placement in this area; a Common one was used)" : "")}, level {min}-{max}");
                        }
                    }
                }

                if (plan.RegularSpawnsPerArea > 0)
                {
                    var common = area.PlacementsOfType(SpawnedCharacterType.Common)
                        .OrderBy(p => string.Equals(p.PlacementType, "Field", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ToList();
                    if (common.Count == 0)
                    {
                        result.Errors.Add($"area {area.Id} has no Common placements to copy for regular spawns");
                    }
                    else
                    {
                        var min = plan.RegularLevelMin ?? area.Levels?.CommonP10 ?? area.Levels?.CommonMin ?? 1;
                        var max = plan.RegularLevelMax ?? area.Levels?.CommonP90 ?? area.Levels?.CommonMax ?? min;
                        var numMin = Math.Max(1, plan.RegularNumMin ?? 1);
                        var numMax = Math.Max(numMin, plan.RegularNumMax ?? 2);
                        for (var i = 0; i < Math.Min(plan.RegularSpawnsPerArea, common.Count); i++)
                        {
                            var p = common[i];
                            var name = Unique(usedNames, $"{prefix}_Common_{plan.PalId}", i);
                            result.Entries.Add(Entry(p, name, SpawnedCharacterType.Common, plan.PalId, min, max, numMin, numMax));
                            result.Copied.Add(p);
                            result.Report.Add($"{area.Id}: regular spawn '{name}' at ({F(p.X)}, {F(p.Y)}, {F(p.Z)}) copied from vanilla Common placement '{p.SpawnerName}' ({p.PlacementType}), level {min}-{max} (Common through spawns/ is UNTESTED in game)");
                        }
                    }
                }
            }
            return result;
        }

        private static SheetSpawnEntry Entry(AreaPlacement p, string name, string type, string palId, int min, int max, int num, int numMax) =>
            new SheetSpawnEntry
            {
                Type = "Sheet",
                Location = new SpawnVector(p.X, p.Y, p.Z),
                Rotation = new SpawnRotator(),
                SpawnerName = name,
                SpawnerType = type,
                SpawnGroupList =
                {
                    new SheetSpawnGroup
                    {
                        Weight = 100,
                        PalList = { new SpawnPalEntry { PalId = palId, Level = min, Level_Max = Math.Max(min, max), Num = num, Num_Max = Math.Max(num, numMax) } },
                    },
                },
            };

        private static string Unique(HashSet<string> used, string baseName, int index)
        {
            var name = index == 0 ? baseName : baseName + "_" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var n = index + 1;
            while (!used.Add(name)) name = baseName + "_" + (++n).ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
