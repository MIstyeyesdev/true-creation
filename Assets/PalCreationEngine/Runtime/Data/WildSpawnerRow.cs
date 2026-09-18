using Newtonsoft.Json;

namespace PalCreationEngine.Data
{
    /// <summary>
    /// One DT_PalWildSpawner row: what can spawn at a named spawner.
    ///
    /// A row holds up to three slots, each naming either a Pal or an NPC with
    /// its level and count ranges. Rows sharing a SpawnerName compete, picked in
    /// proportion to Weight, so a spawn group is a SET of rows rather than one.
    ///
    /// Where a group actually sits in the world is a separate table,
    /// DT_PalSpawnerPlacement, joined on the spawner name with its area prefix
    /// removed. Editing only this table changes what spawns at places that
    /// already exist -- which is what putting a custom Pal into the world takes.
    /// </summary>
    public sealed class WildSpawnerRow
    {
        [JsonProperty("SpawnerName", Order = 0)] public string SpawnerName;
        [JsonProperty("SpawnerType", Order = 1)] public string SpawnerType;

        /// <summary>Relative weight against the other rows sharing SpawnerName.</summary>
        [JsonProperty("Weight", Order = 2)] public float? Weight;

        [JsonProperty("OnlyTime", Order = 3)] public string OnlyTime;
        [JsonProperty("OnlyWeather", Order = 4)] public string OnlyWeather;

        [JsonProperty("Pal_1", Order = 5)] public string Pal_1;
        [JsonProperty("NPC_1", Order = 6)] public string NPC_1;
        [JsonProperty("LvMin_1", Order = 7)] public int? LvMin_1;
        [JsonProperty("LvMax_1", Order = 8)] public int? LvMax_1;
        [JsonProperty("NumMin_1", Order = 9)] public int? NumMin_1;
        [JsonProperty("NumMax_1", Order = 10)] public int? NumMax_1;

        [JsonProperty("Pal_2", Order = 11)] public string Pal_2;
        [JsonProperty("NPC_2", Order = 12)] public string NPC_2;
        [JsonProperty("LvMin_2", Order = 13)] public int? LvMin_2;
        [JsonProperty("LvMax_2", Order = 14)] public int? LvMax_2;
        [JsonProperty("NumMin_2", Order = 15)] public int? NumMin_2;
        [JsonProperty("NumMax_2", Order = 16)] public int? NumMax_2;

        [JsonProperty("Pal_3", Order = 17)] public string Pal_3;
        [JsonProperty("NPC_3", Order = 18)] public string NPC_3;
        [JsonProperty("LvMin_3", Order = 19)] public int? LvMin_3;
        [JsonProperty("LvMax_3", Order = 20)] public int? LvMax_3;
        [JsonProperty("NumMin_3", Order = 21)] public int? NumMin_3;
        [JsonProperty("NumMax_3", Order = 22)] public int? NumMax_3;

        [JsonProperty("bIsAllowRandomizer", Order = 23)] public bool? bIsAllowRandomizer;
        [JsonProperty("bHasWorldTreeAura", Order = 24)] public bool? bHasWorldTreeAura;
    }

    /// <summary>Values EPalSpawnedCharacterType takes in the shipped table.</summary>
    public static class SpawnerType
    {
        private const string Prefix = "EPalSpawnedCharacterType::";

        public const string Common = Prefix + "Common";
        public const string FieldBoss = Prefix + "FieldBoss";
        public const string RandomDungeonBoss = Prefix + "RandomDungeonBoss";
        public const string ImprisonmentBoss = Prefix + "ImprisonmentBoss";
    }
}
