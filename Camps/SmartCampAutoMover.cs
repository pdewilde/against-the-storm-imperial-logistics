using System;
using System.Collections.Generic;
using System.Reflection;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.Persistence;
using ATS_ImperialLogistics.UI;
using Eremite;
using Eremite.Buildings;
using Eremite.Buildings.UI;
using Eremite.MapObjects;
using Eremite.Model;
using Eremite.Services;
using Eremite.Services.Monitors;
using HarmonyLib;
using UnityEngine;

namespace ATS_ImperialLogistics.Camps
{
    public static class SmartCampAutoMover
    {
        private static readonly Dictionary<int, float> LastMoveUnscaledTime = new Dictionary<int, float>();
        private const float MoveCooldownSeconds = 18f;
        private const GridCheck CampPlacementCheck = GridCheck.GridOnlyOthers | GridCheck.Glades | GridCheck.Self;

        // Cached reflection handles for A* GraphNode area connectivity check (without adding AstarPathfindingProject.dll build dependency)
        private static bool _navReflectionInitialized;
        private static MethodInfo _roadsGetNodeMethod;
        private static PropertyInfo _nodeWalkableProp;
        private static PropertyInfo _nodeAreaProp;

        public static void OnSettlementStarted()
        {
            LastMoveUnscaledTime.Clear();
        }

        public static bool IsUnlocked(Building building)
        {
            return building != null && SettlementModState.Current.AutoMoveUnlockedCamps.Contains(building.Id);
        }

        public static bool IsPaused(Building building)
        {
            return building != null && SettlementModState.Current.AutoMovePausedCamps.Contains(building.Id);
        }

        public static bool HasCartUpgrade(Building building)
        {
            return building != null && SettlementModState.Current.CartUpgradedCamps.Contains(building.Id);
        }

        public static List<GoodCost> GetUnlockCost(bool isWoodcutter)
        {
            int packs = isWoodcutter
                ? ModConfig.WoodcutterAutoMoveBuildPacks.Value
                : ModConfig.GathererAutoMoveBuildPacks.Value;
            return new List<GoodCost> { EconomyHelper.BuildPacks(packs) };
        }

        public static List<GoodCost> GetCartUpgradeCost()
        {
            return new List<GoodCost>
            {
                EconomyHelper.TradePacks(ModConfig.CampCartUpgradeTradePacks.Value),
                EconomyHelper.WildfireEssence(ModConfig.CampCartUpgradeWildfireEssence.Value)
            };
        }

        public static void Tick()
        {
            // Never auto-relocate camps while the game is paused (Space / Esc / Popups)
            if (!ModConfig.EnableCampAutoMove.Value || Time.timeScale <= 0f)
            {
                return;
            }

            if (Svc.Buildings == null || Svc.Map == null || Svc.Construction == null)
            {
                return;
            }

            // Never relocate buildings while the player is actively dragging a building
            if (Svc.Mode?.BuildingMovingMode != null && Svc.Mode.BuildingMovingMode.Value)
            {
                return;
            }

            float now = Time.unscaledTime;

            // 1. Check Woodcutters' Camps (Camp)
            if (Svc.Buildings.Camps != null)
            {
                foreach (var camp in Svc.Buildings.Camps.Values)
                {
                    if (!CanConsiderAutoMove(camp, now)) continue;
                    TryEvaluateWoodcutterCamp(camp, now, manualForce: false);
                }
            }

            // 2. Check Gathering Camps (GathererHut)
            if (Svc.Buildings.GathererHuts != null)
            {
                foreach (var hut in Svc.Buildings.GathererHuts.Values)
                {
                    if (!CanConsiderAutoMove(hut, now)) continue;
                    TryEvaluateGathererHut(hut, now, manualForce: false);
                }
            }
        }

        private static bool CanConsiderAutoMove(ProductionBuilding building, float now)
        {
            if (building == null || !building.IsFinished() || building.BuildingState == null)
            {
                return false;
            }

            if (building.BuildingState.isSleeping || building.BuildingState.lifted)
            {
                return false;
            }

            if (!IsUnlocked(building) || IsPaused(building))
            {
                return false;
            }

            if (building.CountWorkers() <= 0)
            {
                return false;
            }

            if (LastMoveUnscaledTime.TryGetValue(building.Id, out float last) && (now - last) < MoveCooldownSeconds)
            {
                return false;
            }

            // Wait for workers to finish current harvest/delivery step so no progress is dropped
            return building.AllWorkersIdling();
        }

