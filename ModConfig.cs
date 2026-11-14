using BepInEx.Configuration;

namespace ATS_ImperialLogistics
{
    public static class ModConfig
    {
        // Pillar 1: Smart Supply Chains
        public static ConfigEntry<bool> EnableSmartSupplyChains;
        public static ConfigEntry<bool> EnableSurplusIngredientPriority;
        public static ConfigEntry<bool> EnableReserveFloors;
        public static ConfigEntry<int> RawFoodSafetyFloor;
        public static ConfigEntry<int> FuelSafetyFloor;
        public static ConfigEntry<bool> EnableSmartIntermediateCaps;
        public static ConfigEntry<int> IntermediateBaseCap;
        public static ConfigEntry<int> IntermediateCapPerConsumer;

        // Pillar 2: Workshop Mastery
        public static ConfigEntry<bool> EnableWorkshopMastery;
        public static ConfigEntry<int> StarterFourthRecipeBuildPacks;
        public static ConfigEntry<int> BlueprintFourthRecipeBuildPacks;
        public static ConfigEntry<int> Grade1UpgradeBuildPacks;
        public static ConfigEntry<int> Grade2UpgradeBuildPacks;
        public static ConfigEntry<int> Grade2UpgradeTradePacks;
        public static ConfigEntry<int> Grade3UpgradeTradePacks;
        public static ConfigEntry<int> Grade3UpgradeWildfireEssence;

        // Pillar 3: Unlockable Smart Camp Auto-Move & Warehouse Logistics
        public static ConfigEntry<bool> EnableCampAutoMove;
        public static ConfigEntry<int> WoodcutterAutoMoveBuildPacks;
        public static ConfigEntry<int> GathererAutoMoveBuildPacks;
        public static ConfigEntry<int> CampCartUpgradeTradePacks;
        public static ConfigEntry<int> CampCartUpgradeWildfireEssence;
        public static ConfigEntry<float> WoodcutterFringeRelocateRatio;
        public static ConfigEntry<bool> NotifyOnCampAutoMove;
        public static ConfigEntry<bool> EnableWarehouseAutomatons;
        public static ConfigEntry<int> WarehouseAutomatonBuildPacks;
        public static ConfigEntry<int> WarehouseAutomatonTradePacks;
        public static ConfigEntry<int> WarehouseAutomatonWildfireEssence;

        // Pillar 4: Continue Settlement (Imperial Overtime)
        public static ConfigEntry<bool> EnableImperialOvertime;
        public static ConfigEntry<int> HearthStormWardBuildPacks;
        public static ConfigEntry<int> HearthStormWardWildfireEssence;
        public static ConfigEntry<int> HearthStormWardHostilityReduction;

        // Balanced Grand Order Meta Payouts
        public static ConfigEntry<bool> ScaleOvertimeRewardsWithDifficulty;
        public static ConfigEntry<int> Wave1BreadReward;
        public static ConfigEntry<int> Wave2BreadReward;
        public static ConfigEntry<int> Wave2MachineryReward;
        public static ConfigEntry<int> Wave2BonusEmbarkPoints;
        public static ConfigEntry<int> Wave3BreadReward;
        public static ConfigEntry<int> Wave3MachineryReward;
        public static ConfigEntry<int> Wave3ArtifactsReward;
        public static ConfigEntry<int> Wave3BonusEmbarkPoints;
        public static ConfigEntry<int> Wave3BonusCycleVillagers;

        // Pillar 5: Imperial Highways (Available Anytime)
        public static ConfigEntry<bool> EnableImperialHighways;
        public static ConfigEntry<float> HighwaySpeedBonus;
        public static ConfigEntry<int> HighwayBuildPacksCost;

