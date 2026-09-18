using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using PalItemgen.Lookup;
using PalItemgen.Model;

namespace PalItemgen.Export
{
    /// <summary>The game tables a building's rows live in. ItemProduct only exists for quarries, logging sites and oil pumps.</summary>
    public enum RowTable { Master, Build, Tech, ItemProduct }

    /// <summary>One raw field of the copied building's game rows that the tab does not edit by name.</summary>
    public sealed class RowField
    {
        public RowTable Table;
        public string Key;
        /// <summary>The original's value, as read from the game tables.</summary>
        public JToken Default;
        public string Id => Table + "." + Key;
        public string DefaultText => RowFieldCatalog.Text(Default);
    }

    /// <summary>
    /// Every field of the game rows (master, build, technology, item product)
    /// of the building an item copies, minus the ones the tab edits by name,
    /// plus the building's Pal work rows (DT_MapObjectAssignData). The package
    /// writer copies them so the copy is exact on paper; the Exact copy and
    /// Generator tabs list them prefilled with the original's values. Edits are
    /// kept as text in StationSpec.RowFields / StationSpec.AssignmentRows.
    /// </summary>
    public static class RowFieldCatalog
    {
        private static readonly HashSet<string> HandledMaster = new HashSet<string>
        {
            "BlueprintClassName", "BlueprintClassSoft", "Hp", "Defense", "MaterialType", "MaterialSubType", "Editor_RowNameHash",
        };

        private static readonly HashSet<string> HandledBuild = new HashSet<string>
        {
            "MapObjectId", "TypeUIDisplay", "RequiredBuildWorkAmount", "InstallMaxNumInBaseCamp",
            "Material1_Id", "Material1_Count", "Material2_Id", "Material2_Count",
            "Material3_Id", "Material3_Count", "Material4_Id", "Material4_Count",
        };

        private static readonly HashSet<string> HandledTech = new HashSet<string>
        {
            "UnlockBuildObjects", "Name", "Description", "IconName", "IsBossTechnology", "LevelCap", "Cost",
        };

        private static readonly HashSet<string> HandledItemProduct = new HashSet<string> { "Editor_RowNameHash" };

        /// <summary>The 22 columns of a DT_MapObjectAssignData row, in table order.</summary>
        public static readonly string[] AssignmentKeys =
        {
            "GenusCategory", "ElementType", "WorkSuitability", "WorkSuitabilityRank", "bPlayerWorkable", "bBaseCampWorkerWorkable",
            "WorkableTribeIDs", "WorkableSizeMin", "WorkableSizeMax", "WorkType", "WorkActionType", "WorkerMaxNum",
            "AffectSanityValue", "AffectFullStomachValue",
            "MultiWorkSuitability1", "MultiWorkType1", "MultiWorkActionType1", "MultiRequiredRank1",
            "MultiWorkSuitability2", "MultiWorkType2", "MultiWorkActionType2", "MultiRequiredRank2",
        };

        public static string TableTitle(RowTable t) => t switch
        {
            RowTable.Master => "Master row",
            RowTable.Build => "Build row",
            RowTable.Tech => "Technology row",
            _ => "Item product row (what it mines / how fast)",
        };

        /// <summary>The fields in table order. Struct-valued fields (soft paths) are skipped.</summary>
        public static List<RowField> List(ProducerInfo mirror)
        {
            var list = new List<RowField>();
            if (mirror == null) return list;
            Collect(list, RowTable.Master, mirror.RawMaster, HandledMaster);
            Collect(list, RowTable.Build, mirror.RawBuild, HandledBuild);
            Collect(list, RowTable.Tech, mirror.RawTech, HandledTech);
            Collect(list, RowTable.ItemProduct, mirror.RawItemProduct, HandledItemProduct);
            return list;
        }

        private static void Collect(List<RowField> list, RowTable table, JObject raw, HashSet<string> handled)
        {
            if (raw == null) return;
            foreach (var p in raw.Properties())
            {
                if (handled.Contains(p.Name)) continue;
                if (p.Value.Type == JTokenType.Object) continue;
                list.Add(new RowField { Table = table, Key = p.Name, Default = p.Value });
            }
        }