        public static bool TryEvaluateWoodcutterCamp(Camp camp, float now, bool manualForce)
        {
            if (camp?.model == null || camp.state == null || !camp.IsFinished() || camp.BuildingState.lifted || Svc.Resources?.AvailableResources == null)
            {
                return false;
            }

            var validTreesOnMap = CollectValidTreesForCamp(camp);
            if (validTreesOnMap.Count == 0)
            {
                return false;
            }

            float maxDist = camp.model.maxDistance;
            Vector2 currentCenter = GetBuildingCenter2D(camp.Field, camp.Size);

            int currentInRange = 0;
            float closestCurrentDist = float.MaxValue;

            for (int i = 0; i < validTreesOnMap.Count; i++)
            {
                var tree = validTreesOnMap[i];
                if (Svc.Resources.InDistance(camp, tree))
                {
                    currentInRange++;
                    float d = Vector2.Distance(currentCenter, new Vector2(tree.Field.x + 0.5f, tree.Field.y + 0.5f));
                    if (d < closestCurrentDist) closestCurrentDist = d;
                }
            }

            bool depleted = currentInRange == 0;
            bool onlyFringeLeft = currentInRange > 0 && closestCurrentDist > (maxDist * ModConfig.WoodcutterFringeRelocateRatio.Value);

            if (!manualForce && !depleted && !onlyFringeLeft)
            {
                return false;
            }

            var targetCoords = new List<Vector2>(validTreesOnMap.Count);
            for (int i = 0; i < validTreesOnMap.Count; i++)
            {
                var f = validTreesOnMap[i].Field;
                targetCoords.Add(new Vector2(f.x + 0.5f, f.y + 0.5f));
            }

            if (TryFindBestRelocationField(camp, targetCoords, maxDist, depleted ? 0 : currentInRange, closestCurrentDist, out Vector2Int bestField))
            {
                ExecuteSafeRelocation(camp, bestField, now);
                return true;
            }

            return false;
        }

        public static bool TryEvaluateGathererHut(GathererHut hut, float now, bool manualForce)
        {
            if (hut?.model == null || hut.state == null || !hut.IsFinished() || hut.BuildingState.lifted || Svc.Deposits?.AvailableDeposits == null)
            {
                return false;
            }

            var validDeposits = CollectValidDepositsForHut(hut);
            if (validDeposits.Count == 0)
            {
                return false;
            }

            if (!manualForce)
            {
                for (int i = 0; i < validDeposits.Count; i++)
                {
                    if (Svc.Deposits.InDistance(hut, validDeposits[i]))
                    {
                        return false;
                    }
                }
            }

            var targetCoords = new List<Vector2>(validDeposits.Count);
            for (int i = 0; i < validDeposits.Count; i++)
            {
                var dep = validDeposits[i];
                targetCoords.Add(new Vector2(dep.Field.x + dep.Size.x * 0.5f, dep.Field.y + dep.Size.y * 0.5f));
            }

            float maxDist = hut.model.maxDistance;
            if (TryFindBestRelocationField(hut, targetCoords, maxDist, 0, float.MaxValue, out Vector2Int bestField))
            {
                ExecuteSafeRelocation(hut, bestField, now);
                return true;
            }

            return false;
        }

        private static List<NaturalResource> CollectValidTreesForCamp(Camp camp)
        {
            var result = new List<NaturalResource>();
            CampMode mode = camp.state.mode;
            bool onlyMarked = mode == CampMode.OnlyMarked || mode == CampMode.NoGladesAndOnlyMarked;
            bool noGlades = mode == CampMode.NoGlades || mode == CampMode.NoGladesAndOnlyMarked;
            bool onlyMarkedGlades = mode == CampMode.OnlyMarkedGlades;

            var seen = new HashSet<NaturalResource>();
            if (camp.state.recipes != null)
            {
                foreach (var rState in camp.state.recipes)
                {
                    if (rState == null || !rState.active) continue;
                    var rModel = camp.GetRecipeModel(rState);
                    string refGood = rModel?.refGood?.Name;
                    if (string.IsNullOrEmpty(refGood)) continue;

                    if (!Svc.Resources.AvailableResources.TryGetValue(refGood, out var list) || list == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        var res = list[i];
                        if (res == null || res.State == null || !res.CanBeGathered() || !res.State.isAvailable)
                        {
                            continue;
                        }

                        if (onlyMarked && !res.IsMarked()) continue;
                        if (noGlades && res.State.isGladeEdge) continue;
                        if (onlyMarkedGlades && res.State.isGladeEdge && !res.IsMarked()) continue;

                        if (seen.Add(res))
                        {
                            result.Add(res);
                        }
                    }
                }
            }

            return result;
        }

