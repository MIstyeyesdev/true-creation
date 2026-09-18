using System;
using System.Collections.Generic;
using System.Linq;
using PalCreationEngine.Data;
using PalCreationEngine.Lookup;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// Refuses the edits that would change a vanilla Pal (audit finding pce-01).
    ///
    /// Route A shares a vanilla blueprint class through the new Pal's own DT_PalBPClass row,
    /// so any <c>blueprints/</c> patch keyed by that class (mesh scale, companion, ranch) would
    /// resize or retarget the vanilla Pal too. Route B allows such patches only on classes the
    /// custom pak provides. Bare <c>BP_X_C</c> keys are refused on both routes: in every log on
    /// this machine 0 of 125 bare keys were ever applied, while 231 of 231 full-path keys were.
    /// </summary>
    public static class RouteGuard
    {
        public static IReadOnlyList<string> Check(PalSchemaPackage package, NewPalPlan plan, ScanIndex scan)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            scan = scan ?? ScanIndex.Empty;
            var errors = new List<string>();

            foreach (var key in package.Blueprints.Edits.Keys)
            {
                if (!key.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"blueprints/ key '{key}' is a bare class name; the loader only ever applied full object paths (/Game/...).");
                    continue;
                }
                if (plan.Route == ModelRoute.A_SharedVanillaBlueprint)
                {
                    if (scan.IsEmpty || scan.ObjectPathExists(key))
                        errors.Add($"Route A: blueprints/ patch would change the vanilla class {key} that the base Pal uses too (pce-01). Choose Route B with a custom pak, or drop the edit.");
                    else if (plan.ModModel != null && plan.ModModel.KnownClassPaths.Any(p => string.Equals(StripClassSuffix(p), StripClassSuffix(key), StringComparison.OrdinalIgnoreCase)))
                        errors.Add($"Route A: blueprints/ patch would change {key}, a class of {plan.ModModel.Mod} that its own Pals use too. Drop the edit.");
                }
                else if (!plan.PakListing.Contains(key) && !plan.PakListing.Contains(StripClassSuffix(key)))
                {
                    errors.Add($"Route B: blueprints/ patch targets {key}, which is not in the pak listing; only classes the pak provides may be patched.");
                }
            }

            if (plan.Route == ModelRoute.B_CustomMeshPak)
            {
                foreach (var path in PathsIn(package, RawTables.PalBPClass, "BPClass").Concat(PathsIn(package, RawTables.CharacterIcon, "Icon")))
                {
                    if (!plan.PakListing.Contains(path) && !plan.PakListing.Contains(StripClassSuffix(path)))
                        errors.Add($"Route B: {path} is not in the pak listing; every blueprint and icon path of the new Pal must come from the pak.");
                }
            }

            return errors;
        }

        private static IEnumerable<string> PathsIn(PalSchemaPackage package, string table, string field)
        {
            if (!package.Raw.TryGetValue(table, out var rows)) yield break;
            foreach (var row in rows.Values)
            {
                var value = row[field]?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)row[field] : null;
                if (!string.IsNullOrEmpty(value)) yield return value;
            }
        }

        /// <summary>"/Game/X/BP_Y.BP_Y_C" -> "/Game/X/BP_Y" so a listing of asset paths matches class paths too.</summary>
        public static string StripClassSuffix(string objectPath)
        {
            if (string.IsNullOrEmpty(objectPath)) return objectPath;
            var dot = objectPath.LastIndexOf('.');
            return dot > 0 ? objectPath.Substring(0, dot) : objectPath;
        }
    }
}
