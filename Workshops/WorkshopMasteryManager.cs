using System;
using System.Collections.Generic;
using System.Linq;
using ATS_API.Helpers;
using ATS_API.Recipes;
using ATS_ImperialLogistics.Helpers;
using ATS_ImperialLogistics.Persistence;
using ATS_ImperialLogistics.UI;
using Eremite;
using Eremite.Buildings;
using Eremite.Buildings.UI;
using Eremite.Model;
using HarmonyLib;
using UnityEngine;

namespace ATS_ImperialLogistics.Workshops
{
    public static class WorkshopMasteryManager
    {
        // Maps recipe model name -> next star tier recipe model
        private static readonly Dictionary<string, WorkshopRecipeModel> NextGradeLookup =
            new Dictionary<string, WorkshopRecipeModel>(StringComparer.Ordinal);

        // Maps any cloned recipe model name -> its original vanilla WorkshopRecipeModel.Name (for lossless vanilla save reversion)
        private static readonly Dictionary<string, string> CloneToVanillaRecipeName =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Maps produced good name -> best representative 1-star recipe model for curated 4th-recipe unlocks
        private static readonly Dictionary<string, WorkshopRecipeModel> OneStarByGood =
            new Dictionary<string, WorkshopRecipeModel>(StringComparer.Ordinal);

        // Curated thematic 4th recipe good per workshop model name
        private static Dictionary<string, string> _curatedFourthGoodByWorkshop;
        private static HashSet<string> _starterWorkshops;
        private static HashSet<string> _excludedWorkshops;
        private static bool _registered;

