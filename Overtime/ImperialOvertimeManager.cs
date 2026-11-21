using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ATS_API.Effects;
using ATS_API.Helpers;
using ATS_API.Localization;
using ATS_API.Orders;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.Persistence;
using ATS_ImperialLogistics.UI;
using Eremite;
using Eremite.Buildings;
using Eremite.Buildings.UI;
using Eremite.Model;
using Eremite.Model.Effects;
using Eremite.Model.Orders;
using Eremite.Services;
using Eremite.Services.Monitors;
using Eremite.View.HUD.Orders;
using Eremite.View.Popups;
using Eremite.WorldMap;
using HarmonyLib;
using UniRx;
using UnityEngine;

namespace ATS_ImperialLogistics.Overtime
{
    public static class ImperialOvertimeManager
    {
        private static OrderModel _wave1Order;
        private static OrderModel _wave2Order;
        private static OrderModel _wave3Order;
        private static OrderModel _wave4Order;
        private static EffectModel _wave1RewardEffect;
        private static EffectModel _wave2RewardEffect;
        private static EffectModel _wave3RewardEffect;
        private static EffectModel _wave4RewardEffect;
        private static EffectModel _stormWardPerkEffect;
        private static IDisposable _orderCompletedSub;
        private static bool _modelsRegistered;

        private static MethodInfo _createFullOrderMethod;
        private static MethodInfo _startOrderMethod;
        private static FieldInfo _orderPickedSubjectField;
        private static FieldInfo _orderTrackingSubjectField;

        // Excess Reputation tracking (High Resolve + Events/Caches/Orders in Overtime)
        private static readonly MethodInfo RrcGetRoundedResolveMethod = AccessTools.Method(typeof(ResolveReputationCalculator), "GetRoundedResolveFor");
        private static readonly MethodInfo RrcCalculateReputationMethod = AccessTools.Method(typeof(ResolveReputationCalculator), "CalculateReputationFor");
        private static readonly FieldInfo RepBarTooltipPositiveField = AccessTools.Field(typeof(ReputationBarTooltipTrigger), "positive");
        private static float _lastResolveExcessRepPerSec;
        private static float _lastExcessRepSaveTime;

        public static bool IsInOvertime()
        {
            var obj = Svc.State?.GameObjectives;
            if (obj == null || !obj.finished || obj.hasLost)
            {
                return false;
            }

            // Supports both standard Reputation victories and Sealed Forest Ancient Seal victories
            return obj.hasWon || SettlementModState.Current.OvertimeInitialized;
        }

