using System.Collections.Generic;

namespace PalCreationEngine.Export
{
    /// <summary>How the new Pal gets a model.</summary>
    public enum ModelRoute
    {
        /// <summary>
        /// Reuse an existing Pal's blueprint class through the new Pal's own DT_PalBPClass row
        /// (the shipped data does this 33 times). No pak. The shared class is never patched.
        /// </summary>
        A_SharedVanillaBlueprint = 0,

        /// <summary>
        /// A custom mesh in a LogicMods pak (the PalVariantPandemonium layout). Blueprint and
        /// icon paths must be inside the pak listing; scale and companion edits are allowed on
        /// those classes only.
        /// </summary>
        B_CustomMeshPak = 1,
    }

    public enum TribeMode
    {
        /// <summary>Write <c>enums/</c> with a new EPalTribeID member named after the Pal (the working mod's way).</summary>
        NewMember = 0,
        /// <summary>Use the base Pal's tribe; no enums file. Side effects UNTESTED (Paldex grouping, combi, partner-skill tribe conditions).</summary>
        BorrowBase = 1,
    }

    public enum SeedMode
    {
        /// <summary>Clone the base Pal's 90-field row, then apply identity (default).</summary>
        CloneBase = 0,
        /// <summary>Seed from the size medians (the old PalFactory route).</summary>
        SizeMedians = 1,
    }

    /// <summary>Player-facing texts; empty strings fall back to the base Pal's text.</summary>
    public sealed class NewPalTexts
    {
        public string DisplayName = "";
        public string BossPrefix = "";
        public string PartnerSkillName = "";
        public string FirstSpawnDesc = "";
        public string LongDesc = "";
    }

    /// <summary>A learnset entry override: move id (bare or namespaced) at a level.</summary>
    public sealed class LearnsetChoice
    {
        public string WazaId;
        public int Level;
    }

    /// <summary>
    /// A fixed breeding pair (DT_PalCombiUnique): two parent tribes, each optionally gender-locked
    /// (None = either), that always produce the new Pal. Tribes bare or namespaced.
    /// </summary>
    public sealed class CombiPair
    {
        public string ParentTribeA;
        public string ParentTribeB;
        public string ParentGenderA = "None";
        public string ParentGenderB = "None";
    }

    /// <summary>
    /// Everything the new-Pal builder needs. Defaults follow the verified working mod's
    /// per-Pal footprint (contract section 6): a regular and a boss row, own BPClass and icon
    /// rows, copied partner/camera/randomizer rows, a learnset, drops, texts, one FieldBoss
    /// spawn plus two Common spawns in the chosen areas, and a habitat map.
    /// </summary>
    public sealed class NewPalPlan
    {
        /// <summary>The new row key (internal id). Must not exist in any vanilla table or enum.</summary>
        public string PalId;

        /// <summary>The vanilla Pal whose model, rows and texts are the starting point.</summary>
        public string BasePalId;

        /// <summary>File stem used for every loader file: enums/&lt;ModName&gt;.json etc.</summary>
        public string ModName;

        public ModelRoute Route = ModelRoute.A_SharedVanillaBlueprint;

        /// <summary>
        /// Optional: a model from a mod installed on the PC running the tool ("Include mods on this PC", contract
        /// section 4 rule 10). Null = the base Pal's own vanilla blueprint, the default. Built in memory from that PC's
        /// mod folders and never written into shipped data; the package then depends on that mod.
        /// </summary>
        public PalCreationEngine.Lookup.ModModelChoice ModModel;

        /// <summary>Route B only: object paths the pak provides (one per line of a repak listing, normalised).</summary>
        public HashSet<string> PakListing = new HashSet<string>();

        public TribeMode Tribe = TribeMode.NewMember;
        public SeedMode Seed = SeedMode.CloneBase;

        /// <summary>
        /// The regular row as edited in the Creation Engine sections (New Pal by Area embeds them):
        /// already the base Pal's own row with the author's changes. When set it IS the regular row -
        /// the builder clones it and applies only identity and the Paldex slot, so what the sections
        /// show is what ships. Null: clone the base row untouched (Seed).
        /// </summary>
        public Data.PalMonsterParameterRow RowTemplate;
        public NewPalTexts Texts = new NewPalTexts();
        public string Language = "en";

        /// <summary>Null keeps the base Pal's ZukanIndex with a new suffix letter; -1 hides it from the Paldex order.</summary>
        public int? ZukanIndex;
        public string ZukanIndexSuffix;

        /// <summary>Null copies the base Pal's learnset.</summary>
        public List<LearnsetChoice> Learnset;

        /// <summary>Null copies the base's partner-skill parameter row unchanged.</summary>
        public string PartnerSkillNameOverride;

        /// <summary>Null copies the base's drop rows.</summary>
        public Data.PalDropItemRow RegularDrops;
        public Data.PalDropItemRow BossDrops;

        /// <summary>Fixed breeding pairs that produce this Pal (raw/DT_PalCombiUnique rows; the working mod ships 2-4 per Pal).</summary>
        public List<CombiPair> CombiPairs = new List<CombiPair>();

        /// <summary>Also write "this Pal + this Pal -> this Pal" so two of them breed true (the working mod does: PinkCat_PVPModElectric key 5029).</summary>
        public bool SelfBreedPair = true;

        public bool IncludeBoss = true;

        /// <summary>Map areas (DT_WorldMapAreaData ids) the Pal spawns in; the first is the primary.</summary>
        public List<string> AreaIds = new List<string>();

        /// <summary>FieldBoss (alpha) spawns per area; each copies a vanilla FieldBoss placement.</summary>
        public int BossSpawnsPerArea = 1;

        /// <summary>Common spawns per area (UNTESTED that the Common type works through spawns/).</summary>
        public int RegularSpawnsPerArea = 2;

        public bool Habitat = true;

        /// <summary>Alpha spawn level range; null = the area's own vanilla alpha range.</summary>
        public int? BossLevelMin, BossLevelMax;
        /// <summary>Regular spawn level range; null = the area's vanilla common range (10th-90th percentile).</summary>
        public int? RegularLevelMin, RegularLevelMax;
        /// <summary>How many appear per regular spawn (Num..Num_Max); null = 1..2.</summary>
        public int? RegularNumMin, RegularNumMax;

        /// <summary>Gear chain (SkillUnlock item + technology row). Off by default.</summary>
        public bool Gear = false;

        /// <summary>Also patch the area's sheet blueprints (append vs replace UNTESTED). Off.</summary>
        public bool ExperimentalSheetPatch = false;

        /// <summary>Also write DT_PalWildSpawner rows (no working mod does; runtime read UNTESTED). Off.</summary>
        public bool LegacyWildSpawner = false;

        public string BossId => "BOSS_" + PalId;
    }
}
