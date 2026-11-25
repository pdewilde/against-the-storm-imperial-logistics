using System;
using System.Collections.Generic;
using System.IO;
using ATS_ImperialLogistics.Helpers;
using BepInEx;
using Eremite.Services;
using HarmonyLib;
using Newtonsoft.Json;

namespace ATS_ImperialLogistics.Persistence
{
    [Serializable]
    public class SettlementModData
    {
        // Fingerprint binding this state file to a specific settlement ("{seed}:{biomeName}:{x},{y}")
        public string SettlementFingerprint = string.Empty;

        // Key: "{workshopId}:{productName}" -> player manually set local limit so auto-cap won't overwrite it
        public HashSet<string> PlayerLockedRecipeLimits = new HashSet<string>(StringComparer.Ordinal);

        // Product names where player set a non-zero Global Limit so newly built workshops also respect it
        public HashSet<string> PlayerLockedGlobalProductLimits = new HashSet<string>(StringComparer.Ordinal);

        // Key: "{workshopId}:{productName}:{setIndex}:{goodName}" -> temporarily disabled due to safety floor
        public HashSet<string> FloorPausedIngredients = new HashSet<string>(StringComparer.Ordinal);

        // Key: "{workshopId}:{productName}:{setIndex}:{goodName}" -> player manually re-enabled during active floor
        public HashSet<string> PlayerManualFloorOverrides = new HashSet<string>(StringComparer.Ordinal);

        // Workshops that unlocked their curated 4th recipe (by building Id)
        public HashSet<int> FourthRecipeUnlockedWorkshops = new HashSet<int>();

        // Camps/GathererHuts that unlocked Smart Auto-Move (by building Id)
        public HashSet<int> AutoMoveUnlockedCamps = new HashSet<int>();

        // Camps/GathererHuts where player paused Smart Auto-Move (by building Id)
        public HashSet<int> AutoMovePausedCamps = new HashSet<int>();

        // Camps/GathererHuts that unlocked Tier 2 Outpost Carts (by building Id)
        public HashSet<int> CartUpgradedCamps = new HashSet<int>();

        // Legacy set preserved for backward save compatibility; migrated into StormWardAppliedReductionByHearth
        public HashSet<int> StormWardHearths = new HashSet<int>();

        // Exact hostility reduction applied per Hearth Id so config changes never drift hostility on demolition
        public Dictionary<int, int> StormWardAppliedReductionByHearth = new Dictionary<int, int>();

        // Storages (Warehouses) with Automaton Logistics Hub deployed (by building Id)
        public HashSet<int> AutomatonStorages = new HashSet<int>();

        // Imperial Overtime state
        public bool OvertimeInitialized = false;
        public int HighestGrandOrderWaveSpawned = 0;
        public HashSet<int> CompletedGrandOrderWaves = new HashSet<int>();
        public int OvertimeBankedBread = 0;
        public int OvertimeBankedMachinery = 0;
        public int OvertimeBankedArtifacts = 0;
        public int OvertimeBankedEmbarkPoints = 0;
        public int OvertimeBankedCycleVillagers = 0;
        public int OvertimeBankedSealFragments = 0;
        public int OvertimeBankedExp = 0;
        public int OvertimeLevelsGained = 0;
        public float OvertimeExcessReputationProgress = 0f;
        public int OvertimeExcessReputationMilestones = 0;
        public int OvertimeExcessReputationEmbarkPointsEarned = 0;

