using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RingOfNoXp.Lookup;
using RingOfNoXp.Model;

namespace RingOfNoXp.Export
{
    /// <summary>
    /// Writes the mod package. Layout follows the one verified on disk for
    /// PalProductionManager: Info.json at the package root, PalSchema JSON under
    /// PalSchema/ (the installer adds the per-package folder), Lua under Scripts/.
    /// </summary>
    public static class PalSchemaWriter
    {
        /// <summary>Writes the package and returns the files it created, relative to the package folder.</summary>
        public static List<string> Write(RingProject project, out string packageFolder, GameData data = null)
        {
            var root = string.IsNullOrWhiteSpace(project.OutputFolder)
                ? UI.DataPaths.DefaultOutputFolder
                : project.OutputFolder;
            packageFolder = Path.Combine(root, project.PackageName);
            var written = new List<string>();

            // Clear the folders we own before writing. Without this a file that is
            // no longer generated (a renamed json, a dropped translations folder)
            // stays in the package and gets faithfully installed into the game.
            foreach (var stale in new[] { "PalSchema", "Scripts" })
            {
                var dir = Path.Combine(packageFolder, stale);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }

            Directory.CreateDirectory(packageFolder);
            WriteFile(packageFolder, "Info.json", InfoJson(project), written);

            // New items go through the typed items/ loader, which is what actually
            // creates the item (it constructs the right PalStatic*ItemData class).
            // raw/ is documented to add rows, but a row alone is not a registered
            // item - the first build wrote items through raw/ and they never
            // appeared in game.
            WriteFile(packageFolder, Path.Combine("PalSchema", "items", project.PackageName + ".json"), ItemsJson(project), written);

            // Tables with no typed loader stay in raw/.
            var raw = RawJson(project);
            if (raw != null)
                WriteFile(packageFolder, Path.Combine("PalSchema", "raw", project.PackageName + ".json"), raw, written);

            // Station patches: PalSchema's blueprints/ loader, keyed by class name
            // with components addressed bare - the shape SmallerPlantations uses.
            var bp = BlueprintsJson(project, data);
            if (bp != null)
                WriteFile(packageFolder, Path.Combine("PalSchema", "blueprints", project.PackageName + ".json"), bp, written);

            if (project.Equip.Enabled && project.Equip.Method != PlayerXpMethod.PalXpOnlyDataOnly)
                WriteFile(packageFolder, Path.Combine("Scripts", "main.lua"), Lua(project), written);

            WriteFile(packageFolder, "README.txt", Readme(project), written);
            return written;
        }

        private static void WriteFile(string root, string relative, string contents, List<string> written)
        {
            var full = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? root);
            File.WriteAllText(full, contents, new UTF8Encoding(false));
            written.Add(relative);
        }

        // ------------------------------------------------------------- Info.json

        private static string InfoJson(RingProject p)
        {
            var b = new JsonBuilder();
            b.Open();
            b.Str("ModName", p.ModName);
            b.Str("PackageName", p.PackageName);
            b.Str("Version", p.Version);
            b.Str("Author", p.Author);
            b.Bool("DebugMode", false);
            b.Raw("Dependencies", "[\n    \"PalSchema\"\n  ]");
            b.Raw("Tags", "[\n    \"PalSchema\",\n    \"Gameplay\"\n  ]");

            var rules = new StringBuilder();
            rules.Append("[\n");
            var needLua = p.Equip.Enabled && p.Equip.Method != PlayerXpMethod.PalXpOnlyDataOnly;
            var entries = new List<string>();
            entries.Add(Rule("PalSchema", "./PalSchema/", false));
            if (needLua) entries.Add(Rule("Lua", "./Scripts", false));
            if (p.AlsoDedicatedServer)
            {
                entries.Add(Rule("PalSchema", "./PalSchema/", true));
                if (needLua) entries.Add(Rule("Lua", "./Scripts", true));
            }
            rules.Append(string.Join(",\n", entries));
            rules.Append("\n  ]");
            b.Raw("InstallRule", rules.ToString());
            b.Close();
            return b.ToString();
        }

        private static string Rule(string type, string target, bool server)
        {
            var s = new StringBuilder();
            s.Append("    {\n      \"Type\": \"").Append(type).Append("\"");
            if (server) s.Append(",\n      \"IsServer\": true");
            s.Append(",\n      \"Targets\": [\n        \"").Append(target).Append("\"\n      ]\n    }");
            return s.ToString();
        }