        public static void RegisterModelsOnInit()
        {
            if (_modelsRegistered || Svc.Cfg == null)
            {
                return;
            }
            _modelsRegistered = true;

            // 1. Register Storm-Ward Hostility Reduction Perk Effect
            _stormWardPerkEffect = CreateStormWardEffect();

            // 2. Register Local Supply Reward Effects with descriptive Citadel Payout Tooltips
            _wave1RewardEffect = CreateRewardEffect(
                "ImperialCharter_Reward1",
                "Imperial Charter Stipend",
                $"Grants +1 Wildfire Essence, +2 Parts, and banks +{Math.Max(0, ModConfig.Wave1BreadReward.Value)} Citadel Food Stockpiles (scales with Difficulty) on delivery.",
                GoodsTypes.Hearth_Parts, 1);

            var wave1PartsEffect = CreateRewardEffect(
                "ImperialCharter_Reward1_Parts",
                "Imperial Charter Parts",
                "Grants +2 Parts for expanding camps and infrastructure.",
                GoodsTypes.Mat_Processed_Parts, 2);

            _wave2RewardEffect = CreateRewardEffect(
                "ImperialCharter_Reward2",
                "Royal Convoy Payout",
                $"Grants +1 Wildfire Essence and banks +{Math.Max(0, ModConfig.Wave2BreadReward.Value)} Food Stockpiles & +{Math.Max(0, ModConfig.Wave2MachineryReward.Value)} Machinery (scales with Difficulty), plus +{Math.Max(0, ModConfig.Wave2BonusEmbarkPoints.Value)} Cycle Embarkation Point.",
                GoodsTypes.Hearth_Parts, 1);

            var wave2PartsEffect = CreateRewardEffect(
                "ImperialCharter_Reward2_Parts",
                "Royal Convoy Parts",
                "Grants +2 Parts for expanding camps and infrastructure.",
                GoodsTypes.Mat_Processed_Parts, 2);

            _wave3RewardEffect = CreateRewardEffect(
                "ImperialCharter_Reward3",
                "Grand Viceroy Treasury",
                $"Grants +2 Wildfire Essence and banks +{Math.Max(0, ModConfig.Wave3BreadReward.Value)} Food Stockpiles, +{Math.Max(0, ModConfig.Wave3MachineryReward.Value)} Machinery, & +{Math.Max(0, ModConfig.Wave3ArtifactsReward.Value)} Artifacts (scales with Difficulty), plus +{Math.Max(0, ModConfig.Wave3BonusEmbarkPoints.Value)} Cycle Embarkation Point and +{Math.Max(0, ModConfig.Wave3BonusCycleVillagers.Value)} Cycle Starting Villager.",
                GoodsTypes.Hearth_Parts, 2);

            var wave3PartsEffect = CreateRewardEffect(
                "ImperialCharter_Reward3_Parts",
                "Grand Viceroy Parts",
                "Grants +3 Parts.",
                GoodsTypes.Mat_Processed_Parts, 3);

            _wave4RewardEffect = CreateRewardEffect(
                "ImperialCharter_Reward4",
                "Ancient Seal Expedition",
                $"Grants +2 Wildfire Essence, +4 Parts, and banks +{Math.Max(0, ModConfig.Wave4SealFragmentsReward.Value)} Cycle Seal Fragment(s) (scales with Difficulty).",
                GoodsTypes.Hearth_Parts, 2);

            var wave4PartsEffect = CreateRewardEffect(
                "ImperialCharter_Reward4_Parts",
                "Ancient Seal Expedition Parts",
                "Grants +4 Parts.",
                GoodsTypes.Mat_Processed_Parts, 4);

            // Safe neutral effect for reputationReward slot (since game is already won)
            EffectModel neutralRepReward = wave1PartsEffect;

            // 3. Register Wave I–IV Grand Orders (tiers empty so they never spawn in standard Year 1-5 draws)
            _wave1Order = BuildGrandOrder(
                "GrandOrder_Wave1",
                "Imperial Charter I: Industrial Foundation",
                new[]
                {
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Building_Materials, 25),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Trade_Goods, 25),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Provisions, 30)
                },
                _wave1RewardEffect,
                neutralRepReward);

            _wave2Order = BuildGrandOrder(
                "GrandOrder_Wave2",
                "Imperial Charter II: Royal Supply Convoy",
                new[]
                {
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Trade_Goods, 45),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Luxury_Goods, 35),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Crops, 35)
                },
                _wave2RewardEffect,
                wave2PartsEffect);

            _wave3Order = BuildGrandOrder(
                "GrandOrder_Wave3",
                "Imperial Charter III: Grand Viceroy Commission",
                new[]
                {
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Luxury_Goods, 90),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Trade_Goods, 90),
                    MakeGoodObjective(GoodsTypes.Tools_Simple_Tools, 45)
                },
                _wave3RewardEffect,
                wave3PartsEffect);

            _wave4Order = BuildGrandOrder(
                "GrandOrder_Wave4",
                "Imperial Charter IV: Ancient Seal Expedition",
                new[]
                {
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Luxury_Goods, 120),
                    MakeGoodObjective(GoodsTypes.Packs_Pack_Of_Trade_Goods, 120),
                    MakeGoodObjective(GoodsTypes.Tools_Simple_Tools, 60)
                },
                _wave4RewardEffect,
                wave4PartsEffect);

            Plugin.Log.LogInfo("ImperialOvertimeManager registered Grand Orders I–IV and Storm-Ward perks.");
        }

        private static EffectModel CreateStormWardEffect()
        {
            string shortName = "HearthStormWard_Effect";
            var builder = new EffectBuilder<HostilityEffectModel>(Plugin.PluginGuid, shortName, null);
            builder.SetPositive(true);
            builder.SetDisplayName("Imperial Storm-Ward", SystemLanguage.English);
            builder.SetDescription(
                $"Reduces Forest Hostility by {Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value)} via reinforced Hearth wildfire beacons.",
                SystemLanguage.English);

            var model = builder.EffectModel;
            var wildfireGood = GoodsTypes.Hearth_Parts.ToGoodModel();
            if (wildfireGood?.icon != null)
            {
                // Assign overrideIcon directly instead of builder.SetIcon(), which feeds non-readable vanilla GPU textures into TextMeshProManager
                model.overrideIcon = wildfireGood.icon;
            }

            model.amount = -Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);
            model.isPerk = true;
            return model;
        }

        private static EffectModel CreateRewardEffect(
            string shortName,
            string title,
            string description,
            GoodsTypes goodType,
            int amount)
        {
            var builder = new GoodsEffectBuilder(Plugin.PluginGuid, shortName);
            builder.SetPositive(true);
            builder.SetDisplayName(title, SystemLanguage.English);
            builder.SetDescription(description, SystemLanguage.English);
            builder.SetGood(amount, goodType.ToName());
            var model = builder.EffectModel;
            var gm = goodType.ToGoodModel();
            if (gm?.icon != null)
            {
                model.overrideIcon = gm.icon;
            }
            return model;
        }

        private static OrderLogic MakeGoodObjective(GoodsTypes goodType, int amount)
        {
            return new GoodLogic
            {
                goodRef = new GoodRef
                {
                    good = goodType.ToGoodModel(),
                    amount = amount
                }
            };
        }

        private static OrderModel BuildGrandOrder(
            string shortName,
            string displayTitle,
            OrderLogic[] objectives,
            EffectModel primaryReward,
            EffectModel secondaryReward)
        {
            var orderData = OrdersManager.New(Plugin.PluginGuid, shortName);
            var model = orderData.Model;

            model.isActive = true;
            model.canBeFailed = false;
            model.tiers = Array.Empty<OrderTierModel>();
            model.excludeOnBiomes = Array.Empty<BiomeModel>();
            model.usabilityTags = Array.Empty<ModelTag>();
            model.requirements = Array.Empty<BaseModelRequirement>();
            model.displayName = LocalizationManager.ToLocaText(Plugin.PluginGuid, shortName, "Title", displayTitle, SystemLanguage.English);
            model.hasCustomDeliverText = false;

            var logicSet = new OrderLogicsSet
            {
                difficulty = OrderDifficulty.Hard,
                logics = objectives
            };
            model.logicsSets = new[] { logicSet };

            model.rewards1 = new[]
            {
                new EffectsTableEntity { effect = primaryReward, chance = 100 }
            };
            model.rewards2 = Array.Empty<EffectsTableEntity>();
            model.rewards3 = Array.Empty<EffectsTableEntity>();
            model.reputationReward = secondaryReward;

            orderData.Sync();
            return model;
        }

        public static void OnSettlementStarted()
        {
            OnSettlementEnded();

            if (Svc.Orders?.OnOrderCompleted != null)
            {
                _orderCompletedSub = Svc.Orders.OnOrderCompleted.Subscribe(OnAnyOrderCompleted);
            }

            // If reloading a settlement already in Continue Settlement, ensure Overtime and active Grand Order wave are reconciled
            if (ModConfig.EnableImperialOvertime.Value && IsInOvertime())
            {
                EnsureOvertimeActive();
            }
        }

        public static void OnSettlementEnded()
        {
            _orderCompletedSub?.Dispose();
            _orderCompletedSub = null;
            _lastResolveExcessRepPerSec = 0f;
            _lastExcessRepSaveTime = 0f;
        }

        public static void Tick()
        {
            if (!ModConfig.EnableImperialOvertime.Value || VanillaSaveConverter.IsRevertLocked)
            {
                return;
            }

            if (IsInOvertime())
            {
                if (!SettlementModState.Current.OvertimeInitialized)
                {
                    // Wait until the Victory popup has closed and gameplay resumes (Time.timeScale > 0)
                    // so Overtime never activates behind the popup if the player clicks "Return to World Map".
                    if (Time.timeScale <= 0f)
                    {
                        return;
                    }
                    EnsureOvertimeActive();
                }
                else
                {
                    EnsureExpectedGrandOrderPresent();
                    SyncActiveGrandOrderProgress();
                }
            }
        }

        public static void EnsureOvertimeActive()
        {
            if (!ModConfig.EnableImperialOvertime.Value || VanillaSaveConverter.IsRevertLocked || Svc.Orders == null)
            {
                return;
            }

            if (!SettlementModState.Current.OvertimeInitialized)
            {
                SettlementModState.Current.OvertimeInitialized = true;
                SettlementModState.Save();

                Svc.News?.PublishNews(
                    "Imperial Overtime Charter Activated!",
                    "Check your Orders panel for Grand Orders that bank Citadel Stockpiles, Seal Fragments & Cycle Boons, and Hearths for Storm-Wards.",
                    AlertSeverity.Info,
                    GoodsTypes.Packs_Pack_Of_Luxury_Goods.ToGoodModel()?.icon,
                    null);
            }

            EnsureExpectedGrandOrderPresent();
            SyncActiveGrandOrderProgress();
        }

        private static void EnsureExpectedGrandOrderPresent()
        {
            var completed = SettlementModState.Current.CompletedGrandOrderWaves;
            int nextExpectedWave = 1;
            while (nextExpectedWave <= 4 && completed != null && completed.Contains(nextExpectedWave))
            {
                nextExpectedWave++;
            }

            if (nextExpectedWave <= 4)
            {
                SpawnGrandOrderWave(nextExpectedWave);
            }
        }

        private static void SpawnGrandOrderWave(int wave)
        {
            OrderModel targetModel = wave switch
            {
                1 => _wave1Order,
                2 => _wave2Order,
                3 => _wave3Order,
                4 => _wave4Order,
                _ => null
            };

            if (targetModel == null || Svc.Orders?.Orders == null)
            {
                return;
            }

            // Prevent duplicate injection if already in Orders list
            if (Svc.Orders.Orders.Any(o => o != null && o.model == targetModel.Name))
            {
                if (SettlementModState.Current.HighestGrandOrderWaveSpawned < wave)
                {
                    SettlementModState.Current.HighestGrandOrderWaveSpawned = wave;
                    SettlementModState.Save();
                }
                SyncActiveGrandOrderProgress();
                return;
            }

            try
            {
                if (_createFullOrderMethod == null)
                {
                    _createFullOrderMethod = AccessTools.Method(typeof(OrdersService), "CreateFullOrder");
                }
                if (_startOrderMethod == null)
                {
                    _startOrderMethod = AccessTools.Method(typeof(OrdersService), "StartOrder");
                }
                if (_orderPickedSubjectField == null)
                {
                    _orderPickedSubjectField = AccessTools.Field(typeof(OrdersService), "orderPickedSubject");
                }

                if (_createFullOrderMethod == null)
                {
                    Plugin.Log.LogError("OrdersService.CreateFullOrder method not found via reflection; cannot spawn Grand Order.");
                    return;
                }

                string fallbackTier = Svc.Cfg.ordersTiers != null && Svc.Cfg.ordersTiers.Length > 0
                    ? Svc.Cfg.ordersTiers[0].Name
                    : string.Empty;

                float now = Svc.GameTimeSvc != null ? Svc.GameTimeSvc.Time : 0.01f;
                var orderState = (OrderState)_createFullOrderMethod.Invoke(
                    Svc.Orders,
                    new object[] { fallbackTier, targetModel, now, "ImperialOvertime", 0 });

                if (orderState != null)
                {
                    orderState.tracked = true;
                    Svc.Orders.Orders.Add(orderState);
                    _startOrderMethod?.Invoke(Svc.Orders, new object[] { orderState });

                    // Notify OrdersMonitor via orderPickedSubject so GoodLogic objectives initialize and subscribe to warehouse events!
                    var pickedSubject = _orderPickedSubjectField?.GetValue(Svc.Orders) as Subject<OrderState>;
                    pickedSubject?.OnNext(orderState);

                    SyncActiveGrandOrderProgress();

                    SettlementModState.Current.HighestGrandOrderWaveSpawned = Math.Max(SettlementModState.Current.HighestGrandOrderWaveSpawned, wave);
                    SettlementModState.Save();
                    Plugin.Log.LogInfo($"Spawned Imperial Overtime Grand Order Wave {wave} ({targetModel.Name}).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to spawn Grand Order Wave {wave}: {ex}");
            }
        }

        /// <summary>
        /// Ensures active Grand Orders always mirror current Main Warehouse stockpiles, including saves created
        /// before OrdersMonitor registration ran.
        /// </summary>
        private static void SyncActiveGrandOrderProgress()
        {
            if (Svc.Orders?.Orders == null || Svc.Storage == null || Svc.Cfg == null)
            {
                return;
            }

            foreach (var orderState in Svc.Orders.Orders)
            {
                if (orderState == null || orderState.completed || orderState.isFailed || !orderState.picked)
                {
                    continue;
                }

                OrderModel model = null;
                if (_wave1Order != null && orderState.model == _wave1Order.Name) model = _wave1Order;
                else if (_wave2Order != null && orderState.model == _wave2Order.Name) model = _wave2Order;
                else if (_wave3Order != null && orderState.model == _wave3Order.Name) model = _wave3Order;
                else if (_wave4Order != null && orderState.model == _wave4Order.Name) model = _wave4Order;

                if (model == null || orderState.objectives == null)
                {
                    continue;
                }

                var logics = model.GetLogics(orderState);
                if (logics == null)
                {
                    continue;
                }

                bool anyChanged = false;
                float now = Mathf.Max(0.01f, Svc.GameTimeSvc != null ? Svc.GameTimeSvc.Time : 0.01f);
                int count = Math.Min(orderState.objectives.Length, logics.Length);

                for (int i = 0; i < count; i++)
                {
                    var objState = orderState.objectives[i];
                    if (objState == null) continue;

                    if (objState.initTime <= 0f)
                    {
                        objState.initTime = now;
                    }

                    if (logics[i] is GoodLogic goodLogic && goodLogic.goodRef?.good != null)
                    {
                        int inWarehouse = Svc.Storage.GetAmount(goodLogic.goodRef.Name);
                        if (objState.amount != inWarehouse)
                        {
                            objState.amount = inWarehouse;
                            anyChanged = true;
                        }
                    }
                }

                if (anyChanged)
                {
                    if (_orderTrackingSubjectField == null)
                    {
                        _orderTrackingSubjectField = AccessTools.Field(typeof(OrdersService), "orderTrackingSubject");
                    }
                    var trackingSubject = _orderTrackingSubjectField?.GetValue(Svc.Orders) as Subject<OrderState>;
                    trackingSubject?.OnNext(orderState);
                }
            }
        }

        private static void OnAnyOrderCompleted(OrderState completedOrder)
        {
            if (!ModConfig.EnableImperialOvertime.Value || completedOrder == null || string.IsNullOrEmpty(completedOrder.model))
            {
                return;
            }

            if (_wave1Order != null && completedOrder.model == _wave1Order.Name)
            {
                GrantGrandOrderMetaRewards(1);
                SpawnGrandOrderWave(2);
            }
            else if (_wave2Order != null && completedOrder.model == _wave2Order.Name)
            {
                GrantGrandOrderMetaRewards(2);
                SpawnGrandOrderWave(3);
            }
            else if (_wave3Order != null && completedOrder.model == _wave3Order.Name)
            {
                GrantGrandOrderMetaRewards(3);
                SpawnGrandOrderWave(4);
            }
            else if (_wave4Order != null && completedOrder.model == _wave4Order.Name)
            {
                GrantGrandOrderMetaRewards(4);
            }
        }

        private static int ScaleSealFragments(int baseAmount)
        {
            if (baseAmount <= 0)
            {
                return 0;
            }

            float mult = 1f;
            if (ModConfig.ScaleOvertimeRewardsWithDifficulty.Value)
            {
                var diff = Svc.BiomeSvc?.Difficulty;
                if (diff != null && diff.rewardsMultiplier > 0f)
                {
                    mult = diff.rewardsMultiplier;
                }
            }

            return Math.Max(1, Mathf.RoundToInt(baseAmount * mult));
        }

        private static int ScaleMetaCurrency(MetaCurrencyTypes currencyType, int baseAmount)
        {
            if (baseAmount <= 0)
            {
                return 0;
            }

            var mc = new MetaCurrency(currencyType.ToName(), baseAmount);
            if (ModConfig.ScaleOvertimeRewardsWithDifficulty.Value)
            {
                var diff = Svc.BiomeSvc?.Difficulty;
                if (diff != null && diff.rewardsMultiplier > 0f)
                {
                    mc = mc * diff.rewardsMultiplier;
                }
                if (Svc.MetaPerks != null)
                {
                    mc = Svc.MetaPerks.GetMetaCurrencyGain(mc);
                }
            }

            return Math.Max(1, mc.amount);
        }

        private static string GetDifficultyLabel()
        {
            var diff = Svc.BiomeSvc?.Difficulty;
            if (!ModConfig.ScaleOvertimeRewardsWithDifficulty.Value || diff == null)
            {
                return string.Empty;
            }
            string name = diff.GetDisplayName();
            return string.IsNullOrEmpty(name)
                ? $" ({diff.rewardsMultiplier:F2}x Difficulty)"
                : $" ({name} {diff.rewardsMultiplier:F2}x)";
        }

        public static bool TryGetDynamicRewardDescription(EffectModel effect, out string description)
        {
            description = null;
            if (effect == null)
            {
                return false;
            }

            // Fast ReferenceEquals checks first before touching any Unity-backed service properties
            bool isWave1 = ReferenceEquals(effect, _wave1RewardEffect);
            bool isWave2 = ReferenceEquals(effect, _wave2RewardEffect);
            bool isWave3 = ReferenceEquals(effect, _wave3RewardEffect);
            bool isWave4 = ReferenceEquals(effect, _wave4RewardEffect);
            bool isWard = ReferenceEquals(effect, _stormWardPerkEffect);

            if (!isWave1 && !isWave2 && !isWave3 && !isWave4 && !isWard)
            {
                return false;
            }

            if (isWard)
            {
                int red = Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);
                description = $"Reduces Forest Hostility by {red} via reinforced Hearth wildfire beacons.";
                return true;
            }

            if (!Svc.Active)
            {
                return false;
            }

            string diffTag = GetDifficultyLabel();

            if (isWave1)
            {
                int w1Bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave1BreadReward.Value));
                description = $"Grants +1 Wildfire Essence, +2 Parts, and banks +{w1Bread} Citadel Food Stockpiles{diffTag} on delivery.";
                return true;
            }

            if (isWave2)
            {
                int w2Bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave2BreadReward.Value));
                int w2Mach = ScaleMetaCurrency(MetaCurrencyTypes.Machinery, Math.Max(0, ModConfig.Wave2MachineryReward.Value));
                int w2Emb = Math.Max(0, ModConfig.Wave2BonusEmbarkPoints.Value);
                description = $"Grants +1 Wildfire Essence and banks +{w2Bread} Food Stockpiles, +{w2Mach} Machinery{diffTag}, and +{w2Emb} Cycle Embarkation Point.";
                return true;
            }

            if (isWave3)
            {
                int w3Bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave3BreadReward.Value));
                int w3Mach = ScaleMetaCurrency(MetaCurrencyTypes.Machinery, Math.Max(0, ModConfig.Wave3MachineryReward.Value));
                int w3Art = ScaleMetaCurrency(MetaCurrencyTypes.Artifacts, Math.Max(0, ModConfig.Wave3ArtifactsReward.Value));
                int w3Emb = Math.Max(0, ModConfig.Wave3BonusEmbarkPoints.Value);
                int w3Vil = Math.Max(0, ModConfig.Wave3BonusCycleVillagers.Value);
                description = $"Grants +2 Wildfire Essence and banks +{w3Bread} Food Stockpiles, +{w3Mach} Machinery, +{w3Art} Artifacts{diffTag}, +{w3Emb} Cycle Embarkation Point, and +{w3Vil} Cycle Starting Villager.";
                return true;
            }

            int w4Fragments = ScaleSealFragments(Math.Max(0, ModConfig.Wave4SealFragmentsReward.Value));
            description = $"Grants +2 Wildfire Essence, +4 Parts, and banks +{w4Fragments} Cycle Seal Fragment{(w4Fragments == 1 ? string.Empty : "s")}{diffTag} toward reforging Ancient Seals.";
            return true;
        }

        private static void GrantGrandOrderMetaRewards(int wave)
        {
            if (!SettlementModState.Current.CompletedGrandOrderWaves.Add(wave))
            {
                return;
            }

            int baseBread = 0;
            int baseMachinery = 0;
            int baseArtifacts = 0;
            int embarkPts = 0;
            int cycleVillagers = 0;
            int baseSealFragments = 0;

            switch (wave)
            {
                case 1:
                    baseBread = Math.Max(0, ModConfig.Wave1BreadReward.Value);
                    break;
                case 2:
                    baseBread = Math.Max(0, ModConfig.Wave2BreadReward.Value);
                    baseMachinery = Math.Max(0, ModConfig.Wave2MachineryReward.Value);
                    embarkPts = Math.Max(0, ModConfig.Wave2BonusEmbarkPoints.Value);
                    break;
                case 3:
                    baseBread = Math.Max(0, ModConfig.Wave3BreadReward.Value);
                    baseMachinery = Math.Max(0, ModConfig.Wave3MachineryReward.Value);
                    baseArtifacts = Math.Max(0, ModConfig.Wave3ArtifactsReward.Value);
                    embarkPts = Math.Max(0, ModConfig.Wave3BonusEmbarkPoints.Value);
                    cycleVillagers = Math.Max(0, ModConfig.Wave3BonusCycleVillagers.Value);
                    break;
                case 4:
                    baseSealFragments = Math.Max(0, ModConfig.Wave4SealFragmentsReward.Value);
                    break;
            }

            int bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, baseBread);
            int machinery = ScaleMetaCurrency(MetaCurrencyTypes.Machinery, baseMachinery);
            int artifacts = ScaleMetaCurrency(MetaCurrencyTypes.Artifacts, baseArtifacts);
            int sealFragments = ScaleSealFragments(baseSealFragments);

            try
            {
                if (Svc.MetaEconomy != null)
                {
                    if (bread > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Food_Stockpiles.ToName(), bread));
                    }
                    if (machinery > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Machinery.ToName(), machinery));
                    }
                    if (artifacts > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Artifacts.ToName(), artifacts));
                    }
                }

                bool worldChanged = false;
                if (Svc.WorldState?.CycleEffects != null)
                {
                    if (embarkPts > 0)
                    {
                        Svc.WorldState.CycleEffects.bonusPreparationPoints += embarkPts;
                        worldChanged = true;
                    }
                    if (cycleVillagers > 0)
                    {
                        if (Svc.CycleEffects != null)
                        {
                            Svc.CycleEffects.ChangeEmbarkVillagers(cycleVillagers);
                        }
                        else
                        {
                            // Inside a settlement scene, CycleEffectsService may not be loaded; update CycleEffectsState
                            // and immediately call RebuildEmbarkBonuses() so the 3 pre-generated caravans gain the extra villager!
                            Svc.WorldState.CycleEffects.bonusEmbarkVillagers += cycleVillagers;
                            Svc.MetaState?.RebuildEmbarkBonuses();
                        }
                        worldChanged = true;
                    }
                }

                if (sealFragments > 0 && Svc.WorldState?.Cycle != null)
                {
                    Svc.WorldState.Cycle.sealFragments += sealFragments;
                    Svc.WorldState.Cycle.totalSealFragments += sealFragments;
                    worldChanged = true;
                }

                if (worldChanged)
                {
                    Svc.WorldState.SaveState();
                }

                // Persist both primary MetaSave and GameWonBackup so leaving Continue Settlement preserves earned Citadel currencies & rebuilt caravans
                Svc.MetaState?.SaveState();
                Svc.MetaState?.SaveGameWonBackup();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed persisting Meta/Cycle rewards for Wave {wave}: {ex}");
            }

            SettlementModState.Current.OvertimeBankedBread += bread;
            SettlementModState.Current.OvertimeBankedMachinery += machinery;
            SettlementModState.Current.OvertimeBankedArtifacts += artifacts;
            SettlementModState.Current.OvertimeBankedEmbarkPoints += embarkPts;
            SettlementModState.Current.OvertimeBankedCycleVillagers += cycleVillagers;
            SettlementModState.Current.OvertimeBankedSealFragments += sealFragments;
            SettlementModState.FlushToDisk(force: true);

            var summaryParts = new List<string>();
            if (bread > 0) summaryParts.Add($"+{bread} Food Stockpiles");
            if (machinery > 0) summaryParts.Add($"+{machinery} Machinery");
            if (artifacts > 0) summaryParts.Add($"+{artifacts} Artifacts");
            if (embarkPts > 0) summaryParts.Add($"+{embarkPts} Cycle Embark Point");
            if (cycleVillagers > 0) summaryParts.Add($"+{cycleVillagers} Cycle Starting Villager");
            if (sealFragments > 0) summaryParts.Add($"+{sealFragments} Seal Fragment{(sealFragments == 1 ? string.Empty : "s")}");

            string diffTag = GetDifficultyLabel();
            string summaryLine = string.Join(", ", summaryParts);
            Plugin.Log.LogInfo($"[Imperial Overtime] Completed Grand Order Wave {wave}{diffTag}! Banked permanently to MetaSave/WorldSave: {summaryLine}");

            Svc.News?.PublishNews(
                $"Grand Order {wave} Completed — Citadel Treasury Updated!",
                $"Banked permanently{diffTag}: {summaryLine}.",
                AlertSeverity.Info,
                GoodsTypes.Packs_Pack_Of_Luxury_Goods.ToGoodModel()?.icon,
                null);
        }

        public static bool IsExcessReputationActive()
        {
            return Svc.Active
                && !VanillaSaveConverter.IsRevertLocked
                && ModConfig.EnableImperialOvertime.Value
                && ModConfig.EnableOvertimeExcessReputation.Value
                && IsInOvertime()
                && SettlementModState.Current.OvertimeInitialized;
        }

        public static void TickResolveExcessReputation(ResolveReputationCalculator calculator)
        {
            if (calculator == null || !IsExcessReputationActive() || Time.timeScale <= 0f)
            {
                _lastResolveExcessRepPerSec = 0f;
                return;
            }

            if (Svc.Effects != null && Svc.Effects.IsReputationFromResolveBlocked())
            {
                _lastResolveExcessRepPerSec = 0f;
                return;
            }

            var races = Svc.Races?.Races;
            if (races == null || Svc.Villagers == null || RrcGetRoundedResolveMethod == null || RrcCalculateReputationMethod == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.25f);
            float totalPerSec = 0f;

            for (int i = 0; i < races.Length; i++)
            {
                var race = races[i];
                if (race == null) continue;

                int pop = Svc.Villagers.GetRaceAmount(race.Name);
                if (pop <= 0) continue;

                int roundedResolve = (int)RrcGetRoundedResolveMethod.Invoke(calculator, new object[] { race });
                float perSec = (float)RrcCalculateReputationMethod.Invoke(calculator, new object[] { race, (float)roundedResolve, pop, 1f });
                if (perSec > 0f)
                {
                    totalPerSec += perSec;
                }
            }

            _lastResolveExcessRepPerSec = totalPerSec;
            if (totalPerSec > 0f && dt > 0f)
            {
                RecordExcessReputation(totalPerSec * dt, ReputationChangeSource.Resolve, "High Resolve");
            }
        }

        public static void RecordExcessReputation(float amount, ReputationChangeSource source, string reason)
        {
            if (amount <= 0f || !IsExcessReputationActive())
            {
                return;
            }

            var state = SettlementModState.Current;
            state.OvertimeExcessReputationProgress += amount;

            int granted = 0;
            while (state.OvertimeExcessReputationProgress >= 1f)
            {
                state.OvertimeExcessReputationProgress -= 1f;
                state.OvertimeExcessReputationMilestones++;
                GrantExcessReputationMilestone(state.OvertimeExcessReputationMilestones, source);
                granted++;
            }

            if (granted > 0)
            {
                SettlementModState.FlushToDisk(force: true);
                _lastExcessRepSaveTime = Time.unscaledTime;
            }
            else if (source != ReputationChangeSource.Resolve || Time.unscaledTime - _lastExcessRepSaveTime >= 10f)
            {
                SettlementModState.Save();
                _lastExcessRepSaveTime = Time.unscaledTime;
            }
        }

        private static void GrantExcessReputationMilestone(int milestone, ReputationChangeSource source)
        {
            var state = SettlementModState.Current;
            int majorEvery = Math.Max(1, ModConfig.ExcessReputationBonusEveryN.Value);
            bool isMajor = (milestone % majorEvery == 0);

            int embarkCap = Math.Max(0, ModConfig.ExcessReputationEmbarkPointsCap.Value);
            bool grantEmbarkPoint = isMajor && (embarkCap == 0 || state.OvertimeExcessReputationEmbarkPointsEarned < embarkCap);
            bool grantFallbackRare = isMajor && !grantEmbarkPoint;

            int amber = Math.Max(0, ModConfig.ExcessReputationAmberReward.Value);
            int wildfire = isMajor ? 1 : 0;
            int embarkPts = grantEmbarkPoint ? 1 : 0;
            int bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.ExcessReputationBreadReward.Value));
            int machinery = grantFallbackRare ? ScaleMetaCurrency(MetaCurrencyTypes.Machinery, 1) : 0;
            int artifacts = grantFallbackRare ? ScaleMetaCurrency(MetaCurrencyTypes.Artifacts, 1) : 0;

            try
            {
                if (Svc.Storage != null)
                {
                    if (amber > 0)
                    {
                        Svc.Storage.Store(new Good(GoodsTypes.Valuable_Amber.ToName(), amber), StorageOperationType.Effect);
                    }
                    if (wildfire > 0)
                    {
                        Svc.Storage.Store(new Good(GoodsTypes.Hearth_Parts.ToName(), wildfire), StorageOperationType.Effect);
                    }
                }

                if (Svc.MetaEconomy != null)
                {
                    if (bread > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Food_Stockpiles.ToName(), bread));
                    }
                    if (machinery > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Machinery.ToName(), machinery));
                    }
                    if (artifacts > 0)
                    {
                        Svc.MetaEconomy.Add(new MetaCurrency(MetaCurrencyTypes.Artifacts.ToName(), artifacts));
                    }
                }

                if (embarkPts > 0 && Svc.WorldState?.CycleEffects != null)
                {
                    Svc.WorldState.CycleEffects.bonusPreparationPoints += embarkPts;
                    Svc.WorldState.SaveState();
                }

                Svc.MetaState?.SaveState();
                Svc.MetaState?.SaveGameWonBackup();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed granting Excess Reputation milestone #{milestone}: {ex}");
            }

            state.OvertimeBankedBread += bread;
            state.OvertimeBankedMachinery += machinery;
            state.OvertimeBankedArtifacts += artifacts;
            state.OvertimeBankedEmbarkPoints += embarkPts;
            state.OvertimeExcessReputationEmbarkPointsEarned += embarkPts;

            var parts = new List<string>();
            if (amber > 0) parts.Add($"+{amber} Amber");
            if (wildfire > 0) parts.Add($"+{wildfire} Wildfire Essence");
            if (embarkPts > 0) parts.Add($"+{embarkPts} Cycle Embarkation Point");
            if (bread > 0) parts.Add($"+{bread} Citadel Food Stockpiles");
            if (machinery > 0) parts.Add($"+{machinery} Machinery");
            if (artifacts > 0) parts.Add($"+{artifacts} Artifact{(artifacts == 1 ? string.Empty : "s")}");

            string diffTag = GetDifficultyLabel();
            string summary = parts.Count > 0 ? string.Join(", ", parts) : "Acclaim recorded";
            string title = isMajor
                ? $"Grand Imperial Acclaim #{milestone} (Excess Reputation!)"
                : $"Imperial Acclaim #{milestone} (Excess Reputation)";

            Plugin.Log.LogInfo($"[Imperial Overtime] {title} via {source}{diffTag}: {summary}");

            var iconGood = isMajor ? GoodsTypes.Hearth_Parts : GoodsTypes.Valuable_Amber;
            Svc.News?.PublishNews(
                title,
                $"Awarded{diffTag}: {summary}.",
                AlertSeverity.Info,
                iconGood.ToGoodModel()?.icon,
                null);
        }

        public static bool TryBuildExcessReputationTooltip(ReputationBarTooltipTrigger trigger, out string desc)
        {
            desc = null;
            if (trigger == null || !IsExcessReputationActive())
            {
                return false;
            }

            bool isPositive = RepBarTooltipPositiveField != null && (bool)RepBarTooltipPositiveField.GetValue(trigger);
            if (!isPositive)
            {
                return false;
            }

            var state = SettlementModState.Current;
            int earned = state.OvertimeExcessReputationMilestones;
            int pct = Mathf.Clamp(Mathf.FloorToInt(state.OvertimeExcessReputationProgress * 100f), 0, 99);
            float perMin = _lastResolveExcessRepPerSec * 60f;

            int amber = Math.Max(0, ModConfig.ExcessReputationAmberReward.Value);
            int bread = ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.ExcessReputationBreadReward.Value));
            int majorEvery = Math.Max(1, ModConfig.ExcessReputationBonusEveryN.Value);
            int embarkCap = Math.Max(0, ModConfig.ExcessReputationEmbarkPointsCap.Value);
            bool canEarnEmbark = embarkCap == 0 || state.OvertimeExcessReputationEmbarkPointsEarned < embarkCap;

            int nextMilestone = earned + 1;
            int toMajor = majorEvery - (earned % majorEvery);
            bool nextIsMajor = (nextMilestone % majorEvery == 0);

            string majorRewardText = canEarnEmbark
                ? $"+1 Wildfire Essence & +1 Cycle Embarkation Point{(embarkCap > 0 ? $" ({state.OvertimeExcessReputationEmbarkPointsEarned}/{embarkCap})" : string.Empty)}"
                : $"+1 Wildfire Essence, +{ScaleMetaCurrency(MetaCurrencyTypes.Machinery, 1)} Machinery & +{ScaleMetaCurrency(MetaCurrencyTypes.Artifacts, 1)} Artifact";

            string majorLine = nextIsMajor
                ? $"<color=#FFD966>Next (#{nextMilestone}) is a Major Acclaim:</color> {majorRewardText}!"
                : $"Major Acclaim ({majorRewardText}) in {toMajor} pt{(toMajor == 1 ? string.Empty : "s")}.";

            desc = $"<color=#FFD966><b>Imperial Overtime Acclaim:</b></color> {earned} earned ({pct}% to #{nextMilestone})"
                + $"\nResolve Gain: <b>+{perMin:F2} / min</b> (also counts Caches, Events & Orders)"
                + $"\nPer +1.0 Rep: +{amber} Amber, +{bread} Citadel Food Stockpiles"
                + $"\n{majorLine}";
            return true;
        }

        public static string BuildOvertimeMoveOnSummary(string vanillaDesc)
        {
            var state = SettlementModState.Current;
            var completed = state.CompletedGrandOrderWaves;
            int charterCount = completed?.Count ?? 0;
            int excessMilestones = state.OvertimeExcessReputationMilestones;

            if (charterCount == 0 && excessMilestones == 0)
            {
                return $"{vanillaDesc}\n<size=85%><color=#FFD966><b>Imperial Overtime:</b></color> 0/4 Charters completed.</size>";
            }

            int totalBread = state.OvertimeBankedBread;
            int totalMachinery = state.OvertimeBankedMachinery;
            int totalArtifacts = state.OvertimeBankedArtifacts;
            int totalEmbarkPts = state.OvertimeBankedEmbarkPoints;
            int totalCycleVillagers = state.OvertimeBankedCycleVillagers;
            int totalSealFragments = state.OvertimeBankedSealFragments;

            // Fallback for older state files created before cumulative banked fields existed
            if (totalBread == 0 && totalMachinery == 0 && totalArtifacts == 0 && totalSealFragments == 0 && charterCount > 0)
            {
                if (completed.Contains(1)) totalBread += ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave1BreadReward.Value));
                if (completed.Contains(2))
                {
                    totalBread += ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave2BreadReward.Value));
                    totalMachinery += ScaleMetaCurrency(MetaCurrencyTypes.Machinery, Math.Max(0, ModConfig.Wave2MachineryReward.Value));
                    totalEmbarkPts += Math.Max(0, ModConfig.Wave2BonusEmbarkPoints.Value);
                }
                if (completed.Contains(3))
                {
                    totalBread += ScaleMetaCurrency(MetaCurrencyTypes.Food_Stockpiles, Math.Max(0, ModConfig.Wave3BreadReward.Value));
                    totalMachinery += ScaleMetaCurrency(MetaCurrencyTypes.Machinery, Math.Max(0, ModConfig.Wave3MachineryReward.Value));
                    totalArtifacts += ScaleMetaCurrency(MetaCurrencyTypes.Artifacts, Math.Max(0, ModConfig.Wave3ArtifactsReward.Value));
                    totalEmbarkPts += Math.Max(0, ModConfig.Wave3BonusEmbarkPoints.Value);
                    totalCycleVillagers += Math.Max(0, ModConfig.Wave3BonusCycleVillagers.Value);
                }
                if (completed.Contains(4))
                {
                    totalSealFragments += ScaleSealFragments(Math.Max(0, ModConfig.Wave4SealFragmentsReward.Value));
                }
            }

            var treasuryLines = new List<string>();
            if (totalBread > 0) treasuryLines.Add($"+{totalBread} Food Stockpiles");
            if (totalMachinery > 0) treasuryLines.Add($"+{totalMachinery} Machinery");
            if (totalArtifacts > 0) treasuryLines.Add($"+{totalArtifacts} Artifacts");

            var cycleLines = new List<string>();
            if (totalSealFragments > 0) cycleLines.Add($"+{totalSealFragments} Seal {(totalSealFragments == 1 ? "Fragment" : "Fragments")}");
            if (totalEmbarkPts > 0) cycleLines.Add($"+{totalEmbarkPts} Embarkation {(totalEmbarkPts == 1 ? "Pt" : "Pts")}");
            if (totalCycleVillagers > 0) cycleLines.Add($"+{totalCycleVillagers} Starting {(totalCycleVillagers == 1 ? "Villager" : "Villagers")}");

            // Replace vanilla boilerplate with a compact 3-line summary so DecisionPopup's fixed-height centered box never overflows
            string diffTag = GetDifficultyLabel();
            string excessTag = excessMilestones > 0 ? $" • {excessMilestones} Excess Rep" : string.Empty;
            string text = $"<size=88%><color=#FFD966><b>Imperial Overtime — {charterCount}/4 Charters{excessTag}{diffTag}</b></color>";
            if (treasuryLines.Count > 0)
            {
                text += $"\n<b>Citadel Banked:</b> {string.Join(", ", treasuryLines)}";
            }

            if (cycleLines.Count > 0)
            {
                text += $"\n<b>Cycle Boons:</b> {string.Join(", ", cycleLines)}";
            }

            return text + "</size>";
        }

        public static void FlushOvertimeOnMoveOn()
        {
            if (!IsInOvertime())
            {
                return;
            }

            try
            {
                SettlementModState.FlushToDisk(force: true);
                Svc.MetaState?.SaveState();
                Svc.WorldState?.SaveState();
                int count = SettlementModState.Current.CompletedGrandOrderWaves?.Count ?? 0;
                Plugin.Log.LogInfo($"[Imperial Overtime] Moving On to World Map — {count}/3 Grand Orders completed and saved to MetaSave & WorldSave.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error flushing Overtime state on MoveOn: {ex}");
            }
        }

        // --- Hearth Storm-Ward UI & Logic ---

        public static void RenderHearthDock(HearthPanel panel, Hearth hearth)
        {
            if (panel == null) return;
            if (!ModConfig.EnableImperialOvertime.Value || VanillaSaveConverter.IsRevertLocked || hearth == null || !hearth.IsFinished() || !IsInOvertime())
            {
                ImperialUiHelper.HideDock(panel.transform);
                return;
            }

            var dock = ImperialUiHelper.GetOrCreatePanelHeaderDock(panel.transform);
            dock.gameObject.SetActive(true);
            ImperialUiHelper.ClearChildren(dock);

            var row = ImperialUiHelper.CreateRow(dock, "StormWardRow");
            var modState = SettlementModState.Current;

            if (modState.StormWardAppliedReductionByHearth.TryGetValue(hearth.Id, out int appliedReduction)
                || modState.StormWardHearths.Contains(hearth.Id))
            {
                int shownReduction = appliedReduction > 0 ? appliedReduction : Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);
                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnWardActive",
                    $"<color=#FFD966>✓ Imperial Storm-Ward Lit (-{shownReduction} Hostility)</color>",
                    false,
                    null,
                    minWidth: 260f);
                ImperialUiHelper.RefreshDockLayout(dock);
                return;
            }

            int reduction = Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);
            var costs = new List<GoodCost>
            {
                EconomyHelper.BuildPacks(ModConfig.HearthStormWardBuildPacks.Value),
                EconomyHelper.WildfireEssence(ModConfig.HearthStormWardWildfireEssence.Value)
            };
            bool canAfford = EconomyHelper.CanAfford(costs);
            string label = $"<b>Light Storm-Ward (-{reduction} Hostility)</b>  [{EconomyHelper.FormatCostSummary(costs)}]";

            ImperialUiHelper.CreateStyledButton(
                row,
                "BtnLightWard",
                label,
                canAfford,
                () =>
                {
                    if (!EconomyHelper.TrySpend(hearth.ModelName, hearth.Id, costs))
                    {
                        Svc.PlayClickFail();
                        RenderHearthDock(panel, hearth);
                        return;
                    }

                    int applied = Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);
                    modState.StormWardHearths.Add(hearth.Id);
                    modState.StormWardAppliedReductionByHearth[hearth.Id] = applied;

                    if (_stormWardPerkEffect != null)
                    {
                        if (_stormWardPerkEffect is HostilityEffectModel hem)
                        {
                            hem.amount = -applied;
                        }
                        _stormWardPerkEffect.Apply();
                    }
                    else
                    {
                        Svc.Hostility?.RemoveEffectHostility(applied);
                    }

                    SettlementModState.Save();
                    Svc.PlayClickSuccess();
                    RenderHearthDock(panel, hearth);
                },
                minWidth: 280f);

            ImperialUiHelper.RefreshDockLayout(dock, () => RenderHearthDock(panel, hearth));
        }

        public static void OnHearthRemoved(Hearth hearth)
        {
            if (hearth == null) return;

            var modState = SettlementModState.Current;
            bool hadRecorded = modState.StormWardAppliedReductionByHearth.TryGetValue(hearth.Id, out int appliedReduction);
            modState.StormWardAppliedReductionByHearth.Remove(hearth.Id);
            bool hadSet = modState.StormWardHearths.Remove(hearth.Id);

            if (hadRecorded || hadSet)
            {
                int reduction = hadRecorded && appliedReduction > 0
                    ? appliedReduction
                    : Mathf.Abs(ModConfig.HearthStormWardHostilityReduction.Value);

                if (_stormWardPerkEffect != null)
                {
                    if (_stormWardPerkEffect is HostilityEffectModel hem)
                    {
                        hem.amount = -reduction;
                    }
                    _stormWardPerkEffect.Remove();
                }
                else
                {
                    Svc.Hostility?.AddEffectHostility(reduction);
                }
                SettlementModState.Save();
            }
        }

        // --- Warehouse Hauler Automaton UI & Logic ---

        public static void RenderStorageDock(StoragePanel panel, Storage storage)
        {
            if (panel == null) return;
            if (!ModConfig.EnableWarehouseAutomatons.Value || VanillaSaveConverter.IsRevertLocked || storage == null || !storage.IsFinished())
            {
                ImperialUiHelper.HideDock(panel.transform);
                return;
            }

            var dock = ImperialUiHelper.GetOrCreatePanelHeaderDock(panel.transform);
            dock.gameObject.SetActive(true);
            ImperialUiHelper.ClearChildren(dock);

            var row = ImperialUiHelper.CreateRow(dock, "StorageAutomatonRow");
            if (SettlementModState.Current.AutomatonStorages.Contains(storage.Id))
            {
                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnAutomatonActive",
                    "<color=#FFD966>✓ Hauler Automaton & +5 Carry Capacity Active</color>",
                    false,
                    null,
                    minWidth: 270f);
                ImperialUiHelper.RefreshDockLayout(dock);
                return;
            }

            var costs = new List<GoodCost>
            {
                EconomyHelper.BuildPacks(ModConfig.WarehouseAutomatonBuildPacks.Value),
                EconomyHelper.TradePacks(ModConfig.WarehouseAutomatonTradePacks.Value),
                EconomyHelper.WildfireEssence(ModConfig.WarehouseAutomatonWildfireEssence.Value)
            };
            bool canAfford = EconomyHelper.CanAfford(costs);
            string label = $"<b>Deploy Hauler Automaton & +5 Carry</b>  [{EconomyHelper.FormatCostSummary(costs)}]";

            ImperialUiHelper.CreateStyledButton(
                row,
                "BtnDeployAutomaton",
                label,
                canAfford,
                () =>
                {
                    if (!EconomyHelper.TrySpend(storage.ModelName, storage.Id, costs))
                    {
                        Svc.PlayClickFail();
                        RenderStorageDock(panel, storage);
                        return;
                    }

                    SettlementModState.Current.AutomatonStorages.Add(storage.Id);
                    if (storage.UpgradableState != null)
                    {
                        storage.UpgradableState.workersExtraCapacity += 5;
                    }
                    if (storage.state != null)
                    {
                        storage.state.extraRange += 5;
                    }

                    TrySpawnHaulerAutomaton(storage);
                    SettlementModState.Save();
                    Svc.PlayClickSuccess();
                    RenderStorageDock(panel, storage);
                },
                minWidth: 290f);

            ImperialUiHelper.RefreshDockLayout(dock, () => RenderStorageDock(panel, storage));
        }

        private static void TrySpawnHaulerAutomaton(Storage storage)
        {
            if (Svc.Automatons == null || Svc.Cfg?.Automatons == null || Svc.Cfg.Automatons.Length == 0)
            {
                return;
            }

            // Prefer Hauler automaton model if present, otherwise first non-workplace-occupying automaton
            AutomatonModel haulerModel = Svc.Cfg.Automatons
                .FirstOrDefault(a => a != null && a.Name.IndexOf("Hauler", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? Svc.Cfg.Automatons.FirstOrDefault(a => a != null && !a.occupiesWorkplace)
                ?? Svc.Cfg.Automatons[0];

            if (haulerModel != null)
            {
                Svc.Automatons.SpawnNewAutomaton(haulerModel, storage);
            }
        }
    }

    [HarmonyPatch]
    internal static class ImperialOvertimePatches
    {
        [HarmonyPatch(typeof(HearthPanel), nameof(HearthPanel.Show))]
        [HarmonyPostfix]
        private static void HearthPanel_Show_Postfix(HearthPanel __instance, Hearth hearth)
        {
            try
            {
                ImperialOvertimeManager.RenderHearthDock(__instance, hearth);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering Hearth dock: {ex}");
            }
        }

        [HarmonyPatch(typeof(Hearth), "OnRemoved")]
        [HarmonyPostfix]
        private static void Hearth_OnRemoved_Postfix(Hearth __instance)
        {
            try
            {
                ImperialOvertimeManager.OnHearthRemoved(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error handling Hearth removal: {ex}");
            }
        }

        [HarmonyPatch(typeof(StoragePanel), nameof(StoragePanel.Show))]
        [HarmonyPostfix]
        private static void StoragePanel_Show_Postfix(StoragePanel __instance, Storage storage)
        {
            try
            {
                ImperialOvertimeManager.RenderStorageDock(__instance, storage);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering Storage dock: {ex}");
            }
        }

        [HarmonyPatch(typeof(EffectModel), nameof(EffectModel.Description), MethodType.Getter)]
        [HarmonyPostfix]
        private static void EffectModel_Description_Postfix(EffectModel __instance, ref string __result)
        {
            try
            {
                if (ImperialOvertimeManager.TryGetDynamicRewardDescription(__instance, out string dynamicDesc))
                {
                    __result = dynamicDesc;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error computing dynamic Overtime reward description: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(GameMenuPopup), "GetAbandonPopupDesc")]
        [HarmonyPostfix]
        private static void GameMenuPopup_GetAbandonPopupDesc_Postfix(ref string __result)
        {
            try
            {
                if (ModConfig.EnableImperialOvertime.Value && ImperialOvertimeManager.IsInOvertime())
                {
                    __result = ImperialOvertimeManager.BuildOvertimeMoveOnSummary(__result ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error building MoveOn Overtime summary: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(GameMenuPopup), "MoveOn")]
        [HarmonyPrefix]
        private static void GameMenuPopup_MoveOn_Prefix()
        {
            try
            {
                ImperialOvertimeManager.FlushOvertimeOnMoveOn();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in MoveOn prefix: {ex}");
            }
        }

        [HarmonyPatch(typeof(ReputationService), nameof(ReputationService.AddReputationPoints))]
        [HarmonyPrefix]
        private static void ReputationService_AddReputationPoints_Prefix(float amount, ReputationChangeSource type, string reason)
        {
            try
            {
                if (amount > 0f && ImperialOvertimeManager.IsExcessReputationActive())
                {
                    ImperialOvertimeManager.RecordExcessReputation(amount, type, reason);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error recording Overtime excess reputation: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ResolveReputationCalculator), "Update")]
        [HarmonyPostfix]
        private static void ResolveReputationCalculator_Update_Postfix(ResolveReputationCalculator __instance)
        {
            try
            {
                ImperialOvertimeManager.TickResolveExcessReputation(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error ticking Resolve excess reputation in Overtime: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ReputationBarTooltipTrigger), "GetDesc")]
        [HarmonyPostfix]
        private static void ReputationBarTooltipTrigger_GetDesc_Postfix(ReputationBarTooltipTrigger __instance, ref string __result)
        {
            try
            {
                if (ImperialOvertimeManager.TryBuildExcessReputationTooltip(__instance, out string excessDesc))
                {
                    __result = excessDesc;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error building Excess Reputation bar tooltip: {ex.Message}");
            }
        }
    }
}