        /// <summary>Value the package writes: the item's edit when there is one, else the original's value.</summary>
        public static JToken Resolve(StationSpec s, RowField f)
        {
            if (s?.RowFields != null && s.RowFields.TryGetValue(f.Id, out var text) && text != null)
                return Parse(text, f.Default);
            return Normalise(f.Default);
        }

        /// <summary>Copies every listed master / build / technology field of the original into the item's JSON objects.</summary>
        public static void Apply(JObject master, JObject build, JObject tech, StationSpec s, ProducerInfo mirror)
        {
            foreach (var f in List(mirror))
            {
                JObject target;
                switch (f.Table)
                {
                    case RowTable.Master: target = master; break;
                    case RowTable.Build: target = build; break;
                    case RowTable.Tech: target = tech; break;
                    default: target = null; break;   // item product rows go to the raw table patch
                }
                if (target == null) continue;
                target[f.Key] = Resolve(s, f);
            }
        }

        /// <summary>
        /// The DT_MapObjectItemProductDataTable row of the copy (a quarry, logging
        /// site or oil pump), for the PalSchema raw table patch. Null when the
        /// original has no such row.
        /// </summary>
        public static JObject ItemProductRow(StationSpec s, ProducerInfo mirror)
        {
            if (mirror?.RawItemProduct == null) return null;
            var row = new JObject();
            foreach (var f in List(mirror).Where(f => f.Table == RowTable.ItemProduct))
                row[f.Key] = Resolve(s, f);
            return row;
        }


        // ------------------------------------------------------------- what each raw field does

        /// <summary>
        /// Plain-English note for a raw row field, shown next to its value on the
        /// Exact copy and Generator tabs. Written from the field names, the values
        /// across all 493 buildings and how the game uses them; "" when unknown.
        /// </summary>
        public static string Describe(RowTable table, string key)
        {
            var map = table switch
            {
                RowTable.Master => MasterNotes,
                RowTable.Build => BuildNotes,
                RowTable.Tech => TechNotes,
                _ => ItemProductNotes,
            };
            return key != null && map.TryGetValue(key, out var s) ? s : "";
        }

        /// <summary>Plain-English note for one column of a Pal work row (DT_MapObjectAssignData).</summary>
        public static string DescribeAssignment(string key) =>
            key != null && AssignmentNotes.TryGetValue(key, out var s) ? s : "";

        private static readonly Dictionary<string, string> MasterNotes = new Dictionary<string, string>
        {
            ["OverrideNameMsgID"] = "text id used as the building's name instead of the usual MAPOBJECT_NAME_<id>; None = the usual one",
            ["bCollectionObject"] = "true = a pick-up object (a dropped resource, a chest to loot), not a placed building; false for every building",
            ["Hp_PVP"] = "hit points used on PvP servers instead of HP",
            ["Defense_PVP"] = "defense used on PvP servers instead of Defense",
            ["bBelongToBaseCamp"] = "true = counts as part of the base it stands in (base range, Pal work, raids, decay rules); false = a free-standing world object",
            ["DistributeExpAroundPlayer"] = "experience handed to players nearby when it is destroyed or collected; 0 = none",
            ["DeteriorationDamage"] = "damage it takes per decay tick when it deteriorates (outside a base, after a base is removed); 0 = never decays",
            ["ExtinguishBurnWorkAmount"] = "work a Pal must do to put it out when it is burning; 0 = it does not catch fire / no extinguish job",
            ["bShowHPGauge"] = "show the hit-point bar over it when damaged",
            ["bInDevelop"] = "Pocketpair's own in-development marker on the row; no known effect in the shipped game, copied as is",
        };