        // ------------------------------------------------------------ items/

        /// <summary>Fallback icons, so a new item never ships pointing at nothing.</summary>
        private const string DefaultRingIcon = "/Game/Others/InventoryItemIcon/Texture/T_itemicon_Accessory_Nonkilling.T_itemicon_Accessory_Nonkilling";
        private const string DefaultPotionIcon = "/Game/Others/InventoryItemIcon/Texture/T_itemicon_Food_Potion_Low.T_itemicon_Food_Potion_Low";

        /// <summary>
        /// The typed items/ loader, in the shape the PalSchema guide documents:
        /// keyed by item id, Name/Description inline, Type naming the item class
        /// PalSchema builds, TypeA/TypeB as BARE enum member names (no EPal
        /// prefix - that form belongs to raw/), and the recipe nested under Recipe.
        /// Note SortID, not SortId: the loader spells that one key differently
        /// from the row struct.
        /// </summary>
        private static string ItemsJson(RingProject p)
        {
            var rows = new List<string>();
            if (p.Consumable.Enabled) rows.Add(ConsumableItem(p.Consumable));
            if (p.Equip.Enabled) rows.Add(EquipItem(p.Equip));

            var b = new StringBuilder();
            b.Append("{\n");
            b.Append(string.Join(",\n", rows));
            b.Append("\n}\n");
            return b.ToString();
        }

        private static List<string> CommonItemFields(ItemCore item, string palSchemaType, string defaultIcon)
        {
            return new List<string>
            {
                Field("Name", item.Name),
                Field("Description", item.Description),
                // "Consumable", "Weapon" or "Armor" - the only three this PalSchema
                // build accepts. Armor covers accessories (PalStaticArmorItemData).
                Field("Type", palSchemaType),
                Field("IconTexture", string.IsNullOrEmpty(item.IconPath) ? defaultIcon : item.IconPath),
                Field("TypeA", item.TypeA),
                Field("TypeB", item.TypeB),
                Field("Rank", item.Rank),
                Field("Rarity", item.Rarity),
                Field("Price", item.Price),
                Field("MaxStackCount", item.MaxStack),
                Field("SortID", item.SortId),
                Field("Weight", item.Weight)
            };
        }

        private static string ConsumableItem(ConsumableSpec c)
        {
            var f = CommonItemFields(c.Item, "Consumable", DefaultPotionIcon);
            f.Add(Field("RestoreSatiety", c.RestoreSatiety));
            f.Add(Field("RestoreHP", c.RestoreHealth));
            f.Add(Field("RestoreSanity", c.RestoreSanity));
            f.Add(Field("CorruptionFactor", 0f));
            if (c.Recipe.Enabled) f.Add(RecipeBlock(c.Recipe));
            return Row(c.Item.Id, f);
        }

        private static string EquipItem(EquipSpec e)
        {
            var f = CommonItemFields(e.Item, "Armor", DefaultRingIcon);
            // "PassiveSkill", NOT "PassiveSkillName". The items/ loader reflects keys
            // onto the PalStaticArmorItemData object in DA_StaticItemDataAsset, and
            // that object spells it PassiveSkill - vanilla Accessory_Nonkilling carries
            // "PassiveSkill": "NonKilling" there. PassiveSkillName is the
            // DT_ItemDataTable row-struct spelling and is not a property of the
            // loader's target: across the shipped asset PassiveSkill appears 560 times
            // and PassiveSkillName zero. Using the row spelling made PalSchema log
            //   [warning] Property 'PassiveSkillName' not found in Item 'Accessory_NoXpRing'
            // and build the ring with no passive at all.
            f.Add(Field("PassiveSkill", e.PassiveId));
            if (e.Recipe.Enabled) f.Add(RecipeBlock(e.Recipe));
            return Row(e.Item.Id, f);
        }

        /// <summary>The nested Recipe object. Only filled material slots are written.</summary>
        private static string RecipeBlock(RecipeSpec r)
        {
            r.Normalise();
            var f = new List<string>
            {
                Field("Product_Count", r.ProductCount),
                Field("WorkAmount", r.WorkAmount)
            };
            for (var i = 0; i < 5; i++)
            {
                if (string.IsNullOrEmpty(r.MaterialIds[i]) || r.MaterialCounts[i] <= 0) continue;
                f.Add(Field($"Material{i + 1}_Id", r.MaterialIds[i]));
                f.Add(Field($"Material{i + 1}_Count", r.MaterialCounts[i]));
            }
            var b = new StringBuilder();
            b.Append(Quote("Recipe")).Append(": {\n");
            b.Append(string.Join(",\n", f.ConvertAll(x => "        " + x)));
            b.Append("\n      }");
            return b.ToString();
        }

