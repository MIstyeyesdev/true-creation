using System;
using System.Collections.Generic;
using PalCreationEngine.Data;

namespace PalCreationEngine.Export
{
    /// <summary>
    /// A funnel companion: the helper that appears beside the player and acts on
    /// its own while the Pal is in the party.
    ///
    /// This is a third in-party system, independent of the other two. Daedream
    /// (DreamDemon) proves it: its DT_PartnerSkillParameter row has SkillName
    /// "Unknown" and PassiveSkills [], and its DT_PalMonsterParameter row has
    /// PassiveSkill1..4 all "None" -- the companion owes nothing to the passive
    /// pipeline or the activated partner skill. It is configured purely by
    /// properties on the Pal Blueprint's PalPartnerSkillParameterComponent.
    /// </summary>
    public sealed class CompanionSetup
    {
        /// <summary>Pal row key the companion belongs to.</summary>
        public string PalId;

        /// <summary>Object path of the Pal's own Blueprint, from bp_paths.json.</summary>
        public string BlueprintObjectPath;

        /// <summary>
        /// Funnel character class asset path, e.g.
        /// /Game/Pal/Blueprint/Character/Funnel/BP_FunnelCharacter_DreamDemon.BP_FunnelCharacter_DreamDemon_C
        ///
        /// This choice fixes the ELEMENT and the attack, because the class picks
        /// the AI action which picks the waza. There is no element field.
        /// </summary>
        public string FunnelCharacterClass;

        /// <summary>
        /// AI controller. Every shipped funnel Pal uses the same one, so this
        /// defaults to it rather than asking.
        /// </summary>
        public string FunnelControllerClass = DefaultControllerPath;

        /// <summary>
        /// Optional. Not funnel-exclusive -- BluePlatypus and DrillGame set it
        /// for their own moves with no funnel at all -- so it is left unset
        /// unless the caller means it.
        /// </summary>
        public string FunnelAttackWazaID;

        /// <summary>
        /// False writes the _NoAutoSpawn pair instead, which is how the Centaurs
        /// ship a companion that appears on command rather than whenever the Pal
        /// is in the party.
        /// </summary>
        public bool AutoSpawn = true;

        public const string DefaultControllerPath =
            "/Game/Pal/Blueprint/Character/Funnel/BP_FunnelCharacterAIController.BP_FunnelCharacterAIController_C";
    }

    /// <summary>
    /// Emits a companion as a PalSchema blueprints/ edit.
    ///
    /// CONFIDENCE, stated plainly: the ranch edits this tool also emits target
    /// StaticCharacterParameterComponent and ActionComponent, and both appear as
    /// properties on the Blueprint's CDO -- which is why two shipped mods can
    /// address them by name and are known to work.
    ///
    /// The funnel component does NOT appear on the CDO. In every Pal Blueprint it
    /// is a construction-script component named PalPartnerSkillParameter_GEN_VARIABLE.
    /// No published mod edits it. The shape below matches the working mods'
    /// structure, but whether PalSchema resolves a _GEN_VARIABLE component by name
    /// is untested -- check UE4SS.log after loading.
    /// </summary>
    public static class CompanionBuilder
    {
        /// <summary>
        /// The component's name as it appears in the Pal Blueprint. Not a CDO
        /// property, unlike the components the proven ranch edits target.
        /// </summary>
        public const string ComponentName = "PalPartnerSkillParameter_GEN_VARIABLE";

        /// <summary>Fallback key: the component's class name rather than its instance name.</summary>
        public const string ComponentClassName = "PalPartnerSkillParameterComponent";

        public static void Apply(
            CompanionSetup setup, PalSchemaBlueprints blueprints, bool useClassNameKey = false)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (blueprints == null) throw new ArgumentNullException(nameof(blueprints));

            var problems = Validate(setup);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Companion setup is not usable:\n  " + string.Join("\n  ", problems));
            }

            var payload = new Dictionary<string, object>();

            if (setup.AutoSpawn)
            {
                payload["FunnelCharacterClass"] = setup.FunnelCharacterClass;
                payload["FunnelControllerClass"] = setup.FunnelControllerClass;
            }
            else
            {
                payload["FunnelCharacterClass_NoAutoSpawn"] = setup.FunnelCharacterClass;
                payload["FunnelControllerClass_NoAutoSpawn"] = setup.FunnelControllerClass;
            }

            if (!string.IsNullOrWhiteSpace(setup.FunnelAttackWazaID))
            {
                payload["FunnelAttackWazaID"] =
                    setup.FunnelAttackWazaID.Contains("::")
                        ? setup.FunnelAttackWazaID
                        : "EPalWazaID::" + setup.FunnelAttackWazaID;
            }

            var components = blueprints.ForBlueprint(setup.BlueprintObjectPath);
            components[useClassNameKey ? ComponentClassName : ComponentName] = payload;
        }

        public static IReadOnlyList<string> Validate(CompanionSetup setup)
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(setup.PalId))
                problems.Add("PalId is required.");

            if (string.IsNullOrWhiteSpace(setup.BlueprintObjectPath))
            {
                problems.Add(
                    "The Pal's own Blueprint object path is required -- the companion is " +
                    "configured on that Blueprint, not in any data table.");
            }

            if (string.IsNullOrWhiteSpace(setup.FunnelCharacterClass))
            {
                problems.Add(
                    "A funnel character class is required. It also decides the element and " +
                    "attack, since the class picks the AI action which picks the waza.");
            }

            if (string.IsNullOrWhiteSpace(setup.FunnelControllerClass))
                problems.Add("A funnel controller class is required.");

            return problems;
        }

        /// <summary>
        /// Things worth telling the user before they ship this. All read off the
        /// shipped data rather than guessed.
        /// </summary>
        public static IReadOnlyList<string> Advisories(CompanionSetup setup)
        {
            var notes = new List<string>
            {
                "Unverified: this edits PalPartnerSkillParameter_GEN_VARIABLE, a " +
                "construction-script component. The ranch edits this tool emits target " +
                "CDO properties instead, and those are proven by shipped mods. Check " +
                "UE4SS.log after loading.",
            };

            if (!string.IsNullOrWhiteSpace(setup.FunnelCharacterClass)
                && setup.FunnelCharacterClass.Contains("FlowerRabbit"))
            {
                notes.Add(
                    "FlowerRabbit's companion collects items rather than attacking - " +
                    "its skill module is the CollectItem family, not ReticleTargetAttack.");
            }

            if (!setup.AutoSpawn)
            {
                notes.Add(
                    "_NoAutoSpawn only ships on BlackCentaur and SaintCentaur, and those " +
                    "pair it with a UniqueSkillModule that summons on command. Without an " +
                    "equivalent trigger the companion may never appear.");
            }

            if (!string.IsNullOrWhiteSpace(setup.FunnelAttackWazaID))
            {
                notes.Add(
                    "FunnelAttackWazaID is not funnel-exclusive - BluePlatypus and DrillGame " +
                    "set it with no funnel at all - so it does not by itself grant a companion.");
            }

            return notes;
        }
    }
}
