using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// The contents of a PalSchema <c>raw/</c> JSON file: a set of data-table
    /// names, each mapping row keys to (possibly partial) rows.
    ///
    /// PalSchema merges what it finds here into the game's real tables, which
    /// gives two modes:
    ///   - a row key that already exists is PATCHED, so only the fields present
    ///     are changed and everything else keeps its shipped value;
    ///   - a row key that does not exist is ADDED, and anything omitted falls
    ///     back to the raw struct default (0 / false / "None") rather than to
    ///     anything sensible. New rows must therefore be complete.
    ///
    /// <see cref="Validate"/> enforces that distinction rather than leaving it
    /// to be discovered as a Pal that spawns but cannot move.
    ///
    /// Deliberately free of any UnityEngine dependency so it can be exercised
    /// by a plain console harness outside the editor.
    /// </summary>
    public sealed class PalSchemaMod
    {
        /// <summary>Rows keyed by Pal internal ID, e.g. "Mishka".</summary>
        public readonly Dictionary<string, PalMonsterParameterRow> Pals =
            new Dictionary<string, PalMonsterParameterRow>();

        /// <summary>Loot rows keyed "{CharacterID}{Level:000}", e.g. "Mishka000".</summary>
        public readonly Dictionary<string, PalDropItemRow> DropItems =
            new Dictionary<string, PalDropItemRow>();

        /// <summary>
        /// Item-lottery rows. The shipped table keys these numerically ("1"
        /// through "8782"), so any non-numeric key is collision-safe -- which
        /// is what published ranch mods rely on.
        /// </summary>
        public readonly Dictionary<string, ItemLotteryRow> ItemLottery =
            new Dictionary<string, ItemLotteryRow>();

        /// <summary>Lottery slot probabilities, keyed by FieldName.</summary>
        public readonly Dictionary<string, FieldLotteryNameRow> FieldLotteryNames =
            new Dictionary<string, FieldLotteryNameRow>();

        /// <summary>Wild spawn rows, keyed by spawner row key.</summary>
        public readonly Dictionary<string, WildSpawnerRow> WildSpawners =
            new Dictionary<string, WildSpawnerRow>();

        public const string PalTable = "DT_PalMonsterParameter";
        public const string DropTable = "DT_PalDropItem_Common";
        public const string ItemLotteryTable = "DT_ItemLotteryDataTable";
        public const string FieldLotteryTable = "DT_FieldLotteryNameDataTable";
        public const string WildSpawnerTable = "DT_PalWildSpawner";

        /// <summary>
        /// Matches the formatting of the shipped tables and of working mods:
        /// two-space indent, nulls omitted (that is what makes partial patch
        /// rows possible), and invariant number formatting so a machine with a
        /// comma decimal separator cannot emit "1,0" into a mod file.
        /// </summary>
        public static JsonSerializerSettings SerializerSettings => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            FloatFormatHandling = FloatFormatHandling.DefaultValue,
            Culture = CultureInfo.InvariantCulture,
            ContractResolver = new DefaultContractResolver(),
        };

        /// <summary>
        /// Builds the loot row key the game expects: CharacterID followed by
        /// the level tier zero-padded to three digits ("Mishka" + 0 -> "Mishka000").
        /// </summary>
        public static string DropRowKey(string characterId, int level)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId is required", nameof(characterId));

            return characterId + level.ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Adds a loot row, filling in the CharacterID and Level that a new
        /// Pal's row needs. Both are omitted by most published mods because
        /// those patch an existing row that already carries them; a Pal that
        /// did not ship with the game has no such row to inherit from.
        /// </summary>
        public void AddDropRow(string characterId, int level, PalDropItemRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));

            row.CharacterID = characterId;
            row.Level = level;
            DropItems[DropRowKey(characterId, level)] = row;
        }

        public string ToJson()
        {
            var document = new Dictionary<string, object>();
            // Lottery tables lead, matching the layout published ranch mods use.
            if (ItemLottery.Count > 0) document[ItemLotteryTable] = ItemLottery;
            if (FieldLotteryNames.Count > 0) document[FieldLotteryTable] = FieldLotteryNames;
            if (Pals.Count > 0) document[PalTable] = Pals;
            if (DropItems.Count > 0) document[DropTable] = DropItems;
            if (WildSpawners.Count > 0) document[WildSpawnerTable] = WildSpawners;

            return JsonConvert.SerializeObject(document, SerializerSettings);
        }

        public static PalSchemaMod FromJson(string json)
        {
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                         ?? new Dictionary<string, object>();

            var mod = new PalSchemaMod();

            if (parsed.TryGetValue(PalTable, out var palsRaw) && palsRaw != null)
            {
                var pals = JsonConvert.DeserializeObject<Dictionary<string, PalMonsterParameterRow>>(
                    palsRaw.ToString());
                if (pals != null)
                    foreach (var kv in pals) mod.Pals[kv.Key] = kv.Value;
            }

            if (parsed.TryGetValue(DropTable, out var dropsRaw) && dropsRaw != null)
            {
                var drops = JsonConvert.DeserializeObject<Dictionary<string, PalDropItemRow>>(
                    dropsRaw.ToString());
                if (drops != null)
                    foreach (var kv in drops) mod.DropItems[kv.Key] = kv.Value;
            }

            if (parsed.TryGetValue(WildSpawnerTable, out var spawnRaw) && spawnRaw != null)
            {
                var spawns = JsonConvert.DeserializeObject<Dictionary<string, WildSpawnerRow>>(
                    spawnRaw.ToString());
                if (spawns != null)
                    foreach (var kv in spawns) mod.WildSpawners[kv.Key] = kv.Value;
            }

            if (parsed.TryGetValue(ItemLotteryTable, out var lotteryRaw) && lotteryRaw != null)
            {
                var lottery = JsonConvert.DeserializeObject<Dictionary<string, ItemLotteryRow>>(
                    lotteryRaw.ToString());
                if (lottery != null)
                    foreach (var kv in lottery) mod.ItemLottery[kv.Key] = kv.Value;
            }

            if (parsed.TryGetValue(FieldLotteryTable, out var fieldRaw) && fieldRaw != null)
            {
                var fields = JsonConvert.DeserializeObject<Dictionary<string, FieldLotteryNameRow>>(
                    fieldRaw.ToString());
                if (fields != null)
                    foreach (var kv in fields) mod.FieldLotteryNames[kv.Key] = kv.Value;
            }

            return mod;
        }

        /// <summary>
        /// Reports problems that would produce a mod which loads but misbehaves.
        /// <paramref name="knownPalIds"/> is the set of row keys that ship with
        /// the game; a row key outside it is a new Pal and must be complete.
        /// Pass null to skip the completeness check.
        /// </summary>
        public IReadOnlyList<string> Validate(ISet<string> knownPalIds) => Validate(knownPalIds, null);

        /// <summary>
        /// <paramref name="providedTextKeys"/>: the text keys the package defines in translations/.
        /// When given (the new-Pal route), a new row must carry OverrideNameTextID None and the
        /// package must provide PAL_NAME_&lt;id&gt; - that is how every vanilla base Pal and every
        /// new Pal of the working mod is named. When null (a legacy raw-only mod with no text
        /// file), the old rule applies: the row itself must name a text key, or the game shows
        /// the raw key.
        /// </summary>
        public IReadOnlyList<string> Validate(ISet<string> knownPalIds, ISet<string> providedTextKeys)
        {
            var problems = new List<string>();

            foreach (var kv in Pals)
            {
                var id = kv.Key;
                var row = kv.Value;
                var isNewPal = knownPalIds != null && !knownPalIds.Contains(id);

                if (isNewPal)
                {
                    foreach (var missing in RowCompleteness.MissingFields(row))
                    {
                        problems.Add(
                            $"{id}: new Pal is missing '{missing}'. A new row has nothing to " +
                            "merge into, so this would fall back to the struct default.");
                    }

                    if (providedTextKeys != null)
                    {
                        var nameKey = "PAL_NAME_" + id;
                        var isBossRow = id.StartsWith("BOSS_", StringComparison.OrdinalIgnoreCase);
                        if (!isBossRow && !providedTextKeys.Contains(nameKey))
                            problems.Add($"{id}: translations/ must provide {nameKey}; the game reads the name from it (OverrideNameTextID stays None).");
                        if (!isBossRow && row.OverrideNameTextID != null && row.OverrideNameTextID != "None" && !providedTextKeys.Contains(row.OverrideNameTextID))
                            problems.Add($"{id}: OverrideNameTextID '{row.OverrideNameTextID}' is not a text key this package defines; display names belong in translations/, the row keeps None.");
                    }
                    else if (row.OverrideNameTextID == null || row.OverrideNameTextID == "None")
                    {
                        // Legacy raw-only mod: the symptom is a Pal that shows the raw lookup key
                        // ("PAL_NAME_Mishka") as its name in game.
                        problems.Add(
                            $"{id}: OverrideNameTextID is \"None\" and this mod ships no translations/, so the game will display the " +
                            "raw text key instead of a name. Add translations/ (preferred) or set a text key.");
                    }
                }
            }

            foreach (var kv in DropItems)
            {
                var key = kv.Key;
                var row = kv.Value;

                if (string.IsNullOrEmpty(row.CharacterID))
                {
                    problems.Add(
                        $"{key}: drop row has no CharacterID. Required when the Pal has no " +
                        "shipped drop row to merge into.");
                }
                else if (knownPalIds != null && !knownPalIds.Contains(row.CharacterID)
                                             && !Pals.ContainsKey(row.CharacterID))
                {
                    problems.Add(
                        $"{key}: CharacterID '{row.CharacterID}' is neither a shipped Pal nor " +
                        "defined by this mod.");
                }
            }

            return problems;
        }
    }
}