        // ------------------------------------------------------------- raw tables

        /// <summary>
        /// The tables with no typed loader of their own, in the shape raw mods use:
        /// { "DT_Table": { "RowKey": { fields } } }. Returns null when there is
        /// nothing to write.
        /// </summary>
        private static string RawJson(RingProject p)
        {
            var food = new List<string>();
            var passives = new List<string>();

            if (p.Consumable.Enabled) food.Add(FoodRow(p.Consumable));
            var techs = new List<string>();
            if (p.Consumable.Enabled && p.Consumable.TechUnlock) techs.Add(TechRow(p.Consumable));
            // The item's pointer at this passive is written by items/ as "PassiveSkill";
            // patching DT_ItemDataTable from raw/ does NOT work - raw runs a whole pass
            // before items/, so it only created a stub row, and the game reads the
            // passive off the PalStaticArmorItemData object anyway. Tested in game:
            // pal XP was identical with and without the ring.
            if (p.Equip.Enabled) passives.Add(PassiveRow(p.Equip));

            if (food.Count == 0 && passives.Count == 0 && techs.Count == 0) return null;

            var b = new JsonBuilder();
            b.Open();
            if (food.Count > 0) b.Table("DT_StatusEffectFood", food);
            if (techs.Count > 0) b.Table("DT_TechnologyRecipeUnlock", techs);
            if (passives.Count > 0) b.Table("DT_PassiveSkill_Main", passives);
            b.Close();
            return b.ToString();
        }

        private static string FoodRow(ConsumableSpec c)
        {
            var f = new List<string>
            {
                Field("EffectTime", c.EffectTime),
                Field("EffectType1", "EPalFoodStatusEffectType::" + c.Effect1Type),
                Field("EffectValue1", c.Effect1Value),
                Field("Interaval1", c.Effect1Interval),
                Field("EffectType2", "EPalFoodStatusEffectType::" + c.Effect2Type),
                Field("EffectValue2", c.Effect2Value),
                Field("Interaval2", c.Effect2Interval)
            };
            // "Interaval" is the game's own spelling; do not correct it.
            return Row(c.Item.Id, f);
        }

        private static string PassiveRow(EquipSpec e)
        {
            var f = new List<string>
            {
                Field("Rank", e.PassiveRank),
                Field("LotteryWeight", e.LotteryWeight),
                Field("OverrideDescMsgID", string.IsNullOrEmpty(e.OverrideDescMsgID) ? "None" : e.OverrideDescMsgID),
                Field("TargetElementType", "EPalElementType::None"),
                Field("EffectType1", "EPalPassiveSkillEffectType::" + e.Effect1Type),
                Field("EffectValue1", e.Effect1Value),
                Field("TargetType1", "EPalPassiveSkillEffectTargetType::" + e.Target1),
                Field("EffectType2", "EPalPassiveSkillEffectType::" + e.Effect2Type),
                Field("EffectValue2", e.Effect2Value),
                Field("TargetType2", "EPalPassiveSkillEffectTargetType::" + e.Target2),
                Field("InvokeActiveOtomo", e.InvokeActiveOtomo),
                Field("InvokeWorker", e.InvokeWorker),
                Field("InvokeRiding", e.InvokeRiding),
                Field("InvokeReserve", e.InvokeReserve),
                Field("InvokeInOtomo", e.InvokeInOtomo),
                Field("InvokeAlways", e.InvokeAlways),
                Field("InvokeInBaseCamp", e.InvokeInBaseCamp),
                Field("Category", "EPalPassiveCategory::" + e.Category)
            };
            return Row(e.PassiveId, f);
        }

        // The translations/ folder is no longer written: Name and Description sit
        // inline in items/, which the PalSchema guide shows and which overrides the
        // localisation entry anyway. One fewer moving part.

