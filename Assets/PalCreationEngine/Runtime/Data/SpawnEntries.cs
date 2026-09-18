using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PalCreationEngine.Data
{
    /// <summary>
    /// One entry of a PalSchema <c>spawns/</c> file. The file is a JSON ARRAY of these.
    ///
    /// Shape copied field for field from the verified working mod (PalVariantPandemonium,
    /// Workshop 3800182837, <c>spawns/spawns.json</c>, 22 entries, log "Added new spawn"):
    /// <code>
    /// { "Type": "Sheet",
    ///   "Location": { "X": -376475.321, "Y": 277800.2, "Z": -2050.0 },
    ///   "Rotation": { "Pitch": 0.0, "Yaw": 0.0, "Roll": 0.0 },
    ///   "SpawnerName": "green_FBOSS_ChickenPal_Ground",
    ///   "SpawnerType": "FieldBoss",
    ///   "SpawnGroupList": [ { "Weight": 100, "PalList": [ { "PalId": "...", "Level": 23, "Level_Max": 23, "Num": 1, "Num_Max": 1 } ] } ] }
    /// </code>
    /// SpawnerType is the BARE EPalSpawnedCharacterType member; there is no MinRespawnDelay.
    /// </summary>
    public sealed class SheetSpawnEntry
    {
        [JsonProperty("Type", Order = 0)] public string Type = "Sheet";
        [JsonProperty("Location", Order = 1)] public SpawnVector Location = new SpawnVector();
        [JsonProperty("Rotation", Order = 2)] public SpawnRotator Rotation = new SpawnRotator();
        [JsonProperty("SpawnerName", Order = 3)] public string SpawnerName;
        [JsonProperty("SpawnerType", Order = 4)] public string SpawnerType;
        [JsonProperty("SpawnGroupList", Order = 5)] public List<SheetSpawnGroup> SpawnGroupList = new List<SheetSpawnGroup>();

        /// <summary>Optional in the loader; omitted (null) unless the author sets one.</summary>
        [JsonProperty("OnlyTime", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
        public string OnlyTime;
    }

    public sealed class SpawnVector
    {
        [JsonProperty("X", Order = 0)] public double X;
        [JsonProperty("Y", Order = 1)] public double Y;
        [JsonProperty("Z", Order = 2)] public double Z;

        public SpawnVector() { }
        public SpawnVector(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public sealed class SpawnRotator
    {
        [JsonProperty("Pitch", Order = 0)] public double Pitch;
        [JsonProperty("Yaw", Order = 1)] public double Yaw;
        [JsonProperty("Roll", Order = 2)] public double Roll;
    }

    public sealed class SheetSpawnGroup
    {
        [JsonProperty("Weight", Order = 0)] public int Weight = 100;
        [JsonProperty("PalList", Order = 1)] public List<SpawnPalEntry> PalList = new List<SpawnPalEntry>();
    }

    public sealed class SpawnPalEntry
    {
        [JsonProperty("PalId", Order = 0)] public string PalId;
        [JsonProperty("Level", Order = 1)] public int Level;
        [JsonProperty("Level_Max", Order = 2)] public int Level_Max;
        [JsonProperty("Num", Order = 3)] public int Num;
        [JsonProperty("Num_Max", Order = 4)] public int Num_Max;
    }

    /// <summary>Helpers for the EPalSpawnedCharacterType values a spawns/ entry uses.</summary>
    public static class SpawnedCharacterType
    {
        public const string EnumName = "EPalSpawnedCharacterType";
        public const string Common = "Common";
        public const string FieldBoss = "FieldBoss";

        /// <summary>"EPalSpawnedCharacterType::FieldBoss" -> "FieldBoss"; bare values pass through.</summary>
        public static string Bare(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var i = value.IndexOf("::", StringComparison.Ordinal);
            return i >= 0 ? value.Substring(i + 2) : value;
        }
    }
}