        public static void Bind(ConfigFile config)
        {
            EnableSmartSupplyChains = config.Bind(
                "1. Supply Chains", "EnableSmartSupplyChains", true,
                "Master toggle for automatic ingredient surplus sorting, safety floors, and smart default intermediate caps.");
            EnableSurplusIngredientPriority = config.Bind(
                "1. Supply Chains", "EnableSurplusIngredientPriority", true,
                "Automatically prioritize whichever checked ingredient currently has the largest warehouse stockpile.");
            EnableReserveFloors = config.Bind(
                "1. Supply Chains", "EnableReserveFloors", true,
                "Protect minimum edible Raw Food and Fuel stocks from being consumed by non-food/non-fuel workshop recipes.");
            RawFoodSafetyFloor = config.Bind(
                "1. Supply Chains", "RawFoodSafetyFloor", 25,
                new ConfigDescription("Minimum total edible raw food kept safe from non-food crafting recipes.", new AcceptableValueRange<int>(0, 200)));
            FuelSafetyFloor = config.Bind(
                "1. Supply Chains", "FuelSafetyFloor", 25,
                new ConfigDescription("Minimum total fuel (Wood + Coal + Sea Marrow + Oil) kept safe from non-fuel crafting recipes.", new AcceptableValueRange<int>(0, 200)));
            EnableSmartIntermediateCaps = config.Bind(
                "1. Supply Chains", "EnableSmartIntermediateCaps", true,
                "Automatically set demand-scaled production limits on intermediate goods (Planks, Flour, Containers, etc.) unless manually edited.");
            IntermediateBaseCap = config.Bind(
                "1. Supply Chains", "IntermediateBaseCap", 25,
                new ConfigDescription("Base production limit assigned to intermediate goods.", new AcceptableValueRange<int>(5, 200)));
            IntermediateCapPerConsumer = config.Bind(
                "1. Supply Chains", "IntermediateCapPerConsumer", 10,
                new ConfigDescription("Additional production cap added per active downstream workshop recipe using that intermediate.", new AcceptableValueRange<int>(0, 100)));

            EnableWorkshopMastery = config.Bind(
                "2. Workshop Mastery", "EnableWorkshopMastery", true,
                "Allow unlocking 1 curated thematic 4th recipe per workshop and upgrading individual recipe star ratings.");
            StarterFourthRecipeBuildPacks = config.Bind(
                "2. Workshop Mastery", "StarterFourthRecipeBuildPacks", 4,
                new ConfigDescription("Packs of Building Materials required to unlock the 4th recipe on Crude Workstation / Field Kitchen / Makeshift Post.", new AcceptableValueRange<int>(0, 50)));
            BlueprintFourthRecipeBuildPacks = config.Bind(
                "2. Workshop Mastery", "BlueprintFourthRecipeBuildPacks", 6,
                new ConfigDescription("Packs of Building Materials required to unlock the curated 4th recipe on standard workshops.", new AcceptableValueRange<int>(0, 50)));
            Grade1UpgradeBuildPacks = config.Bind(
                "2. Workshop Mastery", "Grade1UpgradeBuildPacks", 4,
                new ConfigDescription("Packs of Building Materials required to upgrade a single 0-star recipe to 1-star.", new AcceptableValueRange<int>(0, 50)));
            Grade2UpgradeBuildPacks = config.Bind(
                "2. Workshop Mastery", "Grade2UpgradeBuildPacks", 5,
                new ConfigDescription("Packs of Building Materials required (alongside Trade Packs) to upgrade a single 1-star recipe to 2-star.", new AcceptableValueRange<int>(0, 50)));
            Grade2UpgradeTradePacks = config.Bind(
                "2. Workshop Mastery", "Grade2UpgradeTradePacks", 3,
                new ConfigDescription("Packs of Trade Goods required to upgrade a single 1-star recipe to 2-star.", new AcceptableValueRange<int>(0, 50)));
            Grade3UpgradeTradePacks = config.Bind(
                "2. Workshop Mastery", "Grade3UpgradeTradePacks", 6,
                new ConfigDescription("Packs of Trade Goods required (alongside Wildfire Essence) to master a single 2-star recipe to 3-star.", new AcceptableValueRange<int>(0, 50)));
            Grade3UpgradeWildfireEssence = config.Bind(
                "2. Workshop Mastery", "Grade3UpgradeWildfireEssence", 1,
                new ConfigDescription("Wildfire Essence required to master a single 2-star recipe to 3-star.", new AcceptableValueRange<int>(0, 20)));

            EnableCampAutoMove = config.Bind(
                "3. Camp Auto-Move", "EnableCampAutoMove", true,
                "Allow unlocking Smart Auto-Move and Outpost Cart upgrades on Woodcutters and Gathering Camps.");
            WoodcutterAutoMoveBuildPacks = config.Bind(
                "3. Camp Auto-Move", "WoodcutterAutoMoveBuildPacks", 3,
                new ConfigDescription("Packs of Building Materials required to unlock Smart Auto-Move on a Woodcutters' Camp.", new AcceptableValueRange<int>(0, 50)));
            GathererAutoMoveBuildPacks = config.Bind(
                "3. Camp Auto-Move", "GathererAutoMoveBuildPacks", 4,
                new ConfigDescription("Packs of Building Materials required to unlock Smart Auto-Move on a Gathering Camp.", new AcceptableValueRange<int>(0, 50)));
            CampCartUpgradeTradePacks = config.Bind(
                "3. Camp Auto-Move", "CampCartUpgradeTradePacks", 4,
                new ConfigDescription("Packs of Trade Goods required for Tier 2 Camp Outpost Carts (+5 carry capacity & +20% gather speed).", new AcceptableValueRange<int>(0, 50)));
            CampCartUpgradeWildfireEssence = config.Bind(
                "3. Camp Auto-Move", "CampCartUpgradeWildfireEssence", 1,
                new ConfigDescription("Wildfire Essence required for Tier 2 Camp Outpost Carts.", new AcceptableValueRange<int>(0, 20)));
            WoodcutterFringeRelocateRatio = config.Bind(
                "3. Camp Auto-Move", "WoodcutterFringeRelocateRatio", 0.85f,
                new ConfigDescription("Relocate Woodcutters early if the closest valid tree is beyond this fraction of max radius (1.0 = wait until completely empty).", new AcceptableValueRange<float>(0.5f, 1.05f)));
            NotifyOnCampAutoMove = config.Bind(
                "3. Camp Auto-Move", "NotifyOnCampAutoMove", true,
                "Publish a subtle settlement notification whenever a camp automatically relocates.");
            EnableWarehouseAutomatons = config.Bind(
                "3. Camp Auto-Move", "EnableWarehouseAutomatons", true,
                "Allow deploying Warehouse Hauler Automatons & +5 Carry Capacity on Main and Small Warehouses (available anytime from Year 1).");
            WarehouseAutomatonBuildPacks = config.Bind(
                "3. Camp Auto-Move", "WarehouseAutomatonBuildPacks", 8,
                new ConfigDescription("Packs of Building Materials required to deploy a Warehouse Hauler Automaton & +5 Carry Capacity.", new AcceptableValueRange<int>(0, 50)));
            WarehouseAutomatonTradePacks = config.Bind(
                "3. Camp Auto-Move", "WarehouseAutomatonTradePacks", 6,
                new ConfigDescription("Packs of Trade Goods required to deploy a Warehouse Hauler Automaton.", new AcceptableValueRange<int>(0, 50)));
            WarehouseAutomatonWildfireEssence = config.Bind(
                "3. Camp Auto-Move", "WarehouseAutomatonWildfireEssence", 1,
                new ConfigDescription("Wildfire Essence required to deploy a Warehouse Hauler Automaton.", new AcceptableValueRange<int>(0, 20)));

            EnableImperialOvertime = config.Bind(
                "4. Continue Settlement Overtime", "EnableImperialOvertime", true,
                "Enable Grand Orders, persistent Citadel Bread/Machinery/Artifact rewards, and Hearth Storm-Wards in Continue Settlement.");
            HearthStormWardBuildPacks = config.Bind(
                "4. Continue Settlement Overtime", "HearthStormWardBuildPacks", 10,
                new ConfigDescription("Packs of Building Materials required to light a Hearth Storm-Ward in Continue Settlement.", new AcceptableValueRange<int>(0, 50)));
            HearthStormWardWildfireEssence = config.Bind(
                "4. Continue Settlement Overtime", "HearthStormWardWildfireEssence", 2,
                new ConfigDescription("Wildfire Essence required to light a Hearth Storm-Ward.", new AcceptableValueRange<int>(0, 20)));
            HearthStormWardHostilityReduction = config.Bind(
                "4. Continue Settlement Overtime", "HearthStormWardHostilityReduction", 45,
                new ConfigDescription("Hostility removed per upgraded Hearth Storm-Ward (offsets 1 year of passive hostility scaling).", new AcceptableValueRange<int>(5, 300)));

            ScaleOvertimeRewardsWithDifficulty = config.Bind(
                "4. Continue Settlement Overtime", "ScaleOvertimeRewardsWithDifficulty", true,
                "Scale Grand Order Citadel currency payouts (Food Stockpiles, Machinery, Artifacts) by the settlement's DifficultyModel.rewardsMultiplier and Smoldering City % currency bonus.");
            Wave1BreadReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave1BreadReward", 8,
                new ConfigDescription("Base (1.0x Settler) Citadel Food Stockpiles granted by Grand Order I.", new AcceptableValueRange<int>(0, 200)));
            Wave2BreadReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave2BreadReward", 12,
                new ConfigDescription("Base (1.0x Settler) Citadel Food Stockpiles granted by Grand Order II.", new AcceptableValueRange<int>(0, 200)));
            Wave2MachineryReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave2MachineryReward", 3,
                new ConfigDescription("Base (1.0x Settler) Citadel Machinery granted by Grand Order II.", new AcceptableValueRange<int>(0, 100)));
            Wave2BonusEmbarkPoints = config.Bind(
                "4. Continue Settlement Overtime", "Wave2BonusEmbarkPoints", 1,
                new ConfigDescription("Bonus Embarkation Points for remaining Cycle granted by Grand Order II.", new AcceptableValueRange<int>(0, 20)));
            Wave3BreadReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave3BreadReward", 16,
                new ConfigDescription("Base (1.0x Settler) Citadel Food Stockpiles granted by Grand Order III.", new AcceptableValueRange<int>(0, 200)));
            Wave3MachineryReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave3MachineryReward", 4,
                new ConfigDescription("Base (1.0x Settler) Citadel Machinery granted by Grand Order III.", new AcceptableValueRange<int>(0, 100)));
            Wave3ArtifactsReward = config.Bind(
                "4. Continue Settlement Overtime", "Wave3ArtifactsReward", 4,
                new ConfigDescription("Base (1.0x Settler) Citadel Artifacts granted by Grand Order III.", new AcceptableValueRange<int>(0, 100)));
            Wave3BonusEmbarkPoints = config.Bind(
                "4. Continue Settlement Overtime", "Wave3BonusEmbarkPoints", 1,
                new ConfigDescription("Bonus Embarkation Points for remaining Cycle granted by Grand Order III.", new AcceptableValueRange<int>(0, 20)));
            Wave3BonusCycleVillagers = config.Bind(
                "4. Continue Settlement Overtime", "Wave3BonusCycleVillagers", 1,
                new ConfigDescription("Bonus Starting Villagers for remaining Cycle granted by Grand Order III.", new AcceptableValueRange<int>(0, 10)));

            EnableImperialHighways = config.Bind(
                "5. Imperial Highways", "EnableImperialHighways", true,
                "Unlock Tier 4 Imperial Highways in the Roads build menu in all settlements (available anytime from Year 1).");
            HighwaySpeedBonus = config.Bind(
                "5. Imperial Highways", "HighwaySpeedBonus", 0.45f,
                new ConfigDescription("Villager walk speed bonus on Imperial Highways (0.45 = +45%, 1.50 = +150%, 5.00 = +500% walk speed).", new AcceptableValueRange<float>(0.30f, 5.00f)));
            HighwayBuildPacksCost = config.Bind(
                "5. Imperial Highways", "HighwayBuildPacksCost", 1,
                new ConfigDescription("Packs of Building Materials required per Imperial Highway tile (0 = free).", new AcceptableValueRange<int>(0, 10)));
        }
    }
}
