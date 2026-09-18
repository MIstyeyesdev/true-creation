using System;
using System.Collections.Generic;

namespace RingOfNoXp.Model
{
    /// <summary>How the equipped ring is meant to stop XP. Only the first is data-only.</summary>
    public enum PlayerXpMethod
    {
        /// <summary>DT_PassiveSkill_Main with PalExp_Increase. Pure PalSchema, no Lua. Pal XP only - the passive enum has no player-EXP member.</summary>
        PalXpOnlyDataOnly = 0,

        /// <summary>Lua applies the per-player Exp_Increase buff while the ring is worn. Per-player and multiplayer-correct; needs the native function name from a UE4SS dump.</summary>
        PerPlayerBuffLua = 1,

        /// <summary>Lua flips PalGameWorldSettings.OptionSettings.ExpRate to 0 while worn. Verified property path, but world-wide and it writes saved world config.</summary>
        WorldExpRateLua = 2
    }

    /// <summary>The DT_ItemDataTable fields both tabs share.</summary>
    [Serializable]
    public sealed class ItemCore
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";

        /// <summary>Shipped item this one is cloned from; fills the defaults and the icon.</summary>
        public string CopyFromItemId = "";
        public string IconPath = "";

        public string TypeA = "Consume";
        public string TypeB = "ConsumeOther";
        public int Rank = 1;
        public int Rarity = 0;
        public int MaxStack = 1;
        public float Weight = 1f;
        public int Price = 1000;
        public int SortId = 9999;

