using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.UI;
using ATS_ImperialLogistics.Workshops;
using Eremite.Buildings;
using Eremite.Model;
using Eremite.Services;
using Eremite.View.Popups;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ATS_ImperialLogistics.Persistence
{
    /// <summary>
    /// Maintains MetaSave/WorldSave vanilla hygiene and provides a two-step, accident-proof
    /// Pause Menu action to cleanly revert an active modded settlement back to 100% vanilla save files.
    /// </summary>
    public static class VanillaSaveConverter
    {
        public const string VanillaRoadFallback = "Reinforced Road";

        private static bool _armedForConfirm;
        private static string _lastStatusBanner;

        /// <summary>
        /// Set to true once the player confirms a Vanilla Save Revert in the current settlement session,
        /// preventing mod ticks or unlocks from re-writing custom model IDs before quitting to desktop.
        /// </summary>
        public static bool IsRevertLocked { get; internal set; }

        public static void OnSettlementEnded()
        {
            IsRevertLocked = false;
            _armedForConfirm = false;
            _lastStatusBanner = null;
        }

        private static bool IsHighwayModelName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.EndsWith("_Imperial_Highway", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsModGrandOrderName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("imperiallogistics_GrandOrder_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsModStormWardName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("imperiallogistics_HearthStormWard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsModRecipeCloneName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("imperiallogistics_", StringComparison.OrdinalIgnoreCase) >= 0
                && (name.IndexOf("_ImpG", StringComparison.Ordinal) >= 0
                    || name.IndexOf("_ImpBaseG1", StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// Automatically cleans any legacy Imperial_Highway entries from MetaSave and WorldSave
        /// so finishing a settlement ("Move On") leaves Cycle & Citadel saves 100% vanilla-compatible.
        /// </summary>
        public static void SanitizeMetaAndWorldSavesInMemory()
        {
            try
            {
                bool metaChanged = false;
                var metaContent = Svc.MetaState?.Content;
                if (metaContent != null)
                {
                    if (metaContent.essentialBuildings != null
                        && metaContent.essentialBuildings.RemoveAll(IsHighwayModelName) > 0)
                    {
                        metaChanged = true;
                    }
                    if (metaContent.buildings != null
                        && metaContent.buildings.RemoveAll(IsHighwayModelName) > 0)
                    {
                        metaChanged = true;
                    }
                }

                if (metaChanged)
                {
                    Svc.MetaState.SaveState();
                    Svc.MetaState.SaveGameWonBackup();
                    Plugin.Log.LogInfo("Cleaned legacy Imperial_Highway references from MetaSave for vanilla compatibility.");
                }

                bool worldChanged = false;
                var constructed = Svc.WorldState?.Cycle?.buildingsConstructed;
                if (constructed != null)
                {
                    for (int i = 0; i < constructed.Count; i++)
                    {
                        if (IsHighwayModelName(constructed[i]))
                        {
                            constructed[i] = VanillaRoadFallback;
                            worldChanged = true;
                        }
                    }
                }

                if (worldChanged)
                {
                    Svc.WorldState.SaveState();
                    Plugin.Log.LogInfo("Normalized Imperial_Highway entries in WorldSave.Cycle for vanilla compatibility.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error sanitizing MetaSave/WorldSave: {ex.Message}");
            }
        }

        /// <summary>
        /// Renders the two-step, accident-proof "Prepare Save for Vanilla" control docked below the Pause Menu.
        /// </summary>
        public static void RenderPauseMenuDock(GameMenuPopup popup)
        {
            if (popup == null || !Svc.Active)
            {
                return;
            }

            var dock = ImperialUiHelper.GetOrCreatePanelHeaderDock(popup.transform);
            dock.gameObject.SetActive(true);
            ImperialUiHelper.ClearChildren(dock);

            var row = ImperialUiHelper.CreateRow(dock, "VanillaRevertRow");

            if (IsRevertLocked)
            {
                string msg = string.IsNullOrEmpty(_lastStatusBanner)
                    ? "<color=#9BF09B>[Done] Reverted to Vanilla! Backup created — Exit Game & disable mod.</color>"
                    : _lastStatusBanner;
                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnVanillaRevertDone",
                    msg,
                    false,
                    null,
                    minWidth: 360f,
                    height: 26f,
                    fontSize: 12);
                ImperialUiHelper.RefreshDockLayout(dock);
                return;
            }

            if (!_armedForConfirm)
            {
                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnArmVanillaRevert",
                    "<color=#CCCCCC>Prepare Save for Vanilla (Uninstall Helper)...</color>",
                    true,
                    () =>
                    {
                        _armedForConfirm = true;
                        Svc.PlayClickSuccess();
                        RenderPauseMenuDock(popup);
                    },
                    minWidth: 290f,
                    height: 24f,
                    fontSize: 11);
            }
            else
            {
                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnConfirmVanillaRevert",
                    "<color=#FFD966><b>Confirm Revert to Vanilla (Backs Up Saves)</b></color>",
                    true,
                    () =>
                    {
                        ExecuteVanillaRevert(popup);
                    },
                    minWidth: 270f,
                    height: 26f,
                    fontSize: 12);

                ImperialUiHelper.CreateStyledButton(
                    row,
                    "BtnCancelVanillaRevert",
                    "Cancel",
                    true,
                    () =>
                    {
                        _armedForConfirm = false;
                        Svc.PlayClickSuccess();
                        RenderPauseMenuDock(popup);
                    },
                    minWidth: 76f,
                    height: 26f,
                    fontSize: 12);
            }

            ImperialUiHelper.RefreshDockLayout(dock);
        }

        public static void DisarmOnMenuClose()
        {
            _armedForConfirm = false;
        }

        private static void ExecuteVanillaRevert(GameMenuPopup popup)
        {
            _armedForConfirm = false;

            try
            {
                string profileDir = SettlementModState.GetActiveProfileFolder();
                string backupDir = CreateBackupFolder(profileDir);

                // Ensure sidecar recipe map is complete before stripping
                WorkshopMasteryManager.SyncRecipeMapToState();

                // Lock mod systems immediately so no ticks or exit handlers re-inject mod IDs
                IsRevertLocked = true;

                int roadsReverted = 0;
                int recipesReverted = 0;
                int ordersRemoved = 0;
                int perksRemoved = 0;

                // 1. Roads -> Reinforced Road
                var stateRoads = Svc.State?.Buildings?.roads;
                if (stateRoads != null)
                {
                    foreach (var roadState in stateRoads)
                    {
                        if (roadState != null && IsHighwayModelName(roadState.model))
                        {
                            roadState.model = VanillaRoadFallback;
                            roadsReverted++;
                        }
                    }
                }

                // 2. Settlement Content lists
                var content = Svc.State?.Content;
                if (content != null)
                {
                    content.buildings?.RemoveAll(IsHighwayModelName);
                    content.essentialBuildings?.RemoveAll(IsHighwayModelName);
                    content.replacedBuildings?.RemoveAll(IsHighwayModelName);
                }

                // 3. Stats & Trends ownerModel
                var statsConstructed = Svc.State?.Stats?.buildingsConstructed;
                if (statsConstructed != null)
                {
                    for (int i = 0; i < statsConstructed.Count; i++)
                    {
                        if (IsHighwayModelName(statsConstructed[i]))
                        {
                            statsConstructed[i] = VanillaRoadFallback;
                        }
                    }
                }

                var trends = Svc.State?.Trends;
                if (trends != null)
                {
                    if (trends.queuedOperations != null)
                    {
                        foreach (var op in trends.queuedOperations)
                        {
                            if (op != null && IsHighwayModelName(op.ownerModel))
                            {
                                op.ownerModel = VanillaRoadFallback;
                            }
                        }
                    }
                    if (trends.goodsOperations != null)
                    {
                        foreach (var list in trends.goodsOperations.Values)
                        {
                            if (list == null) continue;
                            foreach (var op in list)
                            {
                                if (op != null && IsHighwayModelName(op.ownerModel))
                                {
                                    op.ownerModel = VanillaRoadFallback;
                                }
                            }
                        }
                    }
                }

                // 4. Workshop recipes & active production slots -> exact vanilla base recipes
                var workshops = Svc.State?.Buildings?.workshops;
                if (workshops != null)
                {
                    foreach (var ws in workshops)
                    {
                        if (ws == null) continue;

                        if (ws.recipes != null)
                        {
                            foreach (var rState in ws.recipes)
                            {
                                if (rState == null || !IsModRecipeCloneName(rState.model)) continue;
                                if (WorkshopMasteryManager.TryGetBaseVanillaRecipeName(rState.model, out string vanillaName))
                                {
                                    RevertRecipeStateIngredients(rState, vanillaName);
                                    rState.model = vanillaName;
                                    recipesReverted++;
                                }
                            }
                        }

                        if (ws.production != null)
                        {
                            foreach (var prod in ws.production)
                            {
                                if (prod == null || !IsModRecipeCloneName(prod.recipe)) continue;
                                if (WorkshopMasteryManager.TryGetBaseVanillaRecipeName(prod.recipe, out string vanillaName))
                                {
                                    prod.recipe = vanillaName;
                                }
                                else
                                {
                                    prod.recipe = null;
                                }
                            }
                        }
                    }
                }

                // 5. Overtime Grand Orders -> strip
                var currentOrders = Svc.State?.Orders?.currentOrders;
                if (currentOrders != null)
                {
                    ordersRemoved = currentOrders.RemoveAll(o => o != null && IsModGrandOrderName(o.model));
                }

                // 6. Hearth Storm-Ward perk -> strip & restore hostility points cleanly
                var perks = Svc.State?.Effects?.perks;
                if (perks != null)
                {
                    var modPerkKeys = perks.Keys.Where(IsModStormWardName).ToList();
                    foreach (string key in modPerkKeys)
                    {
                        perks.Remove(key);
                        perksRemoved++;
                    }
                }

                var wardMap = SettlementModState.Current.StormWardAppliedReductionByHearth;
                if (wardMap != null && wardMap.Count > 0)
                {
                    int totalReduction = 0;
                    foreach (int red in wardMap.Values)
                    {
                        if (red > 0) totalReduction += red;
                    }
                    if (totalReduction > 0)
                    {
                        Svc.Hostility?.AddEffectHostility(totalReduction);
                    }
                    wardMap.Clear();
                    SettlementModState.Current.StormWardHearths.Clear();
                }

                // 7. MetaSave & WorldSave hygiene
                SanitizeMetaAndWorldSavesInMemory();

                // 8. Save in-memory vanilla state to disk immediately
                if (Svc.State is StateService concreteState)
                {
                    concreteState.ForceSaveState();
                }
                Svc.MetaState?.SaveState();
                Svc.MetaState?.SaveGameWonBackup();
                Svc.WorldState?.SaveState();

                // 9. Also scrub backup .save JSON files in the profile directory so no *_Backup.save retains mod IDs
                if (!string.IsNullOrEmpty(profileDir) && Directory.Exists(profileDir))
                {
                    ScrubSaveJsonFilesOnDisk(profileDir);
                }

                string backupLabel = !string.IsNullOrEmpty(backupDir) ? Path.GetFileName(backupDir) : "created";
                _lastStatusBanner = $"<color=#9BF09B>[Done] Reverted ({roadsReverted} roads, {recipesReverted} recipes, {ordersRemoved} orders) — Exit Game & disable mod!</color>";

                Plugin.Log.LogInfo(
                    $"[VanillaSaveConverter] Reverted active settlement to vanilla cleanly "
                    + $"(roads={roadsReverted}, recipes={recipesReverted}, orders={ordersRemoved}, perks={perksRemoved}, backup='{backupLabel}').");

                Svc.PlayClickSuccess();
                RenderPauseMenuDock(popup);
            }
            catch (Exception ex)
            {
                IsRevertLocked = false;
                Plugin.Log.LogError($"[VanillaSaveConverter] Failed reverting save to vanilla: {ex}");
                Svc.PlayClickFail();
                _lastStatusBanner = "<color=#FF8888>Revert failed — check BepInEx LogOutput.log.</color>";
                RenderPauseMenuDock(popup);
            }
        }

        private static void RevertRecipeStateIngredients(WorkshopRecipeState recipeState, string vanillaModelName)
        {
            if (Svc.Recipes == null || string.IsNullOrEmpty(vanillaModelName))
            {
                return;
            }

            try
            {
                var oldIngredients = recipeState.ingredients;
                var vanillaTemplate = Svc.Recipes.CreateWorkshopRecipeState(vanillaModelName);
                var newIngredients = vanillaTemplate?.ingredients;
                if (oldIngredients != null && newIngredients != null)
                {
                    int sets = Math.Min(oldIngredients.Length, newIngredients.Length);
                    for (int s = 0; s < sets; s++)
                    {
                        if (oldIngredients[s] == null || newIngredients[s] == null) continue;
                        var oldByGood = new Dictionary<string, IngredientState>(StringComparer.Ordinal);
                        foreach (var oi in oldIngredients[s])
                        {
                            if (oi != null && !string.IsNullOrEmpty(oi.good.name))
                            {
                                oldByGood[oi.good.name] = oi;
                            }
                        }

                        bool anyAllowed = false;
                        foreach (var ni in newIngredients[s])
                        {
                            if (ni != null && oldByGood.TryGetValue(ni.good.name, out var prev))
                            {
                                ni.allowed = prev.allowed;
                                ni.priority = prev.priority;
                            }
                            if (ni != null && ni.allowed) anyAllowed = true;
                        }
                        if (!anyAllowed && newIngredients[s].Length > 0 && newIngredients[s][0] != null)
                        {
                            newIngredients[s][0].allowed = true;
                        }
                    }
                    recipeState.ingredients = newIngredients;
                }
            }
            catch
            {
                // Keep existing ingredient states if template creation fails
            }
        }

        private static string CreateBackupFolder(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir) || !Directory.Exists(profileDir))
            {
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = Path.Combine(profileDir, $"VanillaRevertBackup_{stamp}");
            Directory.CreateDirectory(backupDir);

            foreach (string file in Directory.GetFiles(profileDir, "*.save"))
            {
                File.Copy(file, Path.Combine(backupDir, Path.GetFileName(file)), overwrite: true);
            }

            string sidecar = SettlementModState.GetStateFilePath();
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, Path.Combine(backupDir, Path.GetFileName(sidecar)), overwrite: true);
            }

            return backupDir;
        }

        private static void ScrubSaveJsonFilesOnDisk(string profileDir)
        {
            string[] candidateFiles =
            {
                "Save.save",
                "Save_Backup.save",
                "MetaSave.save",
                "MetaSave_Backup.save",
                "MetaSave_GameWonBackup.save",
                "MetaSave_GameWonBackup_Backup.save",
                "WorldSave.save",
                "WorldSave_Backup.save"
            };

            foreach (string fileName in candidateFiles)
            {
                string fullPath = Path.Combine(profileDir, fileName);
                if (!File.Exists(fullPath)) continue;

                try
                {
                    string raw = File.ReadAllText(fullPath);
                    if (raw.IndexOf("imperiallogistics_", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var root = JObject.Parse(raw);
                    bool changed = ScrubJsonRoot(root);
                    if (changed)
                    {
                        File.WriteAllText(fullPath, root.ToString(Formatting.Indented));
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[VanillaSaveConverter] Could not scrub '{fileName}': {ex.Message}");
                }
            }
        }

        private static bool ScrubJsonRoot(JObject root)
        {
            bool changed = false;

            // MetaSave / Save content lists
            if (root["content"] is JObject content)
            {
                changed |= RemoveMatchingStrings(content["buildings"] as JArray, IsHighwayModelName);
                changed |= RemoveMatchingStrings(content["essentialBuildings"] as JArray, IsHighwayModelName);
                changed |= RemoveMatchingStrings(content["replacedBuildings"] as JArray, IsHighwayModelName);
            }

            // WorldSave cycle.buildingsConstructed
            if (root["cycle"] is JObject cycle)
            {
                changed |= ReplaceMatchingStrings(cycle["buildingsConstructed"] as JArray, IsHighwayModelName, VanillaRoadFallback);
            }

            // Save stats.buildingsConstructed
            if (root["stats"] is JObject stats)
            {
                changed |= ReplaceMatchingStrings(stats["buildingsConstructed"] as JArray, IsHighwayModelName, VanillaRoadFallback);
            }

            // Save buildings.roads & buildings.workshops
            if (root["buildings"] is JObject buildings)
            {
                if (buildings["roads"] is JArray roads)
                {
                    foreach (var r in roads.OfType<JObject>())
                    {
                        string model = r["model"]?.Value<string>();
                        if (IsHighwayModelName(model))
                        {
                            r["model"] = VanillaRoadFallback;
                            changed = true;
                        }
                    }
                }

                if (buildings["workshops"] is JArray workshops)
                {
                    foreach (var ws in workshops.OfType<JObject>())
                    {
                        if (ws["recipes"] is JArray recipes)
                        {
                            foreach (var r in recipes.OfType<JObject>())
                            {
                                string model = r["model"]?.Value<string>();
                                if (IsModRecipeCloneName(model)
                                    && WorkshopMasteryManager.TryGetBaseVanillaRecipeName(model, out string vanillaName))
                                {
                                    r["model"] = vanillaName;
                                    changed = true;
                                }
                            }
                        }

                        if (ws["production"] is JArray prods)
                        {
                            foreach (var p in prods.OfType<JObject>())
                            {
                                string recipe = p["recipe"]?.Value<string>();
                                if (IsModRecipeCloneName(recipe))
                                {
                                    p["recipe"] = WorkshopMasteryManager.TryGetBaseVanillaRecipeName(recipe, out string vanillaName)
                                        ? (JToken)vanillaName
                                        : JValue.CreateNull();
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }

            // Save trends
            if (root["trends"] is JObject trends)
            {
                if (trends["queuedOperations"] is JArray queued)
                {
                    foreach (var op in queued.OfType<JObject>())
                    {
                        if (IsHighwayModelName(op["ownerModel"]?.Value<string>()))
                        {
                            op["ownerModel"] = VanillaRoadFallback;
                            changed = true;
                        }
                    }
                }

                if (trends["goodsOperations"] is JObject goodsOps)
                {
                    foreach (var prop in goodsOps.Properties())
                    {
                        if (prop.Value is JArray list)
                        {
                            foreach (var op in list.OfType<JObject>())
                            {
                                if (IsHighwayModelName(op["ownerModel"]?.Value<string>()))
                                {
                                    op["ownerModel"] = VanillaRoadFallback;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            }

            // Save orders
            if (root["orders"] is JObject orders && orders["currentOrders"] is JArray currentOrders)
            {
                for (int i = currentOrders.Count - 1; i >= 0; i--)
                {
                    if (currentOrders[i] is JObject o && IsModGrandOrderName(o["model"]?.Value<string>()))
                    {
                        currentOrders.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            // Save effects.perks
            if (root["effects"] is JObject effects && effects["perks"] is JObject perks)
            {
                var toRemove = perks.Properties()
                    .Where(p => IsModStormWardName(p.Name))
                    .Select(p => p.Name)
                    .ToList();
                foreach (string key in toRemove)
                {
                    perks.Remove(key);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool RemoveMatchingStrings(JArray arr, Func<string, bool> predicate)
        {
            if (arr == null) return false;
            bool changed = false;
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i].Type == JTokenType.String && predicate(arr[i].Value<string>()))
                {
                    arr.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        private static bool ReplaceMatchingStrings(JArray arr, Func<string, bool> predicate, string replacement)
        {
            if (arr == null) return false;
            bool changed = false;
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i].Type == JTokenType.String && predicate(arr[i].Value<string>()))
                {
                    arr[i] = replacement;
                    changed = true;
                }
            }
            return changed;
        }
    }

    [HarmonyPatch]
    internal static class VanillaSaveConverterPatches
    {
        [HarmonyPatch(typeof(GameMenuPopup), "Show")]
        [HarmonyPostfix]
        private static void GameMenuPopup_Show_Postfix(GameMenuPopup __instance)
        {
            try
            {
                VanillaSaveConverter.DisarmOnMenuClose();
                VanillaSaveConverter.RenderPauseMenuDock(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error rendering Pause Menu Vanilla Revert dock: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(GameMenuPopup), "Close")]
        [HarmonyPostfix]
        private static void GameMenuPopup_Close_Postfix()
        {
            VanillaSaveConverter.DisarmOnMenuClose();
        }
    }
}
