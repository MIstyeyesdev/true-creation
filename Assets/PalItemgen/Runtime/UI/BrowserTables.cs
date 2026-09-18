using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PalItemgen.Export;
using PalItemgen.Lookup;
using PalItemgen.Model;

namespace PalItemgen.UI
{
    /// <summary>
    /// Turns the lookup tables into browser rows. Column 0 is always the value
    /// handed back when a row is picked (an id or an enum name).
    /// </summary>
    public static class BrowserDatasets
    {
        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Build-wheel categories with how many vanilla buildings use each and a few examples.</summary>
        public static (List<BrowserColumn>, List<string[]>) ProductionTypes(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Category (TypeUIDisplay)", 200),
                new("Vanilla buildings", 110, numeric: true),
                new("Type A / B seen", 220),
                new("Examples", 520),
            };
            var rows = new List<string[]>();
            foreach (var t in data.Enums.TypeUIDisplay)
            {
                var users = data.Buildings.Where(b => b.Build?.TypeUIDisplay == t).ToList();
                var ab = string.Join(", ", users.Select(u => $"{u.Build.TypeA}/{u.Build.TypeB}").Distinct().Take(4));
                var examples = string.Join(", ", users.Select(u => string.IsNullOrEmpty(u.Name) ? u.MapObjectId : u.Name).Take(6));
                rows.Add(new[] { t, N(users.Count), ab, examples });
            }
            return (columns, rows);
        }

