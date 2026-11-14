using System;
using System.Collections.Generic;
using ATS_API.Helpers;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.Persistence;
using Eremite;
using Eremite.Buildings;
using Eremite.Buildings.UI;
using Eremite.Model;
using Eremite.Services;
using HarmonyLib;

namespace ATS_ImperialLogistics.SupplyChain
{
    public static class SmartSupplyChainManager
    {
        private static bool _isAutoUpdatingLimit;
        private static bool _isLoadingSavedLimits;
        private static HashSet<string> _intermediateGoods;
        private static HashSet<string> _rawEdibleFoods;
        private static HashSet<string> _burnableFuels;

        // Reusable scratch collections to avoid GC allocations on 4s slow ticks
        private static readonly List<(IngredientState ing, float score)> ScratchRanked = new List<(IngredientState, float)>(12);
        private static readonly HashSet<string> ScratchCountedInSlot = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> ScratchConsumerCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void EnsureInitialized(bool forceRefreshGoods = false)
        {
            if (Svc.Cfg == null)
            {
                return;
            }

            if (_intermediateGoods == null)
            {
                _intermediateGoods = new HashSet<string>(StringComparer.Ordinal)
                {
                    GoodsTypes.Mat_Processed_Planks.ToName(),
                    GoodsTypes.Mat_Processed_Bricks.ToName(),
                    GoodsTypes.Mat_Processed_Fabric.ToName(),
                    GoodsTypes.Mat_Processed_Pipe.ToName(),
                    GoodsTypes.Crafting_Flour.ToName(),
                    GoodsTypes.Crafting_Dye.ToName(),
                    GoodsTypes.Vessel_Barrels.ToName(),
                    GoodsTypes.Vessel_Pottery.ToName(),
                    GoodsTypes.Vessel_Waterskin.ToName(),
                    GoodsTypes.Metal_Copper_Bar.ToName(),
                    GoodsTypes.Metal_Crystalized_Dew.ToName()
                };
            }

            if (_rawEdibleFoods == null || _burnableFuels == null || forceRefreshGoods)
            {
                _rawEdibleFoods = new HashSet<string>(StringComparer.Ordinal);
                _burnableFuels = new HashSet<string>(StringComparer.Ordinal);

                if (Svc.Cfg.Goods != null)
                {
                    foreach (var g in Svc.Cfg.Goods)
                    {
                        if (g == null || string.IsNullOrEmpty(g.Name)) continue;

                        bool isComplexCategory = g.category != null
                            && g.category.Name != null
                            && g.category.Name.IndexOf("Complex", StringComparison.OrdinalIgnoreCase) >= 0;

                        // Raw edible foods: villagers can eat directly (excludes Grain/Herbs and excludes Complex/Processed foods)
                        if (g.eatable
                            && !isComplexCategory
                            && g.Name.IndexOf("Processed", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            _rawEdibleFoods.Add(g.Name);
                        }

                        if (g.canBeBurned)
                        {
                            _burnableFuels.Add(g.Name);
                        }
                    }
                }
            }
        }

        public static void OnSettlementStarted()
        {
            // Refresh goods classification once all mods have finished registering custom goods
            EnsureInitialized(forceRefreshGoods: true);
        }

        public static void Tick()
        {
            if (Svc.Buildings?.Workshops == null || Svc.Buildings.Storages == null || Svc.Buildings.Storages.Count == 0 || Svc.Storage == null || Svc.Cfg == null)
            {
                return;
            }

            var modState = SettlementModState.Current;

            // If player disabled Smart Supply Chains while ingredients were floor-paused, restore them cleanly first
            if (!ModConfig.EnableSmartSupplyChains.Value)
            {
                if (modState.FloorPausedIngredients.Count > 0 || modState.PlayerManualFloorOverrides.Count > 0)
                {
                    RestoreAllFloorPausedIngredients(Svc.Buildings.Workshops.Values, modState);
                }
                return;
            }

            EnsureInitialized();

            var workshops = Svc.Buildings.Workshops.Values;
            int totalRawFood = CalculateTotalStock(_rawEdibleFoods);
            int totalFuel = CalculateTotalStock(_burnableFuels);

            bool protectFood = ModConfig.EnableReserveFloors.Value && totalRawFood <= ModConfig.RawFoodSafetyFloor.Value;
            bool restoreFood = !ModConfig.EnableReserveFloors.Value || totalRawFood >= ModConfig.RawFoodSafetyFloor.Value + 5;

            bool protectFuel = ModConfig.EnableReserveFloors.Value && totalFuel <= ModConfig.FuelSafetyFloor.Value;
            bool restoreFuel = !ModConfig.EnableReserveFloors.Value || totalFuel >= ModConfig.FuelSafetyFloor.Value + 5;

            Dictionary<string, int> consumerCounts = null;
            if (ModConfig.EnableSmartIntermediateCaps.Value)
            {
                consumerCounts = CountDownstreamConsumers(workshops);
            }

            bool stateDirty = false;
            bool hasTrackedFloorEntries = modState.FloorPausedIngredients.Count > 0 || modState.PlayerManualFloorOverrides.Count > 0;

            foreach (var ws in workshops)
            {
                if (ws == null || ws.state?.recipes == null || !ws.IsFinished())
                {
                    continue;
                }

                foreach (var recipe in ws.state.recipes)
                {
                    if (recipe == null || string.IsNullOrEmpty(recipe.productName)) continue;

                    // 1. Demand-scaled intermediate caps (respecting both local and global player-configured limits)
                    if (consumerCounts != null && _intermediateGoods.Contains(recipe.productName))
                    {
                        bool hasPlayerGlobalLimit = modState.PlayerLockedGlobalProductLimits.Contains(recipe.productName)
                            || (Svc.Workshops != null && Svc.Workshops.GetGlobalLimitFor(recipe.productName) > 0);
                        string lockKey = GetLimitLockKey(ws.Id, recipe.productName);

                        if (!hasPlayerGlobalLimit && !modState.PlayerLockedRecipeLimits.Contains(lockKey))
                        {
                            consumerCounts.TryGetValue(recipe.productName, out int consumers);
                            int desiredCap = ModConfig.IntermediateBaseCap.Value + (consumers * ModConfig.IntermediateCapPerConsumer.Value);
                            if (recipe.limit != desiredCap || recipe.isLimitLocal)
                            {
                                _isAutoUpdatingLimit = true;
                                try
                                {
                                    ws.ChangeLimitFor(recipe, desiredCap, false);
                                }
                                finally
                                {
                                    _isAutoUpdatingLimit = false;
                                }
                            }
                        }
                    }

                    if (!recipe.active || recipe.ingredients == null)
                    {
                        continue;
                    }

                    GoodModel producedModel = Svc.Cfg.ContainsGood(recipe.productName)
                        ? Svc.Cfg.GetGood(recipe.productName)
                        : null;
                    bool producesFood = producedModel != null && producedModel.eatable;
                    bool producesFuel = producedModel != null && producedModel.canBeBurned;

                    for (int setIdx = 0; setIdx < recipe.ingredients.Length; setIdx++)
                    {
                        var set = recipe.ingredients[setIdx];
                        if (set == null || set.Length == 0) continue;

                        // Step A: Restore any previously floor-paused ingredients (and reset temporary manual overrides) if stocks recovered
                        if (hasTrackedFloorEntries)
                        {
                            for (int i = 0; i < set.Length; i++)
                            {
                                var ing = set[i];
                                if (ing == null) continue;

                                bool isRawFood = _rawEdibleFoods.Contains(ing.good.name);
                                bool isFuel = _burnableFuels.Contains(ing.good.name);
                                bool canUnpause = (!isRawFood || producesFood || restoreFood)
                                               && (!isFuel || producesFuel || restoreFuel);

                                if (!canUnpause)
                                {
                                    continue;
                                }

                                string pauseKey = GetFloorPauseKey(ws.Id, recipe.productName, setIdx, ing.good.name);
                                string legacyKey = GetLegacyFloorPauseKey(ws.Id, recipe.model, setIdx, ing.good.name);

                                if (modState.FloorPausedIngredients.Remove(pauseKey) | modState.FloorPausedIngredients.Remove(legacyKey))
                                {
                                    ing.allowed = true;
                                    stateDirty = true;
                                }

                                if (modState.PlayerManualFloorOverrides.Remove(pauseKey))
                                {
                                    stateDirty = true;
                                }
                            }
                        }

                        // Step B: Check if any allowed non-protected alternative currently has enough stock
                        bool hasSafeStockedAlternative = false;
                        for (int i = 0; i < set.Length; i++)
                        {
                            var ing = set[i];
                            if (ing == null || !ing.allowed) continue;
                            bool shouldProtect = (!producesFood && protectFood && _rawEdibleFoods.Contains(ing.good.name))
                                              || (!producesFuel && protectFuel && _burnableFuels.Contains(ing.good.name));
                            if (!shouldProtect && Svc.Storage.Main.GetAmount(ing.good.name) >= ing.good.amount)
                            {
                                hasSafeStockedAlternative = true;
                                break;
                            }
                        }

                        // Step C: Apply floor pause when no safe alternative exists, respecting manual player overrides
                        if (!hasSafeStockedAlternative && ModConfig.EnableReserveFloors.Value && (protectFood || protectFuel))
                        {
                            for (int i = 0; i < set.Length; i++)
                            {
                                var ing = set[i];
                                if (ing == null || !ing.allowed) continue;
                                bool shouldProtect = (!producesFood && protectFood && _rawEdibleFoods.Contains(ing.good.name))
                                                  || (!producesFuel && protectFuel && _burnableFuels.Contains(ing.good.name));
                                if (!shouldProtect) continue;

                                string pauseKey = GetFloorPauseKey(ws.Id, recipe.productName, setIdx, ing.good.name);
                                if (modState.PlayerManualFloorOverrides.Contains(pauseKey))
                                {
                                    continue;
                                }

                                ing.allowed = false;
                                modState.FloorPausedIngredients.Add(pauseKey);
                                hasTrackedFloorEntries = true;
                                stateDirty = true;

                                // Return any buffered emergency units trapped in this workshop's internal storage if unused by sibling recipes
                                ReturnUnusedBufferedIngredientToWarehouse(ws, ing.good.name);
                            }
                        }

                        // Step D: Surplus-First Ingredient Priority (valid range in Workshop.GetIngredientFromSet is 0..3!)
                        if (ModConfig.EnableSurplusIngredientPriority.Value && set.Length > 1)
                        {
                            AssignSurplusPriorities(set, producesFood, protectFood, producesFuel, protectFuel);
                        }
                    }
                }
            }

            if (stateDirty)
            {
                SettlementModState.Save();
            }
        }

        private static void RestoreAllFloorPausedIngredients(IEnumerable<Workshop> workshops, SettlementModData modState)
        {
            foreach (var ws in workshops)
            {
                if (ws?.state?.recipes == null) continue;
                foreach (var recipe in ws.state.recipes)
                {
                    if (recipe?.ingredients == null) continue;
                    for (int setIdx = 0; setIdx < recipe.ingredients.Length; setIdx++)
                    {
                        var set = recipe.ingredients[setIdx];
                        if (set == null) continue;
                        for (int i = 0; i < set.Length; i++)
                        {
                            var ing = set[i];
                            if (ing == null) continue;
                            string pauseKey = GetFloorPauseKey(ws.Id, recipe.productName, setIdx, ing.good.name);
                            string legacyKey = GetLegacyFloorPauseKey(ws.Id, recipe.model, setIdx, ing.good.name);
                            if (modState.FloorPausedIngredients.Contains(pauseKey) || modState.FloorPausedIngredients.Contains(legacyKey))
                            {
                                ing.allowed = true;
                            }
                        }
                    }
                }
            }

            modState.FloorPausedIngredients.Clear();
            modState.PlayerManualFloorOverrides.Clear();
            SettlementModState.Save();
        }

        private static void ReturnUnusedBufferedIngredientToWarehouse(Workshop ws, string goodName)
        {
            if (ws?.IngredientsStorage?.goods == null || Svc.Storage == null || string.IsNullOrEmpty(goodName))
            {
                return;
            }

            int buffered = ws.IngredientsStorage.goods.GetAmount(goodName);
            if (buffered <= 0)
            {
                return;
            }

            // Keep buffered stock if another active recipe in this same workshop still allows this good
            if (ws.state?.recipes != null)
            {
                foreach (var other in ws.state.recipes)
                {
                    if (other == null || !other.active || other.ingredients == null) continue;
                    for (int s = 0; s < other.ingredients.Length; s++)
                    {
                        var set = other.ingredients[s];
                        if (set == null) continue;
                        for (int i = 0; i < set.Length; i++)
                        {
                            var ing = set[i];
                            if (ing != null && ing.allowed && string.Equals(ing.good.name, goodName, StringComparison.Ordinal))
                            {
                                return;
                            }
                        }
                    }
                }
            }

            var toReturn = new Good(goodName, buffered);
            Svc.Storage.Store(toReturn, ws.ModelName, ws.Id, StorageOperationType.IngredientsReturn);
            ws.IngredientsStorage.goods.Remove(toReturn);
        }

        private static void AssignSurplusPriorities(
            IngredientState[] set,
            bool producesFood,
            bool protectFood,
            bool producesFuel,
            bool protectFuel)
        {
            ScratchRanked.Clear();
            for (int i = 0; i < set.Length; i++)
            {
                var ing = set[i];
                if (ing == null || !ing.allowed) continue;

                int stored = Svc.Storage.Main.GetAmount(ing.good.name);
                int req = Math.Max(1, ing.good.amount);
                float score = (float)stored / req;

                bool isProtected = (!producesFood && protectFood && _rawEdibleFoods.Contains(ing.good.name))
                                || (!producesFuel && protectFuel && _burnableFuels.Contains(ing.good.name));
                if (isProtected)
                {
                    score -= 10000f;
                }

                ScratchRanked.Add((ing, score));
            }

            if (ScratchRanked.Count <= 1)
            {
                if (ScratchRanked.Count == 1)
                {
                    ScratchRanked[0].ing.priority = Workshop.MaxPrio; // 3
                }
                return;
            }

            ScratchRanked.Sort((a, b) => b.score.CompareTo(a.score));

            // Map top surplus to priority 3, descending down to 0 (clamped to [0..Workshop.MaxPrio])
            int maxPrio = Math.Min(3, Workshop.MaxPrio);
            for (int rank = 0; rank < ScratchRanked.Count; rank++)
            {
                int assigned = Math.Max(0, maxPrio - rank);
                ScratchRanked[rank].ing.priority = assigned;
            }
        }

        private static Dictionary<string, int> CountDownstreamConsumers(IEnumerable<Workshop> workshops)
        {
            ScratchConsumerCounts.Clear();
            foreach (var ws in workshops)
            {
                if (ws == null || ws.state?.recipes == null || !ws.IsFinished() || ws.BuildingState.isSleeping)
                {
                    continue;
                }

                foreach (var r in ws.state.recipes)
                {
                    if (r == null || !r.active || r.ingredients == null) continue;

                    for (int s = 0; s < r.ingredients.Length; s++)
                    {
                        var set = r.ingredients[s];
                        if (set == null) continue;

                        ScratchCountedInSlot.Clear();
                        for (int i = 0; i < set.Length; i++)
                        {
                            var ing = set[i];
                            if (ing != null && ing.allowed && _intermediateGoods.Contains(ing.good.name))
                            {
                                ScratchCountedInSlot.Add(ing.good.name);
                            }
                        }

                        foreach (var g in ScratchCountedInSlot)
                        {
                            ScratchConsumerCounts.TryGetValue(g, out int c);
                            ScratchConsumerCounts[g] = c + 1;
                        }
                    }
                }
            }
            return ScratchConsumerCounts;
        }

        private static int CalculateTotalStock(HashSet<string> goodsSet)
        {
            int total = 0;
            foreach (var g in goodsSet)
            {
                total += Svc.Storage.Main.GetAmount(g);
            }
            return total;
        }

        public static string GetLimitLockKey(int workshopId, string productName) => $"{workshopId}:{productName}";

        public static string GetFloorPauseKey(int workshopId, string productName, int setIndex, string goodName)
            => $"{workshopId}:{productName}:{setIndex}:{goodName}";

        private static string GetLegacyFloorPauseKey(int workshopId, string recipeModel, int setIndex, string goodName)
            => $"{workshopId}:{recipeModel}:{setIndex}:{goodName}";

        public static void OnManualLimitChanged(Workshop workshop, WorkshopRecipeState recipe, int amount, bool isLocalChange)
        {
            if (_isAutoUpdatingLimit || workshop == null || recipe == null || string.IsNullOrEmpty(recipe.productName))
            {
                return;
            }

            // When vanilla Auto-Load Limits / LoadLimits runs, ignore untouched 0 resets so smart caps stay active on unconfigured goods
            if (_isLoadingSavedLimits && amount <= 0)
            {
                return;
            }

            bool changed = false;
            string key = GetLimitLockKey(workshop.Id, recipe.productName);
            if (SettlementModState.Current.PlayerLockedRecipeLimits.Add(key))
            {
                changed = true;
            }

            if (!isLocalChange && SettlementModState.Current.PlayerLockedGlobalProductLimits.Add(recipe.productName))
            {
                changed = true;
            }

            if (changed)
            {
                SettlementModState.Save();
            }
        }

        public static void OnGlobalLimitSet(string goodName, int limit)
        {
            if (_isAutoUpdatingLimit || string.IsNullOrEmpty(goodName))
            {
                return;
            }

            if (_isLoadingSavedLimits && limit <= 0)
            {
                return;
            }

            if (SettlementModState.Current.PlayerLockedGlobalProductLimits.Add(goodName))
            {
                SettlementModState.Save();
            }
        }

        public static void SetLoadingSavedLimits(bool loading)
        {
            _isLoadingSavedLimits = loading;
        }

        public static void OnManualIngredientToggled(Workshop workshop, WorkshopRecipeState recipe, IngredientState toggledIng)
        {
            if (workshop == null || recipe?.ingredients == null)
            {
                return;
            }

            var modState = SettlementModState.Current;
            bool changed = false;

            for (int s = 0; s < recipe.ingredients.Length; s++)
            {
                var set = recipe.ingredients[s];
                if (set == null) continue;
                for (int i = 0; i < set.Length; i++)
                {
                    var item = set[i];
                    if (item == null) continue;
                    if (toggledIng != null && !ReferenceEquals(item, toggledIng)) continue;

                    string key = GetFloorPauseKey(workshop.Id, recipe.productName, s, item.good.name);
                    string legacyKey = GetLegacyFloorPauseKey(workshop.Id, recipe.model, s, item.good.name);

                    if (modState.FloorPausedIngredients.Remove(key) | modState.FloorPausedIngredients.Remove(legacyKey))
                    {
                        changed = true;
                    }

                    if (item.allowed)
                    {
                        // Player explicitly enabled this ingredient — prevent safety floor from undoing their click until stocks recover
                        if (modState.PlayerManualFloorOverrides.Add(key))
                        {
                            changed = true;
                        }
                    }
                    else
                    {
                        if (modState.PlayerManualFloorOverrides.Remove(key))
                        {
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                SettlementModState.Save();
            }
        }
    }

    [HarmonyPatch]
    internal static class SupplyChainPatches
    {
        [HarmonyPatch(typeof(WorkshopsService), nameof(WorkshopsService.LoadLimits))]
        [HarmonyPrefix]
        private static void WorkshopsService_LoadLimits_Prefix()
        {
            SmartSupplyChainManager.SetLoadingSavedLimits(true);
        }

        [HarmonyPatch(typeof(WorkshopsService), nameof(WorkshopsService.LoadLimits))]
        [HarmonyFinalizer]
        private static void WorkshopsService_LoadLimits_Finalizer()
        {
            SmartSupplyChainManager.SetLoadingSavedLimits(false);
        }

        [HarmonyPatch(typeof(WorkshopsService), nameof(WorkshopsService.SetGlobalLimitFor))]
        [HarmonyPostfix]
        private static void WorkshopsService_SetGlobalLimitFor_Postfix(string goodModel, int limit)
        {
            try
            {
                SmartSupplyChainManager.OnGlobalLimitSet(goodModel, limit);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error tracking global recipe limit: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Workshop), nameof(Workshop.ChangeLimitFor))]
        [HarmonyPrefix]
        private static void Workshop_ChangeLimitFor_Prefix(Workshop __instance, WorkshopRecipeState recipe, int amount, bool isLocalChange)
        {
            try
            {
                SmartSupplyChainManager.OnManualLimitChanged(__instance, recipe, amount, isLocalChange);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error tracking workshop recipe limit: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(WorkshopRecipeSlot), "OnIngredientChanged")]
        [HarmonyPostfix]
        private static void WorkshopRecipeSlot_OnIngredientChanged_Postfix(IWorkshop ___workshop, WorkshopRecipeState ___state, IngredientState __0)
        {
            try
            {
                SmartSupplyChainManager.OnManualIngredientToggled(___workshop as Workshop, ___state, __0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error handling manual ingredient toggle: {ex.Message}");
            }
        }
    }
}
