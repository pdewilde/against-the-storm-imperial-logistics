using System;
using ATS_API.Helpers;
using ATS_ImperialLogistics.Camps;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.Overtime;
using ATS_ImperialLogistics.Persistence;
using ATS_ImperialLogistics.Roads;
using ATS_ImperialLogistics.SupplyChain;
using ATS_ImperialLogistics.Workshops;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ATS_ImperialLogistics
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("API", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.pdewilde.ats.imperiallogistics";
        public const string PluginName = "Imperial Logistics & Overtime";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        private float _nextSlowTickTime;
        private bool _settlementReady;
        private const float SlowTickInterval = 4.0f;

        private void Awake()
        {
            Log = Logger;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            ModConfig.Bind(Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            EventBus.OnInitReferences.AddListener(OnInitReferences);
            EventBus.OnStartGame.AddListener(OnStartSettlement);

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void OnDestroy()
        {
            ImperialOvertimeManager.OnSettlementEnded();
        }

        private void OnInitReferences()
        {
            try
            {
                SmartSupplyChainManager.EnsureInitialized();
                WorkshopMasteryManager.RegisterUpgradedRecipesOnInit();
                ImperialOvertimeManager.RegisterModelsOnInit();
                ImperialHighwayManager.RegisterOnInit();
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during OnInitReferences: {ex}");
            }
        }

        private void OnStartSettlement(bool isNewSettlement)
        {
            try
            {
                VanillaSaveConverter.OnSettlementEnded();
                SettlementModState.LoadForSession(isNewSettlement);
                VanillaSaveConverter.SanitizeMetaAndWorldSavesInMemory();
                WorkshopMasteryManager.SyncRecipeMapToState();
                SmartSupplyChainManager.OnSettlementStarted();
                SmartCampAutoMover.OnSettlementStarted();
                ImperialHighwayManager.OnSettlementStarted();
                ImperialOvertimeManager.OnSettlementStarted();
                _settlementReady = true;
                _nextSlowTickTime = Time.unscaledTime + 1.5f;
                Log.LogInfo($"Settlement session initialized (isNewSettlement={isNewSettlement}).");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during OnStartSettlement: {ex}");
            }
        }

        private void Update()
        {
            if (!Svc.Active)
            {
                if (_settlementReady)
                {
                    _settlementReady = false;
                    ImperialOvertimeManager.OnSettlementEnded();
                    VanillaSaveConverter.OnSettlementEnded();
                }
                return;
            }

            if (!_settlementReady || VanillaSaveConverter.IsRevertLocked || Time.unscaledTime < _nextSlowTickTime)
            {
                return;
            }
            _nextSlowTickTime = Time.unscaledTime + SlowTickInterval;

            try
            {
                SmartSupplyChainManager.Tick();
                SmartCampAutoMover.Tick();
                ImperialOvertimeManager.Tick();
            }
            catch (Exception ex)
            {
                Log.LogError($"Error in ImperialLogistics slow tick: {ex}");
            }
        }
    }
}