        /// <summary>The Monitoring Stand tiers side by side.</summary>
        public static (List<BrowserColumn>, List<string[]>) StandTiers(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 170),
                new("In-game name", 210),
                new("Tech level", 80, numeric: true),
                new("Tech cost", 75, numeric: true),
                new("HP", 70, numeric: true),
                new("Defense", 65, numeric: true),
                new("Build work", 85, numeric: true),
                new("Build cost", 260),
                new("Static mesh", 200),
                new("Footprint (m)", 120),
            };
            var rows = data.StandTiers().Select(b => new[]
            {
                b.MapObjectId,
                b.Name ?? "",
                b.Tech != null ? N(b.Tech.LevelCap) : "",
                b.Tech != null ? N(b.Tech.Cost) : "",
                b.Master != null ? N(b.Master.Hp) : "",
                b.Master != null ? N(b.Master.Defense) : "",
                b.Build != null ? N(b.Build.RequiredBuildWorkAmount) : "",
                b.Build != null ? string.Join(", ", b.Build.Materials.Select(m => $"{m.Count} {data.Item(m.Id)?.Name ?? m.Id}")) : "",
                b.Mesh ?? "",
                b.Overlap != null ? b.Overlap.Metres : "",
            }).ToList();
            return (columns, rows);
        }

        /// <summary>Models the manager item may pull from: production and storage buildings plus the monitoring stands.</summary>
        public static (List<BrowserColumn>, List<string[]>) Models(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 200),
                new("In-game name", 190),
                new("Type A", 80),
                new("Type B", 120),
                new("Footprint (m)", 120),
                new("Graphic", 230),
                new("Onto the stand", 100),
                new("Model class", 240),
            };
            var rows = data.ModelCandidates().Select(b => new[]
            {
                b.MapObjectId,
                b.Name ?? "",
                b.Build?.TypeA ?? "",
                b.Build?.TypeB ?? "",
                b.Overlap != null ? b.Overlap.Metres : "",
                b.GraphicLabel,
                (b.MapObjectId ?? "").StartsWith("BaseCampWorkHard") ? "stand tier" : (b.Swappable ? "yes" : "no (animated)"),
                b.Model ?? "",
            }).ToList();
            return (columns, rows);
        }

        /// <summary>The Generator tab's originals: every station, producer, storage and Pal building.</summary>
        public static (List<BrowserColumn>, List<string[]>) GeneratorSources(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 190),
                new("In-game name", 190),
                new("Type A", 90),
                new("Category", 140),
                new("Pal work", 230),
                new("Crafts", 220),
                new("Footprint (m)", 110),
                new("Extra row", 90),
                new("Model class", 240),
            };
            var rows = data.GeneratorSources().Select(b => new[]
            {
                b.MapObjectId,
                b.Name ?? "",
                b.Build?.TypeA ?? "",
                b.Build?.TypeUIDisplay ?? "",
                RowFieldCatalog.AssignmentSummary(b),
                b.RankMax.HasValue ? b.TypeSummary : "",
                b.Overlap != null ? b.Overlap.Metres : "",
                b.RawItemProduct != null ? "item product" : "",
                b.Model ?? "",
            }).ToList();
            return (columns, rows);
        }

        /// <summary>Buildings a station of the given kind may reuse.</summary>
        public static (List<BrowserColumn>, List<string[]>) ReuseBuildings(GameData data, StationKind kind)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 200),
                new("In-game name", 190),
                new("Rank", 45, numeric: true),
                new("Items it can make", 110, numeric: true),
                new("Item types", 300),
                new("Footprint (m)", 110),
                new("Model class", 230),
            };
            IEnumerable<ProducerInfo> source = kind switch
            {
                StationKind.ConsoleSign => data.Signboards.Where(s => s.MapObjectId != null),
                StationKind.Line => data.Producers.Where(p => p.MapObjectId != null && p.Buildable),
                _ => data.Buildings,
            };
            var rows = source.Select(b => new[]
            {
                b.MapObjectId,
                b.Name ?? "",
                b.RankMax.HasValue ? N(b.RankMax.Value) : "",
                b.RankMax.HasValue ? N(data.ItemsFor(b).Count()) : "",
                string.Join(", ", b.TypesB.Take(6)) + (b.TypesB.Count > 6 ? ", ..." : ""),
                b.Overlap != null ? b.Overlap.Metres : "",
                b.Model ?? "",
            }).ToList();
            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) Icons(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 220),
                new("In-game name", 200),
                new("Texture", 520),
            };
            var rows = data.Icons.OrderBy(k => k.Value.Name).Select(k => new[] { k.Key, k.Value.Name ?? "", k.Value.Path ?? "" }).ToList();
            return (columns, rows);
        }

        /// <summary>The chain station's true producers: every buildable crafting bench, grouped by family, strongest first.</summary>
        public static (List<BrowserColumn>, List<string[]>) ChainBenches(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 200),
                new("In-game name", 210),
                new("Family", 150),
                new("Rank", 50, numeric: true),
                new("Speed", 60, numeric: true),
                new("Tier", 50, numeric: true),
                new("Spots", 50, numeric: true),
                new("Suitability", 130),
            };
            var rows = ChainCatalog.Producers(data).Select(b => new[]
            {
                b.MapObjectId,
                b.Name ?? "",
                ChainCatalog.Label(ChainCatalog.FamilyOf(b)),
                N(ChainCatalog.RankMax(b)),
                b.WorkSpeed.HasValue ? N(b.WorkSpeed.Value) : "-",
                b.Tech != null ? N(ChainCatalog.Tier(b)) : "-",
                N(ChainCatalog.WorkSpots(data, b.MapObjectId)),
                ChainCatalog.Suitability(b),
            }).ToList();
            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) Items(GameData data, bool craftableOnly)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 220),
                new("In-game name", 180),
                new("#", 50, numeric: true),
                new("Type A", 90),
                new("Type B", 140),
                new("Rank", 45, numeric: true),
                new("Recipe", 300),
                new("Tech", 130),
                new("Benches", 220),
            };
            var source = craftableOnly ? data.CraftableItems() : data.Items;
            var rows = source.Select(i =>
            {
                var r = data.RecipeFor(i.Id);
                var recipe = r == null ? "" : string.Join(" + ", r.Materials.Select(m => $"{m.Count} {data.Item(m.Id)?.Name ?? m.Id}")) + (r.Count > 1 ? $"  (x{r.Count})" : "");
                return new[]
                {
                    i.Id, i.Name ?? "", N(i.SortId), i.TypeA ?? "", i.TypeB ?? "", N(i.Rank), recipe, r?.Tech ?? "",
                    string.Join(", ", i.Benches.Take(4)) + (i.Benches.Count > 4 ? ", ..." : ""),
                };
            }).ToList();
            return (columns, rows);
        }
    }
}
