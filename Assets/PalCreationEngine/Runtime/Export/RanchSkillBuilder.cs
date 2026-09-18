using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>One item a Pal can produce at a ranch.</summary>
    public sealed class RanchDrop
    {
        public string ItemId;

        /// <summary>Rolled count, before <see cref="NumUnit"/> is applied.</summary>
        public int MinNum = 1;
        public int MaxNum = 1;

        /// <summary>Multiplier on the rolled count. Leave at 1 unless dropping in bulk.</summary>
        public int NumUnit = 1;

        /// <summary>Relative weight against the other drops in this rank.</summary>
        public float WeightInSlot = 1f;

        public string TreasureBoxGrade = TreasureGrade.Grade1;
        public float BonusExpRate = 1f;

        public RanchDrop() { }

        public RanchDrop(string itemId, int minNum = 1, int maxNum = 1, float weight = 1f)
        {
            ItemId = itemId;
            MinNum = minNum;
            MaxNum = maxNum;
            WeightInSlot = weight;
        }
    }

    /// <summary>
    /// What a Pal produces at one ranch rank. Rank tracks the Pal's condenser
    /// rank, so higher ranks normally list better or more numerous drops.
    /// </summary>
    public sealed class RanchRank
    {
        public int Rank;
        public readonly List<RanchDrop> Drops = new List<RanchDrop>();

        /// <summary>Chance the slot rolls at all. 100 means it always produces.</summary>
        public float SlotProbabilityPercent = 100f;

        public RanchRank() { }

        public RanchRank(int rank, params RanchDrop[] drops)
        {
            Rank = rank;
            if (drops != null) Drops.AddRange(drops);
        }
    }

    /// <summary>
    /// A complete ranch-production setup for one Pal.
    ///
    /// Ranch production is not a passive-skill slot, which is the natural guess
    /// and is wrong. It takes four coordinated pieces, and omitting any one
    /// produces a Pal that sits in the ranch doing nothing:
    ///
    ///   1. DT_ItemLotteryDataTable       - one row per item per rank
    ///   2. DT_FieldLotteryNameDataTable  - one row per rank, slot probability
    ///   3. DT_PalMonsterParameter        - WorkSuitability_MonsterFarm >= 1
    ///   4. blueprints/                   - StaticCharacterParameterComponent
    ///                                      .SpawnItem.FieldLotteryNameByRank
    ///
    /// Structure confirmed against DazziRanch and GutenRanchAscension, both
    /// working published mods.
    /// </summary>
    public sealed class RanchSkillDefinition
    {
        /// <summary>Pal row key in DT_PalMonsterParameter, e.g. "RaijinDaughter".</summary>
        public string PalId;

        /// <summary>
        /// Blueprint object paths to attach the ranch behaviour to. A Pal
        /// normally needs its base BP and its _BOSS variant, or the alpha
        /// version silently produces nothing. Resolve these from bp_paths.json.
        /// </summary>
        public readonly List<string> BlueprintObjectPaths = new List<string>();

        /// <summary>
        /// Prefix for lottery pool names; rank N becomes "{prefix}Rank{N}".
        /// Defaults to "CharacterSpawnItem_{PalId}", matching the shipped
        /// naming. It only has to be unique, not match the Pal's real ID.
        /// </summary>
        public string FieldNamePrefix;

        /// <summary>
        /// Prefix for DT_ItemLotteryDataTable row keys, which become
        /// "{prefix}001", "{prefix}002", ... The shipped table keys rows
        /// numerically, so any non-numeric prefix cannot collide.
        /// </summary>
        public string LotteryKeyPrefix;

        /// <summary>
        /// Written to the Pal's row. Must be at least 1 or the Pal cannot be
        /// assigned to a ranch at all, and none of the rest has any effect.
        /// </summary>
        public int WorkSuitabilityMonsterFarm = 1;

        /// <summary>
        /// Optional animation Blueprint for the produce action, assigned to
        /// ActionComponent.ActionMap under EPalActionType::SpawnItem. Needs a
        /// custom asset shipped in the package; without one the Pal still
        /// produces items, just without a bespoke animation.
        /// </summary>
        public string SpawnItemActionBlueprintPath;

        public readonly List<RanchRank> Ranks = new List<RanchRank>();

        public string ResolvedFieldNamePrefix =>
            string.IsNullOrWhiteSpace(FieldNamePrefix) ? "CharacterSpawnItem_" + PalId : FieldNamePrefix;

        public string ResolvedLotteryKeyPrefix =>
            string.IsNullOrWhiteSpace(LotteryKeyPrefix) ? PalId + "Ranch" : LotteryKeyPrefix;

        public string FieldNameForRank(int rank) =>
            ResolvedFieldNamePrefix + "Rank" + rank.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Applies a <see cref="RanchSkillDefinition"/> to a mod.</summary>
    public static class RanchSkillBuilder
    {
        /// <summary>
        /// Writes all four pieces into <paramref name="mod"/> and
        /// <paramref name="blueprints"/>.
        ///
        /// The Pal row is PATCHED, not replaced: only
        /// WorkSuitability_MonsterFarm is set, so adding ranch production to a
        /// shipped Pal leaves everything else at its real value. Where the Pal
        /// is also being created by this mod, the existing row is updated in
        /// place instead.
        /// </summary>
        public static void Apply(
            RanchSkillDefinition definition,
            PalSchemaMod mod,
            PalSchemaBlueprints blueprints)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (blueprints == null) throw new ArgumentNullException(nameof(blueprints));

            var problems = Validate(definition);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Ranch definition is not usable:\n  " + string.Join("\n  ", problems));
            }

            var sequence = 1;
            foreach (var rank in definition.Ranks.OrderBy(r => r.Rank))
            {
                var fieldName = definition.FieldNameForRank(rank.Rank);

                foreach (var drop in rank.Drops)
                {
                    var key = definition.ResolvedLotteryKeyPrefix +
                              sequence.ToString("000", CultureInfo.InvariantCulture);
                    sequence++;

                    mod.ItemLottery[key] = new ItemLotteryRow
                    {
                        FieldName = fieldName,
                        SlotNo = 1,
                        WeightInSlot = drop.WeightInSlot,
                        StaticItemId = drop.ItemId,
                        MinNum = drop.MinNum,
                        MaxNum = drop.MaxNum,
                        NumUnit = drop.NumUnit,
                        TreasureBoxGrade = drop.TreasureBoxGrade,
                        BonusExpRate = drop.BonusExpRate,
                    };
                }

                mod.FieldLotteryNames[fieldName] =
                    FieldLotteryNameRow.ForSlot(1, rank.SlotProbabilityPercent);
            }

            if (!mod.Pals.TryGetValue(definition.PalId, out var palRow))
            {
                palRow = new PalMonsterParameterRow();
                mod.Pals[definition.PalId] = palRow;
            }
            palRow.WorkSuitability_MonsterFarm = definition.WorkSuitabilityMonsterFarm;

            var rankMap = definition.Ranks
                .OrderBy(r => r.Rank)
                .Select(r => new Dictionary<string, object>
                {
                    ["Key"] = r.Rank,
                    ["Value"] = new Dictionary<string, object> { ["Key"] = definition.FieldNameForRank(r.Rank) },
                })
                .ToList();

            foreach (var path in definition.BlueprintObjectPaths)
            {
                var components = blueprints.ForBlueprint(path);

                components[ComponentNames.StaticCharacterParameter] = new Dictionary<string, object>
                {
                    ["SpawnItem"] = new Dictionary<string, object>
                    {
                        ["FieldLotteryNameByRank"] = rankMap,
                    },
                };

                if (!string.IsNullOrWhiteSpace(definition.SpawnItemActionBlueprintPath))
                {
                    components[ComponentNames.Action] = new Dictionary<string, object>
                    {
                        ["ActionMap"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["Key"] = "EPalActionType::SpawnItem",
                                ["Value"] = definition.SpawnItemActionBlueprintPath,
                            },
                        },
                    };
                }
            }
        }

        public static IReadOnlyList<string> Validate(RanchSkillDefinition definition)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(definition.PalId))
                problems.Add("PalId is required.");

            if (definition.BlueprintObjectPaths.Count == 0)
            {
                problems.Add(
                    "No Blueprint object paths. Without a blueprints edit the lottery rows " +
                    "exist but nothing reads them, and the Pal produces nothing.");
            }

            if (definition.WorkSuitabilityMonsterFarm < 1)
            {
                problems.Add(
                    "WorkSuitabilityMonsterFarm must be at least 1, or the Pal cannot be " +
                    "assigned to a ranch and no other part of this has any effect.");
            }

            if (definition.Ranks.Count == 0)
                problems.Add("No ranks defined.");

            foreach (var rank in definition.Ranks)
            {
                if (rank.Rank < 1)
                    problems.Add($"Rank {rank.Rank} is invalid; ranks start at 1.");

                if (rank.Drops.Count == 0)
                    problems.Add($"Rank {rank.Rank} has no drops.");

                foreach (var drop in rank.Drops)
                {
                    if (string.IsNullOrWhiteSpace(drop.ItemId))
                        problems.Add($"Rank {rank.Rank} has a drop with no ItemId.");
                    if (drop.MinNum > drop.MaxNum)
                        problems.Add($"Rank {rank.Rank}, {drop.ItemId}: MinNum {drop.MinNum} exceeds MaxNum {drop.MaxNum}.");
                    if (drop.WeightInSlot <= 0f)
                        problems.Add($"Rank {rank.Rank}, {drop.ItemId}: WeightInSlot must be greater than zero or it can never be picked.");
                }
            }

            var duplicated = definition.Ranks.GroupBy(r => r.Rank).Where(g => g.Count() > 1).ToList();
            foreach (var group in duplicated)
                problems.Add($"Rank {group.Key} is defined more than once.");

            return problems;
        }

        /// <summary>
        /// Checks every drop's item against the shipped item table. A typo here
        /// produces a mod that loads cleanly and drops nothing, so it is worth
        /// catching before export rather than in game.
        /// </summary>
        public static IReadOnlyList<string> ValidateItemIds(
            RanchSkillDefinition definition, ISet<string> knownItemIds)
        {
            var problems = new List<string>();
            if (knownItemIds == null) return problems;

            foreach (var rank in definition.Ranks)
            foreach (var drop in rank.Drops)
            {
                if (string.IsNullOrWhiteSpace(drop.ItemId)) continue;
                if (knownItemIds.Contains(drop.ItemId)) continue;

                var suggestion = knownItemIds.FirstOrDefault(
                    id => string.Equals(id, drop.ItemId, StringComparison.OrdinalIgnoreCase));

                problems.Add(suggestion != null
                    ? $"Rank {rank.Rank}: item '{drop.ItemId}' is not a real item ID -- did you mean '{suggestion}'? (IDs are case-sensitive.)"
                    : $"Rank {rank.Rank}: item '{drop.ItemId}' is not a real item ID.");
            }

            return problems;
        }
    }
}
