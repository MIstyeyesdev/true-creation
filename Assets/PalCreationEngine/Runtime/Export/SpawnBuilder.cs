using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>Where and how a Pal should appear in the world.</summary>
    public sealed class SpawnPlacement
    {
        /// <summary>Pal row key, e.g. "Mishka".</summary>
        public string PalId;

        /// <summary>
        /// Spawn group to join, e.g. "grass_FBOSS_1" or "desert_A". Groups come
        /// from wild_spawners.json, which also says where each one is placed in
        /// the world and how many instances of it exist.
        /// </summary>
        public string GroupRowKey;

        public int LevelMin = 1;
        public int LevelMax = 10;
        public int CountMin = 1;
        public int CountMax = 1;

        /// <summary>
        /// Weight for a NEW row against the group's existing rows. Ignored when
        /// filling a free slot on an existing row, which inherits that row's
        /// weight.
        /// </summary>
        public float Weight = 100f;

        public string SpawnerType = Data.SpawnerType.Common;
        public string OnlyTime = "EPalOneDayTimeType::Undefined";
        public string OnlyWeather = "EPalWeatherConditionType::Undefined";
    }

    /// <summary>
    /// Puts a Pal into the world by editing DT_PalWildSpawner.
    ///
    /// Two ways in, and which one applies depends on the group:
    ///
    ///   - A row in the group with a free slot (fewer than three Pals) can take
    ///     the new Pal as an extra slot. This is a partial patch: only the new
    ///     slot's six fields are written, so the row's existing Pals are
    ///     untouched.
    ///   - Otherwise a NEW row is added sharing the group's SpawnerName. Rows
    ///     sharing a name compete by weight, so this makes the Pal an
    ///     alternative roll at the same places rather than replacing anything.
    ///
    /// Neither approach touches DT_PalSpawnerPlacement: the placements already
    /// exist in the world, and the Pal inherits every location the group has.
    /// </summary>
    public static class SpawnBuilder
    {
        /// <summary>What a group looked like before the edit, for validation and reporting.</summary>
        public sealed class GroupInfo
        {
            public string GroupRowKey;
            public string SpawnerName;
            public int PlacementCount;

            /// <summary>Row key -> number of free slots (0-3) on that row.</summary>
            public Dictionary<string, int> FreeSlotsByRow = new Dictionary<string, int>();
        }

        public sealed class Result
        {
            public string RowKey;
            public int Slot;
            public bool CreatedNewRow;

            public string Describe() =>
                CreatedNewRow
                    ? $"added a new row '{RowKey}' to the group (competes by weight)"
                    : $"filled slot {Slot} on the existing row '{RowKey}'";
        }

        /// <summary>
        /// Applies the placement to <paramref name="mod"/> and reports what it did.
        /// </summary>
        public static Result Apply(SpawnPlacement placement, GroupInfo group, PalSchemaMod mod)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (mod == null) throw new ArgumentNullException(nameof(mod));

            var problems = Validate(placement, group);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Spawn placement is not usable:\n  " + string.Join("\n  ", problems));
            }

            // Prefer filling a free slot: it leaves the group's roll weights
            // exactly as they were, where an extra row dilutes every existing
            // row's share of the spawns.
            var openRow = group.FreeSlotsByRow
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (openRow != null)
            {
                var slot = 4 - group.FreeSlotsByRow[openRow];
                var patch = new WildSpawnerRow();
                WriteSlot(patch, slot, placement);
                mod.WildSpawners[openRow] = patch;

                return new Result { RowKey = openRow, Slot = slot, CreatedNewRow = false };
            }

            var newKey = NextRowKey(group);
            var row = new WildSpawnerRow
            {
                SpawnerName = group.SpawnerName,
                SpawnerType = placement.SpawnerType,
                Weight = placement.Weight,
                OnlyTime = placement.OnlyTime,
                OnlyWeather = placement.OnlyWeather,
                bIsAllowRandomizer = true,
                bHasWorldTreeAura = false,
            };

            // A new row has no shipped row to merge into, so the two unused
            // slots are written explicitly rather than left to the struct
            // default -- the same rule that applies to a new Pal's own row.
            WriteSlot(row, 1, placement);
            ClearSlot(row, 2);
            ClearSlot(row, 3);

            mod.WildSpawners[newKey] = row;
            return new Result { RowKey = newKey, Slot = 1, CreatedNewRow = true };
        }

        /// <summary>
        /// A row key that does not collide with the group's existing rows. The
        /// shipped table names extra rows for a group "{group}_1", "{group}_2",
        /// so this follows that.
        /// </summary>
        private static string NextRowKey(GroupInfo group)
        {
            var index = 1;
            string candidate;
            do
            {
                candidate = group.GroupRowKey + "_" + index.ToString(CultureInfo.InvariantCulture);
                index++;
            } while (group.FreeSlotsByRow.ContainsKey(candidate));

            return candidate;
        }

        private static void WriteSlot(WildSpawnerRow row, int slot, SpawnPlacement placement)
        {
            Set(row, "Pal_" + slot, placement.PalId);
            Set(row, "NPC_" + slot, "None");
            Set(row, "LvMin_" + slot, placement.LevelMin);
            Set(row, "LvMax_" + slot, placement.LevelMax);
            Set(row, "NumMin_" + slot, placement.CountMin);
            Set(row, "NumMax_" + slot, placement.CountMax);
        }

        private static void ClearSlot(WildSpawnerRow row, int slot)
        {
            Set(row, "Pal_" + slot, "None");
            Set(row, "NPC_" + slot, "None");
            Set(row, "LvMin_" + slot, 0);
            Set(row, "LvMax_" + slot, 0);
            Set(row, "NumMin_" + slot, 0);
            Set(row, "NumMax_" + slot, 0);
        }

        private static void Set(WildSpawnerRow row, string field, object value) =>
            typeof(WildSpawnerRow).GetField(field)?.SetValue(row, value);

        public static IReadOnlyList<string> Validate(SpawnPlacement placement, GroupInfo group)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(placement.PalId))
                problems.Add("PalId is required.");

            if (group == null || string.IsNullOrWhiteSpace(group.SpawnerName))
                problems.Add("A spawn group is required; the Pal has to spawn somewhere that exists.");

            if (group != null && group.PlacementCount == 0)
            {
                problems.Add(
                    $"Group '{group.GroupRowKey}' has no placements in the world, so nothing " +
                    "would spawn from it. Dungeon and event groups look like this - pick a " +
                    "group with placements for an overworld spawn.");
            }

            if (placement.LevelMin < 1)
                problems.Add("Level minimum must be at least 1.");
            if (placement.LevelMin > placement.LevelMax)
                problems.Add($"Level minimum {placement.LevelMin} is above maximum {placement.LevelMax}.");
            if (placement.CountMin < 1)
                problems.Add("Count minimum must be at least 1, or nothing spawns.");
            if (placement.CountMin > placement.CountMax)
                problems.Add($"Count minimum {placement.CountMin} is above maximum {placement.CountMax}.");
            if (placement.Weight <= 0f)
                problems.Add("Weight must be above zero or the row can never be picked.");

            return problems;
        }
    }
}
