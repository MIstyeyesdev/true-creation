using Newtonsoft.Json;

namespace PalCreationEngine.Data
{
    /// <summary>Treasure grades used by DT_ItemLotteryDataTable.</summary>
    public static class TreasureGrade
    {
        private const string Prefix = "EPalMapObjectTreasureGradeType::";

        public const string Grade1 = Prefix + "Grade1";
        public const string Grade2 = Prefix + "Grade2";
        public const string Grade3 = Prefix + "Grade3";
        public const string Grade4 = Prefix + "Grade4";
        public const string Grade5 = Prefix + "Grade5";
        public const string Grade6 = Prefix + "Grade6";
    }

    /// <summary>
    /// One DT_ItemLotteryDataTable row: a single item that can drop from the
    /// pool named by <see cref="FieldName"/>.
    ///
    /// Rows sharing a FieldName and SlotNo compete, picked in proportion to
    /// WeightInSlot. Whether that slot rolls at all is set separately, by the
    /// matching <see cref="FieldLotteryNameRow"/>.
    /// </summary>
    public sealed class ItemLotteryRow
    {
        /// <summary>The pool this row belongs to, e.g. "CharacterSpawnItem_DazziRank1".</summary>
        [JsonProperty("FieldName", Order = 0)] public string FieldName;

        /// <summary>Which slot (1-15) of the pool. Maps to ItemSlot{N}_ProbabilityPercent.</summary>
        [JsonProperty("SlotNo", Order = 1)] public int? SlotNo;

        /// <summary>Relative weight against other rows in the same FieldName + SlotNo.</summary>
        [JsonProperty("WeightInSlot", Order = 2)] public float? WeightInSlot;

        [JsonProperty("StaticItemId", Order = 3)] public string StaticItemId;

        [JsonProperty("MinNum", Order = 4)] public int? MinNum;

        [JsonProperty("MaxNum", Order = 5)] public int? MaxNum;

        /// <summary>
        /// Multiplier applied to the rolled count. Almost always 1; the shipped
        /// table uses 10 and 100 for bulk drops like ore and coins.
        /// </summary>
        [JsonProperty("NumUnit", Order = 6)] public int? NumUnit;

        [JsonProperty("TreasureBoxGrade", Order = 7)] public string TreasureBoxGrade;

        [JsonProperty("BonusExpRate", Order = 8)] public float? BonusExpRate;
    }

    /// <summary>
    /// One DT_FieldLotteryNameDataTable row: the chance each of 15 slots rolls
    /// for the pool this row is keyed by. A ranch pool normally uses slot 1 at
    /// 100% and leaves the rest at zero.
    /// </summary>
    public sealed class FieldLotteryNameRow
    {
        [JsonProperty("ItemSlot1_ProbabilityPercent", Order = 0)] public float? ItemSlot1_ProbabilityPercent;
        [JsonProperty("ItemSlot2_ProbabilityPercent", Order = 1)] public float? ItemSlot2_ProbabilityPercent;
        [JsonProperty("ItemSlot3_ProbabilityPercent", Order = 2)] public float? ItemSlot3_ProbabilityPercent;
        [JsonProperty("ItemSlot4_ProbabilityPercent", Order = 3)] public float? ItemSlot4_ProbabilityPercent;
        [JsonProperty("ItemSlot5_ProbabilityPercent", Order = 4)] public float? ItemSlot5_ProbabilityPercent;
        [JsonProperty("ItemSlot6_ProbabilityPercent", Order = 5)] public float? ItemSlot6_ProbabilityPercent;
        [JsonProperty("ItemSlot7_ProbabilityPercent", Order = 6)] public float? ItemSlot7_ProbabilityPercent;
        [JsonProperty("ItemSlot8_ProbabilityPercent", Order = 7)] public float? ItemSlot8_ProbabilityPercent;
        [JsonProperty("ItemSlot9_ProbabilityPercent", Order = 8)] public float? ItemSlot9_ProbabilityPercent;
        [JsonProperty("ItemSlot10_ProbabilityPercent", Order = 9)] public float? ItemSlot10_ProbabilityPercent;
        [JsonProperty("ItemSlot11_ProbabilityPercent", Order = 10)] public float? ItemSlot11_ProbabilityPercent;
        [JsonProperty("ItemSlot12_ProbabilityPercent", Order = 11)] public float? ItemSlot12_ProbabilityPercent;
        [JsonProperty("ItemSlot13_ProbabilityPercent", Order = 12)] public float? ItemSlot13_ProbabilityPercent;
        [JsonProperty("ItemSlot14_ProbabilityPercent", Order = 13)] public float? ItemSlot14_ProbabilityPercent;
        [JsonProperty("ItemSlot15_ProbabilityPercent", Order = 14)] public float? ItemSlot15_ProbabilityPercent;

        /// <summary>
        /// Sets slot <paramref name="slotNo"/> and leaves the other 14 unset.
        /// Omitted slots take the struct default of 0%, which is the intended
        /// value, so a single-slot pool does not need all 15 spelled out --
        /// published ranch mods write only slot 1 for exactly this reason.
        /// </summary>
        public static FieldLotteryNameRow ForSlot(int slotNo, float probabilityPercent)
        {
            var row = new FieldLotteryNameRow();
            switch (slotNo)
            {
                case 1: row.ItemSlot1_ProbabilityPercent = probabilityPercent; break;
                case 2: row.ItemSlot2_ProbabilityPercent = probabilityPercent; break;
                case 3: row.ItemSlot3_ProbabilityPercent = probabilityPercent; break;
                case 4: row.ItemSlot4_ProbabilityPercent = probabilityPercent; break;
                case 5: row.ItemSlot5_ProbabilityPercent = probabilityPercent; break;
                case 6: row.ItemSlot6_ProbabilityPercent = probabilityPercent; break;
                case 7: row.ItemSlot7_ProbabilityPercent = probabilityPercent; break;
                case 8: row.ItemSlot8_ProbabilityPercent = probabilityPercent; break;
                case 9: row.ItemSlot9_ProbabilityPercent = probabilityPercent; break;
                case 10: row.ItemSlot10_ProbabilityPercent = probabilityPercent; break;
                case 11: row.ItemSlot11_ProbabilityPercent = probabilityPercent; break;
                case 12: row.ItemSlot12_ProbabilityPercent = probabilityPercent; break;
                case 13: row.ItemSlot13_ProbabilityPercent = probabilityPercent; break;
                case 14: row.ItemSlot14_ProbabilityPercent = probabilityPercent; break;
                case 15: row.ItemSlot15_ProbabilityPercent = probabilityPercent; break;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(slotNo), slotNo, "Lottery slots are numbered 1-15.");
            }
            return row;
        }
    }
}
