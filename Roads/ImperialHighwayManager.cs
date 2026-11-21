using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ATS_API.Helpers;
using ATS_API.Localization;
using ATS_ImperialLogistics.Helpers;
using Eremite.Buildings;
using Eremite.Controller.Roads;
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

        // Cached runtime visuals (gilded build icon + gilded road ground atlas material)
        private static Texture2D _highwayIconTexture;
        private static Sprite _highwayIconSprite;
        private static Material _sourceRoadMaterial;
        private static Material _highwayRoadMaterial;
        private static Texture2D _highwayAtlasTexture;

        private static readonly FieldInfo RoadsGenRoadsField = AccessTools.Field(typeof(RoadsGenerator), "roads");
        private static readonly FieldInfo RoadsGenTrianglesField = AccessTools.Field(typeof(RoadsGenerator), "triangles");
        private static readonly FieldInfo RoadsGenMeshField = AccessTools.Field(typeof(RoadsGenerator), "mesh");
        private static readonly FieldInfo RoadsGenMeshFilterField = AccessTools.Field(typeof(RoadsGenerator), "meshFilter");

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

                // Assign a distinct gilded icon sprite (GPU-blitted from Reinforced Road without passing non-readable textures to ATS_API)
                _highwayModel.icon = CreateGildedIconSprite(reinforcedRoad.icon) ?? reinforcedRoad.icon;

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

        // --- Distinct Visual Styling (Gilded Imperial Gold Cobblestone) ---

        /// <summary>
        /// Splits the RoadsGenerator mesh into 2 submeshes whenever Imperial Highways are present:
        /// Submesh 0 uses the biome's vanilla road atlas material; Submesh 1 renders Imperial Highway tiles
        /// with a gilded royal-gold recolored copy of the road atlas so they stand out unmistakably from Reinforced Roads.
        /// </summary>
        internal static void ApplyHighwaySubmeshStyling(RoadsGenerator generator)
        {
            if (generator == null)
            {
                return;
            }

            var meshFilter = RoadsGenMeshFilterField?.GetValue(generator) as MeshFilter;
            var mesh = RoadsGenMeshField?.GetValue(generator) as Mesh;
            var roads = RoadsGenRoadsField?.GetValue(generator) as List<Road>;
            var triangles = RoadsGenTrianglesField?.GetValue(generator) as int[];

            if (meshFilter == null || mesh == null || roads == null || triangles == null)
            {
                return;
            }

            var renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            Material baseMat = GetBaseRoadMaterial(renderer);
            if (baseMat == null)
            {
                return;
            }

            int roadCount = roads.Count;
            if (roadCount == 0 || triangles.Length < roadCount * 6)
            {
                EnsureSingleMaterial(renderer, baseMat);
                return;
            }

            int highwayCount = 0;
            for (int i = 0; i < roadCount; i++)
            {
                var r = roads[i];
                if (r?.model != null && string.Equals(r.model.Name, HighwayModelName, StringComparison.Ordinal))
                {
                    highwayCount++;
                }
            }

            if (highwayCount == 0)
            {
                EnsureSingleMaterial(renderer, baseMat);
                return;
            }

            Material highwayMat = GetOrCreateHighwayRoadMaterial(baseMat);
            if (highwayMat == null)
            {
                EnsureSingleMaterial(renderer, baseMat);
                return;
            }

            var vanillaTris = new int[(roadCount - highwayCount) * 6];
            var highwayTris = new int[highwayCount * 6];
            int vIdx = 0;
            int hIdx = 0;

            for (int i = 0; i < roadCount; i++)
            {
                int src = i * 6;
                var r = roads[i];
                bool isHighway = r?.model != null && string.Equals(r.model.Name, HighwayModelName, StringComparison.Ordinal);
                if (isHighway)
                {
                    highwayTris[hIdx++] = triangles[src];
                    highwayTris[hIdx++] = triangles[src + 1];
                    highwayTris[hIdx++] = triangles[src + 2];
                    highwayTris[hIdx++] = triangles[src + 3];
                    highwayTris[hIdx++] = triangles[src + 4];
                    highwayTris[hIdx++] = triangles[src + 5];
                }
                else
                {
                    vanillaTris[vIdx++] = triangles[src];
                    vanillaTris[vIdx++] = triangles[src + 1];
                    vanillaTris[vIdx++] = triangles[src + 2];
                    vanillaTris[vIdx++] = triangles[src + 3];
                    vanillaTris[vIdx++] = triangles[src + 4];
                    vanillaTris[vIdx++] = triangles[src + 5];
                }
            }

            mesh.subMeshCount = 2;
            mesh.SetTriangles(vanillaTris, 0);
            mesh.SetTriangles(highwayTris, 1);

            var currentMats = renderer.sharedMaterials;
            if (currentMats == null || currentMats.Length != 2 || !ReferenceEquals(currentMats[0], baseMat) || !ReferenceEquals(currentMats[1], highwayMat))
            {
                renderer.sharedMaterials = new[] { baseMat, highwayMat };
            }
        }

        private static Material GetBaseRoadMaterial(MeshRenderer renderer)
        {
            var mats = renderer.sharedMaterials;
            if (mats != null && mats.Length > 0 && mats[0] != null && !ReferenceEquals(mats[0], _highwayRoadMaterial))
            {
                return mats[0];
            }
            return _sourceRoadMaterial;
        }

        private static void EnsureSingleMaterial(MeshRenderer renderer, Material baseMat)
        {
            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length != 1 || !ReferenceEquals(mats[0], baseMat))
            {
                renderer.sharedMaterials = new[] { baseMat };
            }
        }

        private static Material GetOrCreateHighwayRoadMaterial(Material baseMat)
        {
            if (_highwayRoadMaterial != null && ReferenceEquals(_sourceRoadMaterial, baseMat))
            {
                return _highwayRoadMaterial;
            }

            try
            {
                if (_highwayRoadMaterial != null)
                {
                    UnityEngine.Object.Destroy(_highwayRoadMaterial);
                    _highwayRoadMaterial = null;
                }
                if (_highwayAtlasTexture != null)
                {
                    UnityEngine.Object.Destroy(_highwayAtlasTexture);
                    _highwayAtlasTexture = null;
                }

                _sourceRoadMaterial = baseMat;
                _highwayRoadMaterial = new Material(baseMat)
                {
                    name = "ImperialHighway_RoadMat",
                    hideFlags = HideFlags.HideAndDontSave
                };

                Texture srcTex = null;
                string texProp = null;
                if (baseMat.HasProperty("_MainTex") && baseMat.GetTexture("_MainTex") != null)
                {
                    texProp = "_MainTex";
                    srcTex = baseMat.GetTexture("_MainTex");
                }
                else if (baseMat.HasProperty("_BaseMap") && baseMat.GetTexture("_BaseMap") != null)
                {
                    texProp = "_BaseMap";
                    srcTex = baseMat.GetTexture("_BaseMap");
                }
                else if (baseMat.mainTexture != null)
                {
                    texProp = "__mainTexture";
                    srcTex = baseMat.mainTexture;
                }

                if (srcTex != null && srcTex.width > 0 && srcTex.height > 0)
                {
                    _highwayAtlasTexture = CreateGildedTexture(srcTex, Vector2.one, Vector2.zero, srcTex.width, srcTex.height, addBorderGlow: false);
                    if (_highwayAtlasTexture != null)
                    {
                        if (texProp == "__mainTexture")
                        {
                            _highwayRoadMaterial.mainTexture = _highwayAtlasTexture;
                        }
                        else
                        {
                            _highwayRoadMaterial.SetTexture(texProp, _highwayAtlasTexture);
                            if (texProp == "_MainTex" && _highwayRoadMaterial.HasProperty("_BaseMap"))
                            {
                                _highwayRoadMaterial.SetTexture("_BaseMap", _highwayAtlasTexture);
                            }
                        }
                    }
                }
                else
                {
                    // Fallback tint if shader uses no main texture
                    Color goldTint = new Color(1.0f, 0.82f, 0.32f, 1.0f);
                    if (_highwayRoadMaterial.HasProperty("_Color")) _highwayRoadMaterial.SetColor("_Color", goldTint);
                    if (_highwayRoadMaterial.HasProperty("_BaseColor")) _highwayRoadMaterial.SetColor("_BaseColor", goldTint);
                }

                Plugin.Log.LogInfo(
                    $"ImperialHighwayManager built gilded highway material (shader='{baseMat.shader?.name}', texProp='{texProp ?? "none"}', size={srcTex?.width ?? 0}x{srcTex?.height ?? 0}).");
                return _highwayRoadMaterial;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed creating Imperial Highway material: {ex.Message}");
                return null;
            }
        }

        private static Sprite CreateGildedIconSprite(Sprite baseSprite)
        {
            if (_highwayIconSprite != null)
            {
                return _highwayIconSprite;
            }

            if (baseSprite == null || baseSprite.texture == null)
            {
                return null;
            }

            try
            {
                Rect rect = baseSprite.textureRect;
                int w = Mathf.RoundToInt(rect.width);
                int h = Mathf.RoundToInt(rect.height);
                if (w <= 0 || h <= 0 || baseSprite.texture.width <= 0 || baseSprite.texture.height <= 0)
                {
                    return null;
                }

                Vector2 scale = new Vector2(rect.width / baseSprite.texture.width, rect.height / baseSprite.texture.height);
                Vector2 offset = new Vector2(rect.x / baseSprite.texture.width, rect.y / baseSprite.texture.height);

                _highwayIconTexture = CreateGildedTexture(baseSprite.texture, scale, offset, w, h, addBorderGlow: true);
                if (_highwayIconTexture == null)
                {
                    return null;
                }

                _highwayIconSprite = Sprite.Create(
                    _highwayIconTexture,
                    new Rect(0, 0, w, h),
                    new Vector2(0.5f, 0.5f),
                    baseSprite.pixelsPerUnit);
                _highwayIconSprite.name = "ImperialHighway_Icon";
                _highwayIconSprite.hideFlags = HideFlags.HideAndDontSave;
                return _highwayIconSprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed creating gilded Imperial Highway icon, using base icon: {ex.Message}");
                return null;
            }
        }

        private static Texture2D CreateGildedTexture(
            Texture srcTex,
            Vector2 uvScale,
            Vector2 uvOffset,
            int targetWidth,
            int targetHeight,
            bool addBorderGlow)
        {
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(
                targetWidth,
                targetHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);

            try
            {
                Graphics.Blit(srcTex, rt, uvScale, uvOffset);
                RenderTexture.active = rt;

                var tex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, mipChain: !addBorderGlow, linear: false)
                {
                    name = addBorderGlow ? "ImperialHighway_IconTex" : "ImperialHighway_AtlasTex",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = srcTex.wrapMode,
                    filterMode = srcTex.filterMode,
                    anisoLevel = srcTex.anisoLevel
                };

                tex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0, recalculateMipMaps: false);

                Color32[] pixels = tex.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 c = pixels[i];
                    if (c.a == 0)
                    {
                        continue;
                    }

                    // Perceived luminance preserves cobblestone crevices, bevels, and mortar outlines
                    float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    float boosted = Mathf.Clamp01(Mathf.Pow(lum, 0.85f) * 1.15f + 0.05f);

                    float r, g, b;
                    if (boosted < 0.28f)
                    {
                        // Deep burnished bronze-umber crevices so stone grid stays crisp
                        float t = boosted / 0.28f;
                        r = Mathf.Lerp(0.18f, 0.56f, t);
                        g = Mathf.Lerp(0.11f, 0.37f, t);
                        b = Mathf.Lerp(0.05f, 0.11f, t);
                    }
                    else if (boosted < 0.72f)
                    {
                        // Warm royal imperial gold cobblestones
                        float t = (boosted - 0.28f) / 0.44f;
                        r = Mathf.Lerp(0.56f, 0.94f, t);
                        g = Mathf.Lerp(0.37f, 0.74f, t);
                        b = Mathf.Lerp(0.11f, 0.26f, t);
                    }
                    else
                    {
                        // Gleaming pale-gold bevel highlights & metallic studs
                        float t = (boosted - 0.72f) / 0.28f;
                        r = Mathf.Lerp(0.94f, 1.00f, t);
                        g = Mathf.Lerp(0.74f, 0.93f, t);
                        b = Mathf.Lerp(0.26f, 0.62f, t);
                    }

                    pixels[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                        c.a);
                }

                if (addBorderGlow)
                {
                    int border = Math.Max(2, Math.Min(targetWidth, targetHeight) / 36);
                    Color32 rim = new Color32(255, 224, 118, 255);
                    for (int y = 0; y < targetHeight; y++)
                    {
                        for (int x = 0; x < targetWidth; x++)
                        {
                            if (x < border || x >= targetWidth - border || y < border || y >= targetHeight - border)
                            {
                                int idx = y * targetWidth + x;
                                if (pixels[idx].a > 128)
                                {
                                    pixels[idx] = rim;
                                }
                            }
                        }
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(updateMipmaps: !addBorderGlow, makeNoLongerReadable: true);
                return tex;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
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

        [HarmonyPatch(typeof(RoadsGenerator), "CreateMesh")]
        [HarmonyPostfix]
        private static void RoadsGenerator_CreateMesh_Postfix(RoadsGenerator __instance)
        {
            try
            {
                ImperialHighwayManager.ApplyHighwaySubmeshStyling(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error styling Imperial Highway road mesh: {ex.Message}");
            }
        }
    }
}