        /// <summary>"CommonArmor" for wearables; leave empty for a plain consumable.</summary>
        public string StaticClass = "";
        public string DynamicClass = "";
    }

    /// <summary>One DT_ItemRecipeDataTable row: five material slots, same as the game.</summary>
    [Serializable]
    public sealed class RecipeSpec
    {
        public bool Enabled = true;
        public int ProductCount = 1;
        public float WorkAmount = 3000f;
        public string UnlockItemId = "";
        public List<string> MaterialIds = new List<string> { "", "", "", "", "" };
        public List<int> MaterialCounts = new List<int> { 0, 0, 0, 0, 0 };

        public void Normalise()
        {
            while (MaterialIds.Count < 5) MaterialIds.Add("");
            while (MaterialCounts.Count < 5) MaterialCounts.Add(0);
        }
    }

    /// <summary>
    /// Tab 1. A drinkable/edible item plus its DT_StatusEffectFood row. This is
    /// the only route that cancels PLAYER xp with no Lua at all, because the
    /// buff enum (EPalFoodStatusEffectType) is the one place player EXP is
    /// exposed to data.
    /// </summary>
    [Serializable]
    public sealed class ConsumableSpec
    {
        public bool Enabled = true;
        public ItemCore Item = new ItemCore
        {
            Id = "NoXpTonic",
            Name = "Tonic of Stillness",
            Description = "Experience gained is reduced to nothing while this is in effect.",
            // MUST be a Food type. Every one of the 54 shipped items with a
            // DT_StatusEffectFood row is TypeA Food - the buff is applied by the
            // eating path, and a Consume/ConsumeOther item never reaches it.
            TypeA = "Food",
            TypeB = "FoodDishVegetable",
            // MUST be 1. A station accepts an item only when TypeA is in
            // TargetTypesA AND TypeB is in TargetTypesB AND Rank <= TargetRankMax.
            // That third gate is the one that silently blocked this item: the
            // Primitive Workbench is TargetRankMax 1, so a Rank 2 tonic was
            // rejected even after the blueprints/ patch added both types and
            // PalSchema logged "Loaded changes to BP_BuildObject_WorkBench_C".
            // The Workbench is also the ONLY build object in the game unlockable
            // at player level 1 (DT_TechnologyRecipeUnlock: Workbench LevelCap 1;
            // CampFire 2, MedicineFacility_01 12, CookingStove 17), so for a
            // no-XP run it is the only station that can ever be reached.
            Rank = 1,
            Rarity = 2,
            MaxStack = 99,
            Weight = 0.5f,
            Price = 2000,
            SortId = 2144
        };

        // DT_StatusEffectFood. Shipped rows prove negatives work (PoisonMushroom
        // runs HungerResist -25) and that -100 is the engine's "off" (the two
        // InvalidSlipDamage_* passives use exactly -100.0).
        public string Effect1Type = "Exp_Increase";
        public int Effect1Value = -100;
        public int Effect1Interval = 0;
        public string Effect2Type = "None";
        public int Effect2Value = 0;
        public int Effect2Interval = 0;

        /// <summary>Seconds. The longest duration any shipped dish uses is 1800.</summary>
        public int EffectTime = 1800;

        /// <summary>Must be at least 1 or the game has no reason to let you eat it. PoisonMushroom restores exactly 1.</summary>
        public int RestoreSatiety = 1;
        public int RestoreSanity = 0;
        public int RestoreHealth = 0;

        public RecipeSpec Recipe = new RecipeSpec
        {
            WorkAmount = 1500f,
            MaterialIds = new List<string> { "Pal_crystal_S", "Flour", "", "", "" },
            MaterialCounts = new List<int> { 3, 2, 0, 0, 0 }
        };

        // --- Technology unlock -------------------------------------------------
        // 1035 of the 1414 shipped recipes are referenced by NO technology row, so
        // a tech entry is not required for a recipe to exist. Leaving this off
        // keeps the item out of the tech tree UI entirely, which is the point.
        public bool TechUnlock = false;
        /// <summary>Player level the tech becomes available at. 1 = from the start.</summary>
        public int TechLevel = 1;
        /// <summary>Technology points it costs. 0 = free, no point spend.</summary>
        public int TechCost = 0;

        // --- Crafting stations -------------------------------------------------
        // Blueprint names (BP_BuildObject_*) of the stations this item should be
        // craftable at. A station only accepts an item when its TypeA is in
        // TargetTypesA AND its TypeB is in TargetTypesB, so ticking a station that
        // does not already accept both makes the generator patch that station via
        // PalSchema's blueprints/ loader.
        public List<string> Stations = new List<string>
        {
            "BP_BuildObject_WorkBench",          // Primitive Workbench - LevelCap 1, the only
                                                 // build object reachable without spending XP
            "BP_BuildObject_CampFire",           // Campfire - LevelCap 2. Needs NO patch at all:
                                                 // it already accepts Food + FoodDishVegetable and
                                                 // its TargetRankMax is 1, which a Rank 1 item passes.
            "BP_BuildObject_Factory_Hard_01",    // High-Quality Workbench
            "BP_BuildObject_Factory_Hard_02",    // Production Assembly Line
            "BP_BuildObject_Factory_Hard_03",    // Production Assembly Line II
            "BP_BuildObject_MedicineFacility_01",// Medieval Medicine Workbench
            "BP_BuildObject_MedicineFacility_02",// Electric Medicine Workbench
            "BP_BuildObject_MedicineFacility_03",// Advanced Medicine Workbench
        };
    }

    /// <summary>
    /// Tab 2. A worn accessory built the way Ring of Mercy is built: an
    /// otherwise-empty item row whose whole power is one PassiveSkillName
    /// pointing at one DT_PassiveSkill_Main row.
    /// </summary>
    [Serializable]
    public sealed class EquipSpec
    {
        public bool Enabled = true;
        public ItemCore Item = new ItemCore
        {
            Id = "Accessory_NoXpRing",
            Name = "Ring of Stillness",
            Description = "Its wearer learns nothing from what they survive.",
            CopyFromItemId = "Accessory_Nonkilling",
            TypeA = "Accessory",
            TypeB = "Accessory",
            Rank = 2,
            Rarity = 2,
            MaxStack = 1,
            Weight = 2f,
            Price = 16320,
            SortId = 2145,
            StaticClass = "CommonArmor",
            DynamicClass = "CommonArmor"
        };

        // DT_PassiveSkill_Main, shaped like the NonKilling row Ring of Mercy uses.
        public string PassiveId = "NoXpRing";

        // The goal is PLAYER xp. EPalPassiveSkillEffectType has no player-EXP
        // member - PalExp_Increase is pal xp only - so no passive can do this.
        // The passive row stays (so the item's PassiveSkill link is not dangling)
        // but its value is 0, i.e. it changes nothing, until a real player-side
        // lever is found. Do not put -100 back here: it would silently suppress
        // pal xp, which this mod is explicitly not supposed to touch.
        public string Effect1Type = "PalExp_Increase";
        public float Effect1Value = 0f;
        public string Target1 = "ToSelf";
        public string Effect2Type = "no";
        public float Effect2Value = 0f;
        public string Target2 = "ToSelf";

        public int PassiveRank = -1;
        public int LotteryWeight = 100;
        public string Category = "SortNotDisplayable";
        public string OverrideDescMsgID = "";

        public bool InvokeAlways = true;
        public bool InvokeActiveOtomo = false;
        public bool InvokeWorker = false;
        public bool InvokeRiding = false;
        public bool InvokeReserve = false;
        public bool InvokeInOtomo = false;
        public bool InvokeInBaseCamp = false;

        public PlayerXpMethod Method = PlayerXpMethod.PalXpOnlyDataOnly;

        /// <summary>WorldExpRateLua only: value written back when the ring comes off, if the repair file is lost.</summary>
        public float FallbackExpRate = 1f;

        public RecipeSpec Recipe = new RecipeSpec
        {
            WorkAmount = 30000f,
            MaterialIds = new List<string> { "CopperIngot", "Pal_crystal_S", "PalCrystal_Ex", "", "" },
            MaterialCounts = new List<int> { 30, 20, 5, 0, 0 }
        };
    }

    /// <summary>One saved project: the package settings plus both variants.</summary>
    [Serializable]
    public sealed class RingProject
    {
        public string ModName = "Ring of No XP";
        public string PackageName = "RingOfNoXp";
        public string Author = "Michael";
        public string Version = "0.1.0";
        public string OutputFolder = "";

        /// <summary>The Palworld install to install into. Auto-detected on first open.</summary>
        public string GameFolder = "";

        /// <summary>Duplicates every InstallRule with IsServer, which a dedicated server needs.</summary>
        public bool AlsoDedicatedServer = true;

        public ConsumableSpec Consumable = new ConsumableSpec();
        public EquipSpec Equip = new EquipSpec();

        /// <summary>Where this project was saved. Not part of the mod.</summary>
        public string ProjectPath = "";
    }
}