        public static void RegisterUpgradedRecipesOnInit()
        {
            if (_registered || Svc.Cfg?.workshopsRecipes == null)
            {
                return;
            }
            _registered = true;

            BuildCuratedTable();

            var grade0 = Grade.Zero.ToModel();
            var grade1 = Grade.One.ToModel();
            var grade2 = Grade.Two.ToModel();
            var grade3 = Grade.Three.ToModel();

            var gradesByLevel = new Dictionary<int, RecipeGradeModel>
            {
                { 0, grade0 },
                { 1, grade1 },
                { 2, grade2 },
                { 3, grade3 }
            };

            // Snapshot original vanilla workshop recipes before adding clones (skipping deprecated 'todelete' placeholders)
            var baseRecipes = Svc.Cfg.workshopsRecipes
                .Where(r => r != null
                    && r.producedGood?.good != null
                    && !r.Name.Contains("_ImpG")
                    && r.Name.IndexOf("todelete", StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

            // Populate 1-star lookup from existing vanilla 1-star recipes first
            foreach (var r in baseRecipes)
            {
                int lvl = r.grade != null ? r.grade.level : 1;
                string goodName = r.GetProducedGood();
                if (lvl == 1 && !string.IsNullOrEmpty(goodName) && !OneStarByGood.ContainsKey(goodName))
                {
                    OneStarByGood[goodName] = r;
                }
            }

            // For every vanilla recipe, create deterministic +1★ step chain up to 3★ so any recipe can be upgraded individually
            int createdCount = 0;
            foreach (var baseRecipe in baseRecipes)
            {
                int startLevel = baseRecipe.grade != null ? Mathf.Clamp(baseRecipe.grade.level, 0, 3) : 1;
                WorkshopRecipeModel prev = baseRecipe;

                for (int nextLevel = startLevel + 1; nextLevel <= 3; nextLevel++)
                {
                    string shortKey = $"{SanitizeName(baseRecipe.Name)}_ImpG{nextLevel}";
                    string fullRegisteredName = $"{Plugin.PluginGuid}_{shortKey}";

                    WorkshopRecipeModel nextClone = CreateSteppedGradeClone(prev, nextLevel, gradesByLevel[nextLevel], fullRegisteredName);
                    RecipeManager.AddRecipe(Plugin.PluginGuid, shortKey, nextClone);
                    NextGradeLookup[prev.Name] = nextClone;
                    CloneToVanillaRecipeName[nextClone.Name] = baseRecipe.Name;

                    string goodName = nextClone.GetProducedGood();
                    if (nextLevel == 1 && !string.IsNullOrEmpty(goodName) && !OneStarByGood.ContainsKey(goodName))
                    {
                        OneStarByGood[goodName] = nextClone;
                    }

                    prev = nextClone;
                    createdCount++;
                }
            }

            // Also ensure any good that only had 2★ or 3★ in vanilla gets a 1★ clone if needed for 4th-recipe unlocks
            foreach (var r in baseRecipes)
            {
                string goodName = r.GetProducedGood();
                if (string.IsNullOrEmpty(goodName) || OneStarByGood.ContainsKey(goodName))
                {
                    continue;
                }

                string shortKey = $"{SanitizeName(r.Name)}_ImpBaseG1";
                string fullName = $"{Plugin.PluginGuid}_{shortKey}";
                var g1Clone = CreateDownscaledOneStarClone(r, grade1, fullName);
                RecipeManager.AddRecipe(Plugin.PluginGuid, shortKey, g1Clone);
                OneStarByGood[goodName] = g1Clone;
                CloneToVanillaRecipeName[g1Clone.Name] = r.Name;

                // Wire its G1 -> G2 -> G3 upgrade path too
                string g2Key = $"{shortKey}_ImpG2";
                var g2Clone = CreateSteppedGradeClone(g1Clone, 2, grade2, $"{Plugin.PluginGuid}_{g2Key}");
                RecipeManager.AddRecipe(Plugin.PluginGuid, g2Key, g2Clone);
                NextGradeLookup[g1Clone.Name] = g2Clone;
                CloneToVanillaRecipeName[g2Clone.Name] = r.Name;

                string g3Key = $"{shortKey}_ImpG3";
                var g3Clone = CreateSteppedGradeClone(g2Clone, 3, grade3, $"{Plugin.PluginGuid}_{g3Key}");
                RecipeManager.AddRecipe(Plugin.PluginGuid, g3Key, g3Clone);
                NextGradeLookup[g2Clone.Name] = g3Clone;
                CloneToVanillaRecipeName[g3Clone.Name] = r.Name;
                createdCount += 3;
            }

            RecipeManager.SyncRecipes();
            Plugin.Log.LogInfo($"WorkshopMasteryManager registered {createdCount} stepped star-upgrade recipes.");
        }

        public static bool TryGetBaseVanillaRecipeName(string cloneModelName, out string vanillaName)
        {
            vanillaName = null;
            if (string.IsNullOrEmpty(cloneModelName))
            {
                return false;
            }
            if (CloneToVanillaRecipeName.TryGetValue(cloneModelName, out vanillaName))
            {
                return true;
            }
            // Fallback for older legacy prefix if ever present in an old save (e.g. different plugin GUID prefix)
            int idx = cloneModelName.IndexOf("_Imp", StringComparison.Ordinal);
            if (idx > 0)
            {
                foreach (var kv in CloneToVanillaRecipeName)
                {
                    if (kv.Key.EndsWith(cloneModelName.Substring(cloneModelName.IndexOf('_') + 1), StringComparison.Ordinal))
                    {
                        vanillaName = kv.Value;
                        return true;
                    }
                }
            }
            return false;
        }

        public static void SyncRecipeMapToState()
        {
            if (Svc.Buildings?.Workshops == null)
            {
                return;
            }

            var map = SettlementModState.Current.CloneToVanillaRecipeMap;
            bool changed = false;
            foreach (var workshop in Svc.Buildings.Workshops.Values)
            {
                if (workshop?.state?.recipes == null) continue;
                foreach (var r in workshop.state.recipes)
                {
                    if (r != null && TryGetBaseVanillaRecipeName(r.model, out string vanillaName))
                    {
                        if (!map.TryGetValue(r.model, out string existing) || !string.Equals(existing, vanillaName, StringComparison.Ordinal))
                        {
                            map[r.model] = vanillaName;
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

        private static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Recipe";
            return raw.Replace(" ", "_").Replace("[", "").Replace("]", "");
        }

        private static WorkshopRecipeModel CreateSteppedGradeClone(
            WorkshopRecipeModel source,
            int targetLevel,
            RecipeGradeModel targetGrade,
            string cloneName)
        {
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = cloneName;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.grade = targetGrade;

            // Vanilla star efficiency curve: ~18% faster production time & ~20% fewer input materials per star (min 1)
            clone.productionTime = Mathf.Max(4f, Mathf.Round(source.productionTime * 0.82f));

            int oldTotalInputs = 0;
            int newTotalInputs = 0;

            if (source.requiredGoods != null)
            {
                var clonedSets = new GoodsSet[source.requiredGoods.Length];
                for (int s = 0; s < source.requiredGoods.Length; s++)
                {
                    var srcSet = source.requiredGoods[s];
                    var dstSet = new GoodsSet();
                    if (srcSet?.goods != null)
                    {
                        dstSet.goods = new GoodRef[srcSet.goods.Length];
                        for (int i = 0; i < srcSet.goods.Length; i++)
                        {
                            var srcGoodRef = srcSet.goods[i];
                            int oldAmt = srcGoodRef != null ? srcGoodRef.amount : 1;
                            oldTotalInputs += oldAmt;
                            int newAmt = oldAmt > 1 ? Mathf.Max(1, Mathf.FloorToInt(oldAmt * 0.80f)) : 1;
                            if (newAmt == oldAmt && oldAmt >= 3)
                            {
                                newAmt = oldAmt - 1;
                            }
                            newTotalInputs += newAmt;

                            dstSet.goods[i] = new GoodRef
                            {
                                good = srcGoodRef?.good,
                                amount = newAmt
                            };
                        }
                    }
                    clonedSets[s] = dstSet;
                }
                clone.requiredGoods = clonedSets;
            }

            // Cloning producedGood GoodRef cleanly so we never mutate shared references
            clone.producedGood = new GoodRef
            {
                good = source.producedGood.good,
                amount = source.producedGood.amount
            };

            // If all input counts were already 1 (couldn't shrink inputs), grant +1 output yield so +1★ still boosts material ratio!
            if (oldTotalInputs > 0 && newTotalInputs == oldTotalInputs)
            {
                clone.producedGood.amount = source.producedGood.amount + 1;
            }

            return clone;
        }

        private static WorkshopRecipeModel CreateDownscaledOneStarClone(
            WorkshopRecipeModel highStarSource,
            RecipeGradeModel grade1,
            string cloneName)
        {
            var clone = UnityEngine.Object.Instantiate(highStarSource);
            clone.name = cloneName;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.grade = grade1;
            int sourceLvl = highStarSource.grade != null ? Mathf.Max(1, highStarSource.grade.level) : 2;
            float mult = sourceLvl >= 3 ? 1.4f : 1.2f;

            clone.productionTime = Mathf.Round(highStarSource.productionTime * mult);
            clone.producedGood = new GoodRef
            {
                good = highStarSource.producedGood.good,
                amount = highStarSource.producedGood.amount
            };

            if (highStarSource.requiredGoods != null)
            {
                var clonedSets = new GoodsSet[highStarSource.requiredGoods.Length];
                for (int s = 0; s < highStarSource.requiredGoods.Length; s++)
                {
                    var srcSet = highStarSource.requiredGoods[s];
                    var dstSet = new GoodsSet();
                    if (srcSet?.goods != null)
                    {
                        dstSet.goods = new GoodRef[srcSet.goods.Length];
                        for (int i = 0; i < srcSet.goods.Length; i++)
                        {
                            var sg = srcSet.goods[i];
                            dstSet.goods[i] = new GoodRef
                            {
                                good = sg?.good,
                                amount = Mathf.Max(1, Mathf.CeilToInt((sg?.amount ?? 1) * mult))
                            };
                        }
                    }
                    clonedSets[s] = dstSet;
                }
                clone.requiredGoods = clonedSets;
            }
            return clone;
        }

        private static void BuildCuratedTable()
        {
            string flour = GoodsTypes.Crafting_Flour.ToName();
            string barrels = GoodsTypes.Vessel_Barrels.ToName();
            string pottery = GoodsTypes.Vessel_Pottery.ToName();
            string waterskin = GoodsTypes.Vessel_Waterskin.ToName();
            string dye = GoodsTypes.Crafting_Dye.ToName();
            string oil = GoodsTypes.Crafting_Oil.ToName();
            string coal = GoodsTypes.Crafting_Coal.ToName();
            string planks = GoodsTypes.Mat_Processed_Planks.ToName();
            string fabric = GoodsTypes.Mat_Processed_Fabric.ToName();
            string copperBar = GoodsTypes.Metal_Copper_Bar.ToName();
            string tools = GoodsTypes.Tools_Simple_Tools.ToName();
            string buildPacks = GoodsTypes.Packs_Pack_Of_Building_Materials.ToName();
            string tradePacks = GoodsTypes.Packs_Pack_Of_Trade_Goods.ToName();
            string cropsPacks = GoodsTypes.Packs_Pack_Of_Crops.ToName();
            string provPacks = GoodsTypes.Packs_Pack_Of_Provisions.ToName();
            string luxPacks = GoodsTypes.Packs_Pack_Of_Luxury_Goods.ToName();
            string skewers = GoodsTypes.Food_Processed_Skewers.ToName();
            string jerky = GoodsTypes.Food_Processed_Jerky.ToName();
            string coats = GoodsTypes.Needs_Coats.ToName();
            string scrolls = GoodsTypes.Needs_Scrolls.ToName();
            string incense = GoodsTypes.Needs_Incense.ToName();
            string ale = GoodsTypes.Needs_Ale.ToName();
            string tea = GoodsTypes.Needs_Tea.ToName();

            _starterWorkshops = new HashSet<string>(StringComparer.Ordinal)
            {
                WorkshopTypes.Crude_Workstation.ToName(),
                WorkshopTypes.Field_Kitchen.ToName(),
                WorkshopTypes.Makeshift_Post.ToName()
            };

            _excludedWorkshops = new HashSet<string>(StringComparer.Ordinal)
            {
                WorkshopTypes.Temporary_Engineering_Station.ToName()
            };

            _curatedFourthGoodByWorkshop = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Starter Workshops — guaranteed Flour + Containers + Trade Packs safety net
                { WorkshopTypes.Field_Kitchen.ToName(), flour },
                { WorkshopTypes.Crude_Workstation.ToName(), barrels },
                { WorkshopTypes.Makeshift_Post.ToName(), tradePacks },

                // Lumber, Construction & Clay/Geyser Workshops
                { WorkshopTypes.Lumbermill.ToName(), barrels },
                { WorkshopTypes.Carpenter.ToName(), barrels },
                { WorkshopTypes.Workshop.ToName(), buildPacks },
                { WorkshopTypes.Brickyard.ToName(), coal },
                { WorkshopTypes.Kiln.ToName(), pottery },
                { WorkshopTypes.Stamping_Mill.ToName(), pottery },
                { WorkshopTypes.Supplier.ToName(), barrels },
                { WorkshopTypes.Clay_Pit_Workshop.ToName(), pottery },

                // Agriculture, Milling & Cooking Workshops
                { WorkshopTypes.Rain_Mill.ToName(), cropsPacks },
                { WorkshopTypes.Flawless_Rain_Mill.ToName(), cropsPacks },
                { WorkshopTypes.Granary.ToName(), flour },
                { WorkshopTypes.Greenhouse_Workshop.ToName(), cropsPacks },
                { WorkshopTypes.Ranch.ToName(), waterskin },
                { WorkshopTypes.Bakery.ToName(), flour },
                { WorkshopTypes.Cookhouse.ToName(), provPacks },
                { WorkshopTypes.Grill.ToName(), coal },
                { WorkshopTypes.Smokehouse.ToName(), skewers },
                { WorkshopTypes.Butcher.ToName(), waterskin },
                { WorkshopTypes.Cellar.ToName(), barrels },
                { WorkshopTypes.Flawless_Cellar.ToName(), barrels },
                { WorkshopTypes.Beanery.ToName(), pottery },
                { WorkshopTypes.Cannery.ToName(), barrels },
                { WorkshopTypes.Pantry.ToName(), provPacks },
                { WorkshopTypes.Provisioner.ToName(), waterskin },

                // Brewing, Alchemy & Luxury Workshops
                { WorkshopTypes.Brewery.ToName(), barrels },
                { WorkshopTypes.Flawless_Brewery.ToName(), barrels },
                { WorkshopTypes.Distillery.ToName(), ale },
                { WorkshopTypes.Tea_House.ToName(), pottery },
                { WorkshopTypes.Apothecary.ToName(), dye },
                { WorkshopTypes.Alchemist_Hut.ToName(), tea },
                { WorkshopTypes.Druid.ToName(), incense },
                { WorkshopTypes.Flawless_Druid.ToName(), incense },
                { WorkshopTypes.Press.ToName(), flour },
                { WorkshopTypes.Tinctury.ToName(), scrolls },
                { WorkshopTypes.Scribe.ToName(), dye },
                { WorkshopTypes.Artisan.ToName(), luxPacks },

                // Cloth, Leather & Goods Workshops
                { WorkshopTypes.Weaver.ToName(), coats },
                { WorkshopTypes.Clothier.ToName(), fabric },
                { WorkshopTypes.Leatherworks.ToName(), coats },
                { WorkshopTypes.Flawless_Leatherworks.ToName(), coats },
                { WorkshopTypes.Cobbler.ToName(), waterskin },
                { WorkshopTypes.Cooperage.ToName(), planks },
                { WorkshopTypes.Flawless_Cooperage.ToName(), planks },

                // Metallurgy & Heavy Industry
                { WorkshopTypes.Smelter.ToName(), tools },
                { WorkshopTypes.Flawless_Smelter.ToName(), tools },
                { WorkshopTypes.Furnace.ToName(), copperBar },
                { WorkshopTypes.Brick_Oven.ToName(), jerky },
                { WorkshopTypes.Smithy.ToName(), copperBar },
                { WorkshopTypes.Toolshop.ToName(), barrels },
                { WorkshopTypes.Tinkerer.ToName(), tradePacks },
                { WorkshopTypes.Manufactory.ToName(), buildPacks },
                { WorkshopTypes.Finesmith.ToName(), luxPacks },
                { WorkshopTypes.Rainpunk_Foundry.ToName(), luxPacks }
            };
        }

        public static WorkshopRecipeModel GetCuratedBonusRecipe(Workshop workshop)
        {
            if (workshop?.model == null || workshop.state?.recipes == null)
            {
                return null;
            }

            if (_curatedFourthGoodByWorkshop == null)
            {
                BuildCuratedTable();
            }

            if (_excludedWorkshops != null && _excludedWorkshops.Contains(workshop.model.Name))
            {
                return null;
            }

            // Skip glade-event temporary stations that aren't normal buildable/flawless workshops
            if (!workshop.model.isInShop
                && (_starterWorkshops == null || !_starterWorkshops.Contains(workshop.model.Name))
                && !_curatedFourthGoodByWorkshop.ContainsKey(workshop.model.Name))
            {
                return null;
            }

            var existingGoods = new HashSet<string>(
                workshop.state.recipes.Where(r => r != null && !string.IsNullOrEmpty(r.productName)).Select(r => r.productName),
                StringComparer.Ordinal);

            if (_curatedFourthGoodByWorkshop.TryGetValue(workshop.model.Name, out string curatedGood)
                && !existingGoods.Contains(curatedGood)
                && OneStarByGood.TryGetValue(curatedGood, out var curatedRecipe))
            {
                return curatedRecipe;
            }

            // Fallback thematic bridge list if workshop already has curated good or is modded
            string[] fallbacks =
            {
                GoodsTypes.Packs_Pack_Of_Building_Materials.ToName(),
                GoodsTypes.Packs_Pack_Of_Trade_Goods.ToName(),
                GoodsTypes.Vessel_Barrels.ToName(),
                GoodsTypes.Crafting_Flour.ToName(),
                GoodsTypes.Mat_Processed_Planks.ToName()
            };

            foreach (var fb in fallbacks)
            {
                if (!existingGoods.Contains(fb) && OneStarByGood.TryGetValue(fb, out var fbRecipe))
                {
                    return fbRecipe;
                }
            }

            return null;
        }

        public static bool HasUnlockedBonusRecipe(Workshop workshop)
        {
            if (workshop == null) return true;
            return SettlementModState.Current.FourthRecipeUnlockedWorkshops.Contains(workshop.Id);
        }

        public static List<GoodCost> GetBonusRecipeCost(Workshop workshop)
        {
            bool isStarter = _starterWorkshops != null && workshop?.model != null && _starterWorkshops.Contains(workshop.model.Name);
            int packs = isStarter
                ? ModConfig.StarterFourthRecipeBuildPacks.Value
                : ModConfig.BlueprintFourthRecipeBuildPacks.Value;
            return new List<GoodCost> { EconomyHelper.BuildPacks(packs) };
        }

        public static WorkshopRecipeModel GetNextGradeRecipe(WorkshopRecipeState state)
        {
            if (state == null || string.IsNullOrEmpty(state.model))
            {
                return null;
            }

            if (NextGradeLookup.TryGetValue(state.model, out var next))
            {
                return next;
            }
            return null;
        }

        public static List<GoodCost> GetRecipeUpgradeCost(int targetGradeLevel)
        {
            return targetGradeLevel switch
            {
                1 => new List<GoodCost>
                {
                    EconomyHelper.BuildPacks(ModConfig.Grade1UpgradeBuildPacks.Value)
                },
                2 => new List<GoodCost>
                {
                    EconomyHelper.BuildPacks(ModConfig.Grade2UpgradeBuildPacks.Value),
                    EconomyHelper.TradePacks(ModConfig.Grade2UpgradeTradePacks.Value)
                },
                _ => new List<GoodCost>
                {
                    EconomyHelper.TradePacks(ModConfig.Grade3UpgradeTradePacks.Value),
                    EconomyHelper.WildfireEssence(ModConfig.Grade3UpgradeWildfireEssence.Value)
                }
            };
        }

        public static bool TryUnlockBonusRecipe(Workshop workshop, WorkshopPanel panel)
        {
            if (workshop == null || HasUnlockedBonusRecipe(workshop))
            {
                return false;
            }

            var bonusRecipe = GetCuratedBonusRecipe(workshop);
            if (bonusRecipe == null)
            {
                return false;
            }

            var costs = GetBonusRecipeCost(workshop);
            if (!EconomyHelper.TrySpend(workshop.ModelName, workshop.Id, costs))
            {
                Svc.PlayClickFail();
                RenderMasteryDock(panel, workshop);
                return false;
            }

            workshop.AddNewRecipe(bonusRecipe);
            SettlementModState.Current.FourthRecipeUnlockedWorkshops.Add(workshop.Id);
            SettlementModState.Save();

            Svc.PlayClickSuccess();
            RefreshWorkshopPanel(panel, workshop);
            return true;
        }

        public static bool TryUpgradeIndividualRecipe(Workshop workshop, WorkshopRecipeState recipeState, WorkshopPanel panel)
        {
            if (workshop == null || recipeState == null)
            {
                return false;
            }

            var nextModel = GetNextGradeRecipe(recipeState);
            if (nextModel == null)
            {
                return false;
            }

            int nextGrade = nextModel.grade != null ? nextModel.grade.level : 3;
            var costs = GetRecipeUpgradeCost(nextGrade);
            if (!EconomyHelper.TrySpend(workshop.ModelName, workshop.Id, costs))
            {
                Svc.PlayClickFail();
                RenderMasteryDock(panel, workshop);
                return false;
            }

            // Build new IngredientState[][] matching reduced ingredient counts while preserving player's allowed/priority settings
            var oldIngredients = recipeState.ingredients;
            var templateState = Svc.Recipes.CreateWorkshopRecipeState(nextModel.Name);
            var newIngredients = templateState.ingredients;
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
            }

            recipeState.model = nextModel.Name;
            recipeState.ingredients = newIngredients;
            if (TryGetBaseVanillaRecipeName(nextModel.Name, out string vanillaBase))
            {
                SettlementModState.Current.CloneToVanillaRecipeMap[nextModel.Name] = vanillaBase;
            }
            SettlementModState.Save();

            Svc.PlayClickSuccess();
            RefreshWorkshopPanel(panel, workshop);
            return true;
        }

        public static void RefreshWorkshopPanel(WorkshopPanel panel, Workshop workshop)
        {
            if (panel == null || workshop == null) return;
            panel.Show(workshop);
        }

        public static void RenderMasteryDock(WorkshopPanel panel, Workshop workshop)
        {
            if (panel == null) return;
            if (!ModConfig.EnableWorkshopMastery.Value || VanillaSaveConverter.IsRevertLocked || workshop == null || !workshop.IsFinished() || workshop.state?.recipes == null)
            {
                ImperialUiHelper.HideDock(panel.transform);
                return;
            }

            var dock = ImperialUiHelper.GetOrCreatePanelHeaderDock(panel.transform);
            dock.gameObject.SetActive(true);
            ImperialUiHelper.ClearChildren(dock);

            // Row 1: Unlock Curated Bonus Recipe (if not yet unlocked)
            if (!HasUnlockedBonusRecipe(workshop))
            {
                var bonusRecipe = GetCuratedBonusRecipe(workshop);
                if (bonusRecipe != null)
                {
                    var rowBonus = ImperialUiHelper.CreateRow(dock, "BonusRecipeRow");
                    var costs = GetBonusRecipeCost(workshop);
                    bool canAfford = EconomyHelper.CanAfford(costs);
                    string goodLabel = bonusRecipe.producedGood?.good != null
                        ? bonusRecipe.producedGood.DisplayName
                        : bonusRecipe.GetProducedGood();

                    string btnText = $"<b>+ Unlock {goodLabel} (1*)</b>  [{EconomyHelper.FormatCostSummary(costs)}]";
                    ImperialUiHelper.CreateStyledButton(
                        rowBonus,
                        "BtnUnlockFourth",
                        btnText,
                        canAfford,
                        () => TryUnlockBonusRecipe(workshop, panel),
                        minWidth: 260f,
                        height: 24f,
                        fontSize: 12);
                }
            }

            // Rows for Individual Recipe Star Upgrades (2 buttons per row for compact, clean layout)
            RectTransform currentRow = null;
            int buttonsInRow = 0;

            foreach (var rState in workshop.state.recipes)
            {
                if (rState == null) continue;
                var currentModel = workshop.GetRecipeModel(rState);
                if (currentModel == null) continue;

                string goodDisplay = currentModel.producedGood?.good != null
                    ? currentModel.producedGood.DisplayName
                    : rState.productName;
                int curLvl = currentModel.grade != null ? currentModel.grade.level : 1;

                var nextModel = GetNextGradeRecipe(rState);
                if (currentRow == null || buttonsInRow >= 2)
                {
                    currentRow = ImperialUiHelper.CreateRow(dock, "RecipeMasteryRow");
                    buttonsInRow = 0;
                }
                buttonsInRow++;

                if (nextModel == null || curLvl >= 3)
                {
                    ImperialUiHelper.CreateStyledButton(
                        currentRow,
                        $"BtnMax_{rState.productName}",
                        $"<color=#FFD966>[3*] {goodDisplay} (Mastered)</color>",
                        false,
                        null,
                        minWidth: 165f,
                        height: 23f,
                        fontSize: 11);
                }
                else
                {
                    int nextLvl = nextModel.grade != null ? nextModel.grade.level : curLvl + 1;
                    var costs = GetRecipeUpgradeCost(nextLvl);
                    bool canAfford = EconomyHelper.CanAfford(costs);
                    var capturedState = rState;

                    string label = $"<b>{goodDisplay} {curLvl}* -> {nextLvl}*</b> ({EconomyHelper.FormatCostSummary(costs)})";
                    ImperialUiHelper.CreateStyledButton(
                        currentRow,
                        $"BtnUpg_{rState.productName}",
                        label,
                        canAfford,
                        () => TryUpgradeIndividualRecipe(workshop, capturedState, panel),
                        minWidth: 190f,
                        height: 23f,
                        fontSize: 11);
                }
            }

            ImperialUiHelper.RefreshDockLayout(dock, () => RenderMasteryDock(panel, workshop));
        }
    }

    [HarmonyPatch]
    internal static class WorkshopMasteryPatches
    {
        [HarmonyPatch(typeof(WorkshopPanel), nameof(WorkshopPanel.Show))]
        [HarmonyPostfix]
        private static void WorkshopPanel_Show_Postfix(WorkshopPanel __instance, Workshop ___current)
        {
            try
            {
                WorkshopMasteryManager.RenderMasteryDock(__instance, ___current);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error rendering Workshop Mastery dock: {ex}");
            }
        }
    }
}
