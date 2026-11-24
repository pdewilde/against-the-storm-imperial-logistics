using System;
using ATS_ImperialLogistics.Helpers;
using Eremite.Buildings.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ATS_ImperialLogistics.UI
{
    public static class ImperialUiHelper
    {
        public const string DefaultDockName = "ImperialLogistics_Dock";

        private static Sprite _roundedBgSprite;
        private static TMP_FontAsset _cachedFont;

        public static TMP_FontAsset GetGameFont(Transform context)
        {
            if (_cachedFont != null)
            {
                return _cachedFont;
            }

            if (context != null)
            {
                var existingTmp = context.GetComponentInParent<Canvas>()?.GetComponentInChildren<TMP_Text>(true)
                                  ?? context.GetComponentInChildren<TMP_Text>(true);
                if (existingTmp != null && existingTmp.font != null)
                {
                    _cachedFont = existingTmp.font;
                }
            }
            return _cachedFont;
        }

        private static Sprite GetPanelSprite()
        {
            if (_roundedBgSprite != null)
            {
                return _roundedBgSprite;
            }

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var cols = new Color[16];
            for (int i = 0; i < 16; i++) cols[i] = Color.white;
            tex.SetPixels(cols);
            tex.Apply();
            _roundedBgSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _roundedBgSprite.hideFlags = HideFlags.HideAndDontSave;
            return _roundedBgSprite;
        }

        public static Transform ResolveContentTransform(Transform panelRoot)
        {
            if (panelRoot == null) return null;
            return panelRoot.Find("Content") ?? panelRoot;
        }

        public static void HideDock(Transform panelRoot, string dockName = DefaultDockName)
        {
            if (panelRoot == null) return;

            var content = ResolveContentTransform(panelRoot);
            var dockInContent = content?.Find(dockName);
            if (dockInContent != null)
            {
                var pos = dockInContent.GetComponent<ImperialDockPositioner>();
                if (pos != null) pos.SetRefreshCallback(null);
                dockInContent.gameObject.SetActive(false);
            }

            if (content != panelRoot)
            {
                var legacyDock = panelRoot.Find(dockName);
                if (legacyDock != null)
                {
                    legacyDock.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Creates or retrieves a floating dock parented inside PreviewPanel's "Content" GameObject
        /// (so PreviewPanel.Hide() automatically hides it on deselect) and dynamically positioned
        /// cleanly below the lowest visible edge of the building panel (or beside it if tall) without blocking any UI.
        /// </summary>
        public static RectTransform GetOrCreatePanelHeaderDock(Transform panelRoot, string dockName = DefaultDockName)
        {
            var contentParent = ResolveContentTransform(panelRoot) as RectTransform ?? panelRoot as RectTransform;

            // Clean up any legacy dock attached directly to panelRoot
            if (contentParent != panelRoot)
            {
                var legacy = panelRoot.Find(dockName);
                if (legacy != null)
                {
                    UnityEngine.Object.Destroy(legacy.gameObject);
                }
            }

            var existing = contentParent.Find(dockName) as RectTransform;
            if (existing != null)
            {
                var existingPos = existing.GetComponent<ImperialDockPositioner>();
                if (existingPos != null) existingPos.MarkDirty();
                return existing;
            }

            var go = new GameObject(
                dockName,
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(Image),
                typeof(Outline),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(ImperialDockPositioner));

            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(contentParent, false);
            rt.localScale = Vector3.one;

            // Ignore any LayoutGroup on Content so we never perturb the vanilla panel layout
            var ignoreElem = go.GetComponent<LayoutElement>();
            ignoreElem.ignoreLayout = true;

            // Top-right pivot so right edge stays flush with MainPanel and wider text grows leftward on-screen
            rt.anchorMin = contentParent.pivot;
            rt.anchorMax = contentParent.pivot;
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(340f, 34f);

            var bg = go.GetComponent<Image>();
            bg.sprite = GetPanelSprite();
            bg.color = new Color(0.09f, 0.08f, 0.07f, 0.95f);

            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.45f, 0.24f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);

            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 6, 6);
            vlg.spacing = 4f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = go.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var positioner = go.GetComponent<ImperialDockPositioner>();
            positioner.Initialize(contentParent, rt);

            return rt;
        }

        public static void RefreshDockLayout(RectTransform dock, Action liveRebuildCallback = null)
        {
            if (dock == null) return;
            var pos = dock.GetComponent<ImperialDockPositioner>();
            if (pos != null)
            {
                pos.SetRefreshCallback(liveRebuildCallback);
                pos.MarkDirty();
                pos.RepositionNow(skipHorizontalScreenClamp: true);
            }
        }

        public static RectTransform CreateRow(Transform parent, string name = "Row")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;

            return rt;
        }

        public static Button CreateStyledButton(
            Transform parent,
            string objectName,
            string labelRichText,
            bool interactable,
            Action onClick,
            float minWidth = 110f,
            float height = 24f,
            int fontSize = 12)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            var le = go.GetComponent<LayoutElement>();
            le.minWidth = minWidth;
            le.preferredHeight = height;
            le.minHeight = height;

            var img = go.GetComponent<Image>();
            img.sprite = GetPanelSprite();
            img.color = interactable
                ? new Color(0.24f, 0.20f, 0.14f, 0.98f)
                : new Color(0.16f, 0.15f, 0.15f, 0.88f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = interactable;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.15f, 1.0f, 1f);
            colors.pressedColor = new Color(0.85f, 0.80f, 0.70f, 1f);
            colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.88f);
            btn.colors = colors;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            var font = GetGameFont(parent);
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;
            tmp.color = interactable ? new Color(0.96f, 0.91f, 0.78f, 1f) : new Color(0.72f, 0.70f, 0.67f, 1f);
            tmp.text = labelRichText;

            float preferred = tmp.GetPreferredValues(labelRichText).x + 20f;
            le.preferredWidth = Mathf.Max(minWidth, preferred);

            btn.onClick.AddListener(() =>
            {
                try
                {
                    onClick?.Invoke();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"UI button '{objectName}' click error: {ex}");
                }
            });

            var dock = parent.parent as RectTransform;
            if (dock != null)
            {
                var pos = dock.GetComponent<ImperialDockPositioner>();
                if (pos != null) pos.MarkDirty();
            }

            return btn;
        }

        public static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                var childGo = child.gameObject;
                childGo.SetActive(false);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(childGo);
            }
        }
    }

    internal sealed class ImperialDockPositioner : MonoBehaviour
    {
        private static readonly Vector3[] WorldCorners = new Vector3[4];
        private static readonly Vector3[] CanvasCorners = new Vector3[4];

        private RectTransform _contentRect;
        private RectTransform _dockRect;
        private RectTransform _mainPanelRect;
        private RectTransform _canvasRect;
        private Action _liveRebuildCallback;
        private int _lastStockSignature;
        private int _lastActiveChildMask = -1;
        private Vector2 _lastContentSize;
        private Vector3 _lastContentWorldPos;
        private int _dirtyFrames = 5;
        private float _settleUntilTime;
        private bool _needsSettledClamp;
        private float _nextPeriodicCheckTime;
        private bool _isRebuilding;

        public void Initialize(RectTransform contentRect, RectTransform dockRect)
        {
            _contentRect = contentRect;
            _dockRect = dockRect;
            _mainPanelRect = contentRect != null ? contentRect.Find("MainPanel") as RectTransform : null;
            _lastContentWorldPos = contentRect != null ? contentRect.position : Vector3.zero;
            _dirtyFrames = 5;
            _settleUntilTime = Time.unscaledTime + 0.45f;
            _needsSettledClamp = true;
        }

        public void SetRefreshCallback(Action liveRebuildCallback)
        {
            _liveRebuildCallback = liveRebuildCallback;
            _lastStockSignature = EconomyHelper.GetUpgradeGoodsSignature();
        }

        private void OnEnable()
        {
            _dirtyFrames = 5;
            _lastActiveChildMask = -1;
            _lastContentWorldPos = _contentRect != null ? _contentRect.position : Vector3.zero;
            _lastStockSignature = EconomyHelper.GetUpgradeGoodsSignature();
            _settleUntilTime = Time.unscaledTime + 0.45f;
            _needsSettledClamp = true;
            _nextPeriodicCheckTime = Time.unscaledTime + 0.25f;
        }

        public void MarkDirty()
        {
            _dirtyFrames = 3;
            _settleUntilTime = Mathf.Max(_settleUntilTime, Time.unscaledTime + 0.35f);
            _needsSettledClamp = true;
        }

        private void LateUpdate()
        {
            if (_dockRect == null || _contentRect == null || !_contentRect.gameObject.activeInHierarchy)
            {
                return;
            }

            // Detect panel slide-in / reposition animations so the dock stays glued to MainPanel
            // and performs a final settled pass once the DOTween animation finishes.
            Vector3 currentPos = _contentRect.position;
            bool isMoving = (currentPos - _lastContentWorldPos).sqrMagnitude > 0.01f;
            if (isMoving)
            {
                _lastContentWorldPos = currentPos;
                _dirtyFrames = Math.Max(_dirtyFrames, 2);
                _settleUntilTime = Mathf.Max(_settleUntilTime, Time.unscaledTime + 0.15f);
                _needsSettledClamp = true;
            }

            bool settling = isMoving || Time.unscaledTime < _settleUntilTime;
            if (_dirtyFrames > 0)
            {
                _dirtyFrames--;
                RepositionNow(skipHorizontalScreenClamp: settling);
                if (!settling) _needsSettledClamp = false;
                return;
            }

            if (settling)
            {
                return;
            }

            if (_needsSettledClamp)
            {
                _needsSettledClamp = false;
                RepositionNow(skipHorizontalScreenClamp: false);
                return;
            }

            if (Time.unscaledTime < _nextPeriodicCheckTime)
            {
                return;
            }
            _nextPeriodicCheckTime = Time.unscaledTime + 0.45f;

            // Live refresh button affordability/counts if Warehouse upgrade stocks changed while panel is open
            if (_liveRebuildCallback != null && !_isRebuilding && Svc.Active)
            {
                int currentSig = EconomyHelper.GetUpgradeGoodsSignature();
                if (currentSig != _lastStockSignature)
                {
                    _lastStockSignature = currentSig;
                    _isRebuilding = true;
                    try
                    {
                        _liveRebuildCallback.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Error refreshing dock affordability: {ex.Message}");
                    }
                    finally
                    {
                        _isRebuilding = false;
                    }
                    return;
                }
            }

            // Only re-scan child bounds if active sub-panels toggled or Content size changed
            int mask = ComputeActiveChildMask();
            Vector2 contentSize = _contentRect.rect.size;
            if (mask != _lastActiveChildMask || (contentSize - _lastContentSize).sqrMagnitude > 0.5f)
            {
                RepositionNow();
            }
        }

        private int ComputeActiveChildMask()
        {
            if (_contentRect == null) return 0;
            int mask = 17;
            int count = _contentRect.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = _contentRect.GetChild(i);
                if (child == _dockRect) continue;
                mask = (mask * 31) + (child.gameObject.activeSelf ? 1 : 0);
            }
            return mask;
        }

        public void RepositionNow(bool skipHorizontalScreenClamp = false)
        {
            if (_contentRect == null || _dockRect == null)
            {
                return;
            }

            _lastActiveChildMask = ComputeActiveChildMask();
            _lastContentSize = _contentRect.rect.size;
            _lastContentWorldPos = _contentRect.position;

            if (_mainPanelRect == null)
            {
                _mainPanelRect = _contentRect.Find("MainPanel") as RectTransform;
            }
            if (_canvasRect == null)
            {
                var rootCanvas = _contentRect.GetComponentInParent<Canvas>()?.rootCanvas;
                if (rootCanvas != null)
                {
                    _canvasRect = rootCanvas.transform as RectTransform;
                }
            }

            bool hasAll = false;
            float allMinX = float.MaxValue, allMaxX = float.MinValue;
            float allMinY = float.MaxValue, allMaxY = float.MinValue;

            bool hasMain = false;
            float mainMinX = float.MaxValue, mainMaxX = float.MinValue;
            float mainMinY = float.MaxValue, mainMaxY = float.MinValue;

            // Single pass over _contentRect children, capturing _mainPanelRect bounds simultaneously
            int count = _contentRect.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = _contentRect.GetChild(i);
                if (child == _dockRect || !child.gameObject.activeSelf) continue;

                if (child == _mainPanelRect)
                {
                    AccumulateVisibleBounds(child, ref hasMain, ref mainMinX, ref mainMaxX, ref mainMinY, ref mainMaxY);
                    if (hasMain)
                    {
                        IncludeMinMax(mainMinX, mainMaxX, mainMinY, mainMaxY, ref hasAll, ref allMinX, ref allMaxX, ref allMinY, ref allMaxY);
                    }
                }
                else
                {
                    AccumulateVisibleBounds(child, ref hasAll, ref allMinX, ref allMaxX, ref allMinY, ref allMaxY);
                }
            }

            if (!hasAll)
            {
                return;
            }

            float refMinX = hasMain ? mainMinX : allMinX;
            float refMaxX = hasMain ? mainMaxX : allMaxX;
            float panelWidth = Mathf.Max(260f, refMaxX - refMinX);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_dockRect);
            float prefWidth = LayoutUtility.GetPreferredWidth(_dockRect);
            float targetWidth = Mathf.Max(panelWidth, prefWidth);
            _dockRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_dockRect);

            float dockHeight = Mathf.Max(32f, _dockRect.rect.height);

            // Default: place 8px cleanly BELOW the lowest visible element of the building panel, right-aligned with MainPanel
            float targetRightX = refMaxX;
            float targetTopY = allMinY - 8f;

            if (_canvasRect != null)
            {
                _canvasRect.GetWorldCorners(CanvasCorners);
                Vector3 screenBottomLeft = _contentRect.InverseTransformPoint(CanvasCorners[0]);
                Vector3 screenTopRight = _contentRect.InverseTransformPoint(CanvasCorners[2]);

                // Only clamp against horizontal screen edges once the panel's slide-in animation has settled;
                // clamping while Content is still off-screen right shifts the dock hundreds of pixels too far left!
                if (!skipHorizontalScreenClamp)
                {
                    targetRightX = Mathf.Min(targetRightX, screenTopRight.x - 10f);
                }

                // If the building panel is very tall (e.g., Main Warehouse scroll view) and placing below would clip near the bottom,
                // dock neatly 8px to the LEFT of the building panel instead.
                if (targetTopY - dockHeight < screenBottomLeft.y + 18f)
                {
                    targetRightX = allMinX - 8f;
                    targetTopY = Mathf.Min(hasMain ? mainMaxY : allMaxY, screenTopRight.y - 75f);
                }

                if (!skipHorizontalScreenClamp)
                {
                    targetRightX = Mathf.Max(targetRightX, screenBottomLeft.x + targetWidth + 10f);
                }
            }

            _dockRect.anchorMin = _contentRect.pivot;
            _dockRect.anchorMax = _contentRect.pivot;
            _dockRect.pivot = new Vector2(1f, 1f);
            _dockRect.localPosition = new Vector3(targetRightX, targetTopY, 0f);
            _dockRect.SetAsLastSibling();
        }

        private static void IncludeMinMax(
            float srcMinX, float srcMaxX, float srcMinY, float srcMaxY,
            ref bool dstHas, ref float dstMinX, ref float dstMaxX, ref float dstMinY, ref float dstMaxY)
        {
            if (!dstHas)
            {
                dstMinX = srcMinX; dstMaxX = srcMaxX;
                dstMinY = srcMinY; dstMaxY = srcMaxY;
                dstHas = true;
                return;
            }
            if (srcMinX < dstMinX) dstMinX = srcMinX;
            if (srcMaxX > dstMaxX) dstMaxX = srcMaxX;
            if (srcMinY < dstMinY) dstMinY = srcMinY;
            if (srcMaxY > dstMaxY) dstMaxY = srcMaxY;
        }

        private void AccumulateVisibleBounds(
            Transform node,
            ref bool hasAny,
            ref float minX,
            ref float maxX,
            ref float minY,
            ref float maxY)
        {
            if (node == null || node == _dockRect || !node.gameObject.activeSelf)
            {
                return;
            }

            var rt = node as RectTransform;
            if (rt != null)
            {
                // Stop at ScrollRect / Mask / RectMask2D viewports so off-screen scrolled rows don't inflate bounds
                bool isViewportClip = node.GetComponent<ScrollRect>() != null
                                      || node.GetComponent<Mask>() != null
                                      || node.GetComponent<RectMask2D>() != null;
                if (isViewportClip)
                {
                    IncludeRect(rt, ref hasAny, ref minX, ref maxX, ref minY, ref maxY);
                    return;
                }

                var graphic = node.GetComponent<Graphic>();
                if (graphic != null && graphic.enabled && graphic.color.a > 0.02f)
                {
                    IncludeRect(rt, ref hasAny, ref minX, ref maxX, ref minY, ref maxY);
                }
            }

            int childCount = node.childCount;
            for (int i = 0; i < childCount; i++)
            {
                AccumulateVisibleBounds(node.GetChild(i), ref hasAny, ref minX, ref maxX, ref minY, ref maxY);
            }
        }

        private void IncludeRect(
            RectTransform rt,
            ref bool hasAny,
            ref float minX,
            ref float maxX,
            ref float minY,
            ref float maxY)
        {
            if (rt.rect.width <= 1f || rt.rect.height <= 1f)
            {
                return;
            }

            rt.GetWorldCorners(WorldCorners);
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = _contentRect.InverseTransformPoint(WorldCorners[i]);
                if (!hasAny)
                {
                    minX = maxX = local.x;
                    minY = maxY = local.y;
                    hasAny = true;
                }
                else
                {
                    if (local.x < minX) minX = local.x;
                    if (local.x > maxX) maxX = local.x;
                    if (local.y < minY) minY = local.y;
                    if (local.y > maxY) maxY = local.y;
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class ImperialUiPatches
    {
        [HarmonyPatch(typeof(PreviewPanel), nameof(PreviewPanel.Hide))]
        [HarmonyPostfix]
        private static void PreviewPanel_Hide_Postfix(PreviewPanel __instance)
        {
            try
            {
                if (__instance != null)
                {
                    ImperialUiHelper.HideDock(__instance.transform);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Error hiding Imperial UI dock: {ex.Message}");
            }
        }
    }
}