        /// <summary>
        /// One DT_TechnologyRecipeUnlock row, shaped like the shipped
        /// Product_Axe_Grade_01. Name/Description are message IDs in this table,
        /// not literal text. Cost 0 at LevelCap 1 means it costs no technology
        /// points and is available immediately.
        /// </summary>
        private static string TechRow(ConsumableSpec c)
        {
            var f = new List<string>
            {
                Field("UnlockItemRecipes", "[\"" + c.Item.Id + "\"]", raw: true),
                Field("Name", "NAME_RECIPE_" + c.Item.Id),
                Field("Description", "DESC_RECIPE_" + c.Item.Id),
                Field("IconName", c.Item.Id),
                Field("RequireDefeatTowerBoss", "EPalBossType::None"),
                Field("RequireTechnology", "None"),
                Field("RequireResearchId", "None"),
                Field("LevelCap", c.TechLevel),
                Field("Cost", c.TechCost)
            };
            return Row(c.Item.Id, f);
        }

        /// <summary>
        /// Station patches. A station crafts an item only when the item's TypeA is
        /// in TargetTypesA AND its TypeB is in TargetTypesB - the lists are checked
        /// independently. PalSchema SETS properties rather than appending, so each
        /// array is written out in full: the station's shipped contents plus
        /// whichever of our two types was missing.
        /// Returns null when every ticked station already accepts the item.
        /// </summary>
        private static string BlueprintsJson(RingProject p, GameData data)
        {
            if (data == null || !p.Consumable.Enabled) return null;
            var item = p.Consumable.Item;
            var rows = new List<string>();

            foreach (var bp in p.Consumable.Stations)
            {
                var st = data.Station(bp);
                if (st == null) continue;
                if (st.Accepts(item.TypeA, item.TypeB)) continue;   // nothing to do

                var a = new List<string>(st.typesA ?? new List<string>());
                var bList = new List<string>(st.typesB ?? new List<string>());
                if (!a.Contains(item.TypeA)) a.Add(item.TypeA);
                if (!bList.Contains(item.TypeB)) bList.Add(item.TypeB);

                var body = new StringBuilder();
                body.Append("    ").Append(Quote(bp + "_C")).Append(": {\n");
                body.Append("      ").Append(Quote("ItemConverterParameter")).Append(": {\n");
                body.Append("        ").Append(EnumArray("TargetTypesA", "EPalItemTypeA", a)).Append(",\n");
                body.Append("        ").Append(EnumArray("TargetTypesB", "EPalItemTypeB", bList)).Append("\n");
                body.Append("      }\n");
                body.Append("    }");
                rows.Add(body.ToString());
            }

            if (rows.Count == 0) return null;
            var sb = new StringBuilder();
            sb.Append("{\n").Append(string.Join(",\n", rows)).Append("\n}\n");
            return sb.ToString();
        }

