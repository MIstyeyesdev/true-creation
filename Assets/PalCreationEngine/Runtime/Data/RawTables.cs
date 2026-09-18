using System.Collections.Generic;
using Newtonsoft.Json;

namespace PalCreationEngine.Data
{
    /// <summary>
    /// Names of the <c>raw/</c> tables a new Pal touches, spelled the way the verified working
    /// mod (PalVariantPandemonium) spells them. PalSchema accepts a composite table name or its
    /// <c>_Common</c> parent for the same table; the mod's own choice is kept here so the log
    /// lines match what was observed loading with 0 errors.
    /// </summary>
    public static class RawTables
    {
        /// <summary>The stats table itself (rows go through pals/, but base rows are cloned from it).</summary>
        public const string PalMonsterParameterTable = "DT_PalMonsterParameter";
        public const string PalBPClass = "DT_PalBPClass";
        public const string CharacterIcon = "DT_PalCharacterIconDataTable";
        public const string PartnerSkillParameter = "DT_PartnerSkillParameter";
        public const string PartnerSkillIcon = "DT_partnerSkillIconDataTable";
        public const string WazaMasterLevel = "DT_WazaMasterLevel_Common";
        public const string DropItem = "DT_PalDropItem_Common";
        public const string CaptureCameraOffset = "DT_PalUICaptureCameraOffsetData";
        public const string Randomizer = "DT_PalRandomizer";
        public const string PaldexDistribution = "DT_PaldexDistributionData";
        public const string CombiUnique = "DT_PalCombiUnique";
        public const string TechnologyRecipeUnlock = "DT_TechnologyRecipeUnlock";

        /// <summary>The order the working mod's files are written in (one file per table).</summary>
        public static readonly string[] NewPalOrder =
        {
            PalBPClass, CharacterIcon, PartnerSkillIcon, PartnerSkillParameter, WazaMasterLevel,
            DropItem, CaptureCameraOffset, Randomizer, PaldexDistribution, CombiUnique,
        };
    }

    /// <summary>
    /// DT_PalCombiUnique row: a fixed breeding pair. The working mod writes these with numeric keys
    /// 1000..5049 (vanilla 2..259), namespaced tribes, genders EPalGenderType::None (vanilla: 256 of
    /// 258 rows None, one Male + Female pair), ChildCharacterID = the new Pal. Field order is the
    /// working mod's.
    /// </summary>
    public sealed class CombiUniqueRow
    {
        [JsonProperty("ParentTribeA", Order = 0)] public string ParentTribeA;
        [JsonProperty("ParentGenderA", Order = 1)] public string ParentGenderA = "EPalGenderType::None";
        [JsonProperty("ParentTribeB", Order = 2)] public string ParentTribeB;
        [JsonProperty("ParentGenderB", Order = 3)] public string ParentGenderB = "EPalGenderType::None";
        [JsonProperty("ChildCharacterID", Order = 4)] public string ChildCharacterID;
    }

    /// <summary>DT_PalBPClass row: <c>{ "BPClass": "/Game/.../BP_X.BP_X_C" }</c> (a plain path string).</summary>
    public sealed class BpClassRow
    {
        [JsonProperty("BPClass")] public string BPClass;
    }

    /// <summary>DT_PalCharacterIconDataTable row: <c>{ "Icon": "/Game/Pal/Texture/PalIcon/Normal/T_X_icon_normal.T_X_icon_normal" }</c>.</summary>
    public sealed class CharacterIconRow
    {
        [JsonProperty("Icon")] public string Icon;
    }

    /// <summary>DT_WazaMasterLevel_Common row: which move a Pal learns at which level.</summary>
    public sealed class WazaMasterLevelRow
    {
        [JsonProperty("PalId", Order = 0)] public string PalId;
        /// <summary>Namespaced inside a row: "EPalWazaID::PoisonFog".</summary>
        [JsonProperty("WazaID", Order = 1)] public string WazaID;
        [JsonProperty("Level", Order = 2)] public int Level;
    }

    /// <summary>
    /// DT_PaldexDistributionData row: the Paldex habitat map. The <c>Action: "Clear"</c> array
    /// wrapper is what the working mod writes for a NEW row (the loader treats it as "replace
    /// the array with Items"); the export has no Action key because vanilla rows are plain arrays.
    /// </summary>
    public sealed class PaldexDistributionRow
    {
        [JsonProperty("dayTimeLocations", Order = 0)] public PaldexTimeLocations DayTimeLocations = new PaldexTimeLocations();
        [JsonProperty("nightTimeLocations", Order = 1)] public PaldexTimeLocations NightTimeLocations = new PaldexTimeLocations();
    }

    public sealed class PaldexTimeLocations
    {
        [JsonProperty("Locations", Order = 0)] public PaldexLocationList Locations = new PaldexLocationList();
        [JsonProperty("Radius", Order = 1)] public double Radius = 15000.0;
    }

    public sealed class PaldexLocationList
    {
        [JsonProperty("Action", Order = 0)] public string Action = "Clear";
        [JsonProperty("Items", Order = 1)] public List<SpawnVector> Items = new List<SpawnVector>();
    }
}