        private static List<ResourceDeposit> CollectValidDepositsForHut(GathererHut hut)
        {
            var result = new List<ResourceDeposit>();
            if (hut.state.recipes == null) return result;

            var activeModels = new List<GathererHutRecipeModel>();
            foreach (var rState in hut.state.recipes)
            {
                if (rState != null && rState.active)
                {
                    var rm = hut.GetRecipeModel(rState);
                    if (rm != null) activeModels.Add(rm);
                }
            }

            if (activeModels.Count == 0) return result;

            var seen = new HashSet<int>();
            foreach (var kvp in Svc.Deposits.AvailableDeposits)
            {
                var list = kvp.Value;
                if (list == null) continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var dep = list[i];
                    if (dep == null || dep.State == null || !dep.IsActive() || dep.State.chargesLeft <= 0 || !dep.State.isAvailable)
                    {
                        continue;
                    }

                    bool gatherable = false;
                    for (int m = 0; m < activeModels.Count; m++)
                    {
                        if (dep.CanBeGathered(activeModels[m]))
                        {
                            gatherable = true;
                            break;
                        }
                    }

                    if (gatherable && seen.Add(dep.State.id))
                    {
                        result.Add(dep);
                    }
                }
            }

            return result;
        }

        private static bool TryFindBestRelocationField(
            ProductionBuilding building,
            List<Vector2> targetCenters,
            float maxDistance,
            int currentCountToBeat,
            float currentClosestDist,
            out Vector2Int bestField)
        {
            bestField = building.Field;
            Vector2 currentOriginCenter = GetBuildingCenter2D(building.Field, building.Size);

            // Gather storage centers to prefer locations close to Warehouses
            var storageCenters = new List<Vector2>();
            if (Svc.Buildings.Storages != null)
            {
                foreach (var st in Svc.Buildings.Storages.Values)
                {
                    if (st != null && st.IsFinished())
                    {
                        storageCenters.Add(GetBuildingCenter2D(st.Field, st.Size));
                    }
                }
            }

            uint? refWalkableArea = GetMainWarehouseWalkableArea(building);

            // Sort targets by proximity to nearest storage so we sample the best clusters first
            targetCenters.Sort((a, b) => GetMinDist(a, storageCenters, currentOriginCenter)
                .CompareTo(GetMinDist(b, storageCenters, currentOriginCenter)));

            int sampleTargets = Mathf.Min(targetCenters.Count, 45);
            int searchRadius = Mathf.Clamp(Mathf.FloorToInt(maxDistance * 0.6f), 3, 8);
            float effectiveGatherRadius = Mathf.Max(2f, maxDistance - 0.5f);

            var testedFields = new HashSet<Vector2Int>();
            float bestScore = float.NegativeInfinity;
            bool found = false;

            for (int t = 0; t < sampleTargets; t++)
            {
                Vector2 target = targetCenters[t];
                int baseX = Mathf.RoundToInt(target.x);
                int baseY = Mathf.RoundToInt(target.y);

                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    {
                        var candidate = new Vector2Int(baseX + dx, baseY + dy);
                        if (candidate == building.Field || !testedFields.Add(candidate))
                        {
                            continue;
                        }

                        if (!IsValidCampPlacement(building, candidate, refWalkableArea))
                        {
                            continue;
                        }

                        Vector2 candCenter = GetBuildingCenter2D(candidate, building.Size);
                        int targetsInRadius = 0;
                        float closestTarget = float.MaxValue;

                        for (int i = 0; i < targetCenters.Count; i++)
                        {
                            float d = Vector2.Distance(candCenter, targetCenters[i]);
                            if (d <= effectiveGatherRadius)
                            {
                                targetsInRadius++;
                                if (d < closestTarget) closestTarget = d;
                            }
                        }

                        if (targetsInRadius == 0)
                        {
                            continue;
                        }

                        // Ensure fringe relocation actually moves significantly closer to targets
                        if (currentCountToBeat > 0 && closestTarget >= currentClosestDist - 1.5f)
                        {
                            continue;
                        }

                        float storageDist = GetMinDist(candCenter, storageCenters, currentOriginCenter);
                        float moveDist = Vector2.Distance(currentOriginCenter, candCenter);

                        float score = (Mathf.Min(targetsInRadius, 14) * 10f)
                                    - (closestTarget * 4.5f)
                                    - (storageDist * 1.35f)
                                    - (moveDist * 0.25f);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestField = candidate;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private static bool IsValidCampPlacement(ProductionBuilding building, Vector2Int candidate, uint? refWalkableArea)
        {
            Vector2Int size = building.Size;
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    if (!Svc.Map.InBounds(candidate.x + x, candidate.y + y))
                    {
                        return false;
                    }
                }
            }

            if (!Svc.Construction.IsNotOnGlade(candidate, size))
            {
                return false;
            }

            if (!Svc.Map.CanPlaceOnGrid(
                    building,
                    candidate,
                    building.Rotation,
                    building.IsMirrored,
                    CampPlacementCheck))
            {
                return false;
            }

            // Verify A* pathfinding connectivity to the Main Warehouse so camps never teleport into isolated cliff/water pockets
            if (refWalkableArea.HasValue && !HasConnectedPerimeterTile(candidate, size, refWalkableArea.Value))
            {
                return false;
            }

            return true;
        }

        private static void EnsureNavReflection()
        {
            if (_navReflectionInitialized)
            {
                return;
            }
            _navReflectionInitialized = true;

            try
            {
                _roadsGetNodeMethod = AccessTools.Method(typeof(RoadsService), "GetNode", new[] { typeof(int), typeof(int) });
                if (_roadsGetNodeMethod != null)
                {
                    var nodeType = _roadsGetNodeMethod.ReturnType;
                    _nodeWalkableProp = AccessTools.Property(nodeType, "Walkable");
                    _nodeAreaProp = AccessTools.Property(nodeType, "Area");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Navigation connectivity reflection unavailable: {ex.Message}");
            }
        }

        private static bool TryGetTileArea(int x, int y, out uint area)
        {
            area = 0;
            if (_roadsGetNodeMethod == null || _nodeWalkableProp == null || _nodeAreaProp == null || Svc.Roads == null)
            {
                return false;
            }

            if (!Svc.Map.InBounds(x, y))
            {
                return false;
            }

            try
            {
                object node = _roadsGetNodeMethod.Invoke(Svc.Roads, new object[] { x, y });
                if (node == null) return false;
                bool walkable = (bool)_nodeWalkableProp.GetValue(node, null);
                if (!walkable) return false;
                area = (uint)_nodeAreaProp.GetValue(node, null);
                return area > 0;
            }
            catch
            {
                return false;
            }
        }

        private static uint? GetMainWarehouseWalkableArea(ProductionBuilding fallbackBuilding)
        {
            EnsureNavReflection();
            if (_roadsGetNodeMethod == null || Svc.Roads == null)
            {
                return null;
            }

            Building anchor = Svc.Buildings?.Storages != null && Svc.Buildings.Storages.Count > 0 && Svc.Storage?.Main != null
                ? (Building)Svc.Storage.Main
                : fallbackBuilding;

            if (anchor == null)
            {
                return null;
            }

            Vector2Int f = anchor.Field;
            Vector2Int s = anchor.Size;
            for (int x = -1; x <= s.x; x++)
            {
                for (int y = -1; y <= s.y; y++)
                {
                    if (x >= 0 && x < s.x && y >= 0 && y < s.y) continue;
                    if (TryGetTileArea(f.x + x, f.y + y, out uint area))
                    {
                        return area;
                    }
                }
            }

            return null;
        }

        private static bool HasConnectedPerimeterTile(Vector2Int candidate, Vector2Int size, uint targetArea)
        {
            for (int x = -1; x <= size.x; x++)
            {
                for (int y = -1; y <= size.y; y++)
                {
                    if (x >= 0 && x < size.x && y >= 0 && y < size.y) continue;
                    if (TryGetTileArea(candidate.x + x, candidate.y + y, out uint area) && area == targetArea)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ExecuteSafeRelocation(ProductionBuilding building, Vector2Int targetField, float now)
        {
            LastMoveUnscaledTime[building.Id] = now;

            // Stop active worker walk tasks cleanly just like BuildingMover.ResetWorkers
            if (building.Workers != null && Svc.Actors != null)
            {
                for (int i = 0; i < building.Workers.Length; i++)
                {
                    int actorId = building.Workers[i];
                    if (actorId > 0)
                    {
                        try
                        {
                            Svc.Actors.GetActor(actorId)?.StopCurrentTask();
                        }
                        catch
                        {
                            // Safe guard if actor already despawned
                        }
                    }
                }
            }

            Svc.Blackboard?.BuildingMoveStartedCopy?.OnNext(building);
            Svc.Blackboard?.BuildingMoveStarted?.OnNext(building);

            Svc.Map.RemoveFromGrid(building);

            Vector3 oldPos = building.Position;
            building.SetPosition(new Vector3(targetField.x, oldPos.y, targetField.y));

            Svc.Map.PlaceOnGrid(building);
            building.BuildingView?.ShowMoved(targetField, building.Size);
            building.BuildingMoved();

            Svc.Blackboard?.BuildingMoveFinishedCopy?.OnNext(building);
            Svc.Blackboard?.BuildingMoveFinished?.OnNext(building);

            if (ModConfig.NotifyOnCampAutoMove.Value && Svc.News != null)
            {
                Svc.News.PublishNews(
                    $"{building.DisplayName} relocated to fresh resources",
                    "Smart Auto-Move repositioned the camp near valid targets while respecting your glade rules.",
                    AlertSeverity.Info,
                    building.BuildingModel?.icon,
                    null);
            }
        }

        private static Vector2 GetBuildingCenter2D(Vector2Int field, Vector2Int size)
        {
            return new Vector2(field.x + size.x * 0.5f, field.y + size.y * 0.5f);
        }

        private static float GetMinDist(Vector2 point, List<Vector2> candidates, Vector2 fallback)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return Vector2.Distance(point, fallback);
            }

            float min = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                float d = Vector2.Distance(point, candidates[i]);
                if (d < min) min = d;
            }
            return min;
        }

        public static void RenderCampDock(BuildingPanel panel, ProductionBuilding building, bool isWoodcutter)
        {
            if (panel == null) return;
            if (!ModConfig.EnableCampAutoMove.Value || building == null || !building.IsFinished())
            {
                ImperialUiHelper.HideDock(panel.transform);
                return;
            }

            var dock = ImperialUiHelper.GetOrCreatePanelHeaderDock(panel.transform);
            dock.gameObject.SetActive(true);
            ImperialUiHelper.ClearChildren(dock);

            var row1 = ImperialUiHelper.CreateRow(dock, "AutoMoveRow");

            if (!IsUnlocked(building))
            {
                var costs = GetUnlockCost(isWoodcutter);
                bool canAfford = EconomyHelper.CanAfford(costs);
                string label = $"<b>Unlock Smart Auto-Move</b>  [{EconomyHelper.FormatCostSummary(costs)}]";

                ImperialUiHelper.CreateStyledButton(
                    row1,
                    "BtnUnlockAutoMove",
                    label,
                    canAfford,
                    () =>
                    {
                        if (!EconomyHelper.TrySpend(building.ModelName, building.Id, costs))
                        {
                            Svc.PlayClickFail();
                            RenderCampDock(panel, building, isWoodcutter);
                            return;
                        }

                        SettlementModState.Current.AutoMoveUnlockedCamps.Add(building.Id);
                        SettlementModState.Current.AutoMovePausedCamps.Remove(building.Id);
                        SettlementModState.Save();
                        Svc.PlayClickSuccess();
                        RenderCampDock(panel, building, isWoodcutter);
                    },
                    minWidth: 250f);
            }
            else
            {
                bool paused = IsPaused(building);
                string statusText = paused
                    ? "<color=#FFD966><b>Auto-Move: PAUSED</b></color> (Resume)"
                    : "<color=#A8E6A1><b>Auto-Move: ACTIVE</b></color> (Pause)";

                ImperialUiHelper.CreateStyledButton(
                    row1,
                    "BtnToggleAutoMove",
                    statusText,
                    true,
                    () =>
                    {
                        if (IsPaused(building))
                        {
                            SettlementModState.Current.AutoMovePausedCamps.Remove(building.Id);
                        }
                        else
                        {
                            SettlementModState.Current.AutoMovePausedCamps.Add(building.Id);
                        }
                        SettlementModState.Save();
                        Svc.PlayClickSuccess();
                        RenderCampDock(panel, building, isWoodcutter);
                    },
                    minWidth: 185f);

                ImperialUiHelper.CreateStyledButton(
                    row1,
                    "BtnMoveNow",
                    "<b>Scout & Move Now</b>",
                    true,
                    () =>
                    {
                        if (building == null || !building.IsFinished() || building.BuildingState == null || building.BuildingState.lifted || building.BuildingState.isSleeping)
                        {
                            Svc.PlayClickFail();
                            return;
                        }

                        bool moved = isWoodcutter && building is Camp c
                            ? TryEvaluateWoodcutterCamp(c, Time.unscaledTime, manualForce: true)
                            : (!isWoodcutter && building is GathererHut h && TryEvaluateGathererHut(h, Time.unscaledTime, manualForce: true));

                        if (moved)
                        {
                            Svc.PlayClickSuccess();
                        }
                        else
                        {
                            Svc.PlayClickFail();
                        }
                    },
                    minWidth: 130f);
            }

            // Row 2: Tier 2 Outpost Carts (+5 Carry & +20% Gather Speed)
            var row2 = ImperialUiHelper.CreateRow(dock, "CartRow");
            if (!HasCartUpgrade(building))
            {
                var cartCosts = GetCartUpgradeCost();
                bool canAffordCart = EconomyHelper.CanAfford(cartCosts);
                string cartLabel = $"<b>Outpost Carts (+5 Carry, +20% Speed)</b>  [{EconomyHelper.FormatCostSummary(cartCosts)}]";

                ImperialUiHelper.CreateStyledButton(
                    row2,
                    "BtnUpgradeCarts",
                    cartLabel,
                    canAffordCart,
                    () =>
                    {
                        if (!EconomyHelper.TrySpend(building.ModelName, building.Id, cartCosts))
                        {
                            Svc.PlayClickFail();
                            RenderCampDock(panel, building, isWoodcutter);
                            return;
                        }

                        SettlementModState.Current.CartUpgradedCamps.Add(building.Id);
                        if (building.UpgradableState != null)
                        {
                            building.UpgradableState.workersExtraCapacity += 5;
                            building.UpgradableState.productionRateBonus += 0.20f;
                        }
                        SettlementModState.Save();
                        Svc.PlayClickSuccess();
                        RenderCampDock(panel, building, isWoodcutter);
                    },
                    minWidth: 270f,
                    height: 23f,
                    fontSize: 11);
            }
            else
            {
                ImperialUiHelper.CreateStyledButton(
                    row2,
                    "BtnCartsOwned",
                    "<color=#FFD966>✓ Outpost Carts Active (+5 Carry, +20% Speed)</color>",
                    false,
                    null,
                    minWidth: 250f,
                    height: 22f,
                    fontSize: 11);
            }

            ImperialUiHelper.RefreshDockLayout(dock, () => RenderCampDock(panel, building, isWoodcutter));
        }
    }

    [HarmonyPatch]
    internal static class CampAutoMovePatches
    {
        [HarmonyPatch(typeof(CampPanel), nameof(CampPanel.Show))]
        [HarmonyPostfix]
        private static void CampPanel_Show_Postfix(CampPanel __instance, Camp ___camp)
        {
            try
            {
                SmartCampAutoMover.RenderCampDock(__instance, ___camp, isWoodcutter: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering Woodcutter Camp dock: {ex}");
            }
        }

        [HarmonyPatch(typeof(GathererHutPanel), nameof(GathererHutPanel.Show))]
        [HarmonyPostfix]
        private static void GathererHutPanel_Show_Postfix(GathererHutPanel __instance, GathererHut ___hut)
        {
            try
            {
                SmartCampAutoMover.RenderCampDock(__instance, ___hut, isWoodcutter: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering Gatherer Hut dock: {ex}");
            }
        }
    }
}
