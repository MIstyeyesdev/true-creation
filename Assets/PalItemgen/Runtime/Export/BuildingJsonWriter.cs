using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalItemgen.Lookup;
using PalItemgen.Model;

namespace PalItemgen.Export
{
    /// <summary>
    /// Writes the PalSchema buildings/ JSON that adds the stations to the game.
    /// Layout follows PalSchema's PalBuildingModLoader (verified against its
    /// source and the shipped Endgame Ore Pits mod): one object per building
    /// id with the master-table fields at the top level, then BuildingData
    /// (DT_BuildObjectDataTable), Assignments (DT_MapObjectAssignData rows)
    /// and Technology (DT_TechnologyRecipeUnlock). Name/Description are
    /// registered by the loader as MAPOBJECT_NAME_/BUILDOBJECT_DESC_ text.
    ///
    /// Every station gets its OWN id and reuses a vanilla blueprint, so the
    /// vanilla building is never replaced and the station can sit in the tech
    /// tree on its own row.
    /// </summary>
    public static class BuildingJsonWriter
    {
        public static string ResourceIconName(StationSpec s) => s.Id.ToLowerInvariant() + "_icon";

        public static JObject Build(ModProject project, GameData data)
        {
            var root = new JObject();
            foreach (var s in project.Stations.Where(st => st.Enabled))
                root[s.Id] = Station(project, s, data);
            return root;
        }

