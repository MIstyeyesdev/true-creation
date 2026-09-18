using System.Collections.Generic;
using Newtonsoft.Json;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// The contents of a PalSchema <c>blueprints/</c> file: component edits
    /// keyed by full Blueprint object path.
    ///
    /// The shape is confirmed by working published mods (DazziRanch,
    /// GutenRanchAscension):
    ///
    /// <code>
    /// {
    ///   "/Game/Pal/Blueprint/.../BP_X.BP_X_C": {
    ///     "StaticCharacterParameterComponent": { ... }
    ///   }
    /// }
    /// </code>
    ///
    /// Note this is NOT the "BP_X_C:ComponentName" key form, and action values
    /// are plain object-path strings rather than ObjectName/ObjectPath pairs.
    /// </summary>
    public sealed class PalSchemaBlueprints
    {
        /// <summary>Blueprint object path -> component name -> edit payload.</summary>
        public readonly Dictionary<string, Dictionary<string, object>> Edits =
            new Dictionary<string, Dictionary<string, object>>();

        public Dictionary<string, object> ForBlueprint(string objectPath)
        {
            if (!Edits.TryGetValue(objectPath, out var components))
            {
                components = new Dictionary<string, object>();
                Edits[objectPath] = components;
            }
            return components;
        }

        public bool IsEmpty => Edits.Count == 0;

        public string ToJson() =>
            JsonConvert.SerializeObject(Edits, PalSchemaMod.SerializerSettings);
    }

    /// <summary>Component names addressed by blueprint edits.</summary>
    public static class ComponentNames
    {
        public const string StaticCharacterParameter = "StaticCharacterParameterComponent";
        public const string Action = "ActionComponent";
    }
}