        // Exact map of cloned workshop recipe model name -> original vanilla WorkshopRecipeModel.Name for clean vanilla save reversion
        public Dictionary<string, string> CloneToVanillaRecipeMap = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Normalize()
        {
            SettlementFingerprint ??= string.Empty;
            PlayerLockedRecipeLimits = PlayerLockedRecipeLimits != null
                ? new HashSet<string>(PlayerLockedRecipeLimits, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            PlayerLockedGlobalProductLimits = PlayerLockedGlobalProductLimits != null
                ? new HashSet<string>(PlayerLockedGlobalProductLimits, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            FloorPausedIngredients = FloorPausedIngredients != null
                ? new HashSet<string>(FloorPausedIngredients, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            PlayerManualFloorOverrides = PlayerManualFloorOverrides != null
                ? new HashSet<string>(PlayerManualFloorOverrides, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            FourthRecipeUnlockedWorkshops ??= new HashSet<int>();
            AutoMoveUnlockedCamps ??= new HashSet<int>();
            AutoMovePausedCamps ??= new HashSet<int>();
            CartUpgradedCamps ??= new HashSet<int>();
            StormWardHearths ??= new HashSet<int>();
            StormWardAppliedReductionByHearth ??= new Dictionary<int, int>();
            AutomatonStorages ??= new HashSet<int>();
            CompletedGrandOrderWaves ??= new HashSet<int>();
            CloneToVanillaRecipeMap = CloneToVanillaRecipeMap != null
                ? new Dictionary<string, string>(CloneToVanillaRecipeMap, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            int defaultReduction = Math.Abs(ModConfig.HearthStormWardHostilityReduction?.Value ?? 45);
            foreach (int hearthId in StormWardHearths)
            {
                if (!StormWardAppliedReductionByHearth.ContainsKey(hearthId))
                {
                    StormWardAppliedReductionByHearth[hearthId] = defaultReduction;
                }
            }
            foreach (int hearthId in StormWardAppliedReductionByHearth.Keys)
            {
                StormWardHearths.Add(hearthId);
            }
        }
    }

    public static class SettlementModState
    {
        private static SettlementModData _current = new SettlementModData();
        private static bool _dirty;

        public static SettlementModData Current => _current;

        private static string ComputeCurrentSettlementFingerprint()
        {
            var cond = Svc.State?.Conditions;
            if (cond == null)
            {
                return string.Empty;
            }
            return $"{cond.seed}:{cond.biomeName ?? string.Empty}:{cond.worldMapField.x},{cond.worldMapField.y}";
        }

        public static string GetActiveProfileFolder()
        {
            try
            {
                if (Svc.Profiles != null)
                {
                    string profileDir = Svc.Profiles.GetProfileFolderPath();
                    if (!string.IsNullOrEmpty(profileDir) && Directory.Exists(profileDir))
                    {
                        return profileDir;
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return null;
        }

        public static string GetStateFilePath()
        {
            string profileDir = GetActiveProfileFolder();
            if (!string.IsNullOrEmpty(profileDir))
            {
                return Path.Combine(profileDir, "ImperialLogistics_SettlementState.json");
            }

            string dir = Path.Combine(Paths.ConfigPath, "ATS_ImperialLogistics_State");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "settlement_state_default.json");
        }

        public static void LoadForSession(bool isNewSettlement)
        {
            string activeFingerprint = ComputeCurrentSettlementFingerprint();

            if (isNewSettlement)
            {
                _current = new SettlementModData { SettlementFingerprint = activeFingerprint };
                _dirty = false;
                FlushToDisk();
                return;
            }

            string path = GetStateFilePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    _current = JsonConvert.DeserializeObject<SettlementModData>(json) ?? new SettlementModData();
                    _current.Normalize();

                    bool gameFinished = Svc.State?.GameObjectives != null && Svc.State.GameObjectives.finished;

                    // Guard against stale sidecar state from a different settlement
                    if (!string.IsNullOrEmpty(activeFingerprint))
                    {
                        if (!string.IsNullOrEmpty(_current.SettlementFingerprint)
                            && !string.Equals(_current.SettlementFingerprint, activeFingerprint, StringComparison.Ordinal))
                        {
                            Plugin.Log.LogInfo("Detected different settlement seed/coords than cached mod state; starting clean settlement state.");
                            _current = new SettlementModData { SettlementFingerprint = activeFingerprint };
                            FlushToDisk();
                            return;
                        }

                        if (string.IsNullOrEmpty(_current.SettlementFingerprint))
                        {
                            // Legacy state file without fingerprint: if it claims Overtime while the loaded save is unfinished, reset it
                            if (_current.OvertimeInitialized && !gameFinished)
                            {
                                _current = new SettlementModData { SettlementFingerprint = activeFingerprint };
                                FlushToDisk();
                                return;
                            }
                            _current.SettlementFingerprint = activeFingerprint;
                            _dirty = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to load settlement mod state, starting fresh: {ex.Message}");
                    _current = new SettlementModData { SettlementFingerprint = activeFingerprint };
                }
            }
            else
            {
                _current = new SettlementModData { SettlementFingerprint = activeFingerprint };
            }

            _dirty = false;
        }

        /// <summary>
        /// Marks settlement state dirty in memory so it is persisted alongside the game's Save.save checkpoints
        /// (preventing desynchronization if the game crashes mid-season before autosaving).
        /// </summary>
        public static void Save()
        {
            _dirty = true;
        }

        /// <summary>
        /// Immediately flushes settlement state to disk (called on StateService.SaveState, MoveOn, and MetaSave banking).
        /// </summary>
        public static void FlushToDisk(bool force = true)
        {
            if (!force && !_dirty)
            {
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(_current.SettlementFingerprint))
                {
                    string fp = ComputeCurrentSettlementFingerprint();
                    if (!string.IsNullOrEmpty(fp))
                    {
                        _current.SettlementFingerprint = fp;
                    }
                }

                string path = GetStateFilePath();
                string json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(path, json);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to save settlement mod state: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class SettlementModStatePatches
    {
        [HarmonyPatch(typeof(StateService), nameof(StateService.SaveState))]
        [HarmonyPostfix]
        private static void StateService_SaveState_Postfix()
        {
            try
            {
                SettlementModState.FlushToDisk(force: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error flushing mod state on SaveState: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(StateService), nameof(StateService.ForceSaveState))]
        [HarmonyPostfix]
        private static void StateService_ForceSaveState_Postfix()
        {
            try
            {
                SettlementModState.FlushToDisk(force: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error flushing mod state on ForceSaveState: {ex.Message}");
            }
        }
    }
}
