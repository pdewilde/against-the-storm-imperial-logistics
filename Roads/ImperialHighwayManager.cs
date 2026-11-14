using System;
using System.Linq;
using ATS_API.Helpers;
using ATS_API.Localization;
using ATS_ImperialLogistics.Helpers;
using Eremite.Buildings;
using Eremite.Model;
using HarmonyLib;
using UnityEngine;

namespace ATS_ImperialLogistics.Roads
{
    public static class ImperialHighwayManager
    {
        public const string HighwayShortName = "Imperial_Highway";
        public static readonly string HighwayModelName = $"{Plugin.PluginGuid}_{HighwayShortName}";

        private static RoadModel _highwayModel;
        private static RoadModel _reinforcedRoadRef;
        private static float _vanillaMaxVillagerSpeed;
        private static bool _registered;

        public static RoadModel HighwayModel => _highwayModel;

        public static void RegisterOnInit()
        {
            if (_registered || Svc.Cfg?.Buildings == null)
            {
                return;
            }

            try
            {
                var reinforcedRoad = BuildingTypes.Reinforced_Road.ToBuildingModel() as RoadModel
                    ?? Svc.Cfg.Buildings.OfType<RoadModel>().OrderByDescending(r => r.level).FirstOrDefault();

                if (reinforcedRoad == null)
                {
                    Plugin.Log.LogWarning("ImperialHighwayManager: Could not locate Reinforced Road template.");
                    return;
                }

                _reinforcedRoadRef = reinforcedRoad;

                if (Svc.Cfg.perksConfig != null && _vanillaMaxVillagerSpeed <= 0f)
                {
                    _vanillaMaxVillagerSpeed = Svc.Cfg.perksConfig.maxVillagerAbsoluteSpeed;
                }

                // Clone Reinforced Road so prefab, category (Roads), footprintMap, texturePosition, pathTag, and sounds match natively
                _highwayModel = UnityEngine.Object.Instantiate(reinforcedRoad);
                _highwayModel.name = HighwayModelName;
                _highwayModel.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_highwayModel);

                _highwayModel.level = reinforcedRoad.level + 1;
                var orderField = AccessTools.Field(typeof(BuildingModel), "order");
                if (orderField != null)
                {
                    int baseOrder = (int)orderField.GetValue(reinforcedRoad);
                    orderField.SetValue(_highwayModel, baseOrder + 1);
                }
                _highwayModel.replaces = null;

                _highwayModel.isActive = true;
                _highwayModel.isInShop = ModConfig.EnableImperialHighways.Value;
                _highwayModel.canBePicked = true;
                _highwayModel.initiallyEssential = true;
                _highwayModel.dlc = DLCType.None;
                _highwayModel.requirements = Array.Empty<BaseModelRequirement>();
                _highwayModel.usabilityTags = Array.Empty<ModelTag>();

                // Assign icon directly without passing non-readable GPU texture to TextMeshProManager
                _highwayModel.icon = reinforcedRoad.icon;

                ApplyLiveConfigToModel();

                _highwayModel.displayName = LocalizationManager.ToLocaText(
                    Plugin.PluginGuid,
                    HighwayShortName,
                    "DisplayName",
                    "Imperial Highway",
                    SystemLanguage.English);

                var descLoca = LocalizationManager.ToLocaText(
                    Plugin.PluginGuid,
                    HighwayShortName,
                    "Description",
                    GetLiveDescriptionText(),
                    SystemLanguage.English);
                AccessTools.Field(typeof(BuildingModel), "description")?.SetValue(_highwayModel, descLoca);

                // Register in Settings.Buildings and invalidate Settings.buildingsCache so Save/Load and Build Menu resolve it natively
                if (!Svc.Cfg.Buildings.Any(b => b != null && b.Name == HighwayModelName))
                {
                    int len = Svc.Cfg.Buildings.Length;
                    Array.Resize(ref Svc.Cfg.Buildings, len + 1);
                    Svc.Cfg.Buildings[len] = _highwayModel;

                    var buildingsCacheObj = AccessTools.Field(typeof(Settings), "buildingsCache")?.GetValue(Svc.Cfg);
                    if (buildingsCacheObj != null)
                    {
                        AccessTools.Field(buildingsCacheObj.GetType(), "cache")?.SetValue(buildingsCacheObj, null);
                    }
                }

                // Update speed, cost, and speed ceiling immediately whenever sliders move in Options -> Mods
                ModConfig.HighwaySpeedBonus.SettingChanged += OnHighwayConfigChanged;
                ModConfig.HighwayBuildPacksCost.SettingChanged += OnHighwayConfigChanged;
                ModConfig.EnableImperialHighways.SettingChanged += OnHighwayConfigChanged;

                _registered = true;
                int speedBonusPct = Mathf.RoundToInt((_highwayModel.modificator - 1f) * 100f);
                Plugin.Log.LogInfo(
                    $"ImperialHighwayManager registered '{HighwayModelName}' (level={_highwayModel.level}, modificator={_highwayModel.modificator:F2} [+{speedBonusPct}%], cost={ModConfig.HighwayBuildPacksCost.Value} Build Pack).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed registering Imperial Highway: {ex}");
            }
        }