        private static JObject Station(ModProject project, StationSpec s, GameData data)
        {
            // The row names the blueprint that keeps the item's functions; a model of
            // another class is applied at run time by the Lua side (ModelSwap).
            var reuse = data?.Building(s.BlueprintSource(data));
            var bpName = reuse?.Bp ?? ("BP_BuildObject_" + s.ReuseMapObjectId);
            var bpSoft = reuse?.BlueprintSoft ?? $"/Game/Pal/Blueprint/MapObject/BuildObject/{bpName}.{bpName}_C";

            string icon;
            if (!string.IsNullOrEmpty(s.CustomIconPng))
                icon = "$resource/" + ModPackager.SafeName(project.PackageName) + "/" + ResourceIconName(s);
            else if (data != null && data.Icons.TryGetValue(s.IconMapObjectId ?? "", out var ic))
                icon = ic.Path;
            else
                icon = $"/Game/Pal/Texture/BuildObject/PNG/T_icon_buildObject_{s.IconMapObjectId}.T_icon_buildObject_{s.IconMapObjectId}";

            var o = new JObject
            {
                ["Name"] = s.Name,
                ["Description"] = s.Description ?? "",
                ["BlueprintClassName"] = bpName,
                ["BlueprintClassSoft"] = bpSoft,
                ["IconTexture"] = icon,
                ["Hp"] = s.Hp,
                ["Defense"] = s.Defense,
                ["MaterialType"] = s.MaterialType ?? "Wood",
                ["MaterialSubType"] = s.MaterialSubType ?? "Wood",
                ["bBelongToBaseCamp"] = true,
                // Decay outside the base radius. Not copied from the reused blueprint:
                // the ancient buildings carry 8.0, which would make a station crumble in minutes.
                ["DeteriorationDamage"] = 0.12f,
                ["bShowHPGauge"] = true,
            };

            string typeA, typeB, uiDefault;
            switch (s.Kind)
            {
                case StationKind.Manager:
                    typeA = "Pal"; typeB = "Infra_Environment"; uiDefault = "PalManagement";
                    break;
                case StationKind.ConsoleSign:
                    typeA = "Pal"; typeB = "Infra_Environment"; uiDefault = "PalManagement";
                    break;
                case StationKind.Copy:
                    // placeholders: the copied building's own row values replace them below
                    typeA = "Product"; typeB = "Prod_Craft"; uiDefault = "Product_Repair";
                    break;
                default:
                    typeA = "Product"; typeB = "Prod_Craft"; uiDefault = "Product_Repair";
                    break;
            }

            var bd = new JObject
            {
                ["TypeA"] = typeA,
                ["TypeB"] = typeB,
                ["TypeUIDisplay"] = string.IsNullOrEmpty(s.TypeUIDisplay) ? uiDefault : s.TypeUIDisplay,
                ["Rank"] = s.Kind == StationKind.Line ? (reuse?.RankMax ?? 1) : 1,
                ["RequiredBuildWorkAmount"] = s.BuildWorkAmount,
                ["RequiredEnergyType"] = "None",
                ["ConsumeEnergySpeed"] = 0.0f,
            };
            for (var i = 0; i < 4; i++)
            {
                var m = i < s.Materials.Count ? s.Materials[i] : null;
                bd[$"Material{i + 1}_Id"] = m?.ItemId ?? "None";
                bd[$"Material{i + 1}_Count"] = m?.Count ?? 0;
            }
            bd["bIsInstallOnlyHubAround"] = true;
            bd["bIsInstallOnlyOnBase"] = false;
            bd["InstallMaxNumInBaseCamp"] = Math.Max(0, s.LimitPerBase);
            bd["bIsPaintable"] = false;
            o["BuildingData"] = bd;

            var assignments = new JArray();
            if (s.Kind == StationKind.Line)
            {
                // Players may open the crafting menu; base Pals must never work here,
                // the manager clears every order placed at a line station anyway.
                assignments.Add(new JObject
                {
                    ["GenusCategory"] = "None",
                    ["ElementType"] = "None",
                    ["WorkSuitability"] = "Handcraft",
                    ["WorkSuitabilityRank"] = 1,
                    ["bPlayerWorkable"] = s.PlayerWorkable,
                    ["bBaseCampWorkerWorkable"] = false,
                    ["WorkableTribeIDs"] = new JArray(),
                    ["WorkableSizeMin"] = "None",
                    ["WorkableSizeMax"] = "None",
                    ["WorkType"] = "CommonTemp",
                    ["WorkActionType"] = "CommonWork",
                    ["WorkerMaxNum"] = 0,
                    ["AffectSanityValue"] = -0.11f,
                    ["AffectFullStomachValue"] = 1.0f,
                });
            }
            else if ((s.Kind == StationKind.Manager || s.Kind == StationKind.Copy) && data?.Building(s.MirrorId) != null)
            {
                // Exact copy: the original's Pal work rows with every column
                // (or the item's edited rows from the tab).
                assignments = RowFieldCatalog.AssignmentRowsJson(s, data.Building(s.MirrorId));
            }
            else if (reuse != null)
            {
                // Copy the reused building's own assignment rows (most non-bench
                // buildings have none) so the model behaves like the original.
                foreach (var a in reuse.Assignments)
                {
                    assignments.Add(new JObject
                    {
                        ["GenusCategory"] = "None",
                        ["ElementType"] = "None",
                        ["WorkSuitability"] = a.WorkSuitability ?? "None",
                        ["WorkSuitabilityRank"] = a.WorkSuitabilityRank,
                        ["bPlayerWorkable"] = a.PlayerWorkable,
                        ["bBaseCampWorkerWorkable"] = a.BaseCampWorkerWorkable,
                        ["WorkableTribeIDs"] = new JArray(),
                        ["WorkableSizeMin"] = "None",
                        ["WorkableSizeMax"] = "None",
                        ["WorkType"] = a.WorkType ?? "None",
                        ["WorkActionType"] = a.WorkActionType ?? "None",
                        ["WorkerMaxNum"] = a.WorkerMaxNum,
                        ["AffectSanityValue"] = a.AffectSanityValue,
                        ["AffectFullStomachValue"] = a.AffectFullStomachValue,
                    });
                }
            }
            o["Assignments"] = assignments;

            var tech = new JObject
            {
                ["UnlockBuildObjects"] = new JArray(s.Id),
                ["IconName"] = s.Id,
                ["IsBossTechnology"] = s.AncientTech,
                ["Name"] = s.Name,
                ["Description"] = s.Description ?? "",
                ["LevelCap"] = Math.Max(1, s.TechLevel),
                ["Cost"] = Math.Max(0, s.TechCost),
            };
            o["Technology"] = tech;

            // Exact copy: every other field of the copied stand's three rows is
            // written too (PVP HP/defense, burn work, sort id, build exp rate,
            // install rules, tech requirements...), with the tab's edits applied.
            // The values above that the writer fixed (decay, gauge, type A/B,
            // energy, install flags) are replaced by the stand's own here.
            if ((s.Kind == StationKind.Manager || s.Kind == StationKind.Copy) && data != null)
            {
                var mirror = data.Building(s.MirrorId);
                if (mirror != null) RowFieldCatalog.Apply(o, bd, tech, s, mirror);
            }
            return o;
        }

        /// <summary>
        /// PalSchema raw table patch for rows the buildings loader does not
        /// cover: the DT_MapObjectItemProductDataTable row of a copied quarry,
        /// logging site or oil pump (what it produces and how fast). Null when
        /// no item in the package needs one.
        /// </summary>
        public static JObject RawTables(ModProject project, GameData data)
        {
            var table = new JObject();
            foreach (var s in project.Stations.Where(st => st.Enabled && (st.Kind == StationKind.Manager || st.Kind == StationKind.Copy)))
            {
                var mirror = data?.Building(s.MirrorId);
                var row = mirror != null ? RowFieldCatalog.ItemProductRow(s, mirror) : null;
                if (row != null) table[s.Id] = row;
            }
            if (table.Count == 0) return null;
            return new JObject { ["DT_MapObjectItemProductDataTable"] = table };
        }

        public static string ToJson(JObject root) => root.ToString(Formatting.Indented);
    }
}
