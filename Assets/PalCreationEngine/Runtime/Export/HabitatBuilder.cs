using System;
using System.Collections.Generic;
using System.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Builds the Paldex habitat row (<c>DT_PaldexDistributionData</c>) for a Pal spawned by
    /// area. Points, in contract order:
    ///   (1) the emitted spawn locations;
    ///   (2) the area's core Common placements;
    ///   (3) the base Pal's own habitat points that fall inside any of the area's zones;
    ///   (4) when (3) is empty, base points within 15,000 cm of any area placement
    ///       (BadCatgirl's vanilla points fall in no Scorched Ashland trigger volume although
    ///       its spawners are placed there, hence the proximity fallback).
    /// Day and night share (1) and (2); (3)/(4) are computed per set when the base has
    /// different day and night points. The row uses the working mod's <c>Action: Clear</c>
    /// wrapper and Radius 15000 (314 of 367 vanilla rows use that radius).
    /// Effect in game beyond the Paldex map: UNTESTED.
    /// </summary>
    public static class HabitatBuilder
    {
        public const double ProximityCm = 15000.0;

        public sealed class Result
        {
            public PaldexDistributionRow Row;
            public int FromSpawns, FromAreaPlacements, DayInsideZones, NightInsideZones, DayProximity, NightProximity;
            public readonly List<string> Report = new List<string>();
        }

        public static Result Build(NewPalPlan plan, GameData data, IReadOnlyList<SpawnVector> spawnLocations)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (data == null) throw new ArgumentNullException(nameof(data));
            var result = new Result();
            var areas = (plan.AreaIds ?? new List<string>())
                .Select(id => data.MapAreas != null && data.MapAreas.TryGetValue(id ?? "", out var a) ? a : null)
                .Where(a => a != null).ToList();

            var shared = new List<SpawnVector>();
            foreach (var loc in spawnLocations ?? new List<SpawnVector>())
            {
                shared.Add(new SpawnVector(loc.X, loc.Y, loc.Z));
                result.FromSpawns++;
            }
            var areaPlacements = areas.SelectMany(a => a.Placements).ToList();
            foreach (var p in areaPlacements.Where(p => string.Equals(p.SpawnerType, SpawnedCharacterType.Common, StringComparison.OrdinalIgnoreCase)))
            {
                shared.Add(new SpawnVector(p.X, p.Y, p.Z));
                result.FromAreaPlacements++;
            }

            PaldexPointSet basePoints = null;
            var radius = ProximityCm;
            if (data.PaldexPoints != null && !string.IsNullOrEmpty(plan.BasePalId))
            {
                var key = data.PaldexPoints.Keys.FirstOrDefault(k => string.Equals(k, plan.BasePalId, StringComparison.OrdinalIgnoreCase));
                if (key != null) { basePoints = data.PaldexPoints[key]; if (basePoints.Radius > 0) radius = basePoints.Radius; }
            }

            var day = new List<SpawnVector>(shared);
            var night = new List<SpawnVector>(shared);
            if (basePoints != null && areas.Count > 0)
            {
                var dayIn = InsideZones(basePoints.Day, areas);
                var nightIn = InsideZones(basePoints.Night, areas);
                result.DayInsideZones = dayIn.Count;
                result.NightInsideZones = nightIn.Count;
                if (dayIn.Count == 0) { dayIn = Near(basePoints.Day, areaPlacements); result.DayProximity = dayIn.Count; }
                if (nightIn.Count == 0) { nightIn = Near(basePoints.Night, areaPlacements); result.NightProximity = nightIn.Count; }
                day.AddRange(dayIn);
                night.AddRange(nightIn);
                result.Report.Add($"habitat: base {plan.BasePalId} points inside the chosen zones day {result.DayInsideZones}/{basePoints.Day.Count}, night {result.NightInsideZones}/{basePoints.Night.Count}" +
                                  (result.DayProximity + result.NightProximity > 0 ? $"; proximity fallback (<= {ProximityCm:0} cm of an area placement) day {result.DayProximity}, night {result.NightProximity}" : ""));
            }
            else if (basePoints == null)
            {
                result.Report.Add("habitat: paldex_points.json has no entry for the base Pal; only spawn and placement points are used");
            }
            result.Report.Add($"habitat: {result.FromSpawns} spawn points + {result.FromAreaPlacements} area placements; day {day.Count} / night {night.Count} points, radius {radius:0}");

            result.Row = new PaldexDistributionRow
            {
                DayTimeLocations = new PaldexTimeLocations { Locations = new PaldexLocationList { Action = "Clear", Items = day }, Radius = radius },
                NightTimeLocations = new PaldexTimeLocations { Locations = new PaldexLocationList { Action = "Clear", Items = night }, Radius = radius },
            };
            return result;
        }

        public static List<SpawnVector> InsideZones(IEnumerable<double[]> points, IReadOnlyList<MapArea> areas)
        {
            var out_ = new List<SpawnVector>();
            foreach (var p in points ?? Enumerable.Empty<double[]>())
            {
                if (p == null || p.Length < 3) continue;
                if (areas.Any(a => a.Contains(p[0], p[1], p[2]))) out_.Add(new SpawnVector(p[0], p[1], p[2]));
            }
            return out_;
        }

        public static List<SpawnVector> Near(IEnumerable<double[]> points, IReadOnlyList<AreaPlacement> placements, double maxCm = ProximityCm)
        {
            var out_ = new List<SpawnVector>();
            if (placements == null || placements.Count == 0) return out_;
            foreach (var p in points ?? Enumerable.Empty<double[]>())
            {
                if (p == null || p.Length < 3) continue;
                foreach (var q in placements)
                {
                    var dx = p[0] - q.X; var dy = p[1] - q.Y; var dz = p[2] - q.Z;
                    if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= maxCm) { out_.Add(new SpawnVector(p[0], p[1], p[2])); break; }
                }
            }
            return out_;
        }
    }
}