        private static readonly Dictionary<string, string> BuildNotes = new Dictionary<string, string>
        {
            ["TypeA"] = "top-level build menu group (Pal, Product, Storage, Infrastructure, Defense, Food ...)",
            ["SortId"] = "position in the build wheel inside its group (lower = earlier); not tied to the blueprint",
            ["TypeB"] = "sub-group inside TypeA (Prod_Craft, Infra_Environment, Storage_Item ...); decides the wheel's inner ring",
            ["Rank"] = "the building's rank within its type (1 = basic tier); used to order upgrades of the same kind",
            ["BuildCapacity"] = "a capacity number the game reads for some building types (0 for most); copied as is",
            ["AssetValue"] = "how much it adds to the base's total asset value (1 = an ordinary building)",
            ["RequiredEnergyType"] = "power it needs to run: None, or Electricity (a generator in the base); the blueprint decides whether it has a power component",
            ["ConsumeEnergySpeed"] = "power drawn per second while it runs; 0 = draws nothing",
            ["BlueprintItemID"] = "blueprint item the player must own to build it (schematic-style unlocks); None = unlocked through the tech tree only",
            ["OverrideDescMsgID"] = "text id used as the description instead of the usual BUILDOBJECT_DESC_<id>; None = the usual one",
            ["bInstallAtReticle"] = "placed where the reticle points (walls, hanging fixtures) instead of on the ground grid",
            ["InstallNeighborThreshold"] = "snapping distance to neighbouring objects when placing; 0 = the default rule",
            ["bIsInstallOnlyOnBase"] = "can only be built inside a base's range",
            ["bIsInstallOnlyInDoor"] = "can only be built indoors (under a roof)",
            ["bIsInstallOnlyHubAround"] = "can only be built close to the Palbox; the 'Palbox only' line above shows the same value",
            ["bInstallableNoObstacleFromCamera"] = "may be placed even when something stands between the camera and the spot",
            ["BuildExpRate"] = "multiplier on the experience the player gets for building it",
            ["bIsProhibitedInRaidBossArea"] = "cannot be built inside a raid boss arena",
            ["MaxBuildCountInRaidBossArea"] = "how many may stand inside a raid boss arena; 0 = no special limit",
            ["bIsPaintable"] = "can be recoloured with the paint tool",
        };

        private static readonly Dictionary<string, string> TechNotes = new Dictionary<string, string>
        {
            ["UnlockItemRecipes"] = "item recipes this technology unlocks as well (usually none for a building)",
            ["RequireDefeatTowerBoss"] = "tower boss that must be beaten before it can be researched; None = no boss needed",
            ["RequireTechnology"] = "another technology that must be learned first; None = none",
            ["RequireResearchId"] = "Pal Laboratory research that must be finished first; None = none",
            ["Tier"] = "tier the technology tree files it under; 0 = the regular ladder",
        };

        private static readonly Dictionary<string, string> ItemProductNotes = new Dictionary<string, string>
        {
            ["Product_Id"] = "the item the site produces (ore, coal, wood, oil)",
            ["RequiredWorkAmount"] = "work Pals or the player must put in per item produced",
            ["AutoWorkAmountBySec"] = "work the site does by itself every second with nobody at it; 0 = produces only when worked",
        };

        private static readonly Dictionary<string, string> AssignmentNotes = new Dictionary<string, string>
        {
            ["GenusCategory"] = "Pal genus allowed to work here (Humanoid, Dragon, ...); None = any",
            ["ElementType"] = "element the Pal must have to work here; None = any",
            ["WorkSuitability"] = "the work suitability the job asks for (Handcraft = Handiwork, EmitFlame = Kindling, Watering, ...); None = any Pal",
            ["WorkSuitabilityRank"] = "minimum level of that suitability (1 to 5)",
            ["bPlayerWorkable"] = "the player can do this work by hand",
            ["bBaseCampWorkerWorkable"] = "base Pals may take this work",
            ["WorkableTribeIDs"] = "only these Pal species may work here; empty = any",
            ["WorkableSizeMin"] = "smallest Pal size allowed (XS ... XL); None = any",
            ["WorkableSizeMax"] = "largest Pal size allowed; None = any",
            ["WorkType"] = "work category the assignment system files the job under; drives which Pals want it and how they behave at it",
            ["WorkActionType"] = "the action the Pal performs at the spot (hammering, kindling, carrying ...), i.e. its animation",
            ["WorkerMaxNum"] = "how many Pals may work this row at once; 0 = the blueprint's work spots decide",
            ["AffectSanityValue"] = "sanity change per second while a Pal does this work (negative = it drains)",
            ["AffectFullStomachValue"] = "hunger rate multiplier while working (1 = normal)",
            ["MultiWorkSuitability1"] = "a second suitability that also qualifies a Pal (the Ancient Workbench takes Handiwork 6 or Medicine 6); None = not used",
            ["MultiWorkType1"] = "work category used when a Pal qualifies through the second suitability",
            ["MultiWorkActionType1"] = "action (animation) used when a Pal qualifies through the second suitability",
            ["MultiRequiredRank1"] = "minimum level of the second suitability",
            ["MultiWorkSuitability2"] = "a third suitability that also qualifies a Pal; None = not used",
            ["MultiWorkType2"] = "work category used when a Pal qualifies through the third suitability",
            ["MultiWorkActionType2"] = "action (animation) used when a Pal qualifies through the third suitability",
            ["MultiRequiredRank2"] = "minimum level of the third suitability",
        };