        private static void OnHighwayConfigChanged(object sender, EventArgs e)
        {
            ApplyLiveConfigToModel();
            if (Svc.Active && ModConfig.EnableImperialHighways.Value)
            {
                EnsureUnlockedInActiveSettlement();
            }
        }

        public static void ApplyLiveConfigToModel()
        {
            if (_highwayModel == null)
            {
                return;
            }

            bool enabled = ModConfig.EnableImperialHighways.Value;
            _highwayModel.isInShop = enabled;

            float minFloor = _reinforcedRoadRef != null ? _reinforcedRoadRef.modificator + 0.05f : 1.30f;
            float targetMod = Mathf.Max(minFloor, 1f + ModConfig.HighwaySpeedBonus.Value);
            _highwayModel.modificator = targetMod;

            // Ensure vanilla PerksConfig.maxVillagerAbsoluteSpeed ceiling never clamps high-speed Imperial Highways (>100%),
            // and restore vanilla ceiling cleanly if Imperial Highways are disabled.
            if (Svc.Cfg?.perksConfig != null)
            {
                float baseCeiling = _vanillaMaxVillagerSpeed > 0f ? _vanillaMaxVillagerSpeed : Svc.Cfg.perksConfig.maxVillagerAbsoluteSpeed;
                Svc.Cfg.perksConfig.maxVillagerAbsoluteSpeed = enabled
                    ? Mathf.Max(baseCeiling, 3.5f * targetMod)
                    : baseCeiling;
            }

            int packCost = Mathf.Max(0, ModConfig.HighwayBuildPacksCost.Value);
            if (packCost <= 0)
            {
                _highwayModel.requiredGoods = Array.Empty<GoodRef>();
            }
            else
            {
                var buildPackGood = GoodsTypes.Packs_Pack_Of_Building_Materials.ToGoodModel();
                if (buildPackGood != null)
                {
                    _highwayModel.requiredGoods = new[]
                    {
                        new GoodRef
                        {
                            good = buildPackGood,
                            amount = packCost
                        }
                    };
                }
            }
        }

        public static string GetLiveDescriptionText()
        {
            float mod = _highwayModel != null ? _highwayModel.modificator : (1f + ModConfig.HighwaySpeedBonus.Value);
            int speedBonusPct = Mathf.RoundToInt((mod - 1f) * 100f);
            return $"A masterwork arterial trunk road paved with packed building materials. Increases villager walk speed by +{speedBonusPct}% and can be dragged directly over Dirt, Paved, or Reinforced Roads to upgrade them.";
        }

        public static void OnSettlementStarted()
        {
            if (_highwayModel == null)
            {
                return;
            }

            ApplyLiveConfigToModel();
            if (ModConfig.EnableImperialHighways.Value)
            {
                EnsureUnlockedInActiveSettlement();
            }
        }

        private static void EnsureUnlockedInActiveSettlement()
        {
            if (_highwayModel != null && Svc.GameContent != null && !Svc.GameContent.IsUnlocked(_highwayModel))
            {
                Svc.GameContent.Unlock(_highwayModel);
                Plugin.Log.LogInfo("Unlocked Imperial Highway in active settlement Roads build menu.");
            }
        }
    }

    [HarmonyPatch]
    internal static class ImperialHighwayPatches
    {
        [HarmonyPatch(typeof(BuildingModel), nameof(BuildingModel.Description), MethodType.Getter)]
        [HarmonyPostfix]
        private static void BuildingModel_Description_Postfix(BuildingModel __instance, ref string __result)
        {
            try
            {
                if (__instance != null && ReferenceEquals(__instance, ImperialHighwayManager.HighwayModel))
                {
                    __result = ImperialHighwayManager.GetLiveDescriptionText();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error updating highway tooltip: {ex.Message}");
            }
        }
    }
}
