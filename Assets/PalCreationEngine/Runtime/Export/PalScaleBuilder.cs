using System;
using System.Collections.Generic;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// How big a Pal renders, and the collision that has to move with it.
    ///
    /// Visible size is NOT the Size enum. DT_PalSizeParameter holds only
    /// EffectScale and JumpEffectOffsetZ, and just six assets in the whole game
    /// read it -- it scales particles and footstep VFX, nothing else. What
    /// actually resizes a Pal is RelativeScale3D on the Blueprint's
    /// CharacterMesh0 component.
    ///
    /// Vanilla proves both directions: BP_PlantSlime_BOSS renders at 4.0 and
    /// BP_NightLady_RAID at 5.0, while BP_YakushimaBoss001_Small is a bare
    /// RelativeScale3D of 0.2 over an inherited mesh -- same rig, same
    /// animations, one fifth the size. 46 of 292 Alphas are not scaled at all.
    /// </summary>
    public sealed class PalScale
    {
        /// <summary>Pal row key whose collision fields get updated.</summary>
        public string PalId;

        /// <summary>Blueprint object path of the borrowed model, from bp_paths.json.</summary>
        public string BlueprintObjectPath;

        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float ScaleZ = 1f;

        /// <summary>Collision half-height. Does not follow the mesh on its own.</summary>
        public float? CapsuleHalfHeight;
        public float? CapsuleRadius;

        /// <summary>
        /// Where the mesh sits relative to its capsule. Vanilla pairs a -200 Z
        /// with a 200 half-height, so this normally tracks the capsule.
        /// </summary>
        public float? MeshOffsetZ;

        public bool IsUniform =>
            Math.Abs(ScaleX - ScaleY) < 0.0001f && Math.Abs(ScaleY - ScaleZ) < 0.0001f;

        public void SetUniform(float scale)
        {
            ScaleX = ScaleY = ScaleZ = scale;
        }

        /// <summary>
        /// Real values shipped in the game, for a picker. Vanilla scales are
        /// hand-tuned per Pal rather than snapped to a grid -- 55 distinct
        /// values across 292 Alphas -- so these are reference points, not a set
        /// of allowed options.
        /// </summary>
        public static readonly (float Value, string Label)[] References =
        {
            (0.2f, "0.2 - smallest shipped (YakushimaBoss001_Small)"),
            (0.5f, "0.5 - YakushimaBoss001_Green"),
            (0.8f, "0.8 - Anubis, a normal Pal authored small"),
            (1.0f, "1.0 - unchanged (46 of 292 Alphas are this)"),
            (1.3f, "1.3 - most common Alpha bump"),
            (1.5f, "1.5 - typical Alpha"),
            (2.0f, "2.0 - large Alpha"),
            (2.5f, "2.5 - Ganesha, Kelpie"),
            (4.0f, "4.0 - PlantSlime, biggest Alpha"),
            (5.0f, "5.0 - NightLady_RAID, biggest shipped anywhere"),
        };
    }

    /// <summary>
    /// Emits a scale change as the two edits it really needs.
    ///
    /// Collision goes in the Pal's own DT row: MeshCapsuleHalfHeight,
    /// MeshCapsuleRadius and MeshRelativeLocation are ordinary fields there, and
    /// a 0.0 in the row means "use the Blueprint's value", so writing a real
    /// number overrides it. That half needs no blueprint edit at all.
    ///
    /// Only the mesh scale needs a blueprints/ edit, because RelativeScale3D
    /// lives on the Blueprint's component and has no data-table equivalent.
    /// </summary>
    public static class PalScaleBuilder
    {
        /// <summary>The mesh component's name on every Pal actor Blueprint.</summary>
        public const string MeshComponent = "CharacterMesh0";

        /// <summary>
        /// Suggested collision for a given scale, derived from the borrowed
        /// Pal's own capsule.
        ///
        /// Vanilla does NOT scale collision proportionally -- YakushimaBoss001
        /// goes 200/120 at scale 1.5 to 44/22 at scale 0.2, which is 0.22 and
        /// 0.18 against a mesh ratio of 0.13. So this is a starting point the
        /// user is expected to adjust, not a formula the game follows.
        /// </summary>
        public static (float HalfHeight, float Radius, float OffsetZ) SuggestCollision(
            float baseHalfHeight, float baseRadius, float scaleRatio)
        {
            var halfHeight = baseHalfHeight * scaleRatio;
            var radius = baseRadius * scaleRatio;
            // Vanilla pairs the mesh offset with the capsule height.
            return (halfHeight, radius, -halfHeight);
        }

        /// <summary>
        /// The capsule for a TARGET mesh scale when the borrowed model was authored at
        /// <paramref name="authoredScale"/>: the ratio is target / authored, not the absolute
        /// target (audit pce-08: a model authored at 1.5 with a target of 1.5 was being scaled
        /// by 1.5 again). On Route A the mesh-scale edit itself is refused by RouteGuard.
        /// </summary>
        public static (float HalfHeight, float Radius, float OffsetZ) SuggestCollisionForTarget(
            float baseHalfHeight, float baseRadius, float authoredScale, float targetScale)
        {
            var authored = authoredScale > 0f ? authoredScale : 1f;
            return SuggestCollision(baseHalfHeight, baseRadius, targetScale / authored);
        }

        public static void Apply(PalScale scale, PalSchemaMod mod, PalSchemaBlueprints blueprints)
        {
            if (scale == null) throw new ArgumentNullException(nameof(scale));
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (blueprints == null) throw new ArgumentNullException(nameof(blueprints));

            var problems = Validate(scale);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Scale is not usable:\n  " + string.Join("\n  ", problems));
            }

            // -- collision: plain data-table fields on the Pal's own row --
            if (scale.CapsuleHalfHeight.HasValue || scale.CapsuleRadius.HasValue
                                                 || scale.MeshOffsetZ.HasValue)
            {
                if (!mod.Pals.TryGetValue(scale.PalId, out var row))
                    mod.Pals[scale.PalId] = row = new PalMonsterParameterRow();

                if (scale.CapsuleHalfHeight.HasValue)
                    row.MeshCapsuleHalfHeight = scale.CapsuleHalfHeight.Value;
                if (scale.CapsuleRadius.HasValue)
                    row.MeshCapsuleRadius = scale.CapsuleRadius.Value;
                if (scale.MeshOffsetZ.HasValue)
                    row.MeshRelativeLocation = new PalVector(0f, 0f, scale.MeshOffsetZ.Value);
            }

            // -- mesh scale: the one part with no data-table equivalent --
            if (Math.Abs(scale.ScaleX - 1f) > 0.0001f
                || Math.Abs(scale.ScaleY - 1f) > 0.0001f
                || Math.Abs(scale.ScaleZ - 1f) > 0.0001f)
            {
                var components = blueprints.ForBlueprint(scale.BlueprintObjectPath);
                components[MeshComponent] = new Dictionary<string, object>
                {
                    ["RelativeScale3D"] = new Dictionary<string, object>
                    {
                        ["X"] = scale.ScaleX,
                        ["Y"] = scale.ScaleY,
                        ["Z"] = scale.ScaleZ,
                    },
                };
            }
        }

        public static IReadOnlyList<string> Validate(PalScale scale)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(scale.PalId))
                problems.Add("PalId is required.");

            var scaled = Math.Abs(scale.ScaleX - 1f) > 0.0001f
                         || Math.Abs(scale.ScaleY - 1f) > 0.0001f
                         || Math.Abs(scale.ScaleZ - 1f) > 0.0001f;

            if (scaled && string.IsNullOrWhiteSpace(scale.BlueprintObjectPath))
            {
                problems.Add(
                    "A mesh scale needs the borrowed Pal's Blueprint object path - " +
                    "RelativeScale3D lives on the Blueprint, not in any data table.");
            }

            foreach (var (axis, value) in new[]
                     { ("X", scale.ScaleX), ("Y", scale.ScaleY), ("Z", scale.ScaleZ) })
            {
                if (value <= 0f)
                    problems.Add($"Scale {axis} must be above zero.");
            }

            if (scale.CapsuleHalfHeight is <= 0f)
                problems.Add("Capsule half-height must be above zero.");
            if (scale.CapsuleRadius is <= 0f)
                problems.Add("Capsule radius must be above zero.");

            return problems;
        }

        /// <summary>
        /// Notes worth surfacing next to a scale control. These are consequences
        /// the game does not handle for you, each seen in the shipped data.
        /// </summary>
        public static IReadOnlyList<string> Advisories(PalScale scale)
        {
            var notes = new List<string>();
            var s = scale.ScaleX;

            if (Math.Abs(s - 1f) > 0.0001f && !scale.CapsuleHalfHeight.HasValue)
            {
                notes.Add(
                    "Collision is not scaled. The mesh will resize but the capsule stays " +
                    "as the borrowed Pal's, so the Pal is hit and blocked at the old size. " +
                    "Vanilla always adjusts both.");
            }

            if (Math.Abs(s - 1f) > 0.0001f)
            {
                notes.Add(
                    "Movement speeds are in world units, not body lengths. At " +
                    $"{s:0.##}x the Pal will look like it is " +
                    (s < 1f ? "skating" : "shuffling") +
                    " unless the speed fields are adjusted too.");
            }

            if (!scale.IsUniform)
            {
                notes.Add(
                    "Non-uniform scale is allowed - BP_FluffyBird_BOSS ships 1.4/1.3/1.3 - " +
                    "but only 2 Pals in the game do it, so it is untested territory.");
            }

            if (s > 5f || s < 0.2f)
            {
                notes.Add(
                    $"{s:0.##}x is outside the shipped range (0.2 to 5.0). Allowed, " +
                    "but no vanilla Pal is proof it behaves.");
            }

            return notes;
        }
    }
}