        // ------------------------------------------------------------- Pal work rows (DT_MapObjectAssignData)

        /// <summary>A plain Handiwork row, the shape every row has; used when the original has no rows to copy from.</summary>
        public static JObject DefaultAssignmentRow() => new JObject
        {
            ["GenusCategory"] = "None",
            ["ElementType"] = "None",
            ["WorkSuitability"] = "Handcraft",
            ["WorkSuitabilityRank"] = 1,
            ["bPlayerWorkable"] = true,
            ["bBaseCampWorkerWorkable"] = true,
            ["WorkableTribeIDs"] = new JArray(),
            ["WorkableSizeMin"] = "None",
            ["WorkableSizeMax"] = "None",
            ["WorkType"] = "CommonTemp",
            ["WorkActionType"] = "CommonWork",
            ["WorkerMaxNum"] = 0,
            ["AffectSanityValue"] = -0.11,
            ["AffectFullStomachValue"] = 1.0,
            ["MultiWorkSuitability1"] = "None",
            ["MultiWorkType1"] = "None",
            ["MultiWorkActionType1"] = "None",
            ["MultiRequiredRank1"] = 1,
            ["MultiWorkSuitability2"] = "None",
            ["MultiWorkType2"] = "None",
            ["MultiWorkActionType2"] = "None",
            ["MultiRequiredRank2"] = 1,
        };

        /// <summary>The row whose value types the text edits are parsed with: the original's first row, else the default row.</summary>
        public static JObject AssignmentTemplate(ProducerInfo mirror)
        {
            var first = mirror?.RawAssignments != null && mirror.RawAssignments.Count > 0 ? mirror.RawAssignments[0] : null;
            return first != null ? (JObject)first.DeepClone() : DefaultAssignmentRow();
        }

        /// <summary>True while the item simply copies the original's rows (nothing edited, added or removed).</summary>
        public static bool AssignmentsFollowOriginal(StationSpec s) => s?.AssignmentRows == null;

        /// <summary>The original's rows as text, the form the tab shows and the item stores.</summary>
        public static List<Dictionary<string, string>> OriginalAssignmentRowsText(ProducerInfo mirror)
        {
            var list = new List<Dictionary<string, string>>();
            foreach (var raw in mirror?.RawAssignments ?? new List<JObject>())
                list.Add(RowText(raw));
            return list;
        }

        private static Dictionary<string, string> RowText(JObject raw)
        {
            var d = new Dictionary<string, string>();
            foreach (var key in AssignmentKeys) d[key] = Text(raw?[key]);
            if (raw != null)
                foreach (var p in raw.Properties())
                    if (!d.ContainsKey(p.Name) && p.Name != "Editor_RowNameHash") d[p.Name] = Text(p.Value);
            return d;
        }

        /// <summary>The rows the tab shows: the item's own rows once edited, else the original's.</summary>
        public static List<Dictionary<string, string>> AssignmentRowsText(StationSpec s, ProducerInfo mirror) =>
            s?.AssignmentRows != null ? s.AssignmentRows : OriginalAssignmentRowsText(mirror);

        /// <summary>Copies the original's rows into the item (as text) so they can be edited, added to or removed.</summary>
        public static void MaterialiseAssignments(StationSpec s, ProducerInfo mirror)
        {
            if (s == null || s.AssignmentRows != null) return;
            s.AssignmentRows = OriginalAssignmentRowsText(mirror);
        }

