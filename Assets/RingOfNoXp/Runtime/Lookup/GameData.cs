using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RingOfNoXp.Lookup
{
    /// <summary>One row of items.json: the shipped item as the game defines it.</summary>
    [Serializable]
    public sealed class ItemEntry
    {
        public string id;
        public string name;
        public string desc;
        public string icon;
        public string typeA;
        public string typeB;
        public int rank;
        public int rarity;
        public int maxStack;
        public float weight;
        public int sortId;
        public bool craftable;

        /// <summary>"Name  [id]" for pickers, falling back to the id when the export has no name.</summary>
        public string Display =>
            string.IsNullOrEmpty(name) || name == "-" ? id : $"{name}  [{id}]";
    }

    [Serializable]
    internal sealed class ItemArray { public List<ItemEntry> items; }

    [Serializable]
    internal sealed class EnumFile
    {
        public List<string> passiveSkillEffectType;
        public List<string> foodStatusEffectType;
    }

    /// <summary>One row of the game's own experience tables, per character level.</summary>
    [Serializable]
    public sealed class ExpLevel
    {
        public int level;
        /// <summary>EXP awarded for defeating a character of this level (DT_PalExpTable.DropEXP).</summary>
        public int dropExp;
        /// <summary>EXP needed to reach the next level (DT_PalExpTable.NextEXP).</summary>
        public int nextExp;
        public int totalExp;
        public int buildExp;
        public int craftExp;
        public int palNextExp;
        /// <summary>Bonus EXP for capturing a character of this level (DT_PalCaptureBonusExpTable).</summary>
        public int captureBonus;
    }

    [Serializable]
    internal sealed class ExpFile { public List<ExpLevel> levels; }

    /// <summary>
    /// A crafting station. An item is craftable here only if its TypeA is in
    /// TargetTypesA AND its TypeB is in TargetTypesB - the two lists are checked
    /// independently, which is why adding just one of them is often enough.
    /// </summary>
    [Serializable]
    public sealed class StationEntry
    {
        public string mapObjectId;
        /// <summary>Blueprint name, e.g. BP_BuildObject_WorkBench. The PalSchema blueprints/ key is this + "_C".</summary>
        public string bp;
        public string name;
        public List<string> typesA = new List<string>();
        public List<string> typesB = new List<string>();
        public bool buildable;

        public bool AcceptsA(string typeA) => typesA != null && typesA.Contains(typeA);
        public bool AcceptsB(string typeB) => typesB != null && typesB.Contains(typeB);

        /// <summary>True when the station already crafts this pair with no patch at all.</summary>
        public bool Accepts(string typeA, string typeB) => AcceptsA(typeA) && AcceptsB(typeB);
    }

    [Serializable]
    internal sealed class StationFile { public List<StationEntry> stations; }

    /// <summary>
    /// The shipped-game lookups the tool reads from StreamingAssets. Every load
    /// failure is collected rather than thrown: the window still opens with the
    /// fields as free text, which is enough to author a mod by hand.
    /// </summary>
    public sealed class GameData
    {
        public List<ItemEntry> Items { get; private set; } = new List<ItemEntry>();
        public List<string> PassiveEffectTypes { get; private set; } = new List<string>();
        public List<string> FoodEffectTypes { get; private set; } = new List<string>();

        /// <summary>The game's own EXP tables, level 1..100. Empty when exp.json is missing.</summary>
        public List<ExpLevel> ExpLevels { get; private set; } = new List<ExpLevel>();

        /// <summary>Every crafting station, with the item types it will accept.</summary>
        public List<StationEntry> Stations { get; private set; } = new List<StationEntry>();
        public List<string> LoadErrors { get; } = new List<string>();

        /// <summary>False when items.json is missing, which is what the "data missing" notice keys off.</summary>
        public bool IsLoaded => Items.Count > 0;

        public static GameData Load(string directory)
        {
            var data = new GameData();
            data.LoadItems(Path.Combine(directory, "items.json"));
            data.LoadEnums(Path.Combine(directory, "enums.json"));
            data.LoadExp(Path.Combine(directory, "exp.json"));
            data.LoadStations(Path.Combine(directory, "stations.json"));
            return data;
        }

        private void LoadItems(string path)
        {
            if (!File.Exists(path)) { LoadErrors.Add($"items.json not found at {path}"); return; }
            try
            {
                // JsonUtility cannot parse a top-level array, so the file is wrapped first.
                var wrapped = "{\"items\":" + File.ReadAllText(path) + "}";
                var parsed = JsonUtility.FromJson<ItemArray>(wrapped);
                if (parsed?.items != null) Items = parsed.items;
            }
            catch (Exception e) { LoadErrors.Add($"items.json unreadable: {e.Message}"); }
        }

        private void LoadEnums(string path)
        {
            if (!File.Exists(path)) { LoadErrors.Add($"enums.json not found at {path}"); return; }
            try
            {
                var parsed = JsonUtility.FromJson<EnumFile>(File.ReadAllText(path));
                if (parsed?.passiveSkillEffectType != null) PassiveEffectTypes = parsed.passiveSkillEffectType;
                if (parsed?.foodStatusEffectType != null) FoodEffectTypes = parsed.foodStatusEffectType;
            }
            catch (Exception e) { LoadErrors.Add($"enums.json unreadable: {e.Message}"); }
        }

        private void LoadExp(string path)
        {
            if (!File.Exists(path)) { LoadErrors.Add($"exp.json not found at {path}"); return; }
            try
            {
                var parsed = JsonUtility.FromJson<ExpFile>(File.ReadAllText(path));
                if (parsed?.levels != null) ExpLevels = parsed.levels;
            }
            catch (Exception e) { LoadErrors.Add($"exp.json unreadable: {e.Message}"); }
        }

        private void LoadStations(string path)
        {
            if (!File.Exists(path)) { LoadErrors.Add($"stations.json not found at {path}"); return; }
            try
            {
                var parsed = JsonUtility.FromJson<StationFile>(File.ReadAllText(path));
                if (parsed?.stations != null) Stations = parsed.stations;
            }
            catch (Exception e) { LoadErrors.Add($"stations.json unreadable: {e.Message}"); }
        }

        public StationEntry Station(string bp) =>
            string.IsNullOrEmpty(bp) ? null : Stations.FirstOrDefault(s => s.bp == bp);

        /// <summary>The EXP row for a level, or null when out of range or not loaded.</summary>
        public ExpLevel Exp(int level) => ExpLevels.FirstOrDefault(l => l.level == level);

        /// <summary>Every item id, for the material and "copy from" pickers.</summary>
        public IEnumerable<(string Value, string Display)> ItemChoices() =>
            Items.Select(i => (i.id, i.Display));

        /// <summary>Items of one TypeA, so the accessory tab lists accessories and the consumable tab lists consumables.</summary>
        public IEnumerable<(string Value, string Display)> ItemChoices(params string[] typeA)
        {
            var wanted = new HashSet<string>(typeA, StringComparer.OrdinalIgnoreCase);
            return Items.Where(i => wanted.Contains(i.typeA ?? "")).Select(i => (i.id, i.Display));
        }

        /// <summary>Distinct icon paths with the item they belong to, for the icon picker.</summary>
        public IEnumerable<(string Value, string Display)> IconChoices() =>
            Items.Where(i => !string.IsNullOrEmpty(i.icon))
                 .GroupBy(i => i.icon)
                 .Select(g => (g.Key, $"{g.First().Display}"));

        public ItemEntry Find(string id) =>
            string.IsNullOrEmpty(id) ? null : Items.FirstOrDefault(i => string.Equals(i.id, id, StringComparison.OrdinalIgnoreCase));
    }
}
