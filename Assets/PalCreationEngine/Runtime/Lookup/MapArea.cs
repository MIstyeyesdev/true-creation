using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace PalCreationEngine.Lookup
{
    /// <summary>
    /// One in-game map area (a <c>DT_WorldMapAreaData</c> row) with the trigger volumes
    /// that define it and the vanilla spawner placements assigned to it.
    ///
    /// Source: <c>map_areas.json</c>, built from Docs/ExportTOC/map-areas (zones.json,
    /// placements_by_area.csv, area_summary.csv). Entering a zone triggers the area name
    /// in game, so a zone IS the area; spawning a Pal "in an area" means copying the
    /// coordinates of vanilla placements that lie inside its zones.
    /// </summary>
    public sealed class MapArea
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("family")] public string Family;
        [JsonProperty("zones")] public List<AreaZone> Zones = new List<AreaZone>();
        [JsonProperty("levels")] public AreaLevels Levels = new AreaLevels();
        [JsonProperty("counts")] public AreaCounts Counts = new AreaCounts();
        [JsonProperty("topPrefix")] public string TopPrefix;

        /// <summary>Core placements only (tier A inside a zone in 3D, tier B inside in XY).</summary>
        [JsonProperty("placements")] public List<AreaPlacement> Placements = new List<AreaPlacement>();

        public bool Contains(double x, double y, double z) =>
            Zones != null && Zones.Any(zone => zone.Contains(x, y, z));

        public IEnumerable<AreaPlacement> PlacementsOfType(string spawnerType) =>
            Placements.Where(p => string.Equals(p.SpawnerType, spawnerType, StringComparison.OrdinalIgnoreCase));

        public string Label => string.IsNullOrEmpty(Name) || Name == Id ? Id : $"{Name}  ({Id})";
    }

    /// <summary>
    /// A trigger volume. Box test (from zones.json): <c>local = R(quat)^T * (p - center)</c>,
    /// inside when every |local.i| &lt;= halfExtent[i]; halfExtent already includes the world
    /// scale. Sphere test: |p - center| &lt;= radius.
    /// </summary>
    public sealed class AreaZone
    {
        [JsonProperty("zoneId")] public string ZoneId;
        [JsonProperty("shape")] public string Shape;
        [JsonProperty("center")] public double[] Center;
        [JsonProperty("quatWxyz")] public double[] QuatWxyz;
        [JsonProperty("halfExtent")] public double[] HalfExtent;
        [JsonProperty("radius")] public double? Radius;

        public bool Contains(double x, double y, double z)
        {
            if (Center == null || Center.Length < 3) return false;
            var dx = x - Center[0];
            var dy = y - Center[1];
            var dz = z - Center[2];

            if (string.Equals(Shape, "sphere", StringComparison.OrdinalIgnoreCase))
                return Radius.HasValue && Math.Sqrt(dx * dx + dy * dy + dz * dz) <= Radius.Value;

            if (HalfExtent == null || HalfExtent.Length < 3) return false;
            double lx = dx, ly = dy, lz = dz;
            if (QuatWxyz != null && QuatWxyz.Length >= 4)
                RotateByInverse(QuatWxyz, ref lx, ref ly, ref lz);
            const double eps = 1e-6;
            return Math.Abs(lx) <= HalfExtent[0] + eps
                   && Math.Abs(ly) <= HalfExtent[1] + eps
                   && Math.Abs(lz) <= HalfExtent[2] + eps;
        }

        /// <summary>local = R^T d: rotates d by the conjugate quaternion (w, -x, -y, -z).</summary>
        private static void RotateByInverse(double[] q, ref double x, ref double y, ref double z)
        {
            double w = q[0], ux = -q[1], uy = -q[2], uz = -q[3];
            // v' = v + 2w (u x v) + 2 u x (u x v)
            double cx = uy * z - uz * y, cy = uz * x - ux * z, cz = ux * y - uy * x;
            double ccx = uy * cz - uz * cy, ccy = uz * cx - ux * cz, ccz = ux * cy - uy * cx;
            x = x + 2 * w * cx + 2 * ccx;
            y = y + 2 * w * cy + 2 * ccy;
            z = z + 2 * w * cz + 2 * ccz;
        }
    }

    public sealed class AreaLevels
    {
        [JsonProperty("commonMin")] public int? CommonMin;
        [JsonProperty("commonMax")] public int? CommonMax;
        [JsonProperty("commonP10")] public int? CommonP10;
        [JsonProperty("commonP90")] public int? CommonP90;
        [JsonProperty("fieldBossMin")] public int? FieldBossMin;
        [JsonProperty("fieldBossMax")] public int? FieldBossMax;
    }

    public sealed class AreaCounts
    {
        [JsonProperty("common")] public int Common;
        [JsonProperty("fieldBoss")] public int FieldBoss;
        [JsonProperty("randomDungeonBoss")] public int RandomDungeonBoss;
    }

    /// <summary>One vanilla <c>DT_PalSpawnerPlacement</c> row assigned to the area.</summary>
    public sealed class AreaPlacement
    {
        [JsonProperty("spawnerName")] public string SpawnerName;
        [JsonProperty("spawnerType")] public string SpawnerType;
        [JsonProperty("placementType")] public string PlacementType;
        [JsonProperty("tier")] public string Tier;
        [JsonProperty("x")] public double X;
        [JsonProperty("y")] public double Y;
        [JsonProperty("z")] public double Z;
        [JsonProperty("spawnerClass")] public string SpawnerClass;
        [JsonProperty("staticRadius")] public double? StaticRadius;
    }

    /// <summary>A Pal's Paldex habitat points (<c>DT_PaldexDistributionData</c>), from paldex_points.json.</summary>
    public sealed class PaldexPointSet
    {
        [JsonProperty("radius")] public double Radius = 15000.0;
        [JsonProperty("day")] public List<double[]> Day = new List<double[]>();
        [JsonProperty("night")] public List<double[]> Night = new List<double[]>();
    }

    /// <summary>One <c>DT_PalBPClass</c> row: the blueprint class path a Pal id renders with.</summary>
    public sealed class BpClassInfo
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("resolves")] public bool Resolves;
        [JsonProperty("sharedWith")] public List<string> SharedWith = new List<string>();
    }

    /// <summary>
    /// Everything a base Pal already has that a new Pal built from it needs to copy or
    /// point at (from base_links.json). Row keys carry the vanilla spelling.
    /// </summary>
    public sealed class BaseLink
    {
        [JsonProperty("bpClassRow")] public string BpClassRow;
        [JsonProperty("bpClassPath")] public string BpClassPath;
        [JsonProperty("bossRow")] public string BossRow;
        [JsonProperty("bossBpClassRow")] public string BossBpClassRow;
        [JsonProperty("bossBpClassPath")] public string BossBpClassPath;
        [JsonProperty("iconRow")] public string IconRow;
        [JsonProperty("iconPath")] public string IconPath;
        [JsonProperty("partnerIconRow")] public string PartnerIconRow;
        [JsonProperty("partnerParamRow")] public string PartnerParamRow;
        [JsonProperty("bossPartnerParamRow")] public string BossPartnerParamRow;
        [JsonProperty("learnset")] public List<LearnsetEntry> Learnset = new List<LearnsetEntry>();
        [JsonProperty("dropRow")] public string DropRow;
        [JsonProperty("bossDropRow")] public string BossDropRow;
        [JsonProperty("cameraRow")] public string CameraRow;
        [JsonProperty("randomizerRow")] public string RandomizerRow;
        [JsonProperty("paldexRow")] public string PaldexRow;
        [JsonProperty("text")] public Dictionary<string, string> Text = new Dictionary<string, string>();
        [JsonProperty("spawnedInAreas")] public List<string> SpawnedInAreas = new List<string>();
    }

    public sealed class LearnsetEntry
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("wazaId")] public string WazaId;
        [JsonProperty("level")] public int Level;
    }
}