        /// <summary>A new row for "+ row": a copy of the last row, or the default Handiwork row.</summary>
        public static Dictionary<string, string> NewAssignmentRowText(StationSpec s, ProducerInfo mirror)
        {
            var rows = AssignmentRowsText(s, mirror);
            if (rows.Count > 0) return new Dictionary<string, string>(rows[rows.Count - 1]);
            return RowText(DefaultAssignmentRow());
        }

        /// <summary>The Assignments array the package writes for a copy: the original's rows, or the item's edited rows.</summary>
        public static JArray AssignmentRowsJson(StationSpec s, ProducerInfo mirror)
        {
            var arr = new JArray();
            if (s?.AssignmentRows == null)
            {
                foreach (var raw in mirror?.RawAssignments ?? new List<JObject>())
                {
                    var row = new JObject();
                    foreach (var p in raw.Properties())
                        if (p.Name != "Editor_RowNameHash") row[p.Name] = Normalise(p.Value);
                    arr.Add(row);
                }
                return arr;
            }
            var template = AssignmentTemplate(mirror);
            foreach (var edited in s.AssignmentRows)
            {
                var row = new JObject();
                foreach (var p in template.Properties())
                {
                    if (p.Name == "Editor_RowNameHash") continue;
                    row[p.Name] = edited != null && edited.TryGetValue(p.Name, out var text) ? Parse(text, p.Value) : Normalise(p.Value);
                }
                arr.Add(row);
            }
            return arr;
        }

        /// <summary>"Handcraft rank 1, up to 4 Pals" style summary of a building's Pal work rows.</summary>
        public static string AssignmentSummary(ProducerInfo b)
        {
            var rows = b?.RawAssignments;
            if (rows == null || rows.Count == 0) return "none (no Pal works here)";
            var parts = rows.Take(4).Select(r =>
            {
                var suit = Text(r["WorkSuitability"]);
                var rank = Text(r["WorkSuitabilityRank"]);
                var max = Text(r["WorkerMaxNum"]);
                return $"{suit} rank {rank}{(max != "0" && max.Length > 0 ? ", max " + max : "")}";
            });
            var text = string.Join("; ", parts);
            if (rows.Count > 4) text += $"; ... ({rows.Count} rows)";
            return text;
        }

        // ------------------------------------------------------------- text <-> JSON

        /// <summary>Text shown in the tab: enum values without their type prefix, lists comma separated.</summary>
        public static string Text(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return "";
            switch (t.Type)
            {
                case JTokenType.String: return StripEnum(t.Value<string>());
                case JTokenType.Boolean: return t.Value<bool>() ? "true" : "false";
                case JTokenType.Integer: return t.Value<long>().ToString(CultureInfo.InvariantCulture);
                case JTokenType.Float: return t.Value<double>().ToString("0.####", CultureInfo.InvariantCulture);
                case JTokenType.Array: return string.Join(", ", t.Children().Select(Text));
                default: return t.ToString();
            }
        }

        /// <summary>Parses tab text back into the type of the original's value.</summary>
        public static JToken Parse(string text, JToken template)
        {
            text = (text ?? "").Trim();
            var type = template?.Type ?? JTokenType.String;
            switch (type)
            {
                case JTokenType.Boolean:
                    var low = text.ToLowerInvariant();
                    return new JValue(low == "true" || low == "yes" || low == "1" || low == "on");
                case JTokenType.Integer:
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? new JValue(l) : Normalise(template);
                case JTokenType.Float:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? new JValue(d) : Normalise(template);
                case JTokenType.Array:
                    return new JArray(text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => (JToken)new JValue(x)));
                default:
                    return new JValue(text.Length == 0 ? "None" : text);
            }
        }

        /// <summary>The original's value in the form PalSchema reads: enum names without the type prefix.</summary>
        public static JToken Normalise(JToken t)
        {
            if (t == null) return JValue.CreateNull();
            switch (t.Type)
            {
                case JTokenType.String: return new JValue(StripEnum(t.Value<string>()));
                case JTokenType.Array: return new JArray(t.Children().Select(Normalise));
                default: return t.DeepClone();
            }
        }

        private static string StripEnum(string v)
        {
            if (string.IsNullOrEmpty(v)) return v ?? "";
            var i = v.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? v.Substring(i + 2) : v;
        }
    }
}
