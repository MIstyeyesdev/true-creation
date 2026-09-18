using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Finds fields left unset on a row.
    ///
    /// This matters only for a brand-new Pal. PalSchema merges a partial row
    /// into the shipped one, so an edit can safely name just the fields it
    /// changes; a new Pal has no shipped row, and every omitted field silently
    /// takes the raw struct default instead. A Pal missing WalkSpeed/RunSpeed
    /// gets 0 and cannot move; one missing MeshCapsule* gets no collision.
    ///
    /// Reflection over the generated model rather than a hand-kept field list,
    /// so this cannot fall out of step with the schema. Fields listed in
    /// PalMonsterParameterRow.OptionalFields are base-table fields the verified
    /// working mod never writes; they are not required.
    /// </summary>
    public static class RowCompleteness
    {
        private static readonly FieldInfo[] PalFields =
            typeof(PalMonsterParameterRow).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !PalMonsterParameterRow.OptionalFields.Contains(f.Name))
                .ToArray();

        public static IEnumerable<string> MissingFields(PalMonsterParameterRow row)
        {
            if (row == null) yield break;

            foreach (var field in PalFields)
            {
                if (field.GetValue(row) == null)
                    yield return field.Name;
            }
        }

        public static bool IsComplete(PalMonsterParameterRow row) => !MissingFields(row).Any();

        public static int FieldCount => PalFields.Length;
    }
}
