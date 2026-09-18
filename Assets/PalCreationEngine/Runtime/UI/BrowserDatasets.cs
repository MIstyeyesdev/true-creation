using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.UI
{
    /// <summary>
    /// Turns the lookup tables into browser rows.
    ///
    /// Column 0 is always the internal ID, because that is what the caller gets
    /// back when a row is picked. Column 1 is the display name where one is
    /// known -- it stays present but empty until the English text tables are
    /// exported, so the layout does not shift once they arrive.
    /// </summary>
    public static class BrowserDatasets
    {
        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static (List<BrowserColumn>, List<string[]>) Pals(GameData data, bool includeVariants)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 190),
                new("In-game name", 150),
                new("Paldex", 55, numeric: true),
                new("Sfx", 38),
                new("Size", 45),
                new("Element 1", 80),
                new("Element 2", 80),
                new("Genus", 95),
                new("Rarity", 55, numeric: true),
                new("Kind", 70),
                new("What it does", 420),
            };

            var source = includeVariants
                ? data.Pals.Values.OrderBy(p => p.RowKey)
                : data.BasePals;

            var rows = source.Select(p => new[]
            {
                p.RowKey,
                data.Names.Pals.TryGetValue(p.RowKey, out var name) ? name : "",
                N(p.ZukanIndex),
                p.ZukanIndexSuffix ?? "",
                p.Size ?? "",
                p.Element1 ?? "",
                p.Element2 ?? "",
                p.Genus ?? "",
                N(p.Rarity),
                p.IsVariant ? "variant" : "base",
                data.PalDescription(p.RowKey),
            }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) Items(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 210),
                new("In-game name", 150),
                new("Type", 110),
                new("Sub-type", 110),
                new("Rank", 45, numeric: true),
                new("Rarity", 50, numeric: true),
                new("Price", 60, numeric: true),
                new("Weight", 55, numeric: true),
                new("What it does", 460),
            };

            var rows = data.Items.Values
                .OrderBy(i => i.Id)
                .Select(i => new[]
                {
                    i.Id,
                    data.Names.Items.TryGetValue(i.Id, out var name) ? name : "",
                    i.TypeA ?? "",
                    i.TypeB ?? "",
                    N(i.Rank),
                    N(i.Rarity),
                    N(i.Price),
                    N(i.Weight),
                    data.ItemDescription(i.Id),
                }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) PassiveSkills(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Internal ID", 200),
                new("Rank", 45, numeric: true),
                new("Rolls on", 130),
                new("When", 110),
                new("Reaches", 150),
                new("Player?", 60),
                new("Effects", 340),
            };

            var rows = data.PassiveSkills.Values
                .OrderByDescending(s => s.Rank)
                .ThenBy(s => s.Id)
                .Select(s => new[]
                {
                    s.Id,
                    N(s.Rank),
                    // Trim the Add- prefix; the column header already says "rolls on".
                    string.Join(", ", (s.Scope ?? new List<string>())
                        .Select(f => f.StartsWith("Add") ? f.Substring(3) : f)),
                    string.Join(", ", s.Invoke ?? new List<string>()),
                    string.Join(", ", (s.Targets ?? new List<string>()).Where(t => t != "None")),
                    s.AffectsPlayer ? "yes" : "",
                    s.Effects == null || s.Effects.Count == 0
                        ? ""
                        : string.Join(", ", s.Effects.Select(
                            e => $"{e.Type} {e.Value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)}")),
                }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) BlueprintPaths(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("BPClass", 200),
                new("Confidence", 150),
                new("Object path", 460),
            };

            var rows = data.BlueprintPaths
                .OrderBy(kv => kv.Key)
                .Select(kv => new[]
                {
                    kv.Key,
                    kv.Value.Source ?? "",
                    kv.Value.ObjectPath ?? "(unresolved)",
                }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) Npcs(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("CharacterID", 200),
                new("BPClass", 200),
                new("[F] Talk", 70),
                new("Conversation", 190),
                new("AI Response", 130),
                new("Organization", 130),
            };

            var rows = data.Npc.Npcs.Values
                .OrderBy(n => n.RowKey)
                .Select(n => new[]
                {
                    n.RowKey,
                    n.BpClass ?? "",
                    n.CanTalk ? "yes" : "no",
                    data.Npc.GraphFor(n.RowKey) ?? "",
                    n.AiResponse ?? "",
                    n.Organization ?? "",
                }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) TalkFlows(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Conversation", 240),
                new("Used by", 240),
                new("Asset path", 460),
            };

            // Grouped by graph: one row per conversation, listing who uses it,
            // since the same graph is shared by many NPCs.
            var rows = data.Npc.TalkFlows
                .GroupBy(kv => kv.Value.Graph)
                .OrderBy(g => g.Key)
                .Select(g => new[]
                {
                    g.Key ?? "",
                    g.Count() == 1
                        ? g.First().Key
                        : $"{g.First().Key} (+{g.Count() - 1} more)",
                    g.First().Value.AssetPath ?? "",
                }).ToList();

            return (columns, rows);
        }


        /// <summary>
        /// Shop lotteries: the name that goes on a vendor component, and what it
        /// points at. One lottery can roll between several stock lists by weight,
        /// so a lottery is shown with its groups flattened onto one row.
        /// </summary>
        public static (List<BrowserColumn>, List<string[]>) ShopLotteries(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Lottery name", 190),
                new("Stock lists it rolls", 260),
                new("Weights", 110),
                new("Items", 60, numeric: true),
                new("Currency", 150),
            };

            var rows = new List<string[]>();
            foreach (var kv in data.Npc.ShopLotteries.OrderBy(k => k.Key))
            {
                var groups = kv.Value ?? new List<ShopLotteryEntry>();

                var itemCount = groups.Sum(g =>
                    g.ShopGroupName != null
                    && data.Npc.ShopStock.TryGetValue(g.ShopGroupName, out var stock)
                        ? stock.Count
                        : 0);

                // Alternate currency lives on the setting table keyed by the shop
                // group, which is how the Medal/Bounty/Arena shops take medals.
                var currency = groups
                    .Select(g => g.ShopGroupName != null
                                 && data.Npc.ShopCurrencies.TryGetValue(g.ShopGroupName, out var c)
                        ? c : null)
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c));

                rows.Add(new[]
                {
                    kv.Key,
                    string.Join(", ", groups.Select(g => g.ShopGroupName)),
                    string.Join(", ", groups.Select(g => N((int)g.Weight))),
                    N(itemCount),
                    currency ?? "Gold (default)",
                });
            }

            return (columns, rows);
        }

        /// <summary>
        /// Stock lists: the actual goods. Shown with a preview of what is in each,
        /// so a list can be chosen by its contents rather than its name.
        /// </summary>
        public static (List<BrowserColumn>, List<string[]>) ShopStockLists(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Stock list", 190),
                new("Items", 60, numeric: true),
                new("Used by lottery", 180),
                new("Contents", 420),
            };

            // Which lottery references which stock list -- the reverse of the
            // lottery table, so a stock list can be traced back to its vendor.
            var usedBy = new Dictionary<string, List<string>>();
            foreach (var kv in data.Npc.ShopLotteries)
            foreach (var group in kv.Value ?? new List<ShopLotteryEntry>())
            {
                if (string.IsNullOrEmpty(group.ShopGroupName)) continue;
                if (!usedBy.TryGetValue(group.ShopGroupName, out var list))
                    usedBy[group.ShopGroupName] = list = new List<string>();
                list.Add(kv.Key);
            }

            var rows = new List<string[]>();
            foreach (var kv in data.Npc.ShopStock.OrderBy(k => k.Key))
            {
                var stock = kv.Value ?? new List<ShopStockEntry>();
                var preview = stock.Take(6).Select(e =>
                {
                    var name = data.Names.Items.TryGetValue(e.ItemId ?? "", out var display)
                        ? display : e.ItemId;
                    return e.ProductNum > 1 ? $"{name} x{e.ProductNum}" : name;
                });

                rows.Add(new[]
                {
                    kv.Key,
                    N(stock.Count),
                    usedBy.TryGetValue(kv.Key, out var lotteries)
                        ? string.Join(", ", lotteries)
                        : "(none)",
                    string.Join(", ", preview) + (stock.Count > 6 ? ", ..." : ""),
                });
            }

            return (columns, rows);
        }


        /// <summary>
        /// Funnel companion classes. The class fixes the element and attack, so
        /// it is shown with what it actually fires rather than as a bare name.
        /// </summary>
        public static (List<BrowserColumn>, List<string[]>) FunnelClasses(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Funnel class", 300),
                new("Attack", 190),
                new("Element", 90),
                new("Power", 55, numeric: true),
                new("Cooldown", 70, numeric: true),
                new("Range", 100),
                new("Used by", 220),
            };

            var rows = data.Companions.Classes.Values
                .OrderBy(c => c.Class)
                .Select(c =>
                {
                    var w = c.WazaInfo;
                    return new[]
                    {
                        c.Class,
                        // Blank rather than a guess: several classes inherit their
                        // attack from the base class instead of declaring one.
                        c.Waza ?? "(inherited)",
                        w?.Element ?? "",
                        w?.Power != null ? N(w.Power.Value) : "",
                        w?.CoolTime != null ? N(w.CoolTime.Value) : "",
                        w?.MinRange != null ? $"{w.MinRange}-{w.MaxRange}" : "",
                        string.Join(", ", c.UsedBy),
                    };
                }).ToList();

            return (columns, rows);
        }


        /// <summary>
        /// Paldex slots. Column 0 leads with the number so the caller can parse
        /// it back, and every variant sharing the slot is listed with what its
        /// suffix means there.
        /// </summary>
        public static (List<BrowserColumn>, List<string[]>) PaldexSlots(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Slot", 70, numeric: true),
                new("Held by", 190),
                new("In-game name", 150),
                new("Suffixes used", 120),
                new("Variants at this slot", 400),
            };

            var rows = data.ZukanSlots
                .OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
                .Select(kv => new[]
                {
                    // "97 - Ghangler (GhostAnglerfish)" so the field reads the same
                    // as the inline picker and ParseLeadingInt still works.
                    $"{kv.Key} - {data.LabelForPal(kv.Value.Primary)}",
                    kv.Value.Primary,
                    data.Names.Pals.TryGetValue(kv.Value.Primary, out var n) ? n : "",
                    string.Join(", ", kv.Value.SuffixesInUse.Select(x => x == "" ? "(none)" : x)),
                    string.Join(", ", kv.Value.Entries.Select(e => e.SuffixLabel)),
                }).ToList();

            return (columns, rows);
        }

        public static (List<BrowserColumn>, List<string[]>) SpawnGroups(GameData data)
        {
            var columns = new List<BrowserColumn>
            {
                new("Spawn group", 220),
                new("Placements", 90, numeric: true),
                new("Rows", 55, numeric: true),
                new("Free slots", 80, numeric: true),
                new("Pals here", 400),
            };

            var rows = new List<string[]>();
            foreach (var kv in data.SpawnGroups.OrderBy(k => k.Key))
            {
                var group = kv.Value;
                var pals = group.Rows
                    .SelectMany(r => r.Slots)
                    .Select(s => s.Pal)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .Take(6);

                rows.Add(new[]
                {
                    kv.Key,
                    N(group.PlacementCount),
                    N(group.Rows.Count),
                    N(group.Rows.Sum(r => r.FreeSlots)),
                    string.Join(", ", pals),
                });
            }

            return (columns, rows);
        }
    }
}