        /// <summary>An array of namespaced enum members, one per line.</summary>
        private static string EnumArray(string key, string enumName, List<string> members)
        {
            var sb = new StringBuilder();
            sb.Append(Quote(key)).Append(": [\n");
            for (var i = 0; i < members.Count; i++)
            {
                sb.Append("          ").Append(Quote(enumName + "::" + members[i]));
                if (i < members.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("        ]");
            return sb.ToString();
        }

        // ------------------------------------------------------------- lua

        private static string Lua(RingProject p)
        {
            var e = p.Equip;
            var s = new StringBuilder();
            s.Append("-- ").Append(p.ModName).Append(" - generated by the Ring of No XP creator.\n");
            s.Append("-- Ring item: ").Append(e.Item.Id).Append("\n\n");
            s.Append("local RING_ID = \"").Append(e.Item.Id).Append("\"\n\n");

            s.Append("local function log(msg) print(\"[").Append(p.PackageName).Append("] \" .. tostring(msg)) end\n\n");
            s.Append("-- This mod\'s own folder (Mods\\NativeMods\\UE4SS\\Mods\\<Mod>\\). UE4SS runs Lua with the\n");
            s.Append("-- working directory at Pal\\Binaries\\Win64, so a bare file name would land there;\n");
            s.Append("-- Win64 is never a write location. Every file this mod writes goes through mod_path().\n");
            s.Append("local MOD_DIR = (function()\n");
            s.Append("    local info = debug and debug.getinfo and debug.getinfo(1, \"S\")\n");
            s.Append("    local src = tostring(info and info.source or \"\"):gsub(\"^@\", \"\")\n");
            s.Append("    return src:match(\"^(.*)[/\\\\]Scripts[/\\\\][^/\\\\]+$\") or src:match(\"^(.*)[/\\\\][^/\\\\]+$\")\n");
            s.Append("end)()\n");
            s.Append("local function mod_path(name)\n");
            s.Append("    if MOD_DIR and MOD_DIR ~= \"\" then return MOD_DIR .. \"\\\\\" .. name end\n");
            s.Append("    return name\n");
            s.Append("end\n\n");

            s.Append("-- Is the ring in any of the local player's accessory slots?\n");
            s.Append("-- TODO: confirm the slot accessor against a live UE4SS dump; the\n");
            s.Append("-- equipment container name is not recoverable from the FModel export.\n");
            s.Append("local function ring_equipped()\n");
            s.Append("    local worn = false\n");
            s.Append("    pcall(function()\n");
            s.Append("        for _, ps in ipairs(FindAllOf(\"PalPlayerState\") or {}) do\n");
            s.Append("            local inv = ps.InventoryData\n");
            s.Append("            if inv then\n");
            s.Append("                -- placeholder: walk the equipment slots and compare StaticItemId to RING_ID\n");
            s.Append("            end\n");
            s.Append("        end\n");
            s.Append("    end)\n");
            s.Append("    return worn\n");
            s.Append("end\n\n");

            if (e.Method == PlayerXpMethod.WorldExpRateLua)
            {
                s.Append("-- World ExpRate route. PalGameWorldSettings.OptionSettings.ExpRate is a\n");
                s.Append("-- real, named property (see Config/DefaultPalWorldSettings.json in the\n");
                s.Append("-- FModel export). It is WORLD-WIDE, and it is saved world config, so the\n");
                s.Append("-- pre-ring value is stashed to disk before it is ever changed.\n");
                s.Append("local STASH = mod_path(\"").Append(p.PackageName).Append("_exprate.txt\")\n");
                s.Append("local FALLBACK = ").Append(Num(e.FallbackExpRate)).Append("\n\n");
                s.Append("local function settings()\n");
                s.Append("    local ok, ws = pcall(FindFirstOf, \"PalGameWorldSettings\")\n");
                s.Append("    if ok and ws and ws:IsValid() then return ws end\n");
                s.Append("    return nil\n");
                s.Append("end\n\n");
                s.Append("local function stash_read()\n");
                s.Append("    local f = io.open(STASH, \"r\")\n");
                s.Append("    if not f then return nil end\n");
                s.Append("    local v = tonumber(f:read(\"*a\"))\n");
                s.Append("    f:close()\n");
                s.Append("    return v\n");
                s.Append("end\n\n");
                s.Append("local function stash_write(v)\n");
                s.Append("    local f = io.open(STASH, \"w\")\n");
                s.Append("    if not f then return end\n");
                s.Append("    f:write(tostring(v))\n");
                s.Append("    f:close()\n");
                s.Append("end\n\n");
                s.Append("local function stash_clear() os.remove(STASH) end\n\n");
                s.Append("-- Repair on load: if the last session died with the ring on, the stash\n");
                s.Append("-- still holds the real rate. Put it back before anything else runs.\n");
                s.Append("local function repair()\n");
                s.Append("    local saved = stash_read()\n");
                s.Append("    if not saved then return end\n");
                s.Append("    local ws = settings()\n");
                s.Append("    if not ws then return end\n");
                s.Append("    pcall(function() ws.OptionSettings.ExpRate = saved end)\n");
                s.Append("    stash_clear()\n");
                s.Append("    log(\"restored ExpRate to \" .. tostring(saved) .. \" after an unclean exit\")\n");
                s.Append("end\n\n");
                s.Append("local applied = false\n\n");
                s.Append("local function apply(on)\n");
                s.Append("    local ws = settings()\n");
                s.Append("    if not ws then return end\n");
                s.Append("    if on and not applied then\n");
                s.Append("        local current\n");
                s.Append("        pcall(function() current = ws.OptionSettings.ExpRate end)\n");
                s.Append("        stash_write(current or FALLBACK)\n");
                s.Append("        pcall(function() ws.OptionSettings.ExpRate = 0.0 end)\n");
                s.Append("        applied = true\n");
                s.Append("        log(\"ExpRate 0 (ring on); previous \" .. tostring(current))\n");
                s.Append("    elseif not on and applied then\n");
                s.Append("        local saved = stash_read() or FALLBACK\n");
                s.Append("        pcall(function() ws.OptionSettings.ExpRate = saved end)\n");
                s.Append("        stash_clear()\n");
                s.Append("        applied = false\n");
                s.Append("        log(\"ExpRate restored to \" .. tostring(saved) .. \" (ring off)\")\n");
                s.Append("    end\n");
                s.Append("end\n\n");
                s.Append("repair()\n");
                s.Append("-- LoopAsync runs off the game thread; every UObject touch goes through\n");
                s.Append("-- ExecuteInGameThread (the pattern the working Workshop Lua mods use).\n");
                s.Append("LoopAsync(1000, function()\n");
                s.Append("    ExecuteInGameThread(function() apply(ring_equipped()) end)\n");
                s.Append("    return false\n");
                s.Append("end)\n");
            }
            else
            {
                s.Append("-- Per-player buff route. Applies the Exp_Increase status effect at -100\n");
                s.Append("-- to the wearer only, so other players on the server are untouched.\n");
                s.Append("--\n");
                s.Append("-- BLOCKED until one UE4SS dump is taken: the native function that adds a\n");
                s.Append("-- food status effect to a character is not present anywhere in the FModel\n");
                s.Append("-- export (grepped Content/Pal for AddExp/GainExp - zero hits), so its name\n");
                s.Append("-- has to be read off a running game. Fill it in below.\n");
                s.Append("local ADD_EFFECT = nil -- e.g. \"/Script/Pal.PalStatusEffectComponent:AddEffect\"\n\n");
                s.Append("local function apply(on)\n");
                s.Append("    if not ADD_EFFECT then return end\n");
                s.Append("    -- TODO: resolve the wearer's status-effect component and add/remove\n");
                s.Append("    -- EPalFoodStatusEffectType::Exp_Increase at -100 for as long as `on`.\n");
                s.Append("end\n\n");
                s.Append("-- LoopAsync runs off the game thread; every UObject touch goes through\n");
                s.Append("-- ExecuteInGameThread (the pattern the working Workshop Lua mods use).\n");
                s.Append("LoopAsync(1000, function()\n");
                s.Append("    ExecuteInGameThread(function() apply(ring_equipped()) end)\n");
                s.Append("    return false\n");
                s.Append("end)\n");
            }
            return s.ToString();
        }

        // ------------------------------------------------------------- readme

        private static string Readme(RingProject p)
        {
            var s = new StringBuilder();
            s.Append(p.ModName).Append("\n");
            s.Append(new string('=', p.ModName.Length)).Append("\n\n");
            s.Append("Generated by the Ring of No XP creator (Unity).\n\n");

            s.Append("WHAT IS IN HERE\n");
            s.Append("  PalSchema/items/").Append(p.PackageName).Append(".json\n");
            s.Append("      The new items, through PalSchema's typed items loader - the thing that\n");
            s.Append("      actually creates an item. Shape follows the PalSchema \"Creating a New\n");
            s.Append("      Bow\" guide: Name/Description inline, Type naming the item class, and\n");
            s.Append("      TypeA/TypeB as BARE enum names (no EPal prefix - that is raw/ syntax).\n");
            s.Append("      Type may only be Consumable, Weapon or Armor; Armor covers accessories.\n");
            s.Append("  PalSchema/raw/").Append(p.PackageName).Append(".json\n");
            s.Append("      DT_StatusEffectFood and DT_PassiveSkill_Main - the two tables with no\n");
            s.Append("      typed loader. Raw mods are { \"DT_Table\": { \"RowKey\": { fields } } }.\n\n");

            if (p.Consumable.Enabled)
            {
                s.Append("CONSUMABLE: ").Append(p.Consumable.Item.Id).Append("\n");
                s.Append("  ").Append(p.Consumable.Effect1Type).Append(" at ").Append(p.Consumable.Effect1Value);
                s.Append(" for ").Append(p.Consumable.EffectTime).Append("s.\n");
                s.Append("  Player EXP is exposed to data only through EPalFoodStatusEffectType::Exp_Increase,\n");
                s.Append("  so this is the only no-Lua route that reaches it. MEASURED 2026-09-07 (two\n");
                s.Append("  logged sessions): the buff applies and the food rate reads back 0.1 (the\n");
                s.Append("  game floors it at 10%), but player XP per event did NOT change (35/39/39/41\n");
                s.Append("  without vs 34/37/39/41/43/46 with; +1335 XP, level 4 to 7, under the buff).\n");
                s.Append("  Treat it as NOT a cancel until a run shows otherwise. The world ExpRate\n");
                s.Append("  route (plan B) is the alternative and is UNTESTED.\n\n");
            }

            if (p.Equip.Enabled)
            {
                s.Append("EQUIPMENT: ").Append(p.Equip.Item.Id).Append("\n");
                s.Append("  Built like Ring of Mercy (Accessory_Nonkilling): an otherwise-empty item\n");
                s.Append("  row whose whole power is PassiveSkillName -> ").Append(p.Equip.PassiveId).Append(".\n");
                s.Append("  Method: ").Append(p.Equip.Method).Append("\n");
                switch (p.Equip.Method)
                {
                    case PlayerXpMethod.PalXpOnlyDataOnly:
                        s.Append("  Data only, no Lua. Cancels PAL xp. It cannot cancel player xp:\n");
                        s.Append("  EPalPassiveSkillEffectType has PalExp_Increase but no player-EXP member.\n");
                        break;
                    case PlayerXpMethod.PerPlayerBuffLua:
                        s.Append("  Per-player and multiplayer-correct, but Scripts/main.lua is a stub:\n");
                        s.Append("  the native function name needs one UE4SS dump from a running game.\n");
                        break;
                    case PlayerXpMethod.WorldExpRateLua:
                        s.Append("  Uses PalGameWorldSettings.OptionSettings.ExpRate, a real named property.\n");
                        s.Append("  WORLD-WIDE, not per-player, and it writes saved world config - the\n");
                        s.Append("  script stashes the previous rate to disk and repairs it on load.\n");
                        break;
                }
                s.Append("\n");
            }

            s.Append("INSTALL\n");
            s.Append("  Copy this folder into the Workshop mods folder, or point the Palworld Mod\n");
            s.Append("  Uploader at it. PalSchema JSON lands in\n");
            s.Append("  Mods\\NativeMods\\UE4SS\\Mods\\PalSchema\\mods\\").Append(p.PackageName).Append("\\\n");
            return s.ToString();
        }

        // ------------------------------------------------------------- json helpers

        private static string Row(string key, List<string> fields)
        {
            var b = new StringBuilder();
            b.Append("    ").Append(Quote(key)).Append(": {\n");
            b.Append(string.Join(",\n", fields.ConvertAll(f => "      " + f)));
            b.Append("\n    }");
            return b.ToString();
        }

        private static string Field(string key, string value) => Quote(key) + ": " + Quote(value);
        private static string Field(string key, string value, bool raw) => Quote(key) + ": " + (raw ? value : Quote(value));
        private static string Field(string key, int value) => Quote(key) + ": " + value.ToString(CultureInfo.InvariantCulture);
        private static string Field(string key, bool value) => Quote(key) + ": " + (value ? "true" : "false");
        private static string Field(string key, float value) => Quote(key) + ": " + Num(value);

        private static string Num(float v)
        {
            var s = v.ToString("0.0###", CultureInfo.InvariantCulture);
            return s;
        }

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var b = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
            return b.ToString();
        }

        /// <summary>Small ordered object writer so the output stays diff-friendly.</summary>
        private sealed class JsonBuilder
        {
            private readonly StringBuilder _b = new StringBuilder();
            private readonly List<string> _entries = new List<string>();

            public void Open() => _b.Clear();
            public void Str(string k, string v) => _entries.Add("  " + Quote(k) + ": " + Quote(v));
            public void Bool(string k, bool v) => _entries.Add("  " + Quote(k) + ": " + (v ? "true" : "false"));
            public void Raw(string k, string rawValue) => _entries.Add("  " + Quote(k) + ": " + rawValue);

            public void Table(string name, List<string> rows)
            {
                var b = new StringBuilder();
                b.Append("  ").Append(Quote(name)).Append(": {\n");
                b.Append(string.Join(",\n", rows));
                b.Append("\n  }");
                _entries.Add(b.ToString());
            }

            public void Close()
            {
                _b.Append("{\n");
                _b.Append(string.Join(",\n", _entries));
                _b.Append("\n}\n");
            }

            public override string ToString() => _b.ToString();
        }
    }
}
